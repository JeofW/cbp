using System;
using System.Linq;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory.Frames.LootFrame;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

#nullable disable
namespace Bots.Quest
{
    /// <summary>Bound the old loot owner's drain before a mandatory quest stage.</summary>
    public sealed class QuestLootHandoff : Decorator
    {
        private sealed class Drain
        {
            internal object Stage, Actor, Memory, Run, Profile;
            internal BotPoi Poi;
            internal WoWObject Subject;
            internal long Generation, Started, CloseRequested;
            internal ulong Guid;
            internal uint Map, Address;
            internal int ProcessId;
            internal string Reason;
        }
        private static Drain drain;

        public QuestLootHandoff(Composite loot) : base(loot) { }

        private static object Stage()
        {
            var order = QuestState.Instance.Order;
            // Generated work is normally nested in an If. Follow only wrappers
            // still selected by their own order; a global last-created order can
            // otherwise retain a disposed profile's mandatory stage.
            for (int depth = 0; depth < 64; depth++)
            {
                var child = order.CurrentBehavior switch
                {
                    ForcedIf conditional when ReferenceEquals(conditional.IfNode, order.CurrentNode) => conditional.ActiveOrder,
                    ForcedWhile loop when ReferenceEquals(loop.WhileNode, order.CurrentNode) => loop.ActiveOrder,
                    _ => null
                };
                if (child == null)
                {
                    // Give an uninitialized generated conditional one execution
                    // opportunity after the drain budget. Evaluating conditions
                    // remains the conditional owner's job.
                    if (order.CurrentNode is IfNode pendingIf && pendingIf.Body.FirstOrDefault() is PickUpNode or TurnInNode) return pendingIf;
                    if (order.CurrentNode is WhileNode pendingWhile && pendingWhile.Body.FirstOrDefault() is PickUpNode or TurnInNode) return pendingWhile;
                    break;
                }
                order = child;
            }
            if (order.CurrentNode is not (PickUpNode or TurnInNode)) return null;
            if (order.CurrentNode is PickUpNode pickup && order.CurrentBehavior is ForcedQuestPickUp pickupOwner && pickupOwner.QuestId == pickup.QuestId) return pickupOwner;
            if (order.CurrentNode is TurnInNode turnin && order.CurrentBehavior is ForcedQuestTurnIn turninOwner && turninOwner.QuestId == turnin.QuestId) return turninOwner;
            return order.CurrentNode;
        }

        public override RunStatus Tick(object context)
        {
            if (!AllowLoot(Stage(), () => DecoratedChild.Stop(context)))
            {
                base.Stop(context);
                LastStatus = RunStatus.Failure;
                return RunStatus.Failure;
            }
            return base.Tick(context);
        }

        // Also used by the actual forced owners: direct execution cannot remain
        // indefinitely vetoed by a corpse left by the previous objective.
        public static bool CanRunMandatory(ForcedBehavior owner) => ReferenceEquals(Stage(), owner) && !AllowLoot(owner, null);

