using System;
using System.Diagnostics;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing;

/// <summary>
/// Owns flight-to-ground acknowledgement for one caller. The caller must retain
/// this instance while Running and cancel it when its target/work owner ends.
/// </summary>
public sealed class GroundTransition : IDisposable
{
    private readonly GroundTransitionPurpose _purpose;
    private GroundTransitionRuntime? _runtime;
    private GroundTransitionMachine? _machine;
    private double _unknownSince = double.NaN;
    public string Phase => _machine?.Phase ?? "unobserved";
    public GroundTransition(GroundTransitionPurpose purpose) { _purpose = purpose; }

    public GroundTransitionState Tick(WoWPoint destination, WoWObject? subject, Func<bool> admitted)
        => TickCore(destination, subject, admitted, null);

    /// <summary>Continue one ground journey; distance is an estimate for mount cost, not route authority.</summary>
    public GroundTransitionState TickTransit(WoWPoint destination, double remainingDistance, Func<bool> admitted)
    {
        if (_purpose != GroundTransitionPurpose.Transit)
            throw new InvalidOperationException("Transit distance belongs only to a coordinate-transit owner.");
        if (!double.IsFinite(remainingDistance) || remainingDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(remainingDistance));
        return TickCore(destination, null, admitted, remainingDistance);
    }

    private GroundTransitionState TickCore(WoWPoint destination, WoWObject? subject, Func<bool> admitted,
        double? remainingDistance)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        try
        {
            if (!admitted()) { Cancel(); return GroundTransitionState.Revoked; }
            if (_runtime != null && !_runtime.Matches(destination, subject))
            { Cancel(); return GroundTransitionState.Revoked; }
            if (_runtime == null)
            {
                _runtime = new GroundTransitionRuntime(_purpose, destination, subject, admitted);
                _machine = new GroundTransitionMachine(_purpose, _runtime);
            }
            _runtime.SetTransitDistanceEstimate(remainingDistance);
            GroundTransitionState result = _machine!.Tick();
            _unknownSince = double.NaN;
            return result;
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is not ObservationUnavailableException) throw;
            if (_runtime == null || _machine == null) return GroundTransitionState.Unavailable;
            if (!_runtime.Current) { Cancel(); return GroundTransitionState.Revoked; }
            double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            if (double.IsNaN(_unknownSince)) _unknownSince = now;
            GroundTransitionState state = _machine.ObservationUnavailable(error.Message);
            return state != GroundTransitionState.Revoked && now - _unknownSince >= 30
                ? GroundTransitionState.Unavailable : state;
        }
    }

    public void Cancel()
    {
        var runtime = _runtime;
        _runtime = null; _machine = null; _unknownSince = double.NaN;
        runtime?.Cancel();
    }
    public void Dispose() => Cancel();

    /// <summary>
    /// Per-tick attack admission for current original-client state. This is not
    /// target hostility/range/LOS permission; those existing routine guards remain.
    /// </summary>
    public static bool CanActUnmounted(Func<bool>? admitted = null)
    {
        try
        {
            var actor = ObjectManager.Me;
            if (actor == null) return false;
            var stamp = new GroundTransitionContext(null, WoWPoint.Empty, false, admitted ?? (() => true));
            if (!IsUnmountedActorCurrent(stamp)
                || WoWInternals.World.WorldQueryObservation.ReadLocalVehicle(actor)
                || !IsUnmountedActorCurrent(stamp)) return false;
            GroundTransitionRuntime.ObserveUnmounted(stamp);
            return stamp.Current;
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is ObservationUnavailableException) return false;
            throw;
        }
    }

    // Memory/descriptor observations only. The complete vehicle query belongs
    // to admission before preparing a native command. Repeating it while that
    // command is assembled would clear the shared executor's assembly buffer.
    // Native entry still rechecks movement, transport, mount and actual input
    // recipient together with the captured actor/subject/session/caller stamp.
    private static bool IsUnmountedActorCurrent(GroundTransitionContext stamp)
    {
        var actor = stamp.Actor;
        if (!stamp.Current || !ReferenceEquals(stamp.Mover, actor)
            || !actor.TryGetMovementState(out uint flags, out ulong transport)) return false;
        var state = WoWInternals.World.WorldQueryObservation.ReadGroundUnitState(actor);
        return !state.Mounted && (flags & 0x02003000u) == 0 && transport == 0 && !state.OnTaxi
            && actor.TryGetMovementState(out uint finalFlags, out ulong finalTransport)
            && finalFlags == flags && finalTransport == transport && stamp.Current;
    }

    /// <summary>Current grounded interaction range. NPCs also require sight to their origin.</summary>
    public static bool CanInteractWith(WoWObject? subject, Func<bool>? admitted = null)
    {
        try
        {
            if (subject == null) return false;
            WoWPoint destination = subject.Location;
            var stamp = new GroundTransitionContext(subject, destination, true, admitted ?? (() => true));
            if (!stamp.Current || !CanActUnmounted(() => stamp.Current)) return false;
            bool inRange = subject.WithinInteractRange;
            if (!stamp.Current || !inRange) return false;
            var actor = stamp.Actor;
            WoWPoint position = actor.Location;
            if (!GroundApproachSearch.Finite(position) || !GroundApproachSearch.Finite(destination)) return false;
            // A GameObject's model origin can be inside opaque geometry or below
            // its usable surface. The original GO-use contract is identity/range,
            // not a collision ray into that model. Native usability is observed
            // separately before interaction; landing still needs real support.
            if (subject is WoWGameObject gameObject)
                return !gameObject.IsDisabled && !Styx.Logic.Blacklist.Contains(gameObject.Guid)
                    && float.IsFinite(gameObject.InteractRange) && gameObject.InteractRange > 0
                    && stamp.Current && actor.Location.Equals(position);
            // Coincident finite points have no segment to trace. The native
            // collision contract deliberately rejects zero-length queries.
            bool sight = position.Equals(destination)
                || WoWInternals.World.GameWorld.IsInLineOfSight(position.Add(0, 0, 1), destination.Add(0, 0, 1));
            return sight && stamp.Current && actor.Location.Equals(position);
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is ObservationUnavailableException) return false;
            throw;
        }
    }

    /// <summary>
    /// Submits one native interaction after observed ground admission and binds
    /// its entry to that same actor, subject, route and caller context. True is
    /// local executor return only; callers still await the actual UI/world reply.
    /// </summary>
    public static bool TryInteractWith(WoWObject? subject, Func<bool>? admitted = null, bool ignoreTimer = false)
    {
        try
        {
            if (subject == null) return false;
            var stamp = new GroundTransitionContext(subject, subject.Location, true, admitted ?? (() => true));
            WoWPoint position = stamp.Actor.Location;
            if (!CanInteractWith(subject, () => stamp.Current)) return false;
            if (subject is WoWGameObject gameObject
                && (!gameObject.CanUse() || !stamp.Current || !gameObject.CanUseNow() || !stamp.Current)) return false;

            // The native entry guard must not trace collision or execute Lua:
            // either would overwrite the interaction's prepared native command.
            bool Current() => stamp.Current && stamp.Actor.Location.Equals(position)
                && IsUnmountedActorCurrent(stamp) && subject.WithinInteractRange
                && (subject is not WoWGameObject currentObject || !currentObject.IsDisabled
                    && !Styx.Logic.Blacklist.Contains(currentObject.Guid))
                && stamp.Actor.Location.Equals(position) && stamp.Current;
            if (!Current()) return false;
            return subject.TryInteractOwned(Current, ignoreTimer);
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is ObservationUnavailableException) return false;
            throw;
        }
    }
}
