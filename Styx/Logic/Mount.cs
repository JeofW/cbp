using System;
using System.Collections.Generic;
using System.Threading;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

namespace Styx.Logic
{
	public static class Mount
	{
		private static readonly WaitTimer _mountTimer = WaitTimer.TenSeconds;
		private static readonly WaitTimer _combatTimer = WaitTimer.TenSeconds;
		private static readonly List<WoWPoint> _cantMountSpots = new List<WoWPoint>();
		private static CanMountDelegate? _defaultCanMount;
		private static bool _wasMounted;
		private static LocationRetriever? _currentDestinationRetriever;
			private static readonly MountedTravelProgress _mountedTravelProgress = new();
			private const string GroundDismountReceipt = "cb-ground-dismount-submitted";
			private const string GroundDismountPendingReceipt = "cb-ground-dismount-pending";
			private const int GroundDismountLeaseSeconds = 12;

		/// <summary>
		/// Fired when the player mounts up (HB 4.3.4 compatibility).
		/// </summary>
		public static event EventHandler<MountUpEventArgs>? OnMountUp;

		/// <summary>
		/// Fired when the player dismounts (HB 4.3.4 compatibility).
		/// </summary>
		public static event EventHandler<EventArgs>? OnDismount;

		private static LocalPlayer? Me => ObjectManager.Me;

		static Mount()
		{
			BotEvents.Player.OnMobKilled += OnMobKilled;
			BotEvents.OnBotStop += _ => _mountedTravelProgress.Reset();
		}

		private static void OnMobKilled(BotEvents.Player.MobKilledEventArgs args)
		{
			_combatTimer.Reset();
		}

	/// <summary>
	/// True when both the post-combat and post-mount cooldown timers are ready.
	/// Used by Flightor.MountHelper.CanMount so it respects the same cooldowns as
	/// Mount.CanMount() without routing through the LevelBot mount path.
	/// </summary>
	internal static bool AreMountTimersReady => _combatTimer.IsFinished && _mountTimer.IsFinished;

	/// <summary>
	/// Resets the mount timer after a Flightor-initiated mount attempt, preventing
	/// immediate retry spam when the mount is cancelled (e.g. by water or GCD).
	/// </summary>
	internal static void ResetMountTimer() => _mountTimer.Reset();

	public static void Dismount() => Dismount(string.Empty);

		public static void ClearShapeshift()
		{
			LocalPlayer? me = Me;
			if (me == null) return;
			ulong guid = me.Guid;
			ShapeshiftForm shapeshift = me.Shapeshift;
			bool flight = shapeshift == ShapeshiftForm.FlightForm || shapeshift == ShapeshiftForm.EpicFlightForm;
			bool CanClear() => guid != 0 && ReferenceEquals(Me, me) && me.Guid == guid
				&& me.IsValid && me.IsAlive && me.Shapeshift == shapeshift
				&& (!flight || CanRemoveMount(me, guid))
				&& ReferenceEquals(Me, me) && me.Guid == guid;

			if (shapeshift == ShapeshiftForm.Normal || !CanClear()) return;
			Logging.WriteDebug("Canceling Shapeshift form: {0}", shapeshift);
			if (CanClear()) Lua.DoString("CancelShapeshiftForm()");
		}

        public static void Dismount(string reason)
        {
            // The legacy void API has no result channel. An unavailable
            // observation must defer removal rather than tear down its caller's
            // quest/POI. The owned internal API keeps its strict exception
            // contract, and real cancellation/process/executor loss propagates.
            try { TryDismountOwned(reason, () => true, null); }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                if (error is not ObservationUnavailableException) throw;
                RecoveryActions.ReportDeferral(error, "Legacy mount removal");
            }
        }

