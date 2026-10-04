using System;
using Styx.Logic.Pathing;

namespace Styx.Logic.Questing;

public enum QuestWorkflowStatus { Running, Completed, Deferred, Revoked }
public enum QuestWorkflowReceipt { Rejected, Submitted, Pending, ProgressSubmitted, RewardSubmitted }
public enum ArelionUi { None, VendorGossip, Merchant, VieraGossip, LureProgress, LureReward, Other }

public sealed record ArelionObservation(bool? Accepted, int? Credit, long? Wine, long? Scroll,
    bool CanTravel, bool CanAct, object? VieraToken, bool AtLureEndpoint, bool VieraMoving, ArelionUi Ui,
    bool LureInProgress = false);

public interface IArelionLureRuntime
{
    bool Current { get; }
    double Now { get; }
    ArelionObservation Observe();
    GroundTransitionState MoveVendor();
    GroundTransitionState MoveViera(bool followingLure);
    QuestWorkflowReceipt InteractVendor();
    QuestWorkflowReceipt OpenVendor();
    QuestWorkflowReceipt BuyWine();
    QuestWorkflowReceipt InteractViera();
    QuestWorkflowReceipt SelectLureQuest();
    QuestWorkflowReceipt AdvanceLureQuest();
    QuestWorkflowReceipt UseScroll();
    void RetireMovement();
}

/// <summary>
/// Source-bound quest9472: stock29112 -> reward9483 -> observed Viera17226
/// relocation -> item23693/spell30077 -> separately observed normal credit.
/// A submitted interaction, purchase, reward or item use never completes it.
/// </summary>
public sealed class ArelionLureController
{
    private readonly IArelionLureRuntime _runtime;
    private double _nextAction, _waitingSince = double.NaN, _lureSubmitted = double.NaN;
    private double _purchaseSubmitted = double.NaN, _scrollSubmitted = double.NaN, _lastNow = double.NaN;
    private object? _lureTarget;
    private long _wineBeforeLure;
    public string Phase { get; private set; } = "observe";
    public string Reason { get; private set; } = "quest-and-source-contract-unobserved";
    public QuestWorkflowStatus Status { get; private set; } = QuestWorkflowStatus.Running;

