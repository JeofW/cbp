namespace Styx.Logic.Questing.Recovery;

public enum QuestRecoveryStage { Pickup, Navigation, Objective, TurnIn }
public enum QuestRecoveryState { Eligible, Attempting, CoolingDown, HalfOpen, Quarantined, ManualBlacklist, Completed }
public enum QuestRecoveryScope { QuestStage, NpcRelation, Endpoint, Objective }
public enum QuestFailureReason
{
    None, PickupTargetNotOffered, PickupWrongQuestShown, NpcNotFoundInWorld,
    NpcMissingFromDatabase, InteractionTimedOut, PathGenerationFailed,
    EndpointUnreachable, NoNavigableHotspot, NoObjectiveTargetsObserved,
    NoObjectiveProgress, RepeatedDeaths, TurnInQuestIncomplete,
    TurnInTargetNotOffered, UnsupportedObjective, InvalidQuestData,
    InternalBehaviorError, LegacyUnknown, UserExcluded
}

public interface IQuestRecoveryClock { DateTime UtcNow { get; } }

public sealed class QuestRecoveryEnvironment
{
    public QuestRecoveryEnvironment(string settingsRoot, string characterName,
        string realmName, string datasetVersion, string coreVersion,
        string navigationFingerprint)
    {
        SettingsRoot = settingsRoot ?? throw new ArgumentNullException(nameof(settingsRoot));
        CharacterName = characterName ?? throw new ArgumentNullException(nameof(characterName));
        RealmName = realmName ?? throw new ArgumentNullException(nameof(realmName));
        DatasetVersion = datasetVersion ?? throw new ArgumentNullException(nameof(datasetVersion));
        CoreVersion = coreVersion ?? throw new ArgumentNullException(nameof(coreVersion));
        NavigationFingerprint = navigationFingerprint ?? throw new ArgumentNullException(nameof(navigationFingerprint));
    }

    public string SettingsRoot { get; }
    public string CharacterName { get; }
    public string RealmName { get; }
    public string DatasetVersion { get; }
    public string CoreVersion { get; }
    public string NavigationFingerprint { get; }
}

public sealed class QuestRecoveryKey : IEquatable<QuestRecoveryKey>
{
    public uint QuestId { get; init; }
    public QuestRecoveryStage Stage { get; init; }
    public QuestRecoveryScope Scope { get; init; }
    public uint NpcEntry { get; init; }
    public int MapId { get; init; }
    public string Endpoint { get; init; } = "";
    public int ObjectiveIndex { get; init; } = -1;

    public static QuestRecoveryKey ForQuestStage(uint questId, QuestRecoveryStage stage) =>
        new() { QuestId = questId, Stage = stage, Scope = QuestRecoveryScope.QuestStage };

    public static QuestRecoveryKey ForNpc(uint questId, QuestRecoveryStage stage, uint npcEntry) =>
        new() { QuestId = questId, Stage = stage, Scope = QuestRecoveryScope.NpcRelation, NpcEntry = npcEntry };

    public static QuestRecoveryKey ForEndpoint(uint questId, QuestRecoveryStage stage, int mapId, string endpoint) =>
        new()
        {
            QuestId = questId,
            Stage = stage,
            Scope = QuestRecoveryScope.Endpoint,
            MapId = mapId,
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint))
        };

    public static QuestRecoveryKey ForObjective(uint questId, int objectiveIndex) =>
        new()
        {
            QuestId = questId,
            Stage = QuestRecoveryStage.Objective,
            Scope = QuestRecoveryScope.Objective,
            ObjectiveIndex = objectiveIndex
        };

    public bool Equals(QuestRecoveryKey? other) =>
        other is not null &&
        QuestId == other.QuestId &&
        Stage == other.Stage &&
        Scope == other.Scope &&
        NpcEntry == other.NpcEntry &&
        MapId == other.MapId &&
        string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal) &&
        ObjectiveIndex == other.ObjectiveIndex;

    public override bool Equals(object? obj) => Equals(obj as QuestRecoveryKey);

    public override int GetHashCode() => HashCode.Combine(
        QuestId, Stage, Scope, NpcEntry, MapId, Endpoint, ObjectiveIndex);
}

