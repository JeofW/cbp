using Styx;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;

namespace Styx.Logic.Questing.Recovery;

public static class QuestPrerequisiteAuthority
{
    public static QuestPrerequisiteStatus Determine(
        bool relationDataAvailable,
        uint nextQuestId,
        bool nextQuestIsAccepted,
        bool activeGuideContainsNextQuest)
    {
        if (!relationDataAvailable)
            return QuestPrerequisiteStatus.Unknown;
        if (nextQuestId == 0)
            return QuestPrerequisiteStatus.NotActive;
        return nextQuestIsAccepted || activeGuideContainsNextQuest
            ? QuestPrerequisiteStatus.Active
            : QuestPrerequisiteStatus.NotActive;
    }

    public static QuestPrerequisiteStatus Capture(PlayerQuest? quest)
    {
        if (quest is null)
            return QuestPrerequisiteStatus.Unknown;

        try
        {
            uint nextQuestId = quest.NextQuestId;
            if (nextQuestId == 0)
            {
                return Determine(
                    relationDataAvailable: true,
                    nextQuestId,
                    nextQuestIsAccepted: false,
                    activeGuideContainsNextQuest: false);
            }

            if (StyxWoW.Me is null || string.IsNullOrEmpty(ProfileManager.XmlLocation))
                return QuestPrerequisiteStatus.Unknown;
            Profile? profile = ProfileManager.CurrentProfile;
            if (profile is null)
                return QuestPrerequisiteStatus.Unknown;

            bool activeGuideContainsNextQuest = profile.FindQuest(nextQuestId) is not null ||
                ContainsQuest(profile.QuestOrder, nextQuestId);
            QuestCompletionSnapshot completion =
                StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(nextQuestId);
            if (activeGuideContainsNextQuest || completion.IsAccepted)
            {
                return Determine(
                    relationDataAvailable: true,
                    nextQuestId,
                    completion.IsAccepted,
                    activeGuideContainsNextQuest);
            }
            if (completion.State == QuestCompletionState.Unknown)
                return QuestPrerequisiteStatus.Unknown;
            return Determine(
                relationDataAvailable: true,
                nextQuestId,
                nextQuestIsAccepted: false,
                activeGuideContainsNextQuest: false);
        }
        catch (Exception)
        {
            return QuestPrerequisiteStatus.Unknown;
        }
    }

    private static bool ContainsQuest(IEnumerable<OrderNode> nodes, uint questId)
    {
        foreach (OrderNode node in nodes ?? Enumerable.Empty<OrderNode>())
        {
            if (GetQuestId(node) == questId)
                return true;
            if (node is INodeContainer container && ContainsQuest(container.GetNodes(), questId))
                return true;
        }
        return false;
    }

    private static uint GetQuestId(OrderNode node)
    {
        return node switch
        {
            PickUpNode value => value.QuestId,
            TurnInNode value => value.QuestId,
            ObjectiveNode value => value.QuestId,
            MoveToNode value => value.QuestId,
            UseItemNode value => value.QuestId,
            AbandonQuestNode value => value.QuestId,
            _ => 0
        };
    }
}
