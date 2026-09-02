// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedQuestPickUp
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using Styx;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Inventory.Frames;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedQuestPickUp : ForcedBehavior
{
    private static readonly Frame QuestTitleButton = new Frame("QuestTitleButton1");
    private static readonly Frame QuestFrameCompleteQuestButton = new Frame("QuestFrameCompleteQuestButton");
    private static readonly Frame QuestFrameAcceptButton = new Frame("QuestFrameAcceptButton");
    private static readonly Frame QuestFrameCompleteButton = new Frame("QuestFrameCompleteButton");
    private int lastShownQuestId = -1;
    private int _handleQuestFrameAttempts;
    private readonly QuestPickupMismatchTracker _mismatchTracker = new QuestPickupMismatchTracker();
    private IReadOnlyList<uint> _currentInteractionOfferedQuestIds = Array.Empty<uint>();

    public ForcedQuestPickUp(
        uint questId,
        string questName,
        uint giverId,
        string giverName,
        WoWPoint giverLocation,
        QuestObjectType? giverType)
    {
        this.QuestId = questId;
        this.QuestName = questName;
        this.GiverId = giverId;
        this.GiverName = giverName;
        this.GiverLocation = giverLocation;
        this.GiverType = giverType;
    }

    public override bool IsDone
    {
        get
        {
            if (PickupUnavailable)
                return true;

            // PickUp is done when:
            // 1) Quest is in completed quests cache (already turned in)
            // 2) Quest is in the quest log (just accepted, ready for objectives)
            try
            {
                // If quest is in completed cache, skip pickup
                if (ObjectManager.Me.QuestLog.TryGetAuthoritativeCompletedQuests(out var completedQuests)
                    && completedQuests.Contains(this.QuestId))
                    return true;

                // If quest is in log, the PickUp behavior is done
                // This is the key: we just need the quest IN the log, not completed
                if (ObjectManager.Me.QuestLog.ContainsQuest(this.QuestId))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public uint QuestId { get; private set; }

    public string QuestName { get; private set; }

    public uint GiverId { get; private set; }

    public string GiverName { get; private set; }

    public WoWPoint GiverLocation { get; private set; }

    public QuestObjectType? GiverType { get; private set; }

    public bool PickupUnavailable { get; private set; }

    public QuestAttemptOutcome LastOutcome { get; private set; }

    public override void OnStart()
    {
        if (ObjectManager.Me.QuestLog.GetAllQuests().Count >= 25)
        {
            Logging.Write(Color.Red, "You do not have any space in your quest log.");
            Logging.Write(Color.Red, "CopilotBuddy stopped!");
            TreeRoot.Stop();
        }
        string goalText = this.GetGoalText();
        Logging.Write("[PickUp] {0}", (object)goalText);
        TreeRoot.GoalText = goalText;
    }

    private string GetGoalText()
    {
        Styx.Logic.Questing.Quest quest = Styx.Logic.Questing.Quest.FromId(this.QuestId);
        QuestObjectType? giverType = this.GiverType;
        if ((giverType.GetValueOrDefault() != QuestObjectType.Item ? 0 : (giverType.HasValue ? 1 : 0)) != 0)
        {
            WoWItem woWitem = ObjectManager.Me.CarriedItems.FirstOrDefault<WoWItem>((Func<WoWItem, bool>)(woWItem_0 => (int)woWItem_0.Entry == (int)this.GiverId));
            if ((WoWObject)woWitem != (WoWObject)null && !string.IsNullOrEmpty(this.QuestName))
                return string.Format("Picking up quest {0} from item {1}", (object)this.QuestName, (object)woWitem.Name);
        }
        if (quest != null)
        {
            string str = string.Format("Picking up {0}", (object)quest.Name);
            return str;
        }
        if (string.IsNullOrEmpty(this.QuestName))
            return string.Format("Picking up quest with ID {0}", (object)this.QuestId);
        string questInfo = string.Format("Picking up {0}", (object)this.QuestName);
        return questInfo;
    }

    private static LocalPlayer Me => ObjectManager.Me;

    protected override Composite CreateBehavior()
    {
        return (Composite)new DecoratorIsNotPoiType((IEnumerable<PoiType>)new PoiType[3]
        {
            PoiType.Harvest,
            PoiType.Skin,
            PoiType.Loot
        }, (Composite)new PrioritySelector(new Composite[4]
        {
            (Composite)new Decorator(new CanRunDecoratorDelegate(this.ShouldSetPoi), (Composite)new ActionSetPoi(true, (RetrieveBotPoiDelegate)(context => new BotPoi(new PickUpNode(this.GiverLocation, this.GiverId, this.GiverName, this.GiverType, this.QuestId, this.QuestName))))),
            (Composite)new Decorator((CanRunDecoratorDelegate)(context =>
            {
                QuestObjectType? giverType = this.GiverType;
                return giverType.GetValueOrDefault() == QuestObjectType.Item && giverType.HasValue;
            }), (Composite)new Sequence((ContextChangeHandler)(context => (object)ObjectManager.GetObjectsOfType<WoWItem>().FirstOrDefault<WoWItem>((Func<WoWItem, bool>)(woWItem_0 => (int)woWItem_0.Entry == (int)this.GiverId))), new Composite[1]
            {
                (Composite)new DecoratorContinue((CanRunDecoratorDelegate)(context => context != null && context is WoWItem), (Composite)new Sequence(new Composite[3]
                {
                    (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => ((WoWItem)context).UseContainerItem())),
                    (Composite)new WaitContinue(5, new CanRunDecoratorDelegate(this.IsQuestFrameVisible), (Composite)new TreeSharp.Action((ActionDelegate)(context => this.HandleQuestFrame(context)))),
                    (Composite)new WaitContinue(2, (CanRunDecoratorDelegate)(context => false), (Composite)new ActionAlwaysSucceed())
                }))
            })),
            (Composite)new Decorator((CanRunDecoratorDelegate)(context => !(BotPoi.Current.AsObject != (WoWObject)null) ? (double)ForcedQuestPickUp.Me.Location.DistanceSqr(BotPoi.Current.Location) > 6.25 : !BotPoi.Current.AsObject.WithinInteractRange), (Composite)new ActionMoveToPoi()),
            (Composite)new Decorator((CanRunDecoratorDelegate)(context => BotPoi.Current.AsObject != (WoWObject)null && BotPoi.Current.AsObject.WithinInteractRange), (Composite)new Sequence((ContextChangeHandler)(context => (object)BotPoi.Current.AsObject), new Composite[11]
            {
                // HB 4.3.4: 10 elements in sequence
                (Composite)new ActionMoveStop(),
                (Composite)new TreeSharp.Action((ActionDelegate)(context => this.CloseFrames(context))),
                (Composite)new DecoratorContinue((CanRunDecoratorDelegate)(context => context is WoWUnit), (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => ((WoWUnit)context).Target()))),
                (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => ((WoWObject)context).Interact())),
                (Composite)new ActionSleep(1500),
                // DEBUG: log frame visibility after interact + sleep
                (Composite)new TreeSharp.Action((ActionDelegate)(context =>
                {
                    Logging.WriteDebug("[QuestPickUp] After interact: GossipFrame.IsVisible={0}, QuestTitleButton1.IsVisible={1}, QuestFrame.IsVisible={2}",
                        GossipFrame.Instance.IsVisible, ForcedQuestPickUp.QuestTitleButton.IsVisible, QuestFrame.Instance.IsVisible);
                    return RunStatus.Success;
                })),
                (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.IsGossipOrQuestListVisible), (Composite)new Sequence(new Composite[2]
                {
                    (Composite)new TreeSharp.Action((ActionDelegate)(context => this.SelectAvailableQuest(context))),
                    (Composite)new ActionSleep(500)
                })),
                // QuestFrame handler: uses CurrentShownQuestId from memory to distinguish
                // our pickup quest from a turn-in quest that the NPC shows first.
                // Handles: Continue button → reward selection → Complete Quest → Accept.
                (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.IsQuestFrameVisible),
                    (Composite)new TreeSharp.Action((ActionDelegate)(context => this.HandleQuestFrame(context)))),
                (Composite)new TreeSharp.Action((ActionDelegate)(context => this.ClearTarget(context))),
                (Composite)new TreeSharp.Action((ActionDelegate)(context => this.CloseFrames(context))),
                (Composite)new ActionClearPoi("Quest Completed")
            }))
        }));
    }

    private bool ShouldSetPoi(object context)
    {
        BotPoi current = BotPoi.Current;
        return current.Type != PoiType.QuestPickUp || (int)current.Entry != (int)this.GiverId;
    }

    private RunStatus CloseFrames(object context)
    {
        _handleQuestFrameAttempts = 0;
        _currentInteractionOfferedQuestIds = Array.Empty<uint>();
        if (!GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
            return RunStatus.Success;
        GossipFrame.Instance.Close();
        QuestFrame.Instance.Close();
        StyxWoW.Sleep(500);
        return RunStatus.Running;
    }

    private bool IsGossipOrQuestListVisible(object context)
    {
        return GossipFrame.Instance.IsVisible || ForcedQuestPickUp.QuestTitleButton.IsVisible;
    }

    private RunStatus SelectAvailableQuest(object context)
    {
        if (!GossipFrame.Instance.IsVisible && !ForcedQuestPickUp.QuestTitleButton.IsVisible)
            return RunStatus.Success;

        // HB 4.3.4 ForcedQuestPickUp.method_4 pattern:
        // Two distinct quest list sources in WotLK:
        //   1) GossipFrame.AvailableQuests  → gossip-based quests (GossipFrame visible)
        //   2) QuestFrame.AvailableQuests   → native multi-quest frame (QuestTitleButton1/2 visible, no GossipFrame)
        // Both paths end with GossipFrame.Instance.SelectAvailableQuest(index) which calls
        // SelectAvailableQuest(N) / SelectGossipAvailableQuest(N) — works for both frames.
        var gossipQuests = GossipFrame.Instance.AvailableQuests;
        var nativeQuests = QuestFrame.Instance.AvailableQuests;
        _currentInteractionOfferedQuestIds = gossipQuests
            .Select(quest => unchecked((uint)quest.Id))
            .Concat(nativeQuests)
            .Distinct()
            .ToArray();
        // WotLK 3.3.5a: GossipQuestEntry.Id from memory is unreliable (wrong struct layout).
        // GetGossipAvailableQuests() returns 5 values per quest: title, level, isTrivial, isRepeatable, isLegendary.
        List<string> luaDump = gossipQuests.Count > 0 ? Lua.GetReturnValues("return GetGossipAvailableQuests()") : null;

        int questIndex = -1;
        if (gossipQuests.Count > 0)
        {
            // Primary: match by quest ID from memory struct.
            // Cast both sides to long (HB 4.3.4 / 3.3.5a pattern) to avoid sign extension issues.
            for (int i = 0; i < gossipQuests.Count; i++)
            {
                if ((long)gossipQuests[i].Id == (long)this.QuestId)
                {
                    questIndex = i;
                    break;
                }
            }

            // Fallback: match by Lua name from GetGossipAvailableQuests().
            // WotLK 3.3.5a returns 5 values per quest: title, level, isTrivial, isRepeatable, isLegendary.
            // The memory struct (GossipQuestEntry.Id) is unreliable in 3.3.5a — Lua is authoritative.
            if (questIndex == -1 && luaDump != null && luaDump.Count >= 5)
            {
                // Try to match our quest by name (works when profile locale = client locale)
                const int valuesPerQuest = 5;
                for (int k = 0; k < luaDump.Count / valuesPerQuest; k++)
                {
                    if (!string.IsNullOrWhiteSpace(this.QuestName)
                        && string.Equals(
                            (luaDump[k * valuesPerQuest] ?? "").Trim(),
                            this.QuestName.Trim(),
                            StringComparison.Ordinal))
                    {
                        questIndex = k;
                        Logging.WriteDebug("[QuestPickUp] Found quest \"{0}\" via Lua name match at gossip index {1}.", this.QuestName, k);
                        break;
                    }
                }

            }
        }
        else
        {
            // Native multi-quest frame (QuestTitleButton1/2/etc.) — no GossipFrame open.
            if (nativeQuests.Count <= 0)
                return RunStatus.Success;

            for (int j = 0; j < nativeQuests.Count; j++)
            {
                if (nativeQuests[j] == this.QuestId)
                {
                    questIndex = j;
                    break;
                }
            }
        }

        if (questIndex == -1)
        {
            Logging.WriteDebug("[QuestPickUp] Quest \"{0}\" (id={1}) not found in gossip or native quest list (gossip={2}, native={3}).",
                this.QuestName, this.QuestId, gossipQuests.Count, nativeQuests.Count);
            return RunStatus.Failure;
        }

        GossipFrame.Instance.SelectAvailableQuest(questIndex);
        StyxWoW.Sleep(500);
        return RunStatus.Running;
    }

    private bool IsQuestFrameVisible(object context)
    {
        return QuestFrame.Instance.IsVisible;
    }

    /// <summary>
    /// Comprehensive QuestFrame handler for PickUp.
    /// Reads CurrentShownQuestId from memory (0xC0E92C) to determine what the NPC is showing.
    /// Handles the case where a turn-in quest opens directly before our pickup quest is available.
    ///
    /// WoW 3.3.5a QuestFrame button layout:
    ///   - QuestFrameAcceptButton: visible when a new quest can be accepted
    ///   - QuestFrameCompleteButton: "Continue" button shown first on turn-in dialogs
    ///   - QuestFrameCompleteQuestButton: "Complete Quest" shown after Continue + reward selection
    /// </summary>
    private RunStatus HandleQuestFrame(object context)
    {
        if (!QuestFrame.Instance.IsVisible)
            return RunStatus.Success;

        // Safety: prevent infinite retries within one interaction cycle
        if (_handleQuestFrameAttempts++ > 15)
        {
            Logging.WriteDebug("[QuestPickUp] HandleQuestFrame exceeded 15 attempts — closing frame and retrying.");
            _handleQuestFrameAttempts = 0;
            QuestFrame.Instance.Close();
            StyxWoW.Sleep(500);
            return RunStatus.Success;
        }

        uint shownQuestId = QuestFrame.Instance.CurrentShownQuestId;
        string shownQuestName = Lua.GetReturnVal<string>("return GetTitleText()", 0U) ?? "";
        bool acceptVisible = ForcedQuestPickUp.QuestFrameAcceptButton.IsVisible;
        bool continueVisible = ForcedQuestPickUp.QuestFrameCompleteButton.IsVisible;
        bool completeQuestVisible = ForcedQuestPickUp.QuestFrameCompleteQuestButton.IsVisible;
        int numChoices = Lua.GetReturnVal<int>("return GetNumQuestChoices()", 0U);
        bool hasAuthoritativeCompletion = ObjectManager.Me.QuestLog.TryGetAuthoritativeCompletedQuests(
            out var completedQuestIds);
        CompletedQuestCacheStatus completionStatus = ObjectManager.Me.QuestLog.CompletedQuestCacheStatus;
        bool shownQuestCompleted = hasAuthoritativeCompletion
            && shownQuestId != 0
            && completedQuestIds.Contains(shownQuestId);
        var liveOfferedQuestIds = GossipFrame.Instance.AvailableQuests
            .Select(quest => unchecked((uint)quest.Id))
            .Concat(QuestFrame.Instance.AvailableQuests)
            .Concat(_currentInteractionOfferedQuestIds)
            .Distinct()
            .ToArray();
        QuestPickupDialogDecision decision = QuestPickupDialogPolicy.Decide(
            this.QuestId,
            this.QuestName,
            shownQuestId,
            shownQuestName,
            this.GiverId,
            liveOfferedQuestIds,
            acceptVisible,
            continueVisible,
            completeQuestVisible,
            numChoices > 0,
            completionStatus,
            shownQuestCompleted);

        Logging.WriteDebug("[QuestPickUp] HandleQuestFrame: Action={0}, ShownId={1}, TargetId={2}, Accept={3}, Continue={4}, Complete={5}, Completion={6}",
            decision.Action, shownQuestId, this.QuestId, acceptVisible, continueVisible,
            completeQuestVisible, completionStatus);

        if (decision.Action == QuestPickupDialogAction.RejectMismatch)
        {
            LastOutcome = _mismatchTracker.Observe(decision);
            PickupUnavailable = _mismatchTracker.PickupUnavailable;
            Logging.WriteDebug(
                "[QuestPickUp] Rejected mismatched dialog (cycle {0}/3, unavailable={1}): {2}",
                _mismatchTracker.ConfirmedCycles,
                PickupUnavailable,
                decision.Evidence);
            QuestFrame.Instance.Close();
            StyxWoW.Sleep(500);
            _handleQuestFrameAttempts = 0;
            return RunStatus.Success;
        }

        ResetMismatchTracking();

        if (decision.Action == QuestPickupDialogAction.AcceptTarget)
        {
            Logging.WriteDebug("[QuestPickUp] Target quest identity confirmed — accepting quest.");
            QuestFrame.Instance.AcceptQuest();
            StyxWoW.Sleep(500);
            _handleQuestFrameAttempts = 0;
            return RunStatus.Success;
        }

        if (decision.Action == QuestPickupDialogAction.AdvanceCompletedQuest && continueVisible)
        {
            Logging.WriteDebug("[QuestPickUp] Authoritatively completed quest {0} — clicking Continue.", shownQuestId);
            QuestFrame.Instance.ClickContinue();
            StyxWoW.Sleep(1000);
            return RunStatus.Running;
        }

        if (decision.Action == QuestPickupDialogAction.AdvanceCompletedQuest
            && numChoices > 0
            && !completeQuestVisible)
        {
            Logging.WriteDebug("[QuestPickUp] Authoritatively completed quest {0} has {1} reward choices — selecting first.", shownQuestId, numChoices);
            QuestFrame.Instance.SelectQuestReward(0);
            StyxWoW.Sleep(500);
            return RunStatus.Running;
        }

        if (decision.Action == QuestPickupDialogAction.AdvanceCompletedQuest && completeQuestVisible)
        {
            Logging.WriteDebug("[QuestPickUp] Authoritatively completed quest {0} — completing turn-in.", shownQuestId);
            QuestFrame.Instance.CompleteQuest();
            StyxWoW.Sleep(500);
            return RunStatus.Running;
        }

        // No actionable button — frame might be transitioning. Close and let outer loop re-interact.
        Logging.WriteDebug("[QuestPickUp] QuestFrame visible but no actionable button (quest {0}) — closing.", shownQuestId);
        QuestFrame.Instance.Close();
        StyxWoW.Sleep(500);
        _handleQuestFrameAttempts = 0;
        return RunStatus.Success;
    }

    private void ResetMismatchTracking()
    {
        _mismatchTracker.Reset();
        PickupUnavailable = false;
        LastOutcome = null;
    }

    private bool IsCompleteQuestButtonVisible(object context)
    {
        return ForcedQuestPickUp.QuestFrameCompleteQuestButton.IsVisible;
    }

    private RunStatus CompleteQuestBeforeAccept(object context)
    {
        if (this.lastShownQuestId == -1)
            this.lastShownQuestId = (int)QuestFrame.Instance.CurrentShownQuestId;
        if (QuestFrame.Instance.IsVisible && (long)this.lastShownQuestId == (long)QuestFrame.Instance.CurrentShownQuestId)
        {
            QuestFrame.Instance.CompleteQuest();
            StyxWoW.Sleep(500);
            return RunStatus.Running;
        }
        this.lastShownQuestId = -1;
        return RunStatus.Success;
    }

    private RunStatus ClearTarget(object context)
    {
        if (!ObjectManager.Me.GotTarget)
            return RunStatus.Success;
        ObjectManager.Me.ClearTarget();
        StyxWoW.Sleep(300);
        return RunStatus.Running;
    }

    public override string ToString()
    {
        return string.Format("[ForcedQuestPickUp QuestId: {0}, QuestName: {1}]", (object)this.QuestId, (object)this.QuestName);
    }
}