public enum QuestAttemptOutcomeKind
{
    Observation,
    Failure,
    Redirect,
    Success
}

public sealed class QuestAttemptOutcome
{
    private IReadOnlyList<uint> _offeredQuestIds = Array.Empty<uint>();
    private IReadOnlyList<int> _objectiveCounts = Array.Empty<int>();

    public QuestRecoveryKey Key { get; init; } = null!;
    public QuestRecoveryKey? AttemptKey { get; init; }
    public QuestAttemptOutcomeKind Kind { get; init; }
    public long AttemptGeneration { get; init; }
    public QuestFailureReason Reason { get; init; }
    public bool IsFailureEpisode { get; init; }
    public string Evidence { get; init; } = "";
    public uint ObservedQuestId { get; init; }
    public IReadOnlyList<uint> OfferedQuestIds
    {
        get => _offeredQuestIds;
        init => _offeredQuestIds = RecoveryCollection.Copy(value);
    }

    public IReadOnlyList<int> ObjectiveCounts
    {
        get => _objectiveCounts;
        init => _objectiveCounts = RecoveryCollection.Copy(value);
    }

    public static QuestAttemptOutcome Failure(QuestRecoveryKey key,
        QuestFailureReason reason, string evidence) =>
        new() { Key = key ?? throw new ArgumentNullException(nameof(key)), Kind = QuestAttemptOutcomeKind.Failure, Reason = reason, IsFailureEpisode = true, Evidence = evidence ?? "" };

    public static QuestAttemptOutcome Failure(
        QuestRecoveryKey key,
        QuestRecoveryKey attemptKey,
        long attemptGeneration,
        QuestFailureReason reason,
        string evidence) =>
        new()
        {
            Key = key ?? throw new ArgumentNullException(nameof(key)),
            AttemptKey = attemptKey ?? throw new ArgumentNullException(nameof(attemptKey)),
            Kind = QuestAttemptOutcomeKind.Failure,
            AttemptGeneration = attemptGeneration > 0
                ? attemptGeneration
                : throw new ArgumentOutOfRangeException(nameof(attemptGeneration)),
            Reason = reason,
            IsFailureEpisode = true,
            Evidence = evidence ?? ""
        };

    public static QuestAttemptOutcome Observation(QuestRecoveryKey key,
        QuestFailureReason reason, string evidence) =>
        new() { Key = key ?? throw new ArgumentNullException(nameof(key)), Kind = QuestAttemptOutcomeKind.Observation, Reason = reason, IsFailureEpisode = false, Evidence = evidence ?? "" };

    public static QuestAttemptOutcome Redirect(QuestRecoveryKey key,
        QuestFailureReason reason, string evidence) =>
        new() { Key = key ?? throw new ArgumentNullException(nameof(key)), Kind = QuestAttemptOutcomeKind.Redirect, Reason = reason, IsFailureEpisode = false, Evidence = evidence ?? "" };

    public static QuestAttemptOutcome Success(
        QuestRecoveryKey key,
        long attemptGeneration,
        string evidence) =>
        new()
        {
            Key = key ?? throw new ArgumentNullException(nameof(key)),
            AttemptKey = key,
            Kind = QuestAttemptOutcomeKind.Success,
            AttemptGeneration = attemptGeneration > 0
                ? attemptGeneration
                : throw new ArgumentOutOfRangeException(nameof(attemptGeneration)),
            Reason = QuestFailureReason.None,
            IsFailureEpisode = false,
            Evidence = evidence ?? ""
        };
}

public sealed class QuestRecoveryContext
{
    private IReadOnlyList<int> _objectiveCounts = Array.Empty<int>();

