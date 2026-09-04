using Styx;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;

namespace Styx.Logic.Questing.Recovery;

public sealed class QuestDependencyEvidence
{
    public QuestDependencyEvidence(
        uint questId,
        uint prerequisiteQuestId,
        bool isActive,
        bool isAuthoritative)
    {
        QuestId = questId;
        PrerequisiteQuestId = prerequisiteQuestId;
        IsActive = isActive;
        IsAuthoritative = isAuthoritative;
    }

    public uint QuestId { get; }
    public uint PrerequisiteQuestId { get; }
    public bool IsActive { get; }
    public bool IsAuthoritative { get; }
}

public static class QuestPrerequisiteAuthority
{
    private static readonly object DependencySync = new();
    private static QuestDependencyEvidence[] _publishedDependencies = Array.Empty<QuestDependencyEvidence>();
    private static bool _publishedDependencyAuthority;

    public static void PublishAuthoritativeDependencies(
        IEnumerable<QuestDependencyEvidence> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        QuestDependencyEvidence[] snapshot = dependencies
            .Where(item => item is not null && item.QuestId != 0 && item.PrerequisiteQuestId != 0)
            .Select(item => new QuestDependencyEvidence(
                item.QuestId, item.PrerequisiteQuestId, false, true))
            .DistinctBy(item => (item.QuestId, item.PrerequisiteQuestId))
            .ToArray();
        lock (DependencySync)
        {
            _publishedDependencies = snapshot;
            _publishedDependencyAuthority = true;
        }
    }

    public static void ClearPublishedDependencyAuthority()
    {
        lock (DependencySync)
        {
            _publishedDependencies = Array.Empty<QuestDependencyEvidence>();
            _publishedDependencyAuthority = false;
        }
    }

    public static QuestPrerequisiteStatus DetermineFromDependencies(
        uint questId,
        IEnumerable<QuestDependencyEvidence> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        QuestDependencyEvidence[] active = dependencies.Where(item => item.IsActive).ToArray();
        if (active.Any(item => item.IsAuthoritative && item.PrerequisiteQuestId == questId))
            return QuestPrerequisiteStatus.Active;
        return active.Any(item => !item.IsAuthoritative)
            ? QuestPrerequisiteStatus.Unknown
            : QuestPrerequisiteStatus.NotActive;
    }

    public static QuestPrerequisiteStatus DetermineFromPublishedDependencies(
        uint questId,
        IEnumerable<uint> activeQuestIds)
    {
        ArgumentNullException.ThrowIfNull(activeQuestIds);
        QuestDependencyEvidence[] published;
        bool publishedAuthority;
        lock (DependencySync)
        {
            published = _publishedDependencies;
            publishedAuthority = _publishedDependencyAuthority;
        }
        if (!publishedAuthority)
            return QuestPrerequisiteStatus.Unknown;
        var activeSet = new HashSet<uint>(activeQuestIds);
        return DetermineFromDependencies(
            questId,
            published.Select(item => new QuestDependencyEvidence(
                item.QuestId,
                item.PrerequisiteQuestId,
                activeSet.Contains(item.QuestId),
                true)));
    }

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
            if (StyxWoW.Me is null || string.IsNullOrEmpty(ProfileManager.XmlLocation))
                return QuestPrerequisiteStatus.Unknown;
            Profile? profile = ProfileManager.CurrentProfile;
            if (profile is null)
                return QuestPrerequisiteStatus.Unknown;

            var activeIds = profile.Quests.Select(item => item.ID)
                .Concat(GetQuestIds(profile.QuestOrder))
                .Concat(StyxWoW.Me.QuestLog.GetAllQuests().Select(item => item.Id))
                .Where(id => id != 0 && id != quest.Id)
                .Distinct()
                .ToArray();
            if (quest.NextQuestId != 0 && activeIds.Contains(quest.NextQuestId))
                return QuestPrerequisiteStatus.Active;

            return DetermineFromPublishedDependencies(quest.Id, activeIds);
        }
        catch (Exception)
        {
            return QuestPrerequisiteStatus.Unknown;
        }
    }

    private static IEnumerable<uint> GetQuestIds(IEnumerable<OrderNode> nodes)
    {
        foreach (OrderNode node in nodes ?? Enumerable.Empty<OrderNode>())
        {
            uint id = GetQuestId(node);
            if (id != 0)
                yield return id;
            if (node is INodeContainer container)
            {
                foreach (uint child in GetQuestIds(container.GetNodes()))
                    yield return child;
            }
        }
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