        internal static bool TryDismountOwned(string reason, Func<bool> admitted, System.Action? submitted)
        {
                LocalPlayer? me = Me;
                if (me == null || !admitted()) return false;
                Func<bool> ownerCurrent = WorldQueryObservation.CaptureLocalOwner(me);

					ulong guid = me.Guid;
					var initialState = WorldQueryObservation.ReadGroundUnitState(me);
					uint mountDisplayId = initialState.MountDisplayId;
					ShapeshiftForm shapeshift = initialState.Form;
					bool flight = shapeshift == ShapeshiftForm.FlightForm || shapeshift == ShapeshiftForm.EpicFlightForm;
                bool CanDismount()
                {
                    if (!ownerCurrent() || !initialState.Mounted || !admitted() || !CanRemoveMount(me, guid, out var state)) return false;
                    return state.Mounted && state.MountDisplayId == mountDisplayId && state.Form == shapeshift
                        && ReferenceEquals(Me, me) && me.Guid == guid && admitted()
                        && CanRemoveMount(me, guid, out state) && state.Mounted
                        && state.MountDisplayId == mountDisplayId && state.Form == shapeshift && ownerCurrent();
                }
				if (CanDismount())
				{
				if (string.IsNullOrEmpty(reason))
					Logging.WriteDebug("Stop and dismount.");
				else
					Logging.WriteDebug("Stop and dismount. Reason: {0}", reason);

						// A caller's distance or elapsed descent wait does not prove landing.
						// Reobserve this actor around setup before removing its mount/form.
                    if (!CanDismount()) return false;
                    WoWMovement.MoveStop();
                    if (!CanDismount()) return false;

						// The complete Lua transport owns its request/output bytes, verifies
						// the current process/executor/Lua state at native entry, propagates
						// cancellation/fatal ownership loss, and reports pcall failure as
						// UNKNOWN. The marker proves local submission only; observed mount
						// removal remains the later acknowledgement.
							string owner = GroundDismountDispatchContext.Owner ?? Guid.NewGuid().ToString("N");
							if (!Guid.TryParseExact(owner, "N", out Guid ownerGuid) || ownerGuid == Guid.Empty)
								throw new ObservationUnavailableException("mount-dismount", "The dismount actor/session owner is unavailable.");
							string action = flight ? "CancelShapeshiftForm()" : "Dismount()";
                                var receipt = Lua.GetObservedReturnValues(BuildGroundDismountLua(action, owner), CanDismount);
                                if (!ownerCurrent()) throw new ObservationUnavailableException("mount-dismount", "The dismount result belongs to a replaced actor/session.");
							if (receipt.Count != 1 || receipt[0] != GroundDismountReceipt && receipt[0] != GroundDismountPendingReceipt)
								throw new ObservationUnavailableException("mount-dismount", "The original-client dismount submission receipt is unavailable.");

							// The Lua envelope installs the same actor/session token immediately
							// before the action. A post-entry UNKNOWN therefore survives target/
							// POI churn on the client even if this reply cannot be read. Either
							// exact receipt arms the managed pending lease; neither is effect ack.
							submitted?.Invoke();

							// Preserve the existing dispatch event; it is not server acknowledgement.
                    if (receipt[0] == GroundDismountReceipt) RaiseOnDismount(reason);
                    return true;
                }
                return false;
				}

			private static string BuildGroundDismountLua(string action, string owner)
			{
				return "local owner='" + owner + "'; local ttl="
					+ GroundDismountLeaseSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + @";
local now=GetTime()
if type(now)~='number' or now~=now or now<0 or now>=math.huge or now+ttl<=now then error('ground dismount clock unavailable') end
local lease=_G.CopilotBuddy_GroundDismountLease
if lease~=nil then
 if type(lease)~='table' or lease.schema~='cb-ground-dismount-v1'
  or type(lease.owner)~='string' or not string.match(lease.owner,'^[0-9a-f]+$') or #lease.owner~=32
  or type(lease.startedAt)~='number' or lease.startedAt~=lease.startedAt or lease.startedAt<0 or lease.startedAt>=math.huge
  or type(lease.untilAt)~='number' or lease.untilAt~=lease.untilAt or lease.untilAt>=math.huge
  or lease.untilAt<=lease.startedAt or lease.untilAt-lease.startedAt>ttl then
  error('ground dismount lease unavailable or foreign')
 end
 if lease.owner==owner then
  if now<lease.startedAt then error('ground dismount clock moved backward') end
  if now<lease.untilAt then return '" + GroundDismountPendingReceipt + @"' end
 end
end
_G.CopilotBuddy_GroundDismountLease={schema='cb-ground-dismount-v1',owner=owner,startedAt=now,untilAt=now+ttl}
" + action + @"
return '" + GroundDismountReceipt + "'";
			}

			private static bool CanRemoveMount(LocalPlayer player, ulong guid)
				=> CanRemoveMount(player, guid, out _);

