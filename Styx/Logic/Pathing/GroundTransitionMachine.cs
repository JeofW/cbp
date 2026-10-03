using System;
using Styx.Helpers;

namespace Styx.Logic.Pathing;

public enum GroundTransitionState { Pending, Ready, Unavailable, Revoked }
public enum GroundTransitionPurpose { Interaction, Combat }
internal enum GroundDismountState { Rejected, Pending, Submitted, Expired }

internal readonly record struct GroundMotion(WoWPoint Position, bool Mounted, bool Flying,
    bool Falling, bool Swimming, bool OnTransport, bool Immobilized, bool Supported,
    bool Descending, bool InteractionReady, bool PreferFlight);

internal interface IGroundTransitionRuntime
{
    bool Current { get; }
    double Now { get; }
    GroundMotion Observe();
    GroundApproachPlan? Search();
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
            && (_purpose == GroundTransitionPurpose.Combat || observation.InteractionReady);
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
        if (now - _started >= 120)
            return Unavailable("ground-transition-deadline", observation, now);
        if (observation.OnTransport || observation.Swimming)
            return Wait("unresolved", "transport-or-liquid-requires-separate-owner", observation, stop: true);
        if (observation.Falling)
            return Wait("landing", "falling-is-not-grounded-acknowledgement", observation, stop: true);
        if (observation.Immobilized)
            return Wait("blocked", "root-or-stun-prevents-owned-transition", observation, stop: true);

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
                if (!_groundHandoff && observation.PreferFlight)
                    return Approach(observation, now, allowGroundFallback: true);
                _groundHandoff = true;
                if (CommandDue(now)) _runtime.Walk();
                return Progress("ground-mesh", "following-ground-route; interaction-unobserved", observation, now);
            }
            if (!_groundHandoff && _purpose == GroundTransitionPurpose.Interaction && observation.PreferFlight)
                return Approach(observation, now);
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
            // An already-grounded actor has a safe useful fallback: keep making
            // ground progress if the first bounded exterior-flight search cannot
            // prove a landing/onward plan. Do not park the bot and spend later
            // pulses repeating expensive collision/mesh candidate searches.
            if (!allowGroundFallback)
            {
                _runtime.Hold();
                if (!_runtime.Current) return GroundTransitionState.Revoked;
            }
            _plan = _runtime.Search();
            if (!_runtime.Current) return GroundTransitionState.Revoked;
            if (_plan == null)
            {
                if (allowGroundFallback)
                {
                    _groundHandoff = true;
                    _runtime.ResetSearch();
                    if (!_runtime.Current) return GroundTransitionState.Revoked;
                    if (CommandDue(now)) _runtime.Walk();
                    return Progress("ground-mesh", "safe-flight-plan-not-proven; following-ground-route", observation, now);
                }
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