    public ArelionLureController(IArelionLureRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public QuestWorkflowStatus Tick()
    {
        if (Status != QuestWorkflowStatus.Running) return Status;
        if (!_runtime.Current) return End(QuestWorkflowStatus.Revoked, "owner-replaced");
        double now = _runtime.Now;
        if (!double.IsFinite(now) || !double.IsNaN(_lastNow) && now < _lastNow)
            return End(QuestWorkflowStatus.Deferred, "monotonic-clock-unavailable");
        _lastNow = now;
        var state = _runtime.Observe();
        if (!_runtime.Current) return End(QuestWorkflowStatus.Revoked, "observation-owner-replaced");
        if (state.Accepted == false) return End(QuestWorkflowStatus.Revoked, "parent-quest-no-longer-accepted");
        if (state.Accepted != true || !state.Credit.HasValue || state.Credit < 0)
            return Wait("observe", "typed-parent-objective-unavailable", now, 30);
        if (state.Credit >= 1) return End(QuestWorkflowStatus.Completed, "observed-17226-credit-for-quest9472");
        if (!state.CanTravel) return Wait("suspended", "combat-or-actor-state-prevents-owned-quest-action", now, 0);
        if (_lureTarget != null && state.VieraToken != null && !ReferenceEquals(_lureTarget, state.VieraToken))
            return End(QuestWorkflowStatus.Revoked, "lure-recipient-replaced");
        if (!double.IsNaN(_scrollSubmitted))
            return now - _scrollSubmitted >= 15
                ? End(QuestWorkflowStatus.Deferred, "scroll-submitted-but-parent-credit-unobserved")
                : Wait("await-credit", "item-request-is-not-quest-credit", now, 0);

        if (!state.Scroll.HasValue || state.Scroll < 1)
            return Wait("scroll-stock", "provided-scroll23693-unavailable", now, 30);

        if (double.IsNaN(_lureSubmitted) && state.LureInProgress && state.VieraToken != null)
        {
            _lureTarget = state.VieraToken; _wineBeforeLure = state.Wine ?? 0; _lureSubmitted = now;
        }

        // A resumed owner can observe the already lured source endpoint. It need
        // not buy/pay another wine simply because its predecessor was disposed.
        if (state.AtLureEndpoint && !state.VieraMoving && state.VieraToken != null)
        {
            var moved = Move(_runtime.MoveViera(true));
            if (moved != GroundTransitionState.Ready) return Status;
            if (!state.CanAct) return Status;
            if (now < _nextAction) return Status;
            var receipt = _runtime.UseScroll();
            if (!_runtime.Current) return End(QuestWorkflowStatus.Revoked, "scroll-owner-replaced");
            _nextAction = now + 1;
            if (receipt is QuestWorkflowReceipt.Submitted or QuestWorkflowReceipt.Pending)
                _scrollSubmitted = now;
            return Wait("use-scroll", "scroll-request-awaits-typed-parent-credit", now, 30);
        }

        if (!double.IsNaN(_lureSubmitted))
        {
            if (now - _lureSubmitted >= 150)
                return End(QuestWorkflowStatus.Deferred, "lure-endpoint-unobserved-after-bounded-follow");
            Move(_runtime.MoveViera(true));
            if (Status != QuestWorkflowStatus.Running) return Status;
            return Wait("follow-lure", state.Wine.HasValue && state.Wine < _wineBeforeLure
                ? "wine-consumption-observed; waiting-for-source-endpoint" : "lure-reward-request-is-not-relocation", now, 0);
        }

        if (!state.Wine.HasValue || state.Wine < 0)
            return Wait("stock-observation", "wine-stock-unavailable", now, 30);
        if (state.Wine == 0)
        {
            if (!double.IsNaN(_purchaseSubmitted))
                return now - _purchaseSubmitted >= 12
                    ? End(QuestWorkflowStatus.Deferred, "wine-purchase-submitted-but-stock-unobserved")
                    : Wait("await-wine", "purchase-request-is-not-inventory", now, 0);
            var moved = Move(_runtime.MoveVendor());
            if (moved != GroundTransitionState.Ready) { Phase = "travel-wine-vendor"; return Status; }
            if (!state.CanAct || now < _nextAction) return Status;
            QuestWorkflowReceipt receipt;
            if (state.Ui == ArelionUi.Merchant) receipt = _runtime.BuyWine();
            else if (state.Ui == ArelionUi.VendorGossip) receipt = _runtime.OpenVendor();
            else receipt = _runtime.InteractVendor();
            if (!_runtime.Current) return End(QuestWorkflowStatus.Revoked, "vendor-owner-replaced");
            _nextAction = now + 1;
            if (state.Ui == ArelionUi.Merchant && receipt is QuestWorkflowReceipt.Submitted or QuestWorkflowReceipt.Pending)
                _purchaseSubmitted = now;
            return Wait("obtain-wine", "owned-vendor-or-purchase-acknowledgement-pending", now, 30);
        }
        _purchaseSubmitted = double.NaN;

        var approach = Move(_runtime.MoveViera(false));
        if (approach != GroundTransitionState.Ready) { Phase = "travel-viera"; return Status; }
        if (!state.CanAct || now < _nextAction) return Status;
        QuestWorkflowReceipt action;
        if (state.Ui is ArelionUi.LureProgress or ArelionUi.LureReward)
            action = _runtime.AdvanceLureQuest();
        else if (state.Ui == ArelionUi.VieraGossip) action = _runtime.SelectLureQuest();
        else action = _runtime.InteractViera();
        if (!_runtime.Current) return End(QuestWorkflowStatus.Revoked, "lure-ui-owner-replaced");
        _nextAction = now + 1;
        if (state.Ui == ArelionUi.LureReward && action is QuestWorkflowReceipt.RewardSubmitted or QuestWorkflowReceipt.Pending)
        {
            if (state.VieraToken == null) return End(QuestWorkflowStatus.Deferred, "lure-recipient-unobserved");
            _lureTarget = state.VieraToken; _wineBeforeLure = state.Wine.Value; _lureSubmitted = now;
        }
        return Wait("lure-quest9483", "quest-ui-request-is-not-lure-or-parent-completion", now, 30);
    }

    private GroundTransitionState Move(GroundTransitionState result)
    {
        if (!_runtime.Current || result == GroundTransitionState.Revoked)
        { End(QuestWorkflowStatus.Revoked, "movement-owner-replaced"); return GroundTransitionState.Revoked; }
        else if (result == GroundTransitionState.Unavailable) End(QuestWorkflowStatus.Deferred, "owned-route-unavailable");
        else if (result == GroundTransitionState.Pending) _waitingSince = double.NaN;
        return result;
    }
    private QuestWorkflowStatus Wait(string phase, string reason, double now, double timeout)
    {
        if (Phase != phase || double.IsNaN(_waitingSince)) _waitingSince = now;
        Phase = phase; Reason = reason;
        return timeout > 0 && now - _waitingSince >= timeout
            ? End(QuestWorkflowStatus.Deferred, reason + "; bounded-wait-expired") : Status;
    }
    private QuestWorkflowStatus End(QuestWorkflowStatus status, string reason)
    {
        Status = status; Phase = status.ToString(); Reason = reason;
        _runtime.RetireMovement();
        return status;
    }
}
