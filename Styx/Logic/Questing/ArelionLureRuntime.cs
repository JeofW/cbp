using System;
using System.Globalization;
using System.Linq;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

namespace Styx.Logic.Questing;

/// <summary>Original-client adapters for the pinned quest9472/9483 source workflow.</summary>
public sealed class ArelionLureRuntime : IArelionLureRuntime, IDisposable
{
    public static readonly WoWPoint VendorPosition = new(-174.478f, 5529.21f, 29.4909f);
    public static readonly WoWPoint VieraPosition = new(-657.813f, 4137.41f, 64.6336f);
    public static readonly WoWPoint LureEndpoint = new(-720.839f, 4162.23f, 50.8059f);
    private readonly GroundTransitionContext _lifetime;
    private readonly Func<bool> _admitted;
    private bool _disposed;
    private GroundTransition? _travel;
    private string _travelRole = "";
    private bool _followingTransit;
    private WoWObject? _travelSubject;
    private Recipient? _vendor, _viera;
    private long? _wine;
    private double _nextInteraction;
    private ReadyObservation? _ready;

    private sealed record Recipient(WoWUnit Unit, ulong Guid, uint Address, uint Entry)
    {
        internal bool Current => Unit.IsValid && Unit.IsAlive && Unit.Guid == Guid && Unit.BaseAddress == Address && Unit.Entry == Entry;
    }
    private sealed record ReadyObservation(Recipient Target, WoWPoint ActorPosition, WoWPoint TargetPosition,
        object FlightOwner, object? MeshOwner);

    public ArelionLureRuntime(Func<bool> admitted)
    {
        _admitted = admitted ?? throw new ArgumentNullException(nameof(admitted));
        _lifetime = new GroundTransitionContext(null, WoWPoint.Empty, false, () => !_disposed && _admitted(), journeyRoute: true);
        if (_lifetime.Map != 530) throw Unknown("The source workflow requires map530.");
    }
    public bool Current => !_disposed && _lifetime.Current;
    public double Now => Environment.TickCount64 / 1000.0;
    private LocalPlayer Actor => _lifetime.Actor;
    private static ObservationUnavailableException Unknown(string reason) => new("quest9472", reason);
    private void RequireCurrent() { if (!Current) throw Unknown("The quest execution owner changed."); }

    public ArelionObservation Observe()
    {
        RequireCurrent();
        var snapshot = Actor.QuestLog.CaptureSnapshot();
        bool? accepted = snapshot.IsIdentityComplete && Actor.QuestLog.IsSnapshotCurrent(snapshot)
            ? snapshot.AcceptedQuestIds.Contains(9472u) : null;
        int? credit = null;
        if (accepted == true && QuestObjectiveCompletion.TryReadTypedNormalObjectiveProgress(Actor.QuestLog.GetQuestById(9472), 17226, 1, out int progress))
            credit = progress;
        RequireCurrent();
        var stock = Lua.GetObservedReturnValues(ArelionLureScripts.Stock(_lifetime.ActorGuid), () => Current);
        RequireCurrent();
        long? wine = null, scroll = null;
        if (stock.Count == 3 && stock[0] == "stock"
            && long.TryParse(stock[1], NumberStyles.None, CultureInfo.InvariantCulture, out long observedWine)
            && long.TryParse(stock[2], NumberStyles.None, CultureInfo.InvariantCulture, out long observedScroll)
            && observedWine >= 0 && observedWine <= 1000000 && observedScroll >= 0 && observedScroll <= 1000000)
        { wine = observedWine; scroll = observedScroll; }
        _wine = wine;
        var units = ObjectManager.GetObjectsOfType<WoWUnit>().Where(u => u.IsValid && u.IsAlive && u.Guid != 0 && u.BaseAddress != 0).ToArray();
        _viera = ObserveRecipient(_viera, units, 17226);
        _vendor = ObserveRecipient(_vendor, units, 18907);
        RequireCurrent();
        bool lured = _viera?.Current == true && (_viera.Unit.NpcFlags & 3u) == 0
            && _viera.Unit.Location.DistanceSqr(LureEndpoint) <= 140 * 140;
        bool endpoint = lured && _viera!.Unit.Location.DistanceSqr(LureEndpoint) <= 64;
        bool moving = _viera?.Current == true && _viera.Unit.IsMoving;
        if (!Actor.TryGetMovementState(out uint flags, out ulong transport)) throw Unknown("Movement state is unavailable.");
        var ground = WorldQueryObservation.ReadGroundUnitState(Actor);
        bool canTravel = transport == 0 && !Actor.OnTaxi && !Actor.IsSwimming && (!Actor.Combat || ground.Mounted);
        bool canAct = canTravel && !Actor.Combat && !ground.Mounted && (flags & 0x02003000u) == 0
            && !ground.Rooted && !ground.Stunned && !Actor.IsCasting && Actor.ChanneledCastingSpellId == 0;
        var ui = ArelionUi.None;
        if (_vendor?.Current == true && _vendor.Unit.WithinInteractRange || _viera?.Current == true && _viera.Unit.WithinInteractRange)
        {
            var view = Lua.GetObservedReturnValues(
                "local function shown(x) return x and x:IsVisible() and 1 or 0 end return UnitGUID('npc') or '',shown(MerchantFrame),shown(GossipFrame),shown(QuestFrame),shown(QuestFrameCompleteQuestButton),shown(QuestFrameCompleteButton)", () => Current);
            RequireCurrent();
            if (view.Count != 6 || view.Skip(1).Any(value => value != "0" && value != "1")) throw Unknown("Quest/vendor frame observation is incomplete.");
            bool vendor = GuidMatches(view[0], _vendor), viera = GuidMatches(view[0], _viera);
            if (vendor && view[1] == "1") ui = ArelionUi.Merchant;
            else if (vendor && view[2] == "1") ui = ArelionUi.VendorGossip;
            else if (viera && view[2] == "1") ui = ArelionUi.VieraGossip;
            else if (viera && view[3] == "1" && QuestFrame.Instance.CurrentShownQuestId == 9483)
                ui = view[4] == "1" ? ArelionUi.LureReward : view[5] == "1" ? ArelionUi.LureProgress : ArelionUi.Other;
            else if (view.Skip(1).Any(value => value == "1")) ui = ArelionUi.Other;
        }
        RequireCurrent();
        return new(accepted, credit, wine, scroll, canTravel, canAct, _viera, endpoint, moving, ui, lured);
    }

