// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Actions.ForcedBehaviorExecutor
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Quest.QuestOrder;
using Styx;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using TreeSharp;

#nullable disable
namespace Bots.Quest.Actions;

public class ForcedBehaviorExecutor : Composite
{
    private readonly Func<bool> canExecute;

    public ForcedBehaviorExecutor(Bots.Quest.QuestOrder.QuestOrder order) : this(order, null) { }

    // Optional owner policy for instance-specific publication admission. Ordinary
    // QuestBot and existing callers retain the one-argument execution contract.
    public ForcedBehaviorExecutor(Bots.Quest.QuestOrder.QuestOrder order, Func<bool> canExecute)
    {
        this.Order = order != null ? order : throw new ArgumentNullException(nameof(order));
        this.canExecute = canExecute;
    }

    public Bots.Quest.QuestOrder.QuestOrder Order { get; private set; }

    public override void Start(object context)
    {
        // This executor owns a nested branch that is not a GroupComposite child.
        // A restart must release that lifetime before replacing its iterator.
        Stop(context);
        base.Start(context);
    }

    private bool Owns(OrderNodeCollection nodes, OrderNode node, ForcedBehavior behavior)
    {
        return ReferenceEquals(Order.Nodes, nodes)
            && ReferenceEquals(Order.CurrentNode, node)
            && ReferenceEquals(Order.CurrentBehavior, behavior)
            && (canExecute == null || canExecute())
            // Admission can observe reentrant host work; do not trust the identities
            // checked before that callback, including after OnTick/branch Start.
            && ReferenceEquals(Order.Nodes, nodes)
            && ReferenceEquals(Order.CurrentNode, node)
            && ReferenceEquals(Order.CurrentBehavior, behavior);
    }

    private sealed class BranchCleanup : CleanupHandler
    {
        private readonly Composite branch;

        internal BranchCleanup(ForcedBehaviorExecutor owner, Composite branch, object context)
            : base(owner, context)
        {
            this.branch = branch;
        }

        protected override void DoCleanup(object context) => branch.Stop(context);
    }

    private void ReleaseBranch(BranchCleanup cleanup)
    {
        if (cleanup == null)
            return;
        // Dispose marks itself first, so callbacks and repeated Stop cannot drain
        // this branch twice. Remove the completed registration on normal suspension.
        cleanup.Dispose();
        if (CleanupHandlers.Count > 0 && ReferenceEquals(CleanupHandlers.Peek(), cleanup))
            CleanupHandlers.Pop();
    }

