using System;
using Styx.Helpers;

namespace Styx.Logic.Pathing;

public enum GroundTransitionState { Pending, Ready, Unavailable, Revoked }
public enum GroundTransitionPurpose { Interaction, Combat, Transit }
internal enum GroundDismountState { Rejected, Pending, Submitted, Expired }

internal readonly record struct GroundMotion(WoWPoint Position, bool Mounted, bool Flying,
    bool Falling, bool Swimming, bool OnTransport, bool Immobilized, bool Supported,
    bool Descending, bool InteractionReady, bool PreferFlight, bool GroundTravel = false,
    bool InteractionApproachReady = false);

internal interface IGroundTransitionRuntime
{
    bool Current { get; }
    double Now { get; }
    GroundMotion Observe();
    GroundApproachPlan? Search();
    GroundApproachPlan? PrepareNextFlightLeg(GroundApproachPlan active) => null;
    bool SearchExhausted { get; }
    bool Validate(GroundApproachPlan plan);
    void ResetSearch();
    void Hold();
    void Fly(GroundApproachPlan plan);
    void Descend(GroundApproachPlan plan);
    GroundDismountState Dismount();
    void Walk();
    bool RecoveryDeferred(double now);
    void DeferRecovery(double now);
    void Report(string phase, string reason, GroundMotion? observation, GroundApproachPlan? plan);
}

/// <summary>
/// Shared effect/acknowledgement owner for indoor approaches and committed combat.
/// Runtime observations and geometry admit effects; effects never acknowledge
/// ground, mount removal, interaction, combat or quest completion.
/// </summary>
internal sealed class GroundTransitionMachine
{
    private readonly GroundTransitionPurpose _purpose;
    private readonly IGroundTransitionRuntime _runtime;
    private GroundApproachPlan? _plan;
    private GroundMotion? _last;
    private double _started = double.NaN, _lastProgress, _lastCommand = double.NegativeInfinity;
    private WoWPoint _progressOrigin;
    private int _replans;
    private int _flightSearchBatches;
    private double _flightSearchStarted = double.NaN;
    private double _interactionStopStarted = double.NaN;
    private bool _dismountPending, _descent, _groundHandoff, _unavailable;
    internal string Phase { get; private set; } = "unobserved";

    internal GroundTransitionMachine(GroundTransitionPurpose purpose, IGroundTransitionRuntime runtime)
    { _purpose = purpose; _runtime = runtime; }

