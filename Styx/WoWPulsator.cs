using System;
using System.Diagnostics;
using Styx.CommonBot;
using Styx.CommonBot.Routines;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Plugins;
using Styx.WoWInternals;

namespace Styx
{
	/// <summary>
	/// HB 6.2.3 Pulsator.Pulse — ported verbatim from
	/// C:\Users\Texy6\Desktop\newhcb\hb decompile\.hb 6.2.3\Honorbuddy\Styx\Pulsator.cs
	///
	/// Pulse order matches HB 6.2.3 exactly:
	///   Objects   → ObjectManager.Update + Blacklist.Flush
	///   Lua       → Lua.ProcessEvents
	///   InfoPanel → InfoPanel.Update
	///   Looting   → LootTargeting.Pulse
	///   Targeting → Targeting.Pulse + HealTargeting.Pulse
	///   BotEvents → BotEvents.PulseEvents
	///   Plugins   → PluginManager.Pulse
	///   Routine   → CapabilityManager.Pulse + RoutineManager.Current.Pulse
	///
	/// Restored for non-blocking recovery input expiry: WoWMovement.Pulse.
	/// Removed (not in HB 6.2.3): WoWChat, Mount.Pulse,
	/// AvoidanceManager.Pulse, NavAvoidanceUpdater.Invoke.
	/// </summary>
	public static class WoWPulsator
	{
		private static DateTime _lastSlowPulseBreakdownUtc = DateTime.MinValue;

		public static void Pulse(PulseFlags flags)
		{
			var owner = TreeRoot.Current;
			bool workerOwned = TreeRoot.CurrentThreadIsBotThread;
			try
			{
				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				var totalTimer = Stopwatch.StartNew();
				var stageTimer = Stopwatch.StartNew();

				// Expire non-blocking recovery inputs before any potentially expensive pulse work.
				WoWMovement.Pulse();
				long movementMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if ((flags & PulseFlags.Objects) != (PulseFlags)0U)
				{
					ObjectManager.Update();
					TreeRoot.VerifyPulseOwner(owner, workerOwned);
					Blacklist.Flush();
				}
				long objectsMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if ((flags & PulseFlags.Lua) != (PulseFlags)0U)
				{
					Lua.ProcessEvents();
				}
				long luaMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if ((flags & PulseFlags.InfoPanel) != (PulseFlags)0U)
				{
					InfoPanel.Update();
				}
				long infoPanelMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if ((flags & PulseFlags.Looting) != (PulseFlags)0U)
				{
					LootTargeting.Instance.Pulse();
				}
				long lootingMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if ((flags & PulseFlags.Targeting) != (PulseFlags)0U)
				{
					ObserveTargets(Targeting.Instance);
					TreeRoot.VerifyPulseOwner(owner, workerOwned);
					ObserveTargets(HealTargeting.Instance);
				}
				long targetingMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if ((flags & PulseFlags.BotEvents) != (PulseFlags)0U)
				{
					BotEvents.PulseEvents();
				}
				long botEventsMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if ((flags & PulseFlags.Plugins) != (PulseFlags)0U)
				{
					PluginManager.Pulse();
				}
				long pluginsMilliseconds = stageTimer.ElapsedMilliseconds;

				TreeRoot.VerifyPulseOwner(owner, workerOwned);
				stageTimer.Restart();
				if (RoutineManager.Current != null)
				{
					CapabilityManager.Instance.Pulse();
					TreeRoot.VerifyPulseOwner(owner, workerOwned);
					RoutineManager.Current.Pulse();
				}
				long routineMilliseconds = stageTimer.ElapsedMilliseconds;

				long totalMilliseconds = totalTimer.ElapsedMilliseconds;
				DateTime nowUtc = DateTime.UtcNow;
				if (ShouldLogSlowPulseBreakdown(totalMilliseconds, nowUtc, _lastSlowPulseBreakdownUtc))
				{
					_lastSlowPulseBreakdownUtc = nowUtc;
					Logging.WriteDiagnostic(
						"[Pulse] Slow shared pulse: total={0}ms movement={1} objects={2} lua={3} info={4} loot={5} targeting={6} events={7} plugins={8} routine={9}",
						totalMilliseconds, movementMilliseconds, objectsMilliseconds, luaMilliseconds,
						infoPanelMilliseconds, lootingMilliseconds, targetingMilliseconds,
						botEventsMilliseconds, pluginsMilliseconds, routineMilliseconds);
				}
			}
			catch (Exception ex)
			{
				ObservationUnavailableException.RethrowCancellation(ex);
				Logging.WriteException(ex);
			}
		}

		private static void ObserveTargets(Targeting provider)
		{
			try { provider.Pulse(); }
			catch (Exception error) when (ObservationUnavailableException.Find(error) is { })
			{
				var unavailable = ObservationUnavailableException.Find(error)!;
				provider.MarkObservationUnavailable(unavailable.Message);
				ObservationFailureDiagnostics.Report(unavailable, provider.GetType().FullName + ".Pulse");
			}
		}

		internal static bool ShouldLogSlowPulseBreakdown(
			long totalMilliseconds,
			DateTime nowUtc,
			DateTime previousLogUtc)
		{
			return totalMilliseconds >= 200
				&& nowUtc - previousLogUtc >= TimeSpan.FromSeconds(10);
		}
	}
}