			private static bool CanRemoveMount(LocalPlayer player, ulong guid, out WorldQueryObservation.GroundUnitState state)
			{
				state = default;
				if (guid == 0 || !ReferenceEquals(Me, player) || player.Guid != guid || !player.IsValid || !player.IsAlive)
					return false;
				if (!player.TryGetMovementState(out uint flags, out ulong transport)) return false;
				state = WorldQueryObservation.ReadGroundUnitState(player);
                    return state.Mounted && !state.OnTaxi && !state.Rooted && !state.Stunned
                        && transport == 0 && (flags & 0x02003000u) == 0
                        && player.TryGetMovementState(out uint finalFlags, out ulong finalTransport)
                        && finalFlags == flags && finalTransport == transport
                        && ReferenceEquals(Me, player) && player.Guid == guid && player.IsValid && player.IsAlive;
			}

		/// <summary>
		/// HB 6.2.3 Mount.smethod_1: Safely raises OnDismount event,
		/// catching exceptions from individual subscribers.
		/// </summary>
		internal static void RaiseOnDismount(string? reason)
		{
			reason ??= string.Empty;
			EventHandler<EventArgs>? handler = OnDismount;
			if (handler == null)
				return;

			foreach (Delegate d in handler.GetInvocationList())
			{
				try
				{
					d.DynamicInvoke(reason, EventArgs.Empty);
				}
                catch (Exception ex)
                {
                    RecoveryActions.RethrowControlFlow(ex);
                    Logging.WriteException(ex);
				}
			}
		}

		private static readonly Random _random = new Random();

		/// <summary>
		/// Auto-detects and sets mount name if FindMountAutomatically is enabled.
		/// Ported from HB 4.3.4.
		/// </summary>
		public static void AutoDetectMount()
		{
			if (!CharacterSettings.Instance.UseMount || !CharacterSettings.Instance.FindMountAutomatically)
				return;

			if (CharacterSettings.Instance.UseRandomMount)
			{
				// Random mount selection
				var groundMounts = MountHelper.GroundMounts;
				if (groundMounts != null && groundMounts.Count > 0)
				{
					var mount = groundMounts[_random.Next(0, groundMounts.Count)];
					CharacterSettings.Instance.MountName = mount.CreatureSpellId.ToString();
				}

				var flyingMounts = MountHelper.FlyingMounts;
				if (flyingMounts != null && flyingMounts.Count > 0)
				{
					var mount = flyingMounts[_random.Next(0, flyingMounts.Count)];
					CharacterSettings.Instance.FlyingMountName = mount.CreatureSpellId.ToString();
				}
			}
			else
			{
				// Use first available mount if not set
				string mountName = CharacterSettings.Instance.MountName;
				if (string.IsNullOrEmpty(mountName) || mountName == "Mount Name Here" || mountName.Contains("Automatically detected"))
				{
					var groundMounts = MountHelper.GroundMounts;
					if (groundMounts != null && groundMounts.Count > 0)
					{
						var mount = groundMounts[0];
						CharacterSettings.Instance.MountName = mount.CreatureSpellId.ToString();
						Logging.WriteDebug("Auto-detected ground mount: {0}", mount.Name);
					}
				}

				string flyingMount = CharacterSettings.Instance.FlyingMountName;
				if (string.IsNullOrEmpty(flyingMount) || flyingMount.Contains("Automatically detected"))
				{
					var flyingMounts = MountHelper.FlyingMounts;
					if (flyingMounts != null && flyingMounts.Count > 0)
					{
						var mount = flyingMounts[0];
						CharacterSettings.Instance.FlyingMountName = mount.CreatureSpellId.ToString();
						Logging.WriteDebug("Auto-detected flying mount: {0}", mount.Name);
					}
				}
			}
		}

		public static void MountUp()
		{
			if (_defaultCanMount == null)
			{
				_defaultCanMount = DefaultCanMount;
			}
			MountUp(_defaultCanMount);
		}

		private static bool DefaultCanMount()
		{
			return true;
		}

		/// <summary>
		/// Mounts up with a custom can-mount check and destination (HB 4.3.4).
		/// Returns true if mount was attempted.
		/// </summary>
		public static bool MountUp(CanMountDelegate extra, LocationRetriever travelingTo)
		{
			_currentDestinationRetriever = travelingTo;
			return MountUp(extra);
		}