    internal GroundTransitionState Tick()
    {
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        GroundMotion observation = _runtime.Observe();
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        _last = observation;
        double now = _runtime.Now;
        if (!double.IsFinite(now) || !GroundApproachSearch.Finite(observation.Position))
            throw new ObservationUnavailableException("ground-transition", "position or monotonic clock unavailable");
        if (double.IsNaN(_started))
        { _started = _lastProgress = now; _progressOrigin = observation.Position; }
        if (now < _lastProgress) throw new ObservationUnavailableException("ground-transition", "monotonic clock moved backward");
        if (observation.Position.DistanceSqr(_progressOrigin) >= .25f)
        { _lastProgress = now; _progressOrigin = observation.Position; }

        bool authoritativeReady = !observation.OnTransport && !observation.Swimming && !observation.Falling
            && !observation.Immobilized && !observation.Flying && observation.Supported && !observation.Mounted
            && (_purpose == GroundTransitionPurpose.Combat || _purpose == GroundTransitionPurpose.Interaction && observation.InteractionReady);
        if (authoritativeReady)
        {
            _dismountPending = false;
            _runtime.Hold();
            return Result(GroundTransitionState.Ready, "ground-ready", "observed-ground-and-unmounted", observation);
        }

        bool recoveryDeferred = _runtime.RecoveryDeferred(now);
        if (_unavailable || recoveryDeferred)
        {
            if (recoveryDeferred)
                return Result(GroundTransitionState.Unavailable, "unresolved", "owner-recovery-cooldown", observation);
            ResetAfterRecovery(now, observation);
            if (!_runtime.Current) return GroundTransitionState.Revoked;
        }
        // Landing/approach time limits are not a maximum length for a useful
        // ground journey or local flight leg. Actual displacement renews that budget; the existing
        // no-progress watchdog still bounds walls and rejected movement.
        if (_purpose != GroundTransitionPurpose.Combat
            && (observation.GroundTravel && !observation.Flying && observation.Supported
                || _plan?.ProgressOnly == true && observation.Flying)
            && !observation.Falling && !observation.OnTransport
            && !observation.Swimming && !observation.Immobilized
            && now == _lastProgress)
            _started = now;
        if (now - _started >= 120)
            return Unavailable("ground-transition-deadline", observation, now);
        if (observation.OnTransport || observation.Swimming)
            return Wait("unresolved", "transport-or-liquid-requires-separate-owner", observation, stop: true);
        if (observation.Falling)
            return Wait("landing", "falling-is-not-grounded-acknowledgement", observation, stop: true);
        if (observation.Immobilized)
            return Wait("blocked", "root-or-stun-prevents-owned-transition", observation, stop: true);

        if (_purpose == GroundTransitionPurpose.Interaction && observation.InteractionApproachReady
            && !observation.Flying && !observation.Mounted && observation.Supported)
        {
            if (double.IsNaN(_interactionStopStarted)) _interactionStopStarted = now;
            if (now - _interactionStopStarted >= 5)
                return Unavailable("interaction-stop-acknowledgement-timeout", observation, now);
            return Wait("interaction-stop", "usable-range-and-sight; awaiting-observed-stop", observation, stop: true);
        }
        _interactionStopStarted = double.NaN;

        // A useful ground departure can expose a new flight opportunity. The
        // runtime bounds these reviews by time and observed displacement; this
        // owner releases the old ground route once before preparing the flight.
        if (_purpose != GroundTransitionPurpose.Combat && observation.PreferFlight
            && !observation.Flying && observation.Supported && !_descent && !observation.Descending && !_dismountPending
            && !(_groundHandoff && _plan is { ProgressOnly: false }))
        {
            if (_groundHandoff)
            {
                _runtime.Hold();
                if (!_runtime.Current) return GroundTransitionState.Revoked;
                _runtime.ResetSearch(); _plan = null; _groundHandoff = false;
                _flightSearchBatches = 0; _flightSearchStarted = double.NaN;
            }
            return Approach(observation, now, allowGroundFallback: true);
        }

        // Mounted travel is not interaction readiness. Keep a ground mount for
        // the distant mesh leg; use the existing landing/unmount owner only for
        // the final close approach (and for all combat transitions).
        if (_purpose != GroundTransitionPurpose.Combat && observation.GroundTravel
            && !observation.Flying && observation.Supported && !_descent && !observation.Descending)
        {
            _groundHandoff = true;
            if (CommandDue(now)) _runtime.Walk();
            return Progress("ground-travel", "owned-ground-travel; interaction-unobserved", observation, now);
        }

        if (!observation.Flying && observation.Supported)
        {
            if (_descent || observation.Descending)
            {
                _runtime.Hold(); _descent = false; _groundHandoff = true;
                return Result(GroundTransitionState.Pending, "landed", "awaiting-observed-descent-stop", observation);
            }
            if (!observation.Mounted)
            {
                _dismountPending = false;
                _groundHandoff = true;
                if (CommandDue(now)) _runtime.Walk();
                return Progress("ground-mesh", "following-ground-route; interaction-unobserved", observation, now);
            }
            // A shared actor/session lease in the runtime additionally survives
            // target/POI replacement; this local pending flag owns this handoff.
            _groundHandoff = true;
            _runtime.Hold();
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            GroundDismountState dismount = _runtime.Dismount();
            if (dismount == GroundDismountState.Expired)
            {
                _dismountPending = false;
                return Unavailable("dismount-acknowledgement-timeout", observation, now);
            }
            if (dismount == GroundDismountState.Submitted)
            {
                _lastProgress = now;
                _progressOrigin = observation.Position;
            }
            _dismountPending = dismount is GroundDismountState.Pending or GroundDismountState.Submitted;
            return Progress("dismount-pending", "request-is-not-unmounted-acknowledgement", observation, now);
        }

        if (!observation.Flying)
            return Wait("unresolved", "positive-ground-support-unavailable", observation, stop: true);
        if (!observation.Mounted)
            return Wait("landing", "airborne-state-persists-after-mount-removal", observation, stop: true);
        return Approach(observation, now);
    }