        private static bool AllowLoot(object stage, System.Action beforeRelease)
        {
            if (stage == null) { drain = null; return true; }
            try
            {
                var actor = ObjectManager.Me;
                var poi = BotPoi.Current;
                if (!TreeRoot.IsRunning || actor == null || !actor.IsAlive || actor.Combat)
                {
                    drain = null;
                    return true; // Combat/death retain their higher-priority owners.
                }
                if (poi.Type is not (PoiType.Loot or PoiType.Harvest or PoiType.Skin))
                {
                    drain = null;
                    return false; // Do not acquire new incidental loot in this stage.
                }
                var subject = poi.AsObject; // Resolving a wrapper can change POI generation.
                var generation = BotPoi.CurrentGeneration;
                if (drain == null || !ReferenceEquals(drain.Stage, stage)
                    || !ReferenceEquals(drain.Actor, actor) || !ReferenceEquals(drain.Memory, ObjectManager.Wow)
                    || !ReferenceEquals(drain.Run, TreeRoot.RunIdentity)
                    || !ReferenceEquals(drain.Profile, ProfileManager.CurrentProfileSnapshot)
                    || !ReferenceEquals(drain.Poi, poi) || drain.Generation != generation
                    || !ReferenceEquals(drain.Subject, subject) || drain.Map != actor.MapId || drain.Guid != actor.Guid
                    || drain.Address != actor.BaseAddress || drain.ProcessId != ObjectManager.Wow.ProcessId)
                    drain = new Drain { Stage = stage, Actor = actor, Memory = ObjectManager.Wow, Run = TreeRoot.RunIdentity,
                        Profile = ProfileManager.CurrentProfileSnapshot, Poi = poi, Subject = subject, Generation = generation,
                        Started = Environment.TickCount64, Guid = actor.Guid, Map = actor.MapId, Address = actor.BaseAddress,
                        ProcessId = ObjectManager.Wow.ProcessId };
                var owner = drain;
                bool Current() => ReferenceEquals(drain, owner) && ReferenceEquals(ObjectManager.Me, actor)
                    && ReferenceEquals(ObjectManager.Wow, owner.Memory) && ReferenceEquals(TreeRoot.RunIdentity, owner.Run)
                    && TreeRoot.IsRunning && actor.IsAlive && !actor.Combat && actor.MapId == owner.Map && actor.Guid == owner.Guid
                    && actor.BaseAddress == owner.Address && ObjectManager.Wow.ProcessId == owner.ProcessId
                    && ReferenceEquals(ProfileManager.CurrentProfileSnapshot, owner.Profile)
                    && ReferenceEquals(BotPoi.Current, poi) && BotPoi.CurrentGeneration == generation
                    && ReferenceEquals(poi.AsObject, subject) && BotPoi.CurrentGeneration == generation
                    && ReferenceEquals(Stage(), stage);
                if (!Current()) return true;

                long elapsed = Environment.TickCount64 - owner.Started;
                ulong frame = LootFrame.Instance.LootingObjectGuid;
                bool stale = subject == null || !subject.IsValid;
                bool? required = stale ? false : RequiredQuestLoot(subject);
                int budget = required == true ? 10000 : 4000;
                string reason = frame == poi.Guid && frame != 0 ? "loot-frame-in-flight"
                    : stale ? "stale-loot-subject" : required == true ? "required-quest-loot"
                    : required == null ? "quest-loot-requirement-unknown" : "incidental-loot";
                if (owner.Reason != reason)
                {
                    owner.Reason = reason;
                    Logging.WriteDiagnostic("[QuestLootHandoff] stage={0} poi={1} guid={2} reason={3} budgetMs={4}",
                        stage.GetType().Name, poi.Type, poi.Guid, reason, budget);
                }
                if (!Current()) return true;
                if ((!stale || frame != 0) && elapsed < budget) return true;

                if (stage is IfNode or WhileNode)
                {
                    // Conditional admission is not quest-stage authority. Pause
                    // loot for one root tick but retain the exact corpse POI.
                    beforeRelease?.Invoke();
                    if (!Current()) return true;
                    owner.Started = Environment.TickCount64;
                    return false;
                }

                // Drain one matching open window before yielding; never close a
                // different window or claim its closure from the request alone.
                if (frame != 0 && frame == poi.Guid)
                {
                    if (owner.CloseRequested == 0)
                    {
                        owner.CloseRequested = Environment.TickCount64;
                        if (Current()) LootFrame.Instance.Close();
                        return true;
                    }
                    if (Environment.TickCount64 - owner.CloseRequested < 2000) return true;
                }
                beforeRelease?.Invoke();
                if (!Current()) return true;
                Logging.WriteDiagnostic("[QuestLootHandoff] yielding {0} guid={1} to {2}; reason={3}; elapsedMs={4}; requeue=eligible-after-stage",
                    poi.Type, poi.Guid, stage.GetType().Name, reason, elapsed);
                // Do not blacklist or mark looted. The ordinary selector can
                // reconsider this source after mandatory work finishes.
                if (!Current()) return true;
                BotPoi.Clear("Bounded loot drain yielded to mandatory quest stage: " + reason);
                return false;
            }
            catch (Exception error)
            {
                RecoveryActions.ReportDeferral(error, "Mandatory quest loot handoff");
                return true;
            }
        }

        private static bool? RequiredQuestLoot(WoWObject subject)
        {
            var log = ObjectManager.Me.QuestLog;
            var snapshot = log.CaptureSnapshot();
            if (!snapshot.IsComplete) return null;
            var needed = snapshot.Quests.Where(quest => !snapshot.ReadyQuestIds.Contains(quest.Id)
                && !snapshot.FailedQuestIds.Contains(quest.Id)).SelectMany(quest => quest.CollectItemIds)
                .Where(id => id > 0).Select(id => (uint)id).ToHashSet();
            if (!log.IsSnapshotCurrent(snapshot)) return null;
            if (needed.Count == 0) return false;
            if (subject is WoWUnit unit)
                return unit.GetCachedInfo(out var info) ? info.QuestItems.Any(needed.Contains) : null;
            if (subject is WoWGameObject gameObject)
                return gameObject.GetCachedInfo(out var info) ? info.QuestItems.Any(id => needed.Contains((uint)id)) : null;
            return null;
        }
    }
}
