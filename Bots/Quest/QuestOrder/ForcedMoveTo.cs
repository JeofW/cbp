// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedMoveTo
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using System;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedMoveTo : ForcedBehavior
{
    private bool hasReachedLocation;
    private readonly NavType? _navType;
    private LocalPlayer _actor;
    private ulong _actorGuid;
    private uint _map;

    private bool OwnsActor()
    {
        var actor = ObjectManager.Me;
        if (actor == null || !actor.IsValid || !actor.IsAlive || actor.Guid == 0) return false;
        if (_actor == null)
        {
            _actor = actor;
            _actorGuid = actor.Guid;
            _map = actor.MapId;
        }
        return ReferenceEquals(actor, _actor) && actor.Guid == _actorGuid && actor.MapId == _map;
    }

    private static bool IsFinitePoint(WoWPoint point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    public ForcedMoveTo(WoWPoint location, uint questId)
        : this(location, null, 1.5f, questId, null)
    {
    }

    public ForcedMoveTo(WoWPoint location, string locationName, float precision, uint questId, NavType? navType = null)
    {
        this.Location = location;
        this.LocationName = locationName ?? $"<{location.X.ToStringInvariant()}, {location.Y.ToStringInvariant()}, {location.Z.ToStringInvariant()}>";
        this.Precision = precision;
        this.QuestId = questId;
        _navType = navType;
    }

    public WoWPoint Location { get; private set; }

    public string LocationName { get; private set; }

    public float Precision { get; private set; }

    public uint QuestId { get; private set; }

    // Legion: ForcedBehavior.NavType override — null means auto-detect.
    public override NavType? NavType => _navType;

    protected override Composite CreateBehavior()
    {
        return new Action(context =>
        {
            if (!OwnsActor() || !IsFinitePoint(Location) || !float.IsFinite(Precision) || Precision < 0)
                return RunStatus.Failure;
            var actor = _actor;
            object provider = Navigator.NavigationProvider;
            bool Current() => OwnsActor() && !actor.IsCasting && actor.ChanneledCastingSpellId == 0
                && ReferenceEquals(provider, Navigator.NavigationProvider);
            if (!Current() || !IsFinitePoint(actor.Location)) return RunStatus.Failure;
            double distanceSquared = actor.Location.DistanceSqr(Location);
            if (distanceSquared <= (double)Precision * Precision && Current())
            {
                hasReachedLocation = true;
                return RunStatus.Success;
            }

            // Resolve effective NavType: node override → QuestOrder auto-detect (Flightor.CanFly).
            // QuestOrder.NavType is non-nullable and always returns a value — no further fallback needed.
            // Legion: ForcedMoveTo.method_0 line 70 used QuestOrder.Instance.NavType directly.
            NavType effective = _navType ?? QuestOrder.Instance?.NavType ?? (Flightor.CanFly ? Styx.NavType.Fly : Styx.NavType.Run);
            if (!Current()) return RunStatus.Failure;

            if (effective == Styx.NavType.Fly)
            {
                if (distanceSquared < 100f)
                {
                    // A dismount request (which may safely refuse) is not arrival.
                    // Keep the configured 3D precision instead of completing ten
                    // yards early, and never use ground movement while still airborne.
                    Mount.Dismount("ForcedMoveTo: reached destination");
                    if (!Current()) return RunStatus.Failure;
                    bool airborne = actor.Mounted || actor.MovementInfo.IsFlying || actor.IsFalling;
                    if (!Current()) return RunStatus.Failure;
                    if (!airborne)
                    {
                        MoveResult result = Navigator.MoveTo(Location);
                        return Current() ? Navigator.GetRunStatusFromMoveResult(result) : RunStatus.Failure;
                    }
                }
                Flightor.MoveTo(Location, 40f);
                return Current() ? RunStatus.Success : RunStatus.Failure; // local void dispatch, not arrival
            }
            if (effective == Styx.NavType.Run)
            {
                bool shouldMount = Mount.ShouldMount(Location);
                if (!Current()) return RunStatus.Failure;
                if (shouldMount)
                {
                    Mount.StateMount((LocationRetriever)(() => Current() ? Location : WoWPoint.Empty));
                    if (!Current()) return RunStatus.Failure;
                }
                MoveResult result = Navigator.MoveTo(Location);
                return Current() ? Navigator.GetRunStatusFromMoveResult(result) : RunStatus.Failure;
            }
            return RunStatus.Failure;
        });
    }

    public override bool IsDone
    {
        get
        {
            if (!OwnsActor()) return false;
            if (this.QuestId == 0U)
                return this.hasReachedLocation;
            PlayerQuest questById = _actor.QuestLog.GetQuestById(this.QuestId);
            if (!OwnsActor()) return false;
            if (this.hasReachedLocation)
                return true;
            if (questById != null)
                return questById.IsCompleted;
            return false;
        }
    }

    public override void OnStart()
    {
        if (!OwnsActor()) return;
        string goalText = string.Format("Moving to {0}", (object)this.LocationName);
        Logging.Write("[MoveTo] {0}", (object)goalText);
        if (OwnsActor()) TreeRoot.GoalText = goalText;
    }
}