    private GroundTransitionState Approach(GroundMotion observation, double now, bool allowGroundFallback = false)
    {
        if (_plan == null)
        {
            // A partial batch is not evidence that flying is unsuitable. Retain
            // the same search across pulses, while bounding planning separately
            // from the much longer travel/landing lifetime. The time budget bounds
            // repeated queries; an individual native query can still take longer.
            if (allowGroundFallback)
            {
                if (double.IsNaN(_flightSearchStarted)) _flightSearchStarted = now;
                if (_flightSearchBatches >= 8 || now - _flightSearchStarted >= 2)
                    return GroundFallback(observation, now, "flight-planning-budget-exhausted");
                _flightSearchBatches++;
            }
            _runtime.Hold();
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            _plan = _runtime.Search();
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            if (_plan == null)
            {
                if (allowGroundFallback && _runtime.SearchExhausted)
                    return GroundFallback(observation, now, "safe-flight-search-exhausted");
                return _runtime.SearchExhausted ? Unavailable("no-proven-safe-approach", observation, now)
                    : Result(GroundTransitionState.Pending, "searching", "bounded-collision-and-mesh-candidate-search", observation);
            }
            if (allowGroundFallback)
            {
                _runtime.Hold();
                if (!_runtime.Current) return GroundTransitionState.Revoked;
            }
        }
        if (!_runtime.Validate(_plan))
        {
            _runtime.Hold(); _plan = null; _descent = false;
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            if (++_replans > 2) return Unavailable("approach-repeatedly-invalidated", observation, now);
            _runtime.ResetSearch();
            return Result(GroundTransitionState.Pending, "replanning", "collision-or-onward-route-changed", observation);
        }
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        if (_plan.ProgressOnly && observation.Flying
            && observation.Position.DistanceSqr(_plan.AirWaypoint) <= 32f * 32f)
        {
            // Prepare the successor while the current, positively observed leg
            // still has forward runway. A completed local point is not a stop,
            // a landing, or a reason to yield an otherwise ready command pulse.
            var next = _runtime.PrepareNextFlightLeg(_plan);
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            if (next != null)
            {
                _plan = next;
                _flightSearchBatches = 0; _flightSearchStarted = double.NaN;
                _started = _lastProgress = now; _progressOrigin = observation.Position;
                _lastCommand = double.NegativeInfinity;
            }
            else if (observation.Position.DistanceSqr(_plan.AirWaypoint) <= 4f)
            {
                // No safe successor has been observed. Keep the last endpoint
                // as the movement limit; never extrapolate into unknown space.
                return Progress("flight-successor-pending", "next-flight-leg-unobserved; current-endpoint-retained", observation, now);
            }
        }
        if (_plan.ProgressOnly)
        {
            if (CommandDue(now)) _runtime.Fly(_plan);
            return Progress("flight-travel", "local-flight-leg; descent-not-authorized", observation, now);
        }
        bool aboveLanding = observation.Position.Distance2DSqr(_plan.Landing) <= .5625f
            && observation.Position.Z >= _plan.Landing.Z - .25f
            && (!_plan.OpenColumn || observation.Position.Z <= _plan.AirWaypoint.Z + 4);
        if (!_plan.OpenColumn && !aboveLanding)
        {
            _runtime.Hold(); _plan = null; _descent = false;
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            if (++_replans > 2) return Unavailable("covered-landing-column-no-longer-current", observation, now);
            _runtime.ResetSearch();
            return Result(GroundTransitionState.Pending, "replanning", "covered-landing-cannot-be-used-for-lateral-flight", observation);
        }
        if (observation.Flying && aboveLanding)
        {
            if (!_descent)
            {
                _runtime.Hold();
                if (!_runtime.Current) return GroundTransitionState.Revoked;
                _descent = true;
            }
            // Fresh support/clearance is validated by the runtime on every
            // retained descent pulse, including changes to dynamic structures.
            _runtime.Descend(_plan);
            return Progress("descending", "awaiting-supported-ground-observation", observation, now);
        }
        if (_descent) { _runtime.Hold(); _descent = false; }
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        if (CommandDue(now)) _runtime.Fly(_plan);
        return Progress("exterior-approach", "flight-waypoint-is-validated-exterior-region", observation, now);
    }

    private GroundTransitionState GroundFallback(GroundMotion observation, double now, string reason)
    {
        _groundHandoff = true;
        _runtime.ResetSearch();
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        if (CommandDue(now)) _runtime.Walk();
        return Progress("ground-mesh", reason + "; following-ground-route", observation, now);
    }

    private bool CommandDue(double now)
    {
        if (now - _lastCommand < .2) return false;
        _lastCommand = now;
        return _runtime.Current;
    }

    private GroundTransitionState Progress(string phase, string reason, GroundMotion observation, double now)
    {
        if (now - _lastProgress >= 12)
        {
            _runtime.Hold(); _descent = false;
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            if (_dismountPending || ++_replans > 2) return Unavailable("no-observed-transition-progress", observation, now);
            _plan = null; _runtime.ResetSearch(); _lastProgress = now;
            return Result(GroundTransitionState.Pending, "replanning", "no-displacement; previous-route-revoked", observation);
        }
        return Result(GroundTransitionState.Pending, phase, reason, observation);
    }

    internal GroundTransitionState ObservationUnavailable(string reason)
    {
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        _runtime.Hold(); _descent = false;
        return Result(GroundTransitionState.Pending, "UNKNOWN", reason, _last);
    }

    private GroundTransitionState Wait(string phase, string reason, GroundMotion observation, bool stop)
    {
        if (stop) { _runtime.Hold(); _descent = false; }
        return Result(GroundTransitionState.Pending, phase, reason, observation);
    }
    private void ResetAfterRecovery(double now, GroundMotion observation)
    {
        _runtime.Hold();
        if (!_runtime.Current) return;
        _runtime.ResetSearch();
        _plan = null;
        _descent = _groundHandoff = _dismountPending = _unavailable = false;
        _replans = 0;
        _flightSearchBatches = 0; _flightSearchStarted = double.NaN;
        _interactionStopStarted = double.NaN;
        _started = _lastProgress = now;
        _lastCommand = double.NegativeInfinity;
        _progressOrigin = observation.Position;
    }

    private GroundTransitionState Unavailable(string reason, GroundMotion observation, double now)
    {
        _runtime.Hold(); _descent = false;
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        _runtime.DeferRecovery(now);
        _unavailable = true;
        return Result(GroundTransitionState.Unavailable, "unresolved", reason, observation);
    }
    private GroundTransitionState Result(GroundTransitionState state, string phase, string reason, GroundMotion? observation)
    {
        Phase = phase;
        if (!_runtime.Current) return GroundTransitionState.Revoked;
        _runtime.Report(phase, reason, observation, _plan);
        return _runtime.Current ? state : GroundTransitionState.Revoked;
    }
}