		[Obsolete("Use MountUp(CanMountDelegate, LocationRetriever) instead.")]
			public static bool MountUp(CanMountDelegate extra)
			{
				LocalPlayer? me = Me;
				ulong guid = me?.Guid ?? 0;
				bool SameActor() => me != null && guid != 0 && ReferenceEquals(Me, me) && me.Guid == guid
					&& me.IsValid && me.IsAlive && !me.IsGhost;
				if (!SameActor() || !extra() || !SameActor())
					return false;

			if (!LevelbotSettings.Instance.UseMount)
				return false;

				// Auto-detect mount if enabled
				AutoDetectMount();
				if (!SameActor())
					return false;

			if (me.Mounted || me.Dead || me.IsGhost)
				return false;

			// Ghost Wolf (Shaman) / Travel Form (Druid): fallback when regular mounts can't be used.
			// Both spells are outdoors-only in WotLK 3.3.5a (Wowhead verified).
			// In combat → Travel Form only (instant). Ghost Wolf is 2s cast, useless in combat.
			// No ground mounts yet → Ghost Wolf or Travel Form out-of-combat.
			// Already in a speed form → skip (nothing to do).
			bool regularMountBlocked = !me.IsOutdoors || me.Combat
				|| (MountHelper.GroundMounts?.Count ?? 0) == 0;
			if (regularMountBlocked)
			{
				if (me.HasAura("Ghost Wolf") || me.HasAura("Travel Form"))
					return false;
				return TryUseShapeshiftSpeedBuff(me);
			}

			// --- Regular mount path ---
			if (me.Level < 20)
				return false;

            //bool canFly = me.MovementInfo.CanFly;
			// Choose the right mount for this zone up front so the guard and log are accurate.
			// Flightor.CanFly checks IsFlyableArea() + riding skill — NOT the raw movement flag
			// (me.MovementInfo.CanFly is only set while airborne, so it was always false on the ground).
			bool canFly = Flightor.CanFly;
			string flyingMountName = CharacterSettings.Instance.FlyingMountName;
			string effectiveMountName = (canFly && !string.IsNullOrEmpty(flyingMountName))
				? flyingMountName
				: LevelbotSettings.Instance.MountName;

			if (string.IsNullOrEmpty(effectiveMountName))
				return false;

				bool CanContinue()
				{
					if (!SameActor() || !extra() || !SameActor() || me.Mounted || !LevelbotSettings.Instance.UseMount || !CanMount()) return false;
					bool currentCanFly = Flightor.CanFly;
					string currentFlyingName = CharacterSettings.Instance.FlyingMountName;
					string currentName = currentCanFly && !string.IsNullOrEmpty(currentFlyingName)
						? currentFlyingName : LevelbotSettings.Instance.MountName;
					return currentCanFly == canFly && string.Equals(currentName, effectiveMountName, StringComparison.Ordinal) && SameActor();
				}
				if (!CanContinue())
					return false;

				WoWPoint destination = _currentDestinationRetriever?.Invoke() ?? WoWPoint.Empty;
				if (!CanContinue() || !AllowMountAttempt(canFly, effectiveMountName, destination))
			{
				Logging.WriteDebug("Mount-up request cancelled before casting");
				return false;
			}

				if (!CanContinue()) return false;
				WoWMovement.MoveStop();
				if (!CanContinue()) return false;
				Logging.Write("Mounting: {0}{1}", effectiveMountName, canFly ? " [flying]" : "");
				if (!CanContinue()) return false;
				StyxWoW.Sleep(200);

				if (!CanContinue() || me.IsMoving || !DoMount(me, guid, effectiveMountName, CanContinue) || !SameActor()) return false;
				_mountTimer.Reset();
			return true;
		}

		internal static bool AllowMountAttempt(bool isFlying, string mountName, WoWPoint destination)
		{
			var args = new MountUpEventArgs(isFlying, mountName ?? string.Empty)
			{
				Destination = destination
			};
			EventHandler<MountUpEventArgs>? handler = OnMountUp;
			if (handler == null)
				return true;

			foreach (Delegate subscriber in handler.GetInvocationList())
			{
				try
				{
					subscriber.DynamicInvoke(null, args);
				}
				catch (Exception ex)
				{
					RecoveryActions.RethrowControlFlow(ex);
					Logging.WriteException(ex);
				}
			}
			return !args.Cancel;
		}