    private Recipient? ObserveRecipient(Recipient? old, WoWUnit[] units, uint entry)
    {
        var observed = old?.Current == true && units.Any(u => ReferenceEquals(u, old.Unit)) ? old.Unit
            : units.Where(u => u.Entry == entry).OrderBy(u => u.Location.DistanceSqr(Actor.Location)).ThenBy(u => u.Guid).FirstOrDefault();
        if (observed == null) return null;
        return old?.Current == true && ReferenceEquals(old.Unit, observed) ? old : new(observed, observed.Guid, observed.BaseAddress, entry);
    }
    private static bool GuidMatches(string value, Recipient? target) => target != null
        && string.Equals(value, "0x" + target.Guid.ToString("X16", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    public GroundTransitionState MoveVendor() => Move("vendor", _vendor, VendorPosition);
    public GroundTransitionState MoveViera(bool followingLure)
    {
        if (_wine >= 1 && _vendor?.Current == true && _travelRole == "vendor") CloseOwnedMerchant(_vendor);
        bool following = followingLure && (_viera?.Current != true || _viera.Unit.IsMoving
            || _viera.Unit.Location.DistanceSqr(LureEndpoint) > 64);
        return Move("viera", _viera, followingLure ? LureEndpoint : VieraPosition, following);
    }
    private GroundTransitionState Move(string role, Recipient? recipient, WoWPoint fallback, bool transit = false)
    {
        RequireCurrent();
        var subject = recipient?.Current == true ? recipient.Unit : null;
        if (_travel == null || _travelRole != role || _followingTransit != transit || !ReferenceEquals(subject, _travelSubject))
        {
            RetireMovement(); RequireCurrent();
            _travel = new GroundTransition(transit ? GroundTransitionPurpose.Transit : GroundTransitionPurpose.Interaction);
            _travelRole = role; _travelSubject = subject; _followingTransit = transit;
        }
        var destination = subject?.Location ?? fallback;
        var result = transit ? _travel.TickTransit(destination,
            Math.Sqrt(Actor.Location.DistanceSqr(destination)) + Math.Sqrt(destination.DistanceSqr(LureEndpoint)),
            () => Current && (recipient == null || recipient.Current))
            : _travel.Tick(destination, subject, () => Current);
        _ready = result == GroundTransitionState.Ready && recipient?.Current == true
            ? new(recipient, Actor.Location, recipient.Unit.Location, Flightor.RequestIdentity,
                (Navigator.NavigationProvider as MeshNavigator)?.RequestIdentity) : null;
        return result;
    }
    private bool Ready(Recipient? target)
    {
        var ready = _ready;
        if (target == null || ready == null || !ReferenceEquals(target, ready.Target) || !Current || !target.Current
            || !Actor.Location.Equals(ready.ActorPosition) || !target.Unit.Location.Equals(ready.TargetPosition)
            || !ReferenceEquals(Flightor.RequestIdentity, ready.FlightOwner)
            || !ReferenceEquals((Navigator.NavigationProvider as MeshNavigator)?.RequestIdentity, ready.MeshOwner)
            || Actor.Combat || Actor.IsMoving || Actor.IsCasting || Actor.ChanneledCastingSpellId != 0
            || !target.Unit.WithinInteractRange || !Actor.TryGetMovementState(out uint flags, out ulong transport)
            || transport != 0 || (flags & 0x02003000u) != 0) return false;
        var state = WorldQueryObservation.ReadGroundUnitState(Actor);
        return !state.Mounted && !state.OnTaxi && !state.Rooted && !state.Stunned && Current && target.Current;
    }
    private QuestWorkflowReceipt Submit(string script, Recipient? target, Func<bool>? extra = null)
    {
        if (!Ready(target) || extra?.Invoke() == false) return QuestWorkflowReceipt.Rejected;
        bool entryAdmitted = false;
        try
        {
            var values = Lua.GetObservedReturnValues(script, () =>
            {
                bool permitted = Ready(target) && extra?.Invoke() != false;
                if (permitted) entryAdmitted = true;
                return permitted;
            });
            RequireCurrent();
            if (values.Count != 1) return QuestWorkflowReceipt.Pending;
            return values[0] switch
            {
                "submitted" => QuestWorkflowReceipt.Submitted, "pending" => QuestWorkflowReceipt.Pending,
                "progress-submitted" => QuestWorkflowReceipt.ProgressSubmitted, "reward-submitted" => QuestWorkflowReceipt.RewardSubmitted,
                "rejected" => QuestWorkflowReceipt.Rejected, _ => QuestWorkflowReceipt.Pending
            };
        }
        catch (ObservationUnavailableException) when (Current)
        { return entryAdmitted ? QuestWorkflowReceipt.Pending : QuestWorkflowReceipt.Rejected; }
    }
    public QuestWorkflowReceipt InteractVendor() => Interact(_vendor);
    public QuestWorkflowReceipt InteractViera() => Interact(_viera);
    private QuestWorkflowReceipt Interact(Recipient? target)
    {
        if (!Ready(target) || Now < _nextInteraction) return QuestWorkflowReceipt.Rejected;
        _nextInteraction = Now + 3;
        return GroundTransition.TryInteractWith(target!.Unit, () => Ready(target)) ? QuestWorkflowReceipt.Submitted : QuestWorkflowReceipt.Rejected;
    }
    public QuestWorkflowReceipt OpenVendor() => _vendor == null ? QuestWorkflowReceipt.Rejected
        : Submit(ArelionLureScripts.OpenVendor(_lifetime.ActorGuid, _vendor.Guid), _vendor);
    public QuestWorkflowReceipt BuyWine() => _vendor == null ? QuestWorkflowReceipt.Rejected
        : Submit(ArelionLureScripts.BuyWine(_lifetime.ActorGuid, _vendor.Guid), _vendor);
    public QuestWorkflowReceipt SelectLureQuest()
    {
        var target = _viera;
        if (!Ready(target)) return QuestWorkflowReceipt.Rejected;
        var entries = GossipFrame.Instance.AvailableQuests;
        var matching = entries.Where(q => q.Id == 9483).Take(2).ToArray();
        if (matching.Length != 1 || !Ready(target)) return QuestWorkflowReceipt.Rejected;
        var entry = matching[0];
        return Submit(ArelionLureScripts.SelectLure(_lifetime.ActorGuid, target!.Guid, entry.Index, entries.Count), target,
            () => entry.IsCurrent && entry.Id == 9483);
    }
    public QuestWorkflowReceipt AdvanceLureQuest()
    {
        var target = _viera;
        if (!Ready(target) || QuestFrame.Instance.CurrentShownQuestId != 9483) return QuestWorkflowReceipt.Rejected;
        var visible = Lua.GetObservedReturnValues("return QuestFrameCompleteQuestButton and QuestFrameCompleteQuestButton:IsVisible() and 1 or 0", () => Ready(target));
        if (visible.Count != 1 || visible[0] is not ("0" or "1")) return QuestWorkflowReceipt.Rejected;
        return Submit(ArelionLureScripts.AdvanceLure(_lifetime.ActorGuid, target!.Guid, visible[0] == "1"), target,
            () => QuestFrame.Instance.CurrentShownQuestId == 9483);
    }

    public QuestWorkflowReceipt UseScroll()
    {
        var target = _viera;
        if (!Ready(target) || target!.Unit.IsMoving || target.Unit.Location.DistanceSqr(LureEndpoint) > 64
            || (target.Unit.NpcFlags & 3u) != 0) return QuestWorkflowReceipt.Rejected;
        CloseOwnedLureDialog(target);
        if (!Ready(target)) return QuestWorkflowReceipt.Rejected;
        if (Actor.CurrentTargetGuid != target.Guid) { TargetOwned(target); return QuestWorkflowReceipt.Rejected; }
        if (!Actor.IsSafelyFacing(target.Unit, 45))
        { if (Ready(target)) Actor.SetFacing(target.Unit); return QuestWorkflowReceipt.Rejected; }
        var item = Actor.CarriedItems.FirstOrDefault(i => i.IsValid && i.Entry == 23693 && i.OwnerGuid == _lifetime.ActorGuid);
        if (item == null || !item.IsCooldownReady || !Ready(target) || Actor.Inventory?.Backpack == null) return QuestWorkflowReceipt.Rejected;
        ulong guid = item.Guid; uint address = item.BaseAddress;
        var backpack = Actor.Inventory.Backpack.ItemGuids;
        var bags = new ulong[4][];
        for (uint index = 0; index < 4; index++) bags[index] = Actor.GetBagAtIndex(index)?.ItemGuids ?? Array.Empty<ulong>();
        if (!WoWItem.TryResolveContainerLocation(guid, backpack, bags, out int bag, out int slot)) return QuestWorkflowReceipt.Rejected;
        bool ItemCurrent() => Ready(target) && Actor.CurrentTargetGuid == target.Guid && !target.Unit.IsMoving
            && (target.Unit.NpcFlags & 3u) == 0 && target.Unit.Location.DistanceSqr(LureEndpoint) <= 64
            && item.IsValid && item.Guid == guid && item.BaseAddress == address && item.Entry == 23693 && item.OwnerGuid == _lifetime.ActorGuid
            && WoWItem.IsContainerLocationCurrent(Actor, bag, slot, guid);
        string script = ArelionLureScripts.Scroll(_lifetime.ActorGuid, target.Guid, bag, slot);
        if (!ItemCurrent() || !RecoveryActions.BindContainerRequest(guid, 23693, script)) return QuestWorkflowReceipt.Rejected;
        var receipt = Submit(script, target, ItemCurrent);
        RecoveryActions.ObserveContainerReply(script, receipt is QuestWorkflowReceipt.Submitted or QuestWorkflowReceipt.Pending);
        return receipt;
    }
    private void TargetOwned(Recipient target)
    {
        if (!Ready(target) || !target.Unit.CanSelect) return;
        var executor = _lifetime.Executor;
        lock (executor.AssemblyLock)
        {
            if (!Ready(target) || !target.Unit.CanSelect) return;
            executor.Clear();
            executor.AddLine("push {0}", (uint)(target.Guid >> 32));
            executor.AddLine("push {0}", (uint)target.Guid);
            executor.AddLine("call {0}", 5393392U);
            executor.AddLine("add esp, 8"); executor.AddLine("retn");
            if (!Ready(target) || !target.Unit.CanSelect) return;
            executor.Execute();
        }
    }
    private void CloseOwnedMerchant(Recipient vendor)
    {
        RequireCurrent();
        string guid = "0X" + vendor.Guid.ToString("X16", CultureInfo.InvariantCulture);
        Lua.GetObservedReturnValues("if type(UnitGUID)=='function' and type(GetItemCount)=='function' and type(CloseMerchant)=='function' and string.upper(UnitGUID('npc') or '')=='" + guid
            + "' and GetItemCount(29112,false)>=1 and MerchantFrame and MerchantFrame:IsVisible() then CloseMerchant() end return 'observed'", () => Current && vendor.Current);
    }
    private void CloseOwnedLureDialog(Recipient viera)
    {
        string guid = "0X" + viera.Guid.ToString("X16", CultureInfo.InvariantCulture);
        Lua.GetObservedReturnValues("if string.upper(UnitGUID('npc') or '')=='" + guid
            + "' then if QuestFrame and QuestFrame:IsVisible() then CloseQuest() elseif GossipFrame and GossipFrame:IsVisible() then CloseGossip() end end return 'observed'",
            () => Ready(viera) && (viera.Unit.NpcFlags & 3u) == 0 && (QuestFrame.Instance.CurrentShownQuestId == 9483 || QuestFrame.Instance.CurrentShownQuestId == 0));
    }
    public void RetireMovement()
    {
        var old = _travel; _travel = null; _ready = null; _travelSubject = null; _travelRole = "";
        old?.Cancel();
    }
    public void Dispose() { _disposed = true; RetireMovement(); }
}
