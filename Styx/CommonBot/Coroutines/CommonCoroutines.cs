using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Buddy.Coroutines;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.CommonBot.Coroutines
{
    /// <summary>
    /// Port of HB 6.2.3 Styx.CommonBot.Coroutines.CommonCoroutines.
    /// Provides async helper methods for lag-aware sleeps, Lua event waits,
    /// movement stops, and dismounting.
    /// </summary>
    public static class CommonCoroutines
    {
        // HB 6.2.3: Cached localized error strings for mount failures
        private static string? _spellFailedNoMountsAllowed;
        private static string? _spellFailedNotHere;

        // HB 6.2.3: MoveTo logging throttle
        private static WoWPoint _lastMoveToDestination;
        private static readonly WaitTimer _moveToLogThrottle = WaitTimer.OneSecond;

        /// <summary>HB 6.2.3: SPELL_FAILED_NO_MOUNTS_ALLOWED (lazy cached).</summary>
        private static string SpellFailedNoMountsAllowed
        {
            get
            {
                return _spellFailedNoMountsAllowed ??=
                    Lua.GetReturnVal<string>("return SPELL_FAILED_NO_MOUNTS_ALLOWED", 0);
            }
        }

        /// <summary>HB 6.2.3: SPELL_FAILED_NOT_HERE (lazy cached).</summary>
        private static string SpellFailedNotHere
        {
            get
            {
                return _spellFailedNotHere ??=
                    Lua.GetReturnVal<string>("return SPELL_FAILED_NOT_HERE", 0);
            }
        }

        /// <summary>
        /// HB 6.2.3: Sleep for latency + 100ms.
        /// Used after actions that need to wait for server response.
        /// </summary>
        public static Task SleepForLagDuration()
        {
            return Coroutine.Sleep((int)(StyxWoW.WoWClient.Latency + 100U));
        }

        /// <summary>
        /// HB 6.2.3: Random reaction time sleep (200-1000ms, with 10% chance of 600-1000ms).
        /// Used to humanize bot actions.
        /// </summary>
        public static Task SleepForRandomReactionTime()
        {
            return Coroutine.Sleep(
                (StyxWoW.Random.Next(0, 10) == 0)
                    ? StyxWoW.Random.Next(600, 1000)
                    : StyxWoW.Random.Next(200, 700));
        }

        /// <summary>
        /// HB 6.2.3: Random UI interaction sleep (700-3000ms, with 10% chance of 1500-3000ms).
        /// Used when interacting with UI elements (vendor, quest turn-in, etc).
        /// </summary>
        public static Task SleepForRandomUiInteractionTime()
        {
            return Coroutine.Sleep(
                (StyxWoW.Random.Next(0, 10) == 0)
                    ? StyxWoW.Random.Next(1500, 3000)
                    : StyxWoW.Random.Next(700, 2000));
        }

        /// <summary>
        /// HB 6.2.3: Wait for a Lua event with optional alternate condition.
        /// int overload — delegates to TimeSpan overload.
        /// </summary>
        public static Task<bool> WaitForLuaEvent(string eventName, int maxWaitMs,
            Func<bool>? alternateCondition = null, Action? action = null)
        {
            return WaitForLuaEvent(eventName, TimeSpan.FromMilliseconds(maxWaitMs),
                alternateCondition, action);
        }

        /// <summary>
        /// HB 6.2.3: Wait for a Lua event with optional alternate condition.
        /// Attaches event, optionally fires action, waits for event or condition,
        /// then detaches event.
        /// </summary>
        public static async Task<bool> WaitForLuaEvent(string eventName, TimeSpan maxWaitTimeout,
            Func<bool>? alternateCondition = null, Action? action = null)
        {
            bool eventFired = false;
            LuaEventHandlerDelegate handler = (sender, e) => { eventFired = true; };
            bool result;
            try
            {
                Lua.Events.AttachEvent(eventName, handler);
                action?.Invoke();
                result = await Coroutine.Wait(maxWaitTimeout,
                    () => eventFired || (alternateCondition != null && alternateCondition()));
            }
            finally
            {
                Lua.Events.DetachEvent(eventName, handler);
            }
            return result;
        }

        /// <summary>
        /// HB 6.2.3: Stop all movement and wait up to 4s for character to stop.
        /// Returns true if movement was stopped, false if already stationary.
        /// </summary>
        public static async Task<bool> StopMoving(string? reason = null)
        {
            WoWUnit? mover = WoWMovement.ActiveMover;
            if (mover == null || !mover.IsValid || mover.Guid == 0 || !mover.IsMoving)
                return false;
            ulong guid = mover.Guid;
            bool SameMover() => ReferenceEquals(WoWMovement.ActiveMover, mover)
                && mover.Guid == guid && mover.IsValid;
            if (!SameMover()) return false;

            WoWMovement.MoveStop();
            string text = (!string.IsNullOrEmpty(reason)) ? (" Reason: " + reason) : string.Empty;
            Logging.WriteDiagnostic("Stopped moving." + text);

            bool stopped = await Coroutine.Wait(4000, () => !SameMover() || !mover.IsMoving);
            if (!stopped)
            {
                Logging.WriteDiagnostic("Unable to stop moving after 4 seconds of attempting to stop");
            }
            return stopped && SameMover() && !mover.IsMoving;
        }

        /// <summary>
        /// Dismount one observed player. Optional descent is bounded and cannot
        /// authorize removal on timeout, stale identity or unreadable movement.
        /// </summary>
        public static async Task<bool> Dismount(string? reason = null, bool descend = true)
        {
            LocalPlayer? player = StyxWoW.Me;
            if (player == null) return false;
            ulong guid = player.Guid;
            ShapeshiftForm shapeshift = player.Shapeshift;
            bool IsFlightForm(ShapeshiftForm form) => form == ShapeshiftForm.FlightForm || form == ShapeshiftForm.EpicFlightForm;
            bool HasMount() => player.Mounted || IsFlightForm(player.Shapeshift);
            bool SameActor() => guid != 0 && ReferenceEquals(StyxWoW.Me, player) && player.Guid == guid
                && ReferenceEquals(WoWMovement.ActiveMover, player);
            bool Observe(out uint flags)
            {
                flags = 0;
                return SameActor() && player.IsValid && player.IsAlive
                    && player.TryGetMovementState(out flags, out ulong transport)
                    && transport == 0 && (flags & 0x3000u) == 0
                    && SameActor() && player.IsValid && player.IsAlive;
            }
            if (!Observe(out uint flags) || !HasMount() || (!descend && (flags & 0x02000000u) != 0)) return false;

            string text = (!string.IsNullOrEmpty(reason)) ? (" Reason: " + reason) : string.Empty;
            Logging.WriteDiagnostic("Stop and dismount..." + text);

            if (!Observe(out flags) || player.Shapeshift != shapeshift || !HasMount()) return false;
            await StopMoving("Dismounting");
            if (!Observe(out flags) || player.Shapeshift != shapeshift || !HasMount() || player.IsMoving) return false;

            if ((flags & 0x02000000u) != 0)
            {
                if (!descend) return false;
                bool descending = false;
                try
                {
                    if (!Observe(out flags) || player.Shapeshift != shapeshift) return false;
                    descending = true;
                    WoWMovement.Move(WoWMovement.MovementDirection.Descend);
                    await Coroutine.Sleep(150);
                    if (!Observe(out flags) || player.Shapeshift != shapeshift) return false;
                    bool landed = await Coroutine.Wait(40000, () =>
                        !Observe(out uint currentFlags) || player.Shapeshift != shapeshift || (currentFlags & 0x02000000u) == 0);
                    if (!landed)
                    {
                        Logging.WriteDiagnostic("Unable to land after 40 seconds of descending.");
                        return false;
                    }
                }
                finally
                {
                    if (descending && SameActor())
                        WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend);
                }
            }

            if (!Observe(out flags) || (flags & 0x02000000u) != 0 || player.Shapeshift != shapeshift || !HasMount()) return false;
            if (IsFlightForm(shapeshift))
            {
                Lua.DoString("RunMacroText('/cancelform')");
            }
            else
            {
                Lua.DoString("Dismount()");
            }

            if (!(await Coroutine.Wait(4000, () => !SameActor() || !HasMount()))
                || !Observe(out flags) || (flags & 0x02000000u) != 0 || HasMount())
            {
                return false;
            }

            // Notify only after this same player's removal is observed. This is
            // not an exclusive server acknowledgement or physical-ground proof.
            Styx.Logic.Mount.RaiseOnDismount(reason);
            return true;
        }

        /// <summary>HB 6.2.3: Land and dismount. In WotLK equivalent to Dismount.</summary>
        public static Task<bool> LandAndDismount(string? reason = null)
        {
            return Dismount(reason);
        }

        /// <summary>
        /// HB 6.2.3: Navigate to a point with logging throttle.
        /// </summary>
        public static async Task<MoveResult> MoveTo(WoWPoint destination, string? destinationName = null)
        {
            if (destination.DistanceSqr(_lastMoveToDestination) > 25f &&
                _moveToLogThrottle.IsFinished)
            {
                destinationName ??= destination.ToString();
                Logging.WriteDiagnostic("Moving to {0} from {1}",
                    destinationName,
                    (WoWMovement.ActiveMover ?? StyxWoW.Me)?.Location);
                _moveToLogThrottle.Reset();
                _lastMoveToDestination = destination;
            }
            return Navigator.MoveTo(destination);
        }
    }
}