			private static bool DoMount(LocalPlayer me, ulong guid, string mountName, Func<bool> canContinue)
			{
				bool SameActor() => me != null && guid != 0 && ReferenceEquals(Me, me) && me.Guid == guid
					&& me.IsValid && me.IsAlive && !me.IsGhost;
				if (!SameActor() || !canContinue() || string.IsNullOrEmpty(mountName)) return false;

				// Resolve the selected name/ID unchanged. A different companion is not
				// an alias merely because the character belongs to a particular race.
				string lastError = me.LastRedErrorMessage;
				int index = GetMountIndex(mountName);
				if (index <= 0 || !canContinue() || me.IsMoving || !SameActor()) return false;
				Lua.DoString(string.Format("CallCompanion('MOUNT', {0})", index));

			int startTime = Environment.TickCount;

				while (SameActor() && !me.Mounted && Environment.TickCount - startTime < 6500)
			{
				if (me.Combat)
					break;

				if (lastError != "You can't mount here." && me.LastRedErrorMessage == "You can't mount here.")
				{
					AddCantMountSpot(me.Location);
					break;
				}

				StyxWoW.Sleep(250);
			}

			// Mount succeeded — any stale cant-mount spots near this location are now invalid.
			// (e.g. spots recorded during a previous combat pass at this exact location)
				if (!SameActor()) return false;
				if (me.Mounted)
					RemoveCantMountSpotsNear(me.Location, 10f);
				return SameActor(); // local dispatch attempted; timeout is not mount acknowledgement
		}

		/// <summary>
		/// Attempts to use Ghost Wolf (Shaman) or Travel Form (Druid) as a speed buff
		/// when regular mounts can't be used (in combat, or no ground mounts yet).
		/// WotLK 3.3.5a (Wowhead verified):
		/// - Both Ghost Wolf and Travel Form have the "Can only be used outdoors" flag.
		///   Neither works indoors — returns false immediately.
		/// - Ghost Wolf: 2s cast, no explicit combat ban but interrupted by damage in combat.
		/// - Travel Form: instant cast, usable in combat (no cast to interrupt).
		/// </summary>
			private static bool TryUseShapeshiftSpeedBuff(LocalPlayer me)
			{
				ulong guid = me?.Guid ?? 0;
				bool SameActor() => me != null && guid != 0 && ReferenceEquals(Me, me) && me.Guid == guid
					&& me.IsValid && me.IsAlive && !me.IsGhost && !me.Mounted && me.IsOutdoors
					&& LevelbotSettings.Instance.UseMount;
				// Both spells are outdoors-only in WotLK 3.3.5a.
				if (!SameActor())
				return false;

			// In combat: Travel Form only (instant, won't be interrupted).
			// Ghost Wolf is a 2s cast — interrupted by combat damage, not viable.
			if (me.Combat)
			{
				if (SpellManager.HasSpell("Travel Form"))
				{
					Logging.Write("Mounting: Using Travel Form since we are in combat.");
						return SameActor() && SpellManager.HasSpell("Travel Form") && !me.HasAura("Travel Form")
							&& SpellManager.Cast("Travel Form");
				}
				return false;
			}

			// Out of combat, no ground mounts — use class speed buff as fallback
			if (SpellManager.HasSpell("Ghost Wolf"))
			{
				Logging.Write("Mounting: Using Ghost Wolf since we don't have any mounts yet.");
					return SameActor() && !me.Combat && SpellManager.HasSpell("Ghost Wolf") && !me.HasAura("Ghost Wolf")
						&& SpellManager.Cast("Ghost Wolf");
			}
			if (SpellManager.HasSpell("Travel Form"))
			{
				Logging.Write("Mounting: Using Travel Form since we don't have any mounts yet.");
					return SameActor() && SpellManager.HasSpell("Travel Form") && !me.HasAura("Travel Form")
						&& SpellManager.Cast("Travel Form");
			}
			return false;
		}