    protected override IEnumerable<RunStatus> Execute(object context)
    {
        while (true)
        {
            var nodes = Order.Nodes;
            var node = Order.CurrentNode;
            var behavior = Order.CurrentBehavior;
            if (node == null || !Owns(nodes, node, behavior))
            {
                yield return RunStatus.Failure;
                yield break;
            }

            if (behavior == null)
            {
                var completion = GetQuestNodeCompletionAction(node);
                if (!Owns(nodes, node, null))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                if (completion == QuestNodeCompletionAction.Defer)
                {
                    yield return RunStatus.Running;
                    // Do not adopt a replacement order on this suspended iterator.
                    if (!Owns(nodes, node, null))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    continue;
                }

                try
                {
                    behavior = CreateForcedBehavior(node);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException)
                    && !(ex is System.Threading.ThreadInterruptedException))
                {
                    if (Owns(nodes, node, null))
                    {
                        if (node.Element != null)
                            Logging.Write(Color.Red, "Could not create current in quest bot; exception was thrown, Element: {0}", (object)node.Element);
                        else
                            Logging.Write(Color.Red, "Could not create current in quest bot; exception was thrown");
                        Logging.WriteException(ex);
                        if (Owns(nodes, node, null))
                            TreeRoot.Stop();
                    }
                }
                if (!Owns(nodes, node, null))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                if (behavior == null)
                {
                    Logging.Write("Could not create current in quest bot.");
                    if (Owns(nodes, node, null))
                        TreeRoot.Stop();
                    yield return RunStatus.Failure;
                    yield break;
                }
                Order.CurrentBehavior = behavior;
                TreeRoot.GoalText = "";
                if (!Owns(nodes, node, behavior))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                behavior.OnStart();
            }

            Composite branch = null;
            BranchCleanup cleanup = null;
            while (true)
            {
                if (!Owns(nodes, node, behavior))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                bool done = behavior.IsDone;
                if (!Owns(nodes, node, behavior))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                if (done)
                {
                    ReleaseBranch(cleanup);
                    if (!Owns(nodes, node, behavior))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    Logging.WriteDiagnostic("[FBE] Advancing past {0} (IsDone=true), remaining nodes: {1}",
                        behavior, nodes.Count);
                    if (!Owns(nodes, node, behavior))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    behavior.Dispose();
                    if (!Owns(nodes, node, behavior))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    var nextNode = nodes.Count > 1 ? nodes[1] : null;
                    Order.CurrentBehavior = null;
                    Order.Advance();
                    // OnNoMoreNodes can publish replacement work during Advance.
                    if (!Owns(nodes, nextNode, null))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    break;
                }

                bool deferred = behavior.IsExecutionDeferred;
                if (!Owns(nodes, node, behavior))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                if (deferred)
                {
                    ReleaseBranch(cleanup);
                    cleanup = null;
                    branch = null;
                    if (!Owns(nodes, node, behavior))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    // Preserve the generic suspended-iterator compatibility contract.
                    // Root-level protective preemption is a separate owner policy.
                    yield return RunStatus.Running;
                    continue;
                }

                if (branch == null)
                {
                    behavior.OnTick();
                    if (!Owns(nodes, node, behavior))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    branch = behavior.Branch;
                    if (!Owns(nodes, node, behavior) || branch == null)
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                    cleanup = new BranchCleanup(this, branch, context);
                    CleanupHandlers.Push(cleanup);
                    branch.Start(context);
                    if (!Owns(nodes, node, behavior))
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }
                }

                var status = branch.Tick(context);
                if (!Owns(nodes, node, behavior))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                if (status == RunStatus.Running)
                {
                    yield return RunStatus.Running;
                    continue;
                }
                ReleaseBranch(cleanup);
                // Cleanup is a callback boundary; never read a replacement's status.
                yield return Owns(nodes, node, behavior) ? status : RunStatus.Failure;
                yield break;
            }
        }
    }

    private ForcedBehavior CreateForcedBehavior(OrderNode orderNode)
    {
        switch (orderNode.Type)
        {
            case OrderNodeType.Checkpoint:
                return (ForcedBehavior)new ForcedNothing();
            case OrderNodeType.If:
                return (ForcedBehavior)new ForcedIf((IfNode)orderNode);
            case OrderNodeType.While:
                return (ForcedBehavior)new ForcedWhile((WhileNode)orderNode);
            case OrderNodeType.PickUp:
                if (GetQuestNodeCompletionAction(orderNode) == QuestNodeCompletionAction.Skip)
                    return (ForcedBehavior)new ForcedNothing();
                ForcedQuestPickUp pickUp = CreateQuestPickUp((PickUpNode)orderNode);
                // If PickUp returns null (already completed or error), use ForcedNothing to skip
                return pickUp != null ? (ForcedBehavior)pickUp : (ForcedBehavior)new ForcedNothing();
            case OrderNodeType.TurnIn:
                if (GetQuestNodeCompletionAction(orderNode) == QuestNodeCompletionAction.Skip)
                    return (ForcedBehavior)new ForcedNothing();
                ForcedQuestTurnIn turnIn = CreateQuestTurnIn((TurnInNode)orderNode);
                // If TurnIn returns null (already completed), use ForcedNothing to skip
                return turnIn != null ? (ForcedBehavior)turnIn : (ForcedBehavior)new ForcedNothing();
            case OrderNodeType.Objective:
                ObjectiveNode objectiveNode = (ObjectiveNode)orderNode;
                if (GetQuestNodeCompletionAction(orderNode) == QuestNodeCompletionAction.Skip)
                {
                    Logging.WriteDebug("Quest {0} is already completed. Skipping Objective.", (object)objectiveNode.QuestId);
                    return (ForcedBehavior)new ForcedNothing();
                }
                Bots.Quest.Objectives.QuestObjective objective = CreateQuestObjective(objectiveNode);
                if (objective != (Bots.Quest.Objectives.QuestObjective)null)
                    return (ForcedBehavior)new ForcedQuestObjective(objective);
                // If quest is not in log (maybe completed between nodes), skip instead of stopping
                if (!ObjectManager.Me.QuestLog.ContainsQuest(objectiveNode.QuestId))
                {
                    Logging.WriteDebug("Quest {0} not in log. Skipping Objective.", (object)objectiveNode.QuestId);
                    return (ForcedBehavior)new ForcedNothing();
                }
                Logging.Write("Could not create a performable quest objective for objective with ID {0}.", (object)objectiveNode.ObjectiveId);
                return (ForcedBehavior)null;
            case OrderNodeType.SetGrindArea:
                SetGrindAreaNode setGrindAreaNode = (SetGrindAreaNode)orderNode;
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() =>
                {
                    QuestState.Instance.CurrentGrindArea = setGrindAreaNode.GetArea();
                }));
            case OrderNodeType.ClearGrindArea:
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() => StyxWoW.AreaManager.SetArea((GrindArea)null)));
            case OrderNodeType.SetMailbox:
                SetMailboxNode setMailboxNode = (SetMailboxNode)orderNode;
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() =>
                {
                    QuestState.Instance.CurrentMailboxes = setMailboxNode.Mailboxes;
                    ProfileManager.CurrentProfile.MailboxManager.ForcedMailboxes = setMailboxNode.Mailboxes;
                }));
            case OrderNodeType.ClearMailbox:
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() => ProfileManager.CurrentProfile.MailboxManager.ForcedMailboxes = (List<Mailbox>)null));
            case OrderNodeType.SetVendor:
                SetVendorNode setVendorNode = (SetVendorNode)orderNode;
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() =>
                {
                    QuestState.Instance.CurrentVendors = setVendorNode.Vendors;
                    ProfileManager.CurrentProfile.VendorManager.ForcedVendors = setVendorNode.Vendors;
                }));
            case OrderNodeType.ClearVendor:
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() => ProfileManager.CurrentProfile.VendorManager.ForcedVendors = (List<Vendor>)null));
            case OrderNodeType.DisableRepair:
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() => Vendors.RepairDisabled = true));
            case OrderNodeType.EnableRepair:
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() => Vendors.RepairDisabled = false));
            case OrderNodeType.GrindTo:
                return (ForcedBehavior)new ForcedGrindTo((GrindToNode)orderNode);
            case OrderNodeType.AbandonQuest:
                AbandonQuestNode abandonQuestNode = (AbandonQuestNode)orderNode;
                return (ForcedBehavior)new ForcedSingleton(new System.Action(() =>
                {
                    StyxWoW.Me.QuestLog.AbandonQuestById(abandonQuestNode.QuestId);
                }));
            case OrderNodeType.MoveTo:
                MoveToNode moveToNode = (MoveToNode)orderNode;
                return (ForcedBehavior)new ForcedMoveTo(moveToNode.Location, moveToNode.LocationName, moveToNode.Precision, moveToNode.QuestId);
            case OrderNodeType.UseItem:
                UseItemNode useItemNode = (UseItemNode)orderNode;
                return (ForcedBehavior)new ForcedUseItem(useItemNode.ItemRetriever, useItemNode.TargetRetriever, useItemNode.ForceUse, useItemNode.QuestId, useItemNode.Location);
            case OrderNodeType.Code:
                return (ForcedBehavior)new ForcedCodeBehavior((CodeNode)orderNode);
            default:
                return (ForcedBehavior)null;
        }
    }

    private static QuestNodeCompletionAction GetQuestNodeCompletionAction(OrderNode orderNode)
    {
        uint questId;
        Func<QuestCompletionState, bool, QuestNodeCompletionAction> policy;
        switch (orderNode.Type)
        {
            case OrderNodeType.PickUp:
                questId = ((PickUpNode)orderNode).QuestId;
                policy = QuestNodeCompletionPolicy.ForPickup;
                break;
            case OrderNodeType.TurnIn:
                questId = ((TurnInNode)orderNode).QuestId;
                policy = QuestNodeCompletionPolicy.ForTurnIn;
                break;
            case OrderNodeType.Objective:
                questId = ((ObjectiveNode)orderNode).QuestId;
                policy = QuestNodeCompletionPolicy.ForObjective;
                break;
            default:
                return QuestNodeCompletionAction.Execute;
        }

        QuestCompletionSnapshot snapshot = ObjectManager.Me.QuestLog.GetQuestCompletionSnapshot(questId);
        return policy(snapshot.State, snapshot.IsAccepted);
    }

    private static ForcedQuestPickUp CreateQuestPickUp(PickUpNode pickUpNode)
    {
        WoWPoint giverLocation;
        if (pickUpNode.GiverLocation != WoWPoint.Zero)
        {
            giverLocation = pickUpNode.GiverLocation;
        }
        else
        {
            if (pickUpNode.GiverType.HasValue)
            {
                switch (pickUpNode.GiverType.Value)
                {
                    case QuestObjectType.GameObject:
                        Logging.Write("Can not pick up a quest from a gameobject without specifying a location. Please check your profile.");
                        return (ForcedQuestPickUp)null;
                    case QuestObjectType.Npc:
                        NpcResult npcById1 = NpcQueries.GetNpcById(pickUpNode.GiverId);
                        if (npcById1 == (NpcResult)null)
                        {
                            Logging.Write("Could not find quest giver NPC with ID {0} in database.", (object)pickUpNode.GiverId);
                            return (ForcedQuestPickUp)null;
                        }
                        giverLocation = npcById1.Location;
                        break;
                    case QuestObjectType.Item:
                        if ((WoWObject)StyxWoW.Me.CarriedItems.FirstOrDefault<WoWItem>((Func<WoWItem, bool>)(woWItem => (int)woWItem.Entry == (int)pickUpNode.GiverId)) == (WoWObject)null)
                        {
                            Logging.Write(Color.Red, "Could not pickup quest from item with id:{0} the item was not found!", (object)pickUpNode.GiverId);
                            Logging.Write(Color.Red, "CopilotBuddy Stopped!");
                            TreeRoot.Stop();
                        }
                        giverLocation = WoWPoint.Empty;
                        break;
                    default:
                        return (ForcedQuestPickUp)null;
                }
            }
            else
            {
                NpcResult npcById2 = NpcQueries.GetNpcById(pickUpNode.GiverId);
                if (npcById2 == (NpcResult)null)
                {
                    Logging.Write("Could not find quest giver NPC with ID {0} in database.", (object)pickUpNode.GiverId);
                    return (ForcedQuestPickUp)null;
                }
                giverLocation = npcById2.Location;
            }
        }
        return new ForcedQuestPickUp(pickUpNode.QuestId, pickUpNode.QuestName, pickUpNode.GiverId, pickUpNode.GiverName, giverLocation, pickUpNode.GiverType);
    }

    private static ForcedQuestTurnIn CreateQuestTurnIn(TurnInNode turnInNode)
    {
        PlayerQuest questById = ObjectManager.Me.QuestLog.GetQuestById(turnInNode.QuestId);
        if (questById == null)
        {
            Logging.Write("Can not turn in quest {0} (ID: {1}) because I don't have it in my quest log! (Or do I: {2})", (object)Utilities.GetObjectString((object)turnInNode.QuestName, "(null)"), (object)turnInNode.QuestId, (object)ObjectManager.Me.QuestLog.ContainsQuest(turnInNode.QuestId));
            return (ForcedQuestTurnIn)null;
        }
        
        // Try to get quest object for completion info (may be null if not in cache)
        if (ProfileManager.CurrentProfile != (Profile)null)
        {
            QuestInfo quest = ProfileManager.CurrentProfile.FindQuest(turnInNode.QuestId);
            if (quest != null)
            {
                TurnInObjectiveInfo turnIn = quest.FindTurnIn();
                if (turnIn != null)
                    return new ForcedQuestTurnIn(turnInNode.QuestId, turnInNode.QuestName, turnInNode.TurnInId, turnInNode.TurnInName, turnIn.Location, turnInNode.TurnInType);
            }
        }
        
        // If quest object is null (cache miss), use profile or NPC location as fallback
        WoWQuestCompletionInfo completionInfo = new WoWQuestCompletionInfo();
        if (questById != null)
            completionInfo = questById.GetCompletionInfo();
        WoWQuestStep? nullable = new WoWQuestStep?();
        foreach (WoWQuestStep step in completionInfo.Steps.Steps)
        {
            if (step.PoiObjectiveIndex == -1)
            {
                nullable = new WoWQuestStep?(step);
                break;
            }
        }
        if (turnInNode.TurnInLocation != WoWPoint.Zero)
            return new ForcedQuestTurnIn(turnInNode.QuestId, turnInNode.QuestName, turnInNode.TurnInId, turnInNode.TurnInName, turnInNode.TurnInLocation, turnInNode.TurnInType);
        NpcResult npcById = NpcQueries.GetNpcById(turnInNode.TurnInId);
        if (npcById != (NpcResult)null && (!nullable.HasValue || (double)npcById.Location.Distance2DSqr(new WoWPoint(nullable.Value.StepPosition.X, nullable.Value.StepPosition.Y, 0.0f)) <= 400.0))
            return new ForcedQuestTurnIn(turnInNode.QuestId, turnInNode.QuestName, turnInNode.TurnInId, turnInNode.TurnInName, npcById.Location, turnInNode.TurnInType);
        if (!nullable.HasValue)
        {
            Logging.Write("Could not find turn in step. Please specify a turn in override.");
            return (ForcedQuestTurnIn)null;
        }
        var xnaVec = new Tripper.XNAMath.Vector3(nullable.Value.StepPosition.X, nullable.Value.StepPosition.Y, 0.0f);
        if (Navigator.FindHeight(ref xnaVec))
            return new ForcedQuestTurnIn(turnInNode.QuestId, turnInNode.QuestName, turnInNode.TurnInId, turnInNode.TurnInName, new WoWPoint(xnaVec.X, xnaVec.Y, xnaVec.Z), turnInNode.TurnInType);
        Logging.Write("Could not find a height to turn in quest {0}. Consider overriding this quest in your profile.", (object)questById.Name);
        return (ForcedQuestTurnIn)null;
    }

    internal static int ResolveQuestObjectiveIndex(ObjectiveNode node,
        IReadOnlyList<Styx.Logic.Questing.Quest.QuestObjective> objectives)
    {
        // Generated dataset rows can describe alternative sources for one item.
        // Their row index need not equal the live quest's objective index.
        int index = node.ObjectiveIndex;
        if (index >= 0 && index < objectives.Count &&
            (node.ObjectiveId == 0 || objectives[index].ID == node.ObjectiveId))
            return index;
        if (node.ObjectiveId != 0)
            for (int i = 0; i < objectives.Count; i++)
                if (objectives[i].ID == node.ObjectiveId)
                    return i;
        return -1;
    }
    private static Bots.Quest.Objectives.QuestObjective CreateQuestObjective(ObjectiveNode objectiveNode)
    {
        // Check if quest is in log
        if (!ObjectManager.Me.QuestLog.ContainsQuest(objectiveNode.QuestId))
        {
            Logging.Write("Could not find quest with ID {0} in quest log.", (object)objectiveNode.QuestId);
            return (Bots.Quest.Objectives.QuestObjective)null;
        }
        
        // Get PlayerQuest from quest log
        PlayerQuest questById = ObjectManager.Me.QuestLog.GetQuestById(objectiveNode.QuestId);
        if (questById == null)
        {
            Logging.Write("Quest {0} is in log but GetQuestById returned null.", (object)objectiveNode.QuestId);
            return (Bots.Quest.Objectives.QuestObjective)null;
        }
        
        // Get objectives from cache
        List<Styx.Logic.Questing.Quest.QuestObjective> objectives = questById.GetObjectives();
        Styx.Logic.Questing.Quest.QuestObjective? nullable = new Styx.Logic.Questing.Quest.QuestObjective?();
        int objectiveIndex = 0;
        
        objectiveIndex = ResolveQuestObjectiveIndex(objectiveNode, objectives);
        if (objectiveIndex >= 0)
            nullable = objectives[objectiveIndex];
        if (!nullable.HasValue)
        {
            Logging.Write("Could not find objective with ID {0} or Index {1} in quest {2}.", 
                (object)objectiveNode.ObjectiveId, (object)objectiveNode.ObjectiveIndex, (object)questById.Name);
            return (Bots.Quest.Objectives.QuestObjective)null;
        }
        
        WoWQuestCompletionInfo completionInfo = questById.GetCompletionInfo();
        List<WoWQuestStep> list = ((IEnumerable<WoWQuestStep>)completionInfo.Steps.Steps).Where<WoWQuestStep>((Func<WoWQuestStep, bool>)(s => s.PoiObjectiveIndex == objectiveIndex)).ToList<WoWQuestStep>();
        return QuestManager.CreateQuestObjective(nullable.Value, questById, list, (List<Bots.Quest.Objectives.QuestObjective>)null);
    }
}
