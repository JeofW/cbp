using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Combat
{
	public static class SpellManager
	{
		#region Constants - Offsets 3.3.5a (12340)

		// Cooldown linked list head — HB 3.3.5a decompile: address 13890996 = SpellCooldownPtr(0xD3F5AC) + 8.
		private const uint CooldownListBase = 0xD3F5B4;  // 13890996 decimal

		private const uint PendingSpellListBase = 0xAF5254;

		#endregion

		private static int _lastKnownSpellCount;
		private static readonly Dictionary<string, WoWSpell> _knownSpells = new Dictionary<string, WoWSpell>(StringComparer.OrdinalIgnoreCase);
		private static readonly object _cooldownSync = new object();
		private static readonly Dictionary<int, long> _cooldownReadyAtTicks = new Dictionary<int, long>();
			private static readonly Dictionary<int, long> _castVerificationUntilTicks = new Dictionary<int, long>();
			private static readonly Dictionary<int, long> _readinessProbeNotBeforeTicks = new Dictionary<int, long>();
				private static readonly HashSet<int> _failedCastCandidates = new HashSet<int>();
				private static readonly HashSet<int> _seenCastCandidates = new HashSet<int>();
			private static bool _castSelectionClaimed;
			private static int _claimedCastCandidateId;
			private static object? _castSelectionBot, _castSelectionRun;
		private const int CastAttemptVerificationDelayMs = 250;
		private const int UnavailableProbeBackoffMs = 250;
		private const int FailedProbeBackoffMs = 500;
		private static SpellObservationContext? _cooldownContext;
		private static long _cooldownEpoch, _lastCooldownObservationTicks;

		internal sealed class SpellObservationContext
		{
			private readonly object _actor, _memory, _executor;
			private readonly object? _bot, _run;
			private readonly ulong _guid;
			private readonly uint _address, _map;
			private readonly bool _running;
			private readonly long _epoch;
			private readonly Func<bool> _current;
			internal readonly long ObservedAt;

			internal SpellObservationContext(object actor, object memory, object executor, object? bot, object? run,
				ulong guid, uint address, uint map, bool running, long epoch, long observedAt, Func<bool> current)
			{
				_actor = actor; _memory = memory; _executor = executor; _bot = bot; _run = run;
				_guid = guid; _address = address; _map = map; _running = running;
				_epoch = epoch; ObservedAt = observedAt; _current = current;
			}

			internal bool SameOwner(SpellObservationContext other) => ReferenceEquals(_actor, other._actor)
				&& ReferenceEquals(_memory, other._memory) && ReferenceEquals(_executor, other._executor)
				&& ReferenceEquals(_bot, other._bot) && ReferenceEquals(_run, other._run)
				&& _guid == other._guid && _address == other._address
				&& _map == other._map && _running == other._running && _epoch == other._epoch;

			internal void RequireCurrent()
			{
				if (!_current()) throw new ObservationUnavailableException("spell-cooldown",
					"The actor, world, executor, run or clock changed during the spell observation.");
			}
		}

		internal static SpellObservationContext CaptureSpellObservation()
		{
			var actor = StyxWoW.Me;
			var memory = ObjectManager.Wow;
			var executor = ObjectManager.Executor;
			var bot = TreeRoot.Current;
			var run = TreeRoot.RunIdentity;
			bool worker = TreeRoot.CurrentThreadIsBotThread, running = TreeRoot.IsRunning;
			TreeRoot.VerifyPulseOwner(bot, worker);
			long epoch;
			lock (_cooldownSync) epoch = _cooldownEpoch;
			long now = Environment.TickCount64;
			if (actor == null || memory == null || executor == null || !actor.IsValid || now < 0)
				throw new ObservationUnavailableException("spell-cooldown", "A complete current spell observation context is unavailable.");
			ulong guid = actor.Guid;
			uint address = actor.BaseAddress, map = actor.MapId;
			if (guid == 0 || address == 0)
				throw new ObservationUnavailableException("spell-cooldown", "The spell observation has no valid actor identity.");
			bool Current()
			{
				TreeRoot.VerifyPulseOwner(bot, worker);
				lock (_cooldownSync)
					return epoch == _cooldownEpoch && Environment.TickCount64 >= now
						&& ReferenceEquals(StyxWoW.Me, actor) && ReferenceEquals(ObjectManager.Wow, memory)
						&& ReferenceEquals(ObjectManager.Executor, executor) && ReferenceEquals(TreeRoot.Current, bot)
						&& ReferenceEquals(TreeRoot.RunIdentity, run)
						&& TreeRoot.IsRunning == running && actor.IsValid && actor.Guid == guid
						&& actor.BaseAddress == address && actor.MapId == map;
			}
			var observation = new SpellObservationContext(actor, memory, executor, bot, run, guid, address, map, running, epoch, now, Current);
			observation.RequireCurrent();
			return observation;
		}

		private static void PrepareCooldownContext(SpellObservationContext observation)
		{
			lock (_cooldownSync)
			{
				observation.RequireCurrent();
				if (_cooldownContext == null || !_cooldownContext.SameOwner(observation)
					|| observation.ObservedAt < _lastCooldownObservationTicks)
				{
					_cooldownReadyAtTicks.Clear();
					_castVerificationUntilTicks.Clear();
					_readinessProbeNotBeforeTicks.Clear();
				}
				_cooldownContext = observation;
				_lastCooldownObservationTicks = observation.ObservedAt;
			}
		}

			private static void ResetCooldownObservations()
			{
				lock (_cooldownSync)
				{
				_cooldownEpoch++;
				_cooldownContext = null;
				_lastCooldownObservationTicks = 0;
				_cooldownReadyAtTicks.Clear();
					_castVerificationUntilTicks.Clear();
						_readinessProbeNotBeforeTicks.Clear();
						_failedCastCandidates.Clear();
						_seenCastCandidates.Clear();
						_castSelectionClaimed = false;
					_claimedCastCandidateId = 0;
					_castSelectionBot = null;
					_castSelectionRun = null;
				}
			}

			/// <summary>
			/// Starts one routine decision pulse for expensive current cast admission.
			/// This does not authorize a cast; it only bounds how many Singular
			/// candidates may cross the strict CanCast observation in one pulse.
			/// </summary>
			public static void BeginCastSelectionPulse()
			{
				object? bot = TreeRoot.Current;
				object? run = TreeRoot.RunIdentity;
				lock (_cooldownSync)
				{
					bool ownerChanged = !ReferenceEquals(_castSelectionBot, bot)
						|| !ReferenceEquals(_castSelectionRun, run);
						// Wrap when all candidates actually seen in the previous pulse have
						// failed. A sole recovered candidate must not lose a whole extra
						// pulse; an untried lower priority still gets its bounded turn first.
						if (ownerChanged || !_castSelectionClaimed
							|| _failedCastCandidates.IsSupersetOf(_seenCastCandidates))
							_failedCastCandidates.Clear();
						_seenCastCandidates.Clear();
					_castSelectionBot = bot;
					_castSelectionRun = run;
					_castSelectionClaimed = false;
					_claimedCastCandidateId = 0;
				}
			}

			/// <summary>
			/// Reserves this pulse's one strict cast-admission observation. A failed
			/// candidate is skipped for the remainder of the current selection pass,
			/// allowing lower priorities to be considered on following pulses.
			/// </summary>
			public static bool TryClaimCastCandidate(string spellName)
			{
				if (string.IsNullOrWhiteSpace(spellName)) return false;
					WoWSpell? spell = GetSpellByName(spellName);
					if (spell == null || spell.Id <= 0) return false;
					// A current negative observation must not spend the sole expensive
					// candidate slot again. Expiry only permits a fresh final query;
					// it never establishes readiness or authorizes native submission.
					try
					{
						var observedOwner = CaptureSpellObservation();
						lock (_cooldownSync)
						{
							long now = Environment.TickCount64;
							if (_cooldownContext != null && _cooldownContext.SameOwner(observedOwner)
								&& now >= _lastCooldownObservationTicks
								&& ((_cooldownReadyAtTicks.TryGetValue(spell.Id, out long cooldown) && now < cooldown)
									|| (_castVerificationUntilTicks.TryGetValue(spell.Id, out long verification) && now < verification)
									|| (_readinessProbeNotBeforeTicks.TryGetValue(spell.Id, out long retry) && now < retry)))
								return false;
						}
					}
					catch (ObservationUnavailableException) { return false; }
					lock (_cooldownSync)
				{
						if (!ReferenceEquals(_castSelectionBot, TreeRoot.Current)
							|| !ReferenceEquals(_castSelectionRun, TreeRoot.RunIdentity))
							return false;
						_seenCastCandidates.Add(spell.Id);
						if (_failedCastCandidates.Contains(spell.Id)) return false;
					if (_castSelectionClaimed)
						return _claimedCastCandidateId == spell.Id;
					_castSelectionClaimed = true;
					_claimedCastCandidateId = spell.Id;
					return true;
				}
			}

			public static void RecordCastCandidateResult(string spellName, bool submitted)
			{
				WoWSpell? spell = GetSpellByName(spellName);
				if (spell == null || spell.Id <= 0) return;
				lock (_cooldownSync)
				{
					if (!ReferenceEquals(_castSelectionBot, TreeRoot.Current)
						|| !ReferenceEquals(_castSelectionRun, TreeRoot.RunIdentity)
						|| !_castSelectionClaimed || _claimedCastCandidateId != spell.Id)
						return;
					if (submitted) _failedCastCandidates.Clear();
					else _failedCastCandidates.Add(spell.Id);
				}
			}

		public static Dictionary<string, WoWSpell> KnownSpells => _knownSpells;

		// HB 4.3.4 compatibility aliases
		public static Dictionary<string, WoWSpell> Spells => _knownSpells;
		// BUG-09 fix: Return defensive copy so callers can't corrupt the internal dictionary
		public static Dictionary<string, WoWSpell> RawSpells => new Dictionary<string, WoWSpell>(_knownSpells, StringComparer.OrdinalIgnoreCase);

		public static int NumKnownSpells
		{
			get
			{
				LocalPlayer? me = StyxWoW.Me;
				return me?.KnownSpells.Count ?? 0;
			}
		}

		public static void Refresh()
		{
			Logging.WriteDebug("Refresh() called. LastCount={0}, CurrentCount={1}", _lastKnownSpellCount, NumKnownSpells);
			
			if (_lastKnownSpellCount == 0 || NumKnownSpells != _lastKnownSpellCount)
			{
				Logging.Write("Building spell book");
				_knownSpells.Clear();

				LocalPlayer? me = StyxWoW.Me;
				if (me == null)
				{
					Logging.WriteDebug("ERROR: LocalPlayer is null!");
					return;
				}

				var knownSpells = me.KnownSpells;
				Logging.WriteDebug("Found {0} spells from LocalPlayer.KnownSpells", knownSpells.Count);
				
				foreach (WoWSpell spell in knownSpells)
				{
					if (!_knownSpells.ContainsKey(spell.Name))
					{
						_knownSpells.Add(spell.Name, spell);
						Logging.Write("Adding {0}", spell.Name);
					}
				}

				// Druid form-specific spell aliases (ported from HB 4.3.4 smethod_4).
				// In WotLK 3.3.5a, bear/cat variants share the same base name from the
				// client, so only one survives the ContainsKey check above. This second
				// pass adds form-qualified keys by spell ID so CRs that call e.g.
				// HasSpell("Swipe (Bear)") or CanCast("Mangle (Cat)") resolve correctly.
				foreach (WoWSpell spell in knownSpells)
				{
					switch (spell.Id)
					{
						case 779:   // Swipe (Bear)
							_knownSpells["Swipe (Bear)"] = spell;
							break;
						case 62078: // Swipe (Cat)
							_knownSpells["Swipe (Cat)"] = spell;
							break;
						case 33876: // Mangle (Cat)
							_knownSpells["Mangle (Cat)"] = spell;
							break;
						case 33878: // Mangle (Bear)
							_knownSpells["Mangle (Bear)"] = spell;
							break;
						case 16979: // Feral Charge (Bear)
							_knownSpells["Feral Charge (Bear)"] = spell;
							break;
						case 49376: // Feral Charge (Cat) — WotLK 3.0.2+
							_knownSpells["Feral Charge (Cat)"] = spell;
							break;
					}
				}

				_lastKnownSpellCount = NumKnownSpells;
				Logging.Write("Spell book built");
			}
		}

		public static bool CastBarVisible
		{
			get
			{
				try
				{
					LocalPlayer? me = StyxWoW.Me;
					return me != null && me.Casting > 0;
				}
				catch
				{
					return false;
				}
			}
		}

		public static bool GlobalCooldown
		{
			get
			{
				// HB 4.3.4 reads the cooldown list directly without grabbing a frame.
				// Using AcquireFrame(true) injects code and can clash with other
				// executor calls; remove it to mimic the original behavior and
				// eliminate freezes.
				try
				{
					Memory? memory = ObjectManager.Wow;
					if (memory == null) return false;

					long frequency;
					long counter;
					QueryPerformanceFrequency(out frequency);
					QueryPerformanceCounter(out counter);
					long currentTime = counter * 1000L / frequency;

					uint cooldownPtr = memory.Read<uint>(CooldownListBase);

					while (cooldownPtr != 0U && (cooldownPtr & 1U) == 0U)
					{
						uint startTime = memory.Read<uint>(cooldownPtr + 16U);
						uint duration = memory.Read<uint>(cooldownPtr + 44U);

						if ((ulong)(startTime + duration) > (ulong)currentTime)
						{
							return true;
						}

						cooldownPtr = memory.Read<uint>(cooldownPtr + 4U);
					}

					return false;
				}
				catch
				{
					return false;
				}
			}
		}

		public static TimeSpan GlobalCooldownLeft
		{
			get
			{
				// Mirror HB 4.3.4: raw walk of cooldown list, no frame lock.
				try
				{
					Memory? memory = ObjectManager.Wow;
					if (memory == null) return TimeSpan.Zero;

					long frequency;
					long counter;
					QueryPerformanceFrequency(out frequency);
					QueryPerformanceCounter(out counter);
	
					long currentTime = counter * 1000L / frequency;

					uint cooldownPtr = memory.Read<uint>(CooldownListBase);

					while (cooldownPtr != 0U && (cooldownPtr & 1U) == 0U)
					{
						uint startTime = memory.Read<uint>(cooldownPtr + 16U);
						uint duration = memory.Read<uint>(cooldownPtr + 44U);

						long endTime = startTime + duration;
						if (endTime > currentTime)
						{
							return TimeSpan.FromMilliseconds(endTime - currentTime);
						}

						cooldownPtr = memory.Read<uint>(cooldownPtr + 4U);
					}

					return TimeSpan.Zero;
				}
				catch
				{
					return TimeSpan.Zero;
				}
			}
		}

		/// <summary>
		/// Gets the remaining cooldown for a specific spell by walking the cooldown
		/// linked list via pure ReadProcessMemory. Zero Execute() overhead.
		/// Node layout (HB 4.3.4 Struct78): +0x04=Next, +0x08=SpellId,
		/// +0x10=StartTime, +0x14=SpellCooldown, +0x2C=GCDDuration.
		/// </summary>
		internal static TimeSpan CalculateCooldownRemaining(
			uint startTime,
			uint spellCooldown,
			uint globalCooldown,
			long currentTime)
		{
			uint effectiveDuration = Math.Max(spellCooldown, globalCooldown);
			long remaining = (long)startTime + effectiveDuration - currentTime;
			return remaining > 0 ? TimeSpan.FromMilliseconds(remaining) : TimeSpan.Zero;
		}

		internal static TimeSpan CalculateTrackedCooldownRemaining(long currentTicks, long readyAtTicks)
		{
			long remaining = readyAtTicks - currentTicks;
			return remaining > 0 ? TimeSpan.FromMilliseconds(remaining) : TimeSpan.Zero;
		}

		internal static bool IsCooldownReady(
			double availabilitySeconds,
			uint lagToleranceMs,
			bool accountForLagTolerance)
		{
			if (availabilitySeconds < 0)
				return false;

			double toleranceMs = accountForLagTolerance ? lagToleranceMs : 0U;
			return availabilitySeconds * 1000.0 <= toleranceMs;
		}

		internal static bool IsTrackedCooldownBlocking(
			TimeSpan remaining,
			uint lagToleranceMs,
			bool accountForLagTolerance,
			bool isCastVerification)
		{
			double toleranceMs = accountForLagTolerance && !isCastVerification
				? lagToleranceMs
				: 0U;
			return remaining.TotalMilliseconds > toleranceMs;
		}

		internal static bool TryParseAvailability(
			IReadOnlyList<string> values,
			out double availabilitySeconds)
		{
			availabilitySeconds = -1;
			return values != null &&
			       values.Count == 2 &&
			       string.Equals(values[0], "ok", StringComparison.Ordinal) &&
			       double.TryParse(
				       values[1], NumberStyles.Float, CultureInfo.InvariantCulture,
				       out availabilitySeconds) &&
			       double.IsFinite(availabilitySeconds) &&
			       (availabilitySeconds == -1 || availabilitySeconds >= 0) &&
			       availabilitySeconds < TimeSpan.MaxValue.TotalSeconds;
		}

		private static string CreateCooldownQuery(int spellId, bool requireUsable)
		{
			// Validate components before arithmetic: NaN makes `left > 0` false,
			// which must not turn an incomplete observation into a ready spell.
			string query = "local function finite(v) return type(v)=='number' and v==v and v~=math.huge and v~=-math.huge end " +
				"local n=GetSpellInfo(" + spellId.ToString(CultureInfo.InvariantCulture) + "); " +
				"if type(n)~='string' or n=='' then return 'unknown-name',-1 end " +
				"local s,d,e=GetSpellCooldown(n); " +
				"if not finite(s) or not finite(d) or s<0 or d<0 or (e~=0 and e~=1) then return 'unknown-cooldown',-1 end " +
				"if e==0 then return 'disabled',-1 end " +
				"local now=GetTime(); if not finite(now) or now<0 then return 'unknown-clock',-1 end " +
				"local left=s+d-now; if not finite(left) then return 'unknown-duration',-1 end " +
				"if left>0 then return 'ok',left end ";
			if (requireUsable)
				query += "local usable=IsUsableSpell(n); if not usable then return 'ok',-1 end " +
					"if usable~=true and usable~=1 then return 'unknown-usability',-1 end ";
			return query + "return 'ok',0";
		}

		private static TimeSpan GetTrackedCooldownTimeLeft(
			Dictionary<int, long> deadlines,
			int spellId,
			long currentTicks)
		{
			lock (_cooldownSync)
			{
				if (!deadlines.TryGetValue(spellId, out long readyAtTicks))
					return TimeSpan.Zero;

				TimeSpan remaining = CalculateTrackedCooldownRemaining(currentTicks, readyAtTicks);
				if (remaining <= TimeSpan.Zero)
					deadlines.Remove(spellId);
				return remaining;
			}
		}

		private static void TrackCooldown(int spellId, TimeSpan duration, long currentTicks, SpellObservationContext? observation = null)
		{
			TrackDeadline(_cooldownReadyAtTicks, spellId, duration, currentTicks, observation);
		}

		private static void TrackDeadline(
			Dictionary<int, long> deadlines,
			int spellId,
			TimeSpan duration,
			long currentTicks, SpellObservationContext? observation = null)
		{
			lock (_cooldownSync)
			{
				if (observation != null)
				{
					observation.RequireCurrent();
					if (!ReferenceEquals(_cooldownContext, observation))
						throw new ObservationUnavailableException("spell-cooldown", "A later observation superseded this cooldown reply.");
					// The returned duration is still remaining at the client query.
					// Starting it before a delayed dispatch would expire it too early.
					currentTicks = Environment.TickCount64;
					_lastCooldownObservationTicks = currentTicks;
				}
				if (duration <= TimeSpan.Zero) { deadlines.Remove(spellId); return; }
				double milliseconds = Math.Ceiling(duration.TotalMilliseconds);
				if (currentTicks < 0 || milliseconds >= long.MaxValue - currentTicks)
					throw new ObservationUnavailableException("spell-cooldown", "The observed cooldown deadline is not representable.");
				deadlines[spellId] = currentTicks + (long)milliseconds;
			}
		}

		private static bool IsSpellAvailable(
			WoWSpell spell,
			uint lagToleranceMs,
			bool accountForLagTolerance)
		{
			if (spell == null || spell.Id <= 0) return false;
			try
			{
				var observation = CaptureSpellObservation();
				PrepareCooldownContext(observation);
				long currentTicks = Environment.TickCount64;
				TimeSpan verification = GetTrackedCooldownTimeLeft(
					_castVerificationUntilTicks, spell.Id, currentTicks);
				if (IsTrackedCooldownBlocking(
					verification, lagToleranceMs, accountForLagTolerance, true))
					return false;

				TimeSpan tracked = GetTrackedCooldownTimeLeft(
					_cooldownReadyAtTicks, spell.Id, currentTicks);
				if (IsTrackedCooldownBlocking(
					tracked, lagToleranceMs, accountForLagTolerance, false))
					return false;

				TimeSpan probeBackoff = GetTrackedCooldownTimeLeft(
					_readinessProbeNotBeforeTicks, spell.Id, currentTicks);
				if (probeBackoff > TimeSpan.Zero)
					return false;

				// One localized query retains readiness and usability together.
				// Its marker distinguishes a ready value from transport failure.
				List<string> values = Lua.GetReturnValues(CreateCooldownQuery(spell.Id, true));
				observation.RequireCurrent();

				if (!TryParseAvailability(values, out double availabilitySeconds))
				{
					TrackDeadline(
						_readinessProbeNotBeforeTicks, spell.Id,
						TimeSpan.FromMilliseconds(FailedProbeBackoffMs), currentTicks, observation);
					return false;
				}

				if (availabilitySeconds >= 0)
					TrackCooldown(spell.Id, TimeSpan.FromSeconds(availabilitySeconds), currentTicks, observation);
				else
					TrackDeadline(
						_readinessProbeNotBeforeTicks, spell.Id,
						TimeSpan.FromMilliseconds(UnavailableProbeBackoffMs), currentTicks, observation);

				return IsCooldownReady(
					availabilitySeconds, lagToleranceMs, accountForLagTolerance);
			}
			catch (ObservationUnavailableException) { return false; }
		}

		public static TimeSpan GetSpellCooldownTimeLeft(int spellId)
		{
			if (spellId <= 0)
				throw new ObservationUnavailableException("spell-cooldown", "Invalid spell identity for cooldown observation.");
			var observation = CaptureSpellObservation();
			PrepareCooldownContext(observation);
			// Numeric readers are also used in positive comparisons and for action
			// acknowledgement. Neither a sentinel nor a local submission hold is an
			// observed cooldown. Read the authoritative query independently.
			List<string> values = Lua.GetReturnValues(CreateCooldownQuery(spellId, false));
			observation.RequireCurrent();
			if (!TryParseAvailability(values, out double availabilitySeconds) ||
			    availabilitySeconds < 0)
				throw new ObservationUnavailableException("spell-cooldown",
					"Cooldown unavailable for spell " + spellId.ToString(CultureInfo.InvariantCulture) + ".");

			TimeSpan observed = TimeSpan.FromSeconds(availabilitySeconds);
			TrackCooldown(spellId, observed, observation.ObservedAt, observation);
			return observed;
		}

		public static bool IsCurrentSpell(int spellId)
		{
			try
			{
				Memory? memory = ObjectManager.Wow;
				if (memory == null)
					return false;

				uint nodePtr = memory.Read<uint>(PendingSpellListBase);

				while (nodePtr != 0U && (nodePtr & 1U) == 0U)
				{
					if (memory.Read<uint>(nodePtr + 0x20U) == (uint)spellId)
						return true;
					nodePtr = memory.Read<uint>(nodePtr + 0x04U);
				}

				return false;
			}
			catch
			{
				return false;
			}
		}

		public static bool IsCurrentSpell(string spellName)
		{
			WoWSpell? spell = GetSpellByName(spellName);
			return spell != null && IsCurrentSpell(spell.Id);
		}

		public static bool HasSpell(string name)
		{
			return _knownSpells.ContainsKey(name);
		}

		public static bool HasSpell(int id)
		{
			foreach (var spell in _knownSpells.Values)
			{
				if (spell.Id == id)
					return true;
			}
			return false;
		}

		public static bool HasSpell(WoWSpell spell)
		{
			return spell != null && HasSpell(spell.Id);
		}

		public static WoWSpell? GetSpellByName(string name)
		{
			if (_knownSpells.TryGetValue(name, out WoWSpell? spell))
			{
				return spell;
			}
			return null;
		}

		public static bool CanCastSpell(string name)
		{
			if (!HasSpell(name))
			{
				// Only log once per spell to avoid spam
				return false;
			}

			if (GlobalCooldown)
				return false;

			WoWSpell? spell = GetSpellByName(name);
			if (spell == null)
				return false;

			return IsSpellAvailable(spell, 0U, false);
		}

		// HB 4.3.4 compatibility wrappers
		public static bool CanCast(string spellName) => CanCast(spellName, false);

		/// <summary>
		/// HB 4.3.4 overload: CanCast(string, bool checkRange) — uses current target.
		/// </summary>
		public static bool CanCast(string spellName, bool checkRange)
			=> CanCast(spellName, StyxWoW.Me?.CurrentTarget!, checkRange);

		// HB convenience overload: allow calling CanCast with just an int spellId
		public static bool CanCast(int spellId)
		{
			return CanCast(spellId, StyxWoW.Me?.CurrentTarget);
		}

		/// <summary>
		/// HB 4.3.4 overload: CanCast(int, bool checkRange) — uses current target.
		/// </summary>
		public static bool CanCast(int spellId, bool checkRange)
			=> CanCast(spellId, StyxWoW.Me?.CurrentTarget!, checkRange);

		public static bool CanCast(int spellId, WoWUnit target, bool checkRange = true, bool checkMovement = false)
		{
			// Convert spellId to name and use existing CanCast
			WoWSpell? spell = Spells.Values.FirstOrDefault(s => s.Id == spellId);
			if (spell == null)
				return false;
			return CanCast(spell.Name, target, checkRange, checkMovement);
		}

		/// <summary>
		/// HB 4.3.4 SpellManager.cs line 166: CanCast with full validation.
		/// Uses an authoritative, deadline-cached client cooldown query with lag
		/// tolerance and checks IsCasting, movement, range, and power.
		/// </summary>
		public static bool CanCast(string spellName, WoWUnit target, bool checkRange = true, bool checkMovement = false)
		{
			WoWSpell? spell = GetSpellByName(spellName);
			if (spell == null)
				return false;
			return CanCast(spell, target, checkRange, checkMovement);
		}

		/// <summary>
		/// HB 4.3.4 overload: CanCast(WoWSpell) — no target, no range check.
		/// </summary>
		public static bool CanCast(WoWSpell spell)
			=> CanCast(spell, false);

		/// <summary>
		/// HB 4.3.4 overload: CanCast(WoWSpell, bool checkRange) — uses current target.
		/// </summary>
		public static bool CanCast(WoWSpell spell, bool checkRange)
			=> CanCast(spell, StyxWoW.Me?.CurrentTarget!, checkRange);

		public static bool CanCast(WoWSpell spell, WoWUnit target, bool checkRange = true, bool checkMovement = false)
		{
			return CanCast(spell, target, checkRange, checkMovement, true);
		}

		/// <summary>
		/// HB 4.3.4 SpellManager.cs line 166-222: CanCast with accountForLagTolerance.
		/// Preserves the original range/casting semantics while using the WotLK
		/// client API for reliable spell-specific cooldown state.
		/// </summary>
		public static bool CanCast(WoWSpell spell, WoWUnit target, bool checkRange, bool checkMovement, bool accountForLagTolerance)
		{
			if (spell == null)
				return false;

			if (!HasSpell(spell.Name))
				return false;

			LocalPlayer? me = StyxWoW.Me;
			if (me == null)
				return false;

			// Cast time, funnel state and range can depend on current client
			// state. Use one valid observation, not an ID-only cached default.
			if (!spell.TryGetCurrentSpellInfo(out var castTime, out var isFunnel, out var minRange, out var maxRange))
				return false;

			// HB 4.3.4: Range checks
				// The exact local recipient is already at the caster. Tracing from
				// its eye position back to itself is an invalid zero-length native
				// collision query, not a failed line of sight for self support.
				if (checkRange && target != null && !ReferenceEquals(target, me))
			{
				if (!target.InLineOfSpellSight)
					return false;
				if (maxRange != 0f && target.Distance > (double)maxRange)
					return false;
				if (maxRange == 0f && !target.IsWithinMeleeRange)
					return false;
				if (minRange != 0f && target.Distance < (double)minRange)
					return false;
			}

			// HB 4.3.4: Movement check (cast time or funnel spells can't be cast while moving)
			if (checkMovement && (castTime != 0U || isFunnel) && me.IsMoving)
				return false;

				if (me.IsCasting) return false;
				// Network timing is an optional early-queue allowance, not evidence
				// that a spell is ready. An unavailable measurement must not disable
				// every cast. Fall back to the existing strict current cooldown and
				// usability query; zero allowance is not a reported zero latency.
				uint? lag = null;
				if (accountForLagTolerance && me.ChanneledCastingSpellId == 0)
				{
					try
					{
						uint measured = StyxWoW.WoWClient.Latency;
						if (measured <= uint.MaxValue / 2U) lag = measured * 2U;
					}
					catch (ObservationUnavailableException) { }
				}
				if (!ReferenceEquals(StyxWoW.Me, me) || me.IsCasting) return false;
				return IsSpellAvailable(spell, lag.GetValueOrDefault(), lag.HasValue);
		}

		public static bool Cast(string spellName) => Cast(spellName, StyxWoW.Me?.CurrentTarget);

		/// <summary>
		/// HB 4.3.4 line 308: Resolves spell by name, delegates to Cast(WoWSpell, WoWUnit).
		/// No CanCast guard — callers check CanCast separately before calling Cast.
		/// </summary>
		public static bool Cast(string spellName, WoWUnit target)
		{
			WoWSpell? spell = GetSpellByName(spellName);
			if (spell == null)
				return false;
			return Cast(spell, target);
		}

		public static bool Cast(WoWSpell spell)
		{
			if (spell == null)
				return false;
			return Cast(spell, StyxWoW.Me?.CurrentTarget);
		}

		/// <summary>
		/// HB 4.3.4 line 333: Cast spell on target via GUID-based CastSpellById.
		/// No CanCast guard — callers check CanCast separately before calling Cast.
		/// </summary>
		public static bool Cast(WoWSpell spell, WoWUnit target)
		{
			if (spell == null)
				return false;

			ulong castTargetGuid = target?.Guid ?? 0UL;

			// WotLK combo finisher: SPELL_ATTR1_REQ_COMBO_POINTS1 = 0x10 (bit 4 of AttributesEx).
			// Confirmed from live memory: SnD (5171) AttributesEx=0x5000010.
			// BuffSelf passes Me.Guid → sub_72BDB0 @ 0x80D661 calls CanAttack(player, self) = false
			// → silent drop. Remap to CurrentTarget so CanAttack passes.
			if ((spell.AttributesEx & 0x50u) != 0)
			{
				WoWUnit? comboTarget = StyxWoW.Me?.CurrentTarget;
				if (comboTarget != null)
					castTargetGuid = comboTarget.Guid;
			}

			return TryCastSpellById(spell.Id, castTargetGuid);
		}

		/// <summary>Cast a spell by ID on the current target.</summary>
		public static bool Cast(int spellId) => Cast(spellId, StyxWoW.Me?.CurrentTarget);

		/// <summary>Cast a spell by ID on a specific target using GUID-based casting.</summary>
		public static bool Cast(int spellId, WoWUnit target)
		{
			WoWSpell? spell = _knownSpells.Values.FirstOrDefault(s => s.Id == spellId);
			if (spell == null)
				return false;
			return Cast(spell, target);
		}

		#region FEAT-06: CanBuff / Buff / CastRandom / BuffRandom (merged from SpellManagerEx)

		private static readonly Random _spellRandom = new Random(Environment.TickCount);

		/// <summary>Check if we can cast a buff (CanCast + target doesn't already have aura).</summary>
		public static bool CanBuff(string spellName, WoWUnit target = null, bool checkRange = false)
		{
			target ??= StyxWoW.Me;
			if (!CanCast(spellName, target, checkRange))
				return false;
			return !target.HasAura(spellName);
		}

		/// <summary>Check if we can cast a buff by spell ID.</summary>
		public static bool CanBuff(int spellId, WoWUnit target = null, bool checkRange = false)
		{
			WoWSpell? spell = _knownSpells.Values.FirstOrDefault(s => s.Id == spellId);
			if (spell == null) return false;
			return CanBuff(spell.Name, target, checkRange);
		}

		/// <summary>Check if we can cast a buff (WoWSpell overload).</summary>
		public static bool CanBuff(WoWSpell spell, WoWUnit target = null, bool checkRange = false)
		{
			if (spell == null) return false;
			return CanBuff(spell.Name, target, checkRange);
		}

		/// <summary>Cast a buff on a target (cast only if target doesn't already have the aura).</summary>
		public static bool Buff(string spellName, WoWUnit target = null)
		{
			target ??= StyxWoW.Me;
			WoWSpell? spell = GetSpellByName(spellName);
			if (spell == null) return false;
			return Cast(spell, target);
		}

		/// <summary>Cast a buff by spell ID.</summary>
		public static bool Buff(int spellId, WoWUnit target = null)
		{
			target ??= StyxWoW.Me;
			WoWSpell? spell = _knownSpells.Values.FirstOrDefault(s => s.Id == spellId);
			if (spell == null) return false;
			return Cast(spell, target);
		}

		/// <summary>Cast a buff (WoWSpell overload).</summary>
		public static bool Buff(WoWSpell spell, WoWUnit target = null)
		{
			if (spell == null) return false;
			return Cast(spell, target ?? StyxWoW.Me);
		}

		/// <summary>Cast a random castable spell from the list on a target.</summary>
		public static bool CastRandom(IEnumerable<string> spellNames, WoWUnit target = null, bool checkRange = false)
		{
			target ??= StyxWoW.Me?.CurrentTarget;
			var spells = spellNames.Select(n => GetSpellByName(n)).Where(s => s != null).ToList();
			return CastRandom(spells, target, checkRange);
		}

		/// <summary>Cast a random castable spell from the list on a target.</summary>
		public static bool CastRandom(IEnumerable<WoWSpell> spellList, WoWUnit target = null, bool checkRange = false)
		{
			target ??= StyxWoW.Me?.CurrentTarget;
			var list = spellList.Where(s => s != null).ToList();
			while (list.Count > 0)
			{
				int idx = _spellRandom.Next(0, list.Count);
				if (CanCast(list[idx], target, checkRange) && Cast(list[idx], target))
				{
					return true;
				}
				list.RemoveAt(idx);
			}
			return false;
		}

		/// <summary>Buff a random castable spell from the list on a target (skips if aura present).</summary>
		public static bool BuffRandom(IEnumerable<string> spellNames, WoWUnit target = null, bool checkRange = false)
		{
			target ??= StyxWoW.Me;
			var spells = spellNames.Select(n => GetSpellByName(n)).Where(s => s != null).ToList();
			return BuffRandom(spells, target, checkRange);
		}

		/// <summary>Buff a random castable spell from the list on a target (skips if aura present).</summary>
		public static bool BuffRandom(IEnumerable<WoWSpell> spellList, WoWUnit target = null, bool checkRange = false)
		{
			target ??= StyxWoW.Me;
			var list = spellList.Where(s => s != null).ToList();
			while (list.Count > 0)
			{
				int idx = _spellRandom.Next(0, list.Count);
				if (CanBuff(list[idx], target, checkRange) && Buff(list[idx], target))
				{
					return true;
				}
				list.RemoveAt(idx);
			}
			return false;
		}

		// HB 4.3.4: bool-as-2nd-arg overloads (avoids ambiguity with WoWUnit target)
		public static bool CastRandom(IEnumerable<string> spellNames, bool checkRange)
			=> CastRandom(spellNames, StyxWoW.Me?.CurrentTarget, checkRange);

		public static bool CastRandom(IEnumerable<WoWSpell> spells, bool checkRange)
			=> CastRandom(spells, StyxWoW.Me?.CurrentTarget, checkRange);

		public static bool BuffRandom(IEnumerable<string> spellNames, bool checkRange)
			=> BuffRandom(spellNames, StyxWoW.Me?.CurrentTarget, checkRange);

		public static bool BuffRandom(IEnumerable<WoWSpell> spells, bool checkRange)
			=> BuffRandom(spells, StyxWoW.Me?.CurrentTarget, checkRange);

		// HB 4.3.4: CastRandom / BuffRandom with int spell IDs
		public static bool CastRandom(IEnumerable<int> spellIds, WoWUnit target, bool checkRange)
		{
			var spells = spellIds.Select(id => _knownSpells.Values.FirstOrDefault(s => s.Id == id))
			                     .OfType<WoWSpell>();
			return CastRandom(spells, target, checkRange);
		}

		public static bool CastRandom(IEnumerable<int> spellIds, WoWUnit target)
			=> CastRandom(spellIds, target, false);

		public static bool CastRandom(IEnumerable<int> spellIds, bool checkRange)
			=> CastRandom(spellIds, StyxWoW.Me?.CurrentTarget, checkRange);

		public static bool BuffRandom(IEnumerable<int> spellIds, WoWUnit target, bool checkRange)
		{
			var spells = spellIds.Select(id => _knownSpells.Values.FirstOrDefault(s => s.Id == id))
			                     .OfType<WoWSpell>();
			return BuffRandom(spells, target, checkRange);
		}

		public static bool BuffRandom(IEnumerable<int> spellIds, WoWUnit target)
			=> BuffRandom(spellIds, target, false);

		public static bool BuffRandom(IEnumerable<int> spellIds, bool checkRange)
			=> BuffRandom(spellIds, StyxWoW.Me?.CurrentTarget, checkRange);

		#endregion

		public static void CastSpellById(uint spellId)
		{
			CastSpellById((int)spellId);
		}

		public static void CastSpellById(int spellId)
		{
			CastSpellById(spellId, 0UL);
		}

		/// <summary>
		/// Casts a spell by ID on a specific target via native Spell_C::CastSpell.
		/// HB 4.3.4: LegacySpellManager.smethod_1(spellId, 0, targetGuid, 0)
		/// Pushes 8 args onto stack (cdecl, 0x20 cleanup).
		/// </summary>
		public static void CastSpellById(int spellId, ulong targetGuid)
		{
			TryCastSpellById(spellId, targetGuid);
		}

		// A true result records completed local dispatch, not native/server acceptance.
		// An executor exception also cannot establish that no native effect occurred.
		private static bool TryCastSpellById(int spellId, ulong targetGuid)
		{
			StyxWoW.ResetAfk();

			ExecutorRand? executor = ObjectManager.Executor;
			if (executor == null)
			{
				Logging.WriteDebug("[SpellManager] Invalid executor for CastSpellById");
				return false;
			}

			// Split 64-bit GUID into two 32-bit halves (HB 4.3.4: Struct72.smethod_4)
			uint guidLow = (uint)(targetGuid & 0xFFFFFFFF);
			uint guidHigh = (uint)(targetGuid >> 32);

			try
			{
				var observation = CaptureSpellObservation();
				PrepareCooldownContext(observation);
				Logging.WriteDebug("Spell_C::CastSpell({0}, 0, 0x{1:X}, 0)", spellId, targetGuid);
				lock (executor.AssemblyLock)
				{
					observation.RequireCurrent();
					executor.Clear();
					// HB 4.3.4 exact push order (8 args, right-to-left):
					executor.AddLine("push 0");                // arg8: unk3
					executor.AddLine("push 0");                // arg7: unk2
					executor.AddLine("push 0");                // arg6: unk1
					executor.AddLine("push 0");                // arg5: targetFlags
					executor.AddLine("push {0}", guidHigh);    // arg4: GUID high 32 bits
					executor.AddLine("push {0}", guidLow);     // arg3: GUID low 32 bits
					executor.AddLine("push 0");                // arg2: itemIndex (0 = no item)
					executor.AddLine("push {0}", spellId);     // arg1: spellId
					executor.AddLine("call {0}", (uint)Patchables.GlobalOffsets.Spell_C__CastSpell);
					executor.AddLine("add esp, 0x20");         // cdecl cleanup: 8 * 4 = 32 = 0x20
					executor.AddLine("retn");
					observation.RequireCurrent();
					if (!RecoveryActions.BeforeSpellSubmission(spellId, targetGuid)) return false;
					executor.Execute();
				}

				// The client cooldown can arrive a frame after the native call. Hold this
				// spell briefly in the dispatch owner's context. A first numeric read
				// must not erase the hold; a replacement run must not inherit it.
				lock (_cooldownSync)
				{
					observation.RequireCurrent();
					if (_cooldownContext == null || !_cooldownContext.SameOwner(observation))
						throw new ObservationUnavailableException("spell-cooldown", "The cast submission owner was replaced.");
					// An independent query may have completed during native dispatch.
					// Keep that newer publication and its observed cooldowns intact.
					TrackDeadline(_castVerificationUntilTicks, spellId,
						TimeSpan.FromMilliseconds(CastAttemptVerificationDelayMs), Environment.TickCount64, _cooldownContext);
				}
				return true;
			}
			catch (Exception ex)
			{
				RecoveryActions.RethrowControlFlow(ex);
				Logging.WriteException(ex);
				return false;
			}
		}

		public static void CastSpellById(int spellId, WoWUnit target)
		{
			CastSpellById(spellId, target.Guid);
		}

		public static bool CastSpell(string name)
		{
			return CastSpell(name, 0UL, true);
		}

		public static bool CastableSpell(WoWSpell spell)
		{
			try
			{
				if (spell.Id == 0)
					return false;

				LocalPlayer? me = StyxWoW.Me;
				if (me == null)
					return false;

				if (me.IsCasting)
					return false;

				if (!_knownSpells.ContainsKey(spell.Name))
					return false;

				// Check if spell is on cooldown
				if (spell.Cooldown)
					return false;

				// Check aura effects
				for (int i = 0; i < 3; i++)
				{
					var effect = spell.GetSpellEffect(i);
					if (effect == null) continue;

					WoWApplyAuraType auraType = effect.AuraType;
					if (auraType == WoWApplyAuraType.ModStat ||
						auraType == WoWApplyAuraType.ModStealth ||
						auraType == WoWApplyAuraType.ModSpeedAlways ||
						auraType == WoWApplyAuraType.ModSpeedFlight ||
						auraType == WoWApplyAuraType.PeriodicHeal ||
						auraType == WoWApplyAuraType.ModResistance ||
						auraType == WoWApplyAuraType.FeignDeath)
					{
						return true;
					}
				}

				// Check power type and cost
				WoWPowerType powerType = spell.PowerType;
				if (powerType == WoWPowerType.Runes)
					return true;

				int powerCost = spell.PowerCost;
				if (powerCost == 0)
					return true;

				// Check if moving (for cast time spells)
				if (spell.CastTime != 0U && me.IsMoving)
					return false;

				// Check available power
				if (me.GetCurrentPower(powerType) >= powerCost)
					return true;

				return false;
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
				return false;
			}
		}

		public static void StopCasting()
		{
			try
			{
				LocalPlayer? me = StyxWoW.Me;
				if (me != null && me.IsCasting)
				{
					Lua.DoString("SpellStopCasting()");
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
		}

		public static bool CastSpell(string name, WoWUnit target)
		{
			return CastSpell(name, target.Guid, true);
		}

		public static bool CastSpell(string name, ulong targetGuid, bool returnImmediately)
		{
			if (!CanCastSpell(name))
			{
				Logging.WriteDebug("[SpellManager] Cannot cast spell: {0}", name);
				return false;
			}

			WoWSpell? spell = GetSpellByName(name);
			if (spell == null)
				return false;

			if (!TryCastSpellById(spell.Id, targetGuid))
				return false;

			if (!returnImmediately)
			{
				// Wait for cast to complete
				StyxWoW.Sleep((int)spell.CastTime);

				// Wait for GCD
				while (GlobalCooldown)
				{
					StyxWoW.Sleep(10);
				}
			}

			Logging.WriteDebug("[SpellManager] Cast spell: {0}", name);
			return true;
		}

		// Struct for Spell_C__HandleTerrainClick
		[Flags]
		public enum MouseButton : uint
		{
			None = 0U,
			Left = 1U,
			Middle = 2U,
			Right = 4U,
			XButton1 = 8U,
			XButton2 = 16U
		}

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		private struct TerrainClickInfo
		{
			public ulong TargetGuid;
			public WoWPoint Location;
			[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.U4)]
			public MouseButton Button;
		}
		
		// Original 3.3.5a contract: 8438592 decimal is 0x80C340, not 0x80B740.
		private const uint Spell_C__HandleTerrainClick = 0x80C340; // 8438592U

		public static bool ClickRemoteLocation(WoWPoint location)
		{
			StyxWoW.ResetAfk();
			
			ExecutorRand? executor = ObjectManager.Executor;
			if (executor == null)
			{
				Logging.WriteDebug("[SpellManager] Invalid executor for ClickRemoteLocation");
				return false;
			}

			var click = new TerrainClickInfo
			{
				Location = location,
				TargetGuid = 0UL,
				Button = MouseButton.Left
			};

			try
			{
				lock (executor.AssemblyLock)
				{
					// Allocate memory for the click struct
					uint structPtr = executor.Memory.AllocateMemory(System.Runtime.InteropServices.Marshal.SizeOf(click));
					if (structPtr == 0U)
					{
						Logging.WriteDebug("[SpellManager] Could not allocate memory for ClickRemoteLocation");
						return false;
					}

					try
					{
						// Write struct to allocated memory
						executor.Memory.Write(structPtr, click);
						
						// Call Spell_C__HandleTerrainClick
						executor.Clear();
						executor.AddLine("push {0}", structPtr);
						executor.AddLine("call {0}", Spell_C__HandleTerrainClick);
						executor.AddLine("add esp, 4");  // Clean up stack (cdecl)
						executor.AddLine("retn");
						executor.Execute();
						
						// Build12340 defines only AL on both return paths.
						// Undefined high EAX bits must not turn refusal into success.
						byte result;
						using (StyxWoW.Memory.TemporaryCacheState(false))
						{
							result = executor.Memory.Read<byte>(executor.ReturnPointer);
						}
						return result != 0;
					}
					finally
					{
						executor.Memory.FreeMemory(structPtr);
					}
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
				return false;
			}
		}

		#region LuaEvent Auto-Refresh (ported from HB 4.3.4 smethod_0/1/2/3)

		/// <summary>
		/// TreeRoot.OnBotStart already calls this on every bot start. Keep that
		/// direct call as the sole refresh owner; appending another BotStart
		/// callback here grows work on each restart and misses first-run binding.
		/// </summary>
		internal static void Initialize()
		{
			ResetCooldownObservations();
			_knownSpells.Clear();
			RefreshSpellsAndBindLuaEvents();
			Logging.WriteDebug("[SpellManager] Initialize — refreshed and bound owned Lua events");
		}

		/// <summary>
		/// Explicit engine teardown releases only this owner's Lua subscriptions
		/// and clears the spellbook. Repeated teardown is safe.
		/// </summary>
		internal static void Shutdown()
		{
			ResetCooldownObservations();
			_knownSpells.Clear();
			_lastKnownSpellCount = 0;
			Lua.Events.DetachEvent("LEARNED_SPELL_IN_TAB", new LuaEventHandlerDelegate(OnSpellBookChanged));
			Lua.Events.DetachEvent("ACTIVE_TALENT_GROUP_CHANGED", new LuaEventHandlerDelegate(OnSpellBookChanged));
			Logging.WriteDebug("[SpellManager] Shutdown — released owned Lua events");
		}

		/// <summary>
		/// Rebuild once per direct initialization and bind exactly one owned
		/// handler per Lua event without removing any other subscriber.
		/// </summary>
		private static void RefreshSpellsAndBindLuaEvents()
		{
			_lastKnownSpellCount = 0;
			Refresh();

			// Idempotent: detach first to avoid duplicate subscriptions (HB 4.3.4 pattern)
			Lua.Events.DetachEvent("LEARNED_SPELL_IN_TAB", new LuaEventHandlerDelegate(OnSpellBookChanged));
			Lua.Events.DetachEvent("ACTIVE_TALENT_GROUP_CHANGED", new LuaEventHandlerDelegate(OnSpellBookChanged));
			Lua.Events.AttachEvent("LEARNED_SPELL_IN_TAB", new LuaEventHandlerDelegate(OnSpellBookChanged));
			Lua.Events.AttachEvent("ACTIVE_TALENT_GROUP_CHANGED", new LuaEventHandlerDelegate(OnSpellBookChanged));

			Logging.WriteDebug("[SpellManager] Subscribed to LEARNED_SPELL_IN_TAB, ACTIVE_TALENT_GROUP_CHANGED");
		}

		/// <summary>
		/// HB 4.3.4 smethod_3: Lua event handler. Forces a full spellbook rebuild
		/// when the player learns a new spell or switches talent spec.
		/// </summary>
		private static void OnSpellBookChanged(object sender, LuaEventArgs e)
		{
			ResetCooldownObservations();
			Logging.Write("[SpellManager] Spellbook change detected ({0}) \u2014 rebuilding", e.EventName);
			_lastKnownSpellCount = 0;
			Refresh();
		}

		#endregion

		#region Native Methods

		[DllImport("kernel32.dll")]
		private static extern bool QueryPerformanceFrequency(out long frequency);

		[DllImport("kernel32.dll")]
		private static extern bool QueryPerformanceCounter(out long counter);

		#endregion
	}
}