		private static int GetMountIndex(string mountName)
		{
			// Use Lua to find mount index
			string luaCode = string.Format(@"
				local mountName = string.lower('{0}')
				for i = 1, GetNumCompanions('MOUNT') do
					local _, name, id = GetCompanionInfo('MOUNT', i)
					if string.lower(name) == mountName or tostring(id) == mountName then
						return i
					end
				end
				return 0
			", mountName.Replace("'", "\\'"));

			string result = Lua.GetReturnVal<string>(luaCode, 0);
			if (int.TryParse(result, out int index))
			{
				return index;
			}
			return 0;
		}

		public static bool CanMount()
		{
			LocalPlayer? me = Me;
			ulong guid = me?.Guid ?? 0;
			bool SameActor() => me != null && guid != 0 && ReferenceEquals(Me, me) && me.Guid == guid
				&& me.IsValid && me.IsAlive && !me.IsGhost && me.Level >= 20
				&& me.IsOutdoors && !me.IsSwimming && !me.Combat;
			if (!SameActor())
				return false;

			// Check if player can use mounts at all
			// WotLK: Ground mounts at level 20 (except Paladin/Warlock at 20)
			int requiredLevel = 20;
			if (me.Level < requiredLevel)
				return false;

			// Check if player has any mounts available
			if (MountHelper.NumMounts <= 0)
				return false;

			if (!_combatTimer.IsFinished)
				return false;

			if (!_mountTimer.IsFinished)
				return false;

			if (me.Dead || me.IsGhost)
				return false;

			WoWPoint location = me.Location;

			// Check if we're in a known "can't mount" spot
			foreach (WoWPoint spot in _cantMountSpots)
			{
				if (location.Distance(spot) < 10f)
					return false;
			}

			// Transient states: combat, swimming, indoors — don't permanently blacklist the location.
			// Cant-mount spots are only for permanent geometry (low ceiling), not transient conditions.
			if (!me.IsOutdoors || me.IsSwimming || me.Combat)
				return false;

			// HB 4.3.4 ceiling raycast — permanent geometry: low ceiling blocks mount.
			float boundingHeight = me.BoundingHeight;
			WoWPoint headPos = location + new WoWPoint(0f, 0f, boundingHeight);
			WoWPoint aboveHead = headPos + new WoWPoint(0f, 0f, boundingHeight / 2f);
			if (GameWorld.TraceLine(headPos, aboveHead, GameWorld.CGWorldFrameHitFlags.HitTestLOS))
			{
				if (SameActor()) AddCantMountSpot(location);
				return false;
			}

			return SameActor();
		}

		public static bool IsOutdoors
		{
			get
			{
				LocalPlayer? me = Me;
				return me?.IsOutdoors ?? false;
			}
		}

		public static void AddCantMountSpot(WoWPoint location)
		{
			if (!_cantMountSpots.Contains(location))
			{
				_cantMountSpots.Add(location);
				Logging.Write(System.Drawing.Color.Red, "Blacklisted mount spot at: {0}", location);
			}
		}

		/// <summary>
		/// Returns true if <paramref name="location"/> is within 10y of a known can't-mount spot.
		/// Ported from HB 6.2.3 Mount.smethod_6.
		/// </summary>
		internal static bool IsInCantMountSpot(WoWPoint location)
		{
			return _cantMountSpots.Any(spot => spot.Distance(location) < 10f);
		}

		public static void ClearCantMountSpots()
		{
			_cantMountSpots.Clear();
		}

		/// <summary>
		/// Removes all cant-mount spots within <paramref name="radius"/> yards of <paramref name="center"/>.
		/// Call after a successful mount or harvest to clean up stale entries.
		/// </summary>
		public static void RemoveCantMountSpotsNear(WoWPoint center, float radius)
		{
			_cantMountSpots.RemoveAll(spot => spot.Distance(center) < radius);
		}

		[Obsolete("StateMount(LocationRetriever) should be used.")]
		public static void StateMount()
		{
			StateMount(static () => WoWPoint.Empty);
		}

		public static void StateMount(LocationRetriever travelingTo)
		{
			if (!LevelbotSettings.Instance.UseMount || Me?.Mounted == true || !CanMount())
				return;

			MountUp(travelingTo);
		}

			public static void MountUp(LocationRetriever travelingTo)
			{
				ArgumentNullException.ThrowIfNull(travelingTo);
				_currentDestinationRetriever = travelingTo;
				LocalPlayer? actor = Me;
				ulong guid = actor?.Guid ?? 0;
				WoWPoint destination = travelingTo();
				bool OwnsDestination()
				{
					if (actor == null || guid == 0 || !ReferenceEquals(Me, actor) || actor.Guid != guid
						|| !ReferenceEquals(_currentDestinationRetriever, travelingTo)) return false;
					WoWPoint current = travelingTo();
					return current.Equals(destination) && ReferenceEquals(Me, actor) && actor.Guid == guid
						&& ReferenceEquals(_currentDestinationRetriever, travelingTo);
				}
				MountUp(() =>
				{
					if (!OwnsDestination()) return false;
					WoWUnit? firstUnit = Targeting.Instance.FirstUnit;
					if (firstUnit != null && firstUnit.Distance < MountDistance)
						return false;

					// Destination-free explicit mounting retains its legacy contract.
					// Ordinary travel must still amortize the selected mount's setup.
					bool worthwhile = destination.Equals(WoWPoint.Empty) || actor!.Mounted || ShouldMount(destination);
					return worthwhile && OwnsDestination();
				});
		}

		public static bool ShouldMount(WoWPoint travelingTo)
		{
			LocalPlayer? me = Me;
			if (me == null)
				return false;

			if (me.Mounted)
				return false;

				return TravelTimeEstimator.ShouldMount(travelingTo, MountDistance);
		}

		/// <summary>
		/// Check if we should dismount for a given destination.
		/// Ported from HB 4.3.4.
		/// </summary>
		public static bool ShouldDismount(WoWPoint travelingTo)
		{
			LocalPlayer? me = Me;
			if (me == null || !me.Mounted) { _mountedTravelProgress.Reset(); return false; }
			ulong guid = me.Guid;
			uint map = me.MapId;
			object memory = ObjectManager.Wow;
			var poi = BotPoi.Current;
			var kind = poi.Type;
			bool Current() => guid != 0 && ReferenceEquals(Me, me) && me.Guid == guid
				&& me.MapId == map && me.IsValid && me.IsAlive && !me.IsGhost && me.Mounted
				&& !me.IsOnTransport && !me.OnTaxi && !me.InVehicle
				&& ReferenceEquals(ObjectManager.Wow, memory)
				&& ReferenceEquals(BotPoi.Current, poi) && poi.Type == kind;
			if (!Current() || me.IsFlying) { _mountedTravelProgress.Reset(); return false; }

			if (me.Combat)
			{
				bool stop = _mountedTravelProgress.ShouldStop(me, memory, guid, map,
					Environment.TickCount64, me.Location, travelingTo, me.HealthPercent,
					me.Rooted, me.Stunned, out string reason);
				if (!Current()) return false;
				if (stop)
				{
					Logging.WriteDebug("Mounted escape yielded to combat: {0}.", reason);
					return Current();
				}
			}
			else _mountedTravelProgress.Reset();

			if (travelingTo == WoWPoint.Empty)
				return false;

			WoWPoint location = me.Location;
			float distance = location.Distance(travelingTo);

			// If at a hotspot and there's a target nearby
			if (kind == PoiType.Hotspot)
			{
				if (distance <= 100f && Levelbot.Decorators.Combat.DecoratorNeedToFindTarget
					.IsRequestedMountedTarget(Targeting.Instance.FirstUnit) && Current())
				{
					Logging.WriteDebug("Dismount to pull near hotspot.");
					return Current();
				}
			}

			// If at a kill POI and we're close
			if (kind == PoiType.Kill)
			{
				if (distance <= CharacterSettings.Instance.PullDistance)
				{
					Logging.WriteDebug("Dismount to kill bot poi.");
					return Current();
				}
			}

			// Dismount for interacting with objects/NPCs
			if (BotPoi.Current.Type == PoiType.Loot || 
				BotPoi.Current.Type == PoiType.Skin ||
				BotPoi.Current.Type == PoiType.Harvest ||
				BotPoi.Current.Type == PoiType.Sell ||
				BotPoi.Current.Type == PoiType.Repair ||
				BotPoi.Current.Type == PoiType.Train ||
				BotPoi.Current.Type == PoiType.Mail)
			{
				if (distance <= 10f)
				{
					Logging.WriteDebug("Dismount for interaction.");
					return Current();
				}
			}

			return false;
		}

		/// <summary>
		/// Pulses mount state and fires events (call from main bot pulse).
		/// </summary>
		public static void Pulse()
		{
			var me = Me;
			if (me == null) return;

			bool isMounted = me.Mounted;

			if (!isMounted && _wasMounted)
			{
				// Just dismounted
				RaiseOnDismount(string.Empty);
			}

			_wasMounted = isMounted;
		}

		public static float MountDistance => (float)LevelbotSettings.Instance.MountDistance;

		public delegate bool CanMountDelegate();
	}
}