    public int PlayerLevel { get; init; }
    public string EquipmentFingerprint { get; init; } = "";
    public string DatasetVersion { get; init; } = "unknown";
    public string CoreVersion { get; init; } = "unknown";
    public string NavigationFingerprint { get; init; } = "unknown";
    public DateTime LastProgressUtc { get; init; }
    public IReadOnlyList<int> ObjectiveCounts
    {
        get => _objectiveCounts;
        init => _objectiveCounts = RecoveryCollection.Copy(value);
    }
}

public sealed class QuestRecoveryDecision
{
    public QuestRecoveryState State { get; init; }
    public bool MayAttempt { get; init; }
    public long AttemptGeneration { get; init; }
    public DateTime? RetryUtc { get; init; }
    public string ResetReason { get; init; } = "";
    public string Status { get; init; } = "";
}

public sealed class QuestRecoveryEvidence
{
    public DateTime ObservedUtc { get; init; }
    public QuestFailureReason Reason { get; init; }
    public string Text { get; init; } = "";
    public int? EpisodeCount { get; init; }
    public long? RecoveryCycleId { get; init; }
    public QuestRecoveryKey? SourceKey { get; init; }
}

public sealed class QuestRecoveryRecord
{
    private IReadOnlyList<int> _objectiveCounts = Array.Empty<int>();
    private IReadOnlyList<QuestRecoveryEvidence> _evidence = Array.Empty<QuestRecoveryEvidence>();

    public QuestRecoveryKey Key { get; init; } = null!;
    public QuestRecoveryState State { get; init; }
    public QuestFailureReason Reason { get; init; }
    public DateTime? FirstFailureUtc { get; init; }
    public DateTime? LastFailureUtc { get; init; }
    public DateTime? CooldownUntilUtc { get; init; }
    public DateTime? NextHalfOpenUtc { get; init; }
    public int EpisodeCount { get; init; }
    public long RecoveryCycleId { get; init; }
    public long AttemptGeneration { get; init; }
    public int AttemptCountInEpisode { get; init; }
    public int DeathCountInEpisode { get; init; }
    public DateTime? LastProgressUtc { get; init; }
    public IReadOnlyList<int> ObjectiveCounts
    {
        get => _objectiveCounts;
        init => _objectiveCounts = RecoveryCollection.Copy(value);
    }

    public int PlayerLevelAtFailure { get; init; }
    public string EquipmentFingerprint { get; init; } = "";
    public string DatasetVersion { get; init; } = "unknown";
    public string CoreVersion { get; init; } = "unknown";
    public string NavigationFingerprint { get; init; } = "unknown";
    public IReadOnlyList<QuestRecoveryEvidence> Evidence
    {
        get => _evidence;
        init => _evidence = RecoveryCollection.Copy(value);
    }

    public static QuestRecoveryRecord Create(QuestRecoveryKey key) =>
        new() { Key = key ?? throw new ArgumentNullException(nameof(key)), State = QuestRecoveryState.Eligible };
}

public sealed class QuestRecoveryDocument
{
    private IReadOnlyList<DateTime> _rollingFailureUtc = Array.Empty<DateTime>();
    private IReadOnlyList<QuestRecoveryRecord> _records = Array.Empty<QuestRecoveryRecord>();

    public int SchemaVersion { get; init; } = 1;
    public string CharacterName { get; init; } = "";
    public string RealmName { get; init; } = "";
    public long LastAttemptGeneration { get; init; }
    public IReadOnlyList<DateTime> RollingFailureUtc
    {
        get => _rollingFailureUtc;
        init => _rollingFailureUtc = RecoveryCollection.Copy(value);
    }

    public IReadOnlyList<QuestRecoveryRecord> Records
    {
        get => _records;
        init => _records = RecoveryCollection.Copy(value);
    }
}

internal static class RecoveryCollection
{
    public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>? source) =>
        Array.AsReadOnly((source ?? Array.Empty<T>()).ToArray());
}
