using System.IO;

namespace Styx.Logic.Questing.Recovery;

public sealed class QuestRecoveryManager
{
    private const int MaximumEvidenceRecords = 10;
    private static readonly TimeSpan RollingFailureWindow = TimeSpan.FromHours(1);
    private readonly object _sync = new();
    private readonly IQuestRecoveryClock _clock;
    private readonly Action<string> _log;
    private Dictionary<QuestRecoveryKey, QuestRecoveryRecord> _records = new();
    private List<DateTime> _rollingFailureUtc = new();
    private QuestRecoveryEnvironment? _environment;
    private QuestRecoveryStore? _store;
    private bool _dirty;

    public static QuestRecoveryManager Instance { get; } = new();

    public QuestRecoveryManager(IQuestRecoveryClock? clock = null, Action<string>? log = null)
    {
        _clock = clock ?? new SystemQuestRecoveryClock();
        _log = log ?? (_ => { });
    }

    public void Configure(QuestRecoveryEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        lock (_sync)
        {
            var sanitized = Sanitize(environment);
            if (_environment is not null && IsSameStore(_environment, sanitized))
            {
                _environment = MergeRicherConfiguration(_environment, sanitized);
                return;
            }

            FlushCore();
            _environment = sanitized;
            var identityDirectory = $"{sanitized.CharacterName}-{sanitized.RealmName}";
            var storePath = Path.Combine(
                sanitized.SettingsRoot,
                "QuestRecovery",
                identityDirectory,
                "quest-recovery.json");
            _store = new QuestRecoveryStore(storePath, _log);

            var document = _store.Load();
            if (HasDifferentIdentity(document, sanitized))
            {
                _log($"Quest recovery store identity mismatch at '{storePath}'; ignoring its records.");
                document = new QuestRecoveryDocument();
            }

            _records = document.Records
                .Where(record => record.Key is not null)
                .GroupBy(record => record.Key)
                .ToDictionary(group => group.Key, group => group.Last());
            _rollingFailureUtc = document.RollingFailureUtc.ToList();
            _dirty = false;
            ImportLegacyCore(identityDirectory);
        }
    }

    public QuestRecoveryDecision Evaluate(QuestRecoveryKey key, QuestRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            EnsureConfiguredCore();
            return EvaluateCore(key, NormalizeContext(context));
        }
    }

    public QuestRecoveryDecision TryBeginAttempt(QuestRecoveryKey key, QuestRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            EnsureConfiguredCore();
            var normalizedContext = NormalizeContext(context);
            var decision = EvaluateCore(key, normalizedContext);
            if (!decision.MayAttempt)
            {
                return decision;
            }

            var current = FindRecordCore(key) ?? QuestRecoveryRecord.Create(key);
            _records[current.Key] = Copy(current, state: QuestRecoveryState.Attempting);
            _dirty = true;
            return new QuestRecoveryDecision
            {
                State = QuestRecoveryState.Attempting,
                MayAttempt = true,
                ResetReason = decision.ResetReason,
                Status = "Attempt ownership acquired."
            };
        }
    }

    public QuestRecoveryDecision Report(QuestAttemptOutcome outcome, QuestRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(outcome.Key);
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            EnsureConfiguredCore();
            var normalizedContext = NormalizeContext(context);
            var terminal = FindQuestTerminalCore(outcome.Key.QuestId);
            if (terminal is not null)
            {
                return QuestRecoveryPolicy.Evaluate(terminal, normalizedContext, RollingFailureCountCore(), _clock.UtcNow);
            }

            var current = FindRecordCore(outcome.Key) ?? QuestRecoveryRecord.Create(outcome.Key);
            var policyInput = current.State == QuestRecoveryState.Attempting
                ? Copy(current, state: QuestRecoveryState.Eligible)
                : current;
            QuestRecoveryRecord updated;

            if (!outcome.IsFailureEpisode &&
                current.State == QuestRecoveryState.Quarantined &&
                current.Reason == QuestFailureReason.LegacyUnknown &&
                HasLiveQuestEvidence(outcome))
            {
                updated = Copy(
                    current,
                    state: QuestRecoveryState.HalfOpen,
                    cooldownUntilUtc: null,
                    replaceCooldownUntilUtc: true,
                    nextHalfOpenUtc: null,
                    replaceNextHalfOpenUtc: true);
            }
            else if (outcome.IsFailureEpisode || outcome.Reason == QuestFailureReason.TurnInQuestIncomplete)
            {
                updated = QuestRecoveryPolicy.ApplyFailure(policyInput, outcome.Reason, normalizedContext, _clock.UtcNow);
                if (outcome.IsFailureEpisode && updated.EpisodeCount > current.EpisodeCount)
                {
                    _rollingFailureUtc.Add(_clock.UtcNow);
                }
            }
            else
            {
                updated = current;
            }

            updated = WithEvidence(updated, outcome.Reason, outcome.Evidence, _clock.UtcNow);
            _records[current.Key] = updated;
            _dirty = true;
            LogTransition(current, updated);
            return QuestRecoveryPolicy.Evaluate(updated, normalizedContext, RollingFailureCountCore(), _clock.UtcNow);
        }
    }

    public void ReportProgress(
        QuestRecoveryKey key,
        IReadOnlyList<int> objectiveCounts,
        QuestRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(objectiveCounts);
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            EnsureConfiguredCore();
            if (FindQuestTerminalCore(key.QuestId) is not null)
            {
                return;
            }

            var current = FindRecordCore(key);
            QuestRecoveryRecord updated;
            if (current is null)
            {
                updated = Copy(
                    QuestRecoveryRecord.Create(key),
                    objectiveCounts: objectiveCounts,
                    lastProgressUtc: _clock.UtcNow);
            }
            else
            {
                updated = QuestRecoveryPolicy.ApplyProgress(current, objectiveCounts, _clock.UtcNow);
            }

            _records[key] = updated;
            _dirty = true;
            if (current is not null)
            {
                LogTransition(current, updated);
            }
        }
    }

    public void MarkCompleted(uint questId)
    {
        lock (_sync)
        {
            EnsureConfiguredCore();
            var matching = _records.Values.Where(record => record.Key.QuestId == questId).ToArray();
            foreach (var record in matching.Where(record => record.State != QuestRecoveryState.ManualBlacklist))
            {
                _records[record.Key] = Copy(
                    record,
                    state: QuestRecoveryState.Completed,
                    cooldownUntilUtc: null,
                    replaceCooldownUntilUtc: true,
                    nextHalfOpenUtc: null,
                    replaceNextHalfOpenUtc: true);
            }

            var completionKey = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.TurnIn);
            if (!_records.Values.Any(record => record.Key.QuestId == questId && record.State == QuestRecoveryState.Completed))
            {
                _records[completionKey] = new QuestRecoveryRecord
                {
                    Key = completionKey,
                    State = QuestRecoveryState.Completed
                };
            }

            _dirty = true;
        }
    }

    public void SetManualBlacklist(uint questId, bool blacklisted)
    {
        lock (_sync)
        {
            EnsureConfiguredCore();
            foreach (var key in _records
                .Where(pair => pair.Key.QuestId == questId && pair.Value.State == QuestRecoveryState.ManualBlacklist)
                .Select(pair => pair.Key)
                .ToArray())
            {
                _records.Remove(key);
            }

            if (blacklisted)
            {
                var key = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Pickup);
                _records[key] = new QuestRecoveryRecord
                {
                    Key = key,
                    State = QuestRecoveryState.ManualBlacklist,
                    Reason = QuestFailureReason.UserExcluded
                };
            }

            _dirty = true;
        }
    }

    public void RetryNow(QuestRecoveryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_sync)
        {
            EnsureConfiguredCore();
            if (FindQuestTerminalCore(key.QuestId) is not null || !_records.TryGetValue(key, out var current))
            {
                return;
            }

            _records[key] = Copy(
                current,
                state: QuestRecoveryState.HalfOpen,
                cooldownUntilUtc: null,
                replaceCooldownUntilUtc: true,
                nextHalfOpenUtc: null,
                replaceNextHalfOpenUtc: true);
            _dirty = true;
        }
    }

    public IReadOnlyList<QuestRecoveryRecord> GetEntries()
    {
        lock (_sync)
        {
            return _records.Values
                .OrderBy(record => record.Key.QuestId)
                .ThenBy(record => record.Key.Stage)
                .ThenBy(record => record.Key.Scope)
                .ThenBy(record => record.Key.ObjectiveIndex)
                .ToArray();
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            EnsureConfiguredCore();
            FlushCore();
        }
    }

    private QuestRecoveryDecision EvaluateCore(QuestRecoveryKey key, QuestRecoveryContext context)
    {
        var current = FindRecordCore(key);
        if (current is { State: QuestRecoveryState.Quarantined, Reason: QuestFailureReason.LegacyUnknown } &&
            HasLegacyResetEvidence(current, context))
        {
            return RollingFailureCountCore() >= 6
                ? new QuestRecoveryDecision
                {
                    State = QuestRecoveryState.HalfOpen,
                    MayAttempt = false,
                    Status = "Rolling-hour retry budget exhausted."
                }
                : new QuestRecoveryDecision
                {
                    State = QuestRecoveryState.HalfOpen,
                    MayAttempt = true,
                    ResetReason = "Live evidence changed since legacy import.",
                    Status = "One controlled legacy probe is allowed."
                };
        }

        return QuestRecoveryPolicy.Evaluate(current, context, RollingFailureCountCore(), _clock.UtcNow);
    }

    private QuestRecoveryRecord? FindRecordCore(QuestRecoveryKey key) =>
        _records.TryGetValue(key, out var exact) ? exact : FindQuestControlCore(key.QuestId);

    private QuestRecoveryRecord? FindQuestControlCore(uint questId) =>
        FindQuestTerminalCore(questId)
        ?? _records.Values.FirstOrDefault(record =>
            record.Key.QuestId == questId && record.Reason == QuestFailureReason.LegacyUnknown);

    private QuestRecoveryRecord? FindQuestTerminalCore(uint questId) =>
        _records.Values.FirstOrDefault(record =>
            record.Key.QuestId == questId && record.State == QuestRecoveryState.ManualBlacklist)
        ?? _records.Values.FirstOrDefault(record =>
            record.Key.QuestId == questId && record.State == QuestRecoveryState.Completed);

    private int RollingFailureCountCore()
    {
        var windowStart = _clock.UtcNow.Subtract(RollingFailureWindow);
        return _rollingFailureUtc.Count(timestamp => timestamp > windowStart && timestamp <= _clock.UtcNow);
    }

    private QuestRecoveryContext NormalizeContext(QuestRecoveryContext context)
    {
        var environment = _environment!;
        return new QuestRecoveryContext
        {
            PlayerLevel = context.PlayerLevel,
            EquipmentFingerprint = context.EquipmentFingerprint,
            DatasetVersion = Richer(context.DatasetVersion, environment.DatasetVersion),
            CoreVersion = Richer(context.CoreVersion, environment.CoreVersion),
            NavigationFingerprint = Richer(context.NavigationFingerprint, environment.NavigationFingerprint),
            LastProgressUtc = context.LastProgressUtc,
            ObjectiveCounts = context.ObjectiveCounts
        };
    }

    private void ImportLegacyCore(string identityDirectory)
    {
        var environment = _environment!;
        var legacyPath = Path.Combine(
            environment.SettingsRoot,
            "WholesomeAutoQuest",
            identityDirectory,
            "quest_blacklist.txt");
        var markerPath = legacyPath + ".migrated";
        if (File.Exists(markerPath) || !File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            QuestRecoveryStore.BackupLegacyOnce(legacyPath);
            foreach (var questId in QuestRecoveryStore.ReadLegacyIds(legacyPath))
            {
                if (_records.Values.Any(record => record.Key.QuestId == questId))
                {
                    continue;
                }

                var key = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Pickup);
                _records[key] = new QuestRecoveryRecord
                {
                    Key = key,
                    State = QuestRecoveryState.Quarantined,
                    Reason = QuestFailureReason.LegacyUnknown,
                    DatasetVersion = environment.DatasetVersion,
                    CoreVersion = environment.CoreVersion,
                    NavigationFingerprint = environment.NavigationFingerprint,
                    Evidence = new[]
                    {
                        new QuestRecoveryEvidence
                        {
                            ObservedUtc = _clock.UtcNow,
                            Reason = QuestFailureReason.LegacyUnknown,
                            Text = "Imported from quest_blacklist.txt."
                        }
                    }
                };
            }

            _dirty = true;
            FlushCore();
            if (!_dirty)
            {
                File.WriteAllText(markerPath, "1");
            }
        }
        catch (Exception ex)
        {
            _log($"Quest recovery legacy migration failed: {ex}");
        }
    }

    private void FlushCore()
    {
        if (!_dirty || _store is null || _environment is null)
        {
            return;
        }

        var now = _clock.UtcNow;
        _rollingFailureUtc.RemoveAll(timestamp => timestamp <= now.Subtract(RollingFailureWindow));
        var compacted = CompactCompletedRecords(_records.Values);
        var document = new QuestRecoveryDocument
        {
            SchemaVersion = 1,
            CharacterName = _environment.CharacterName,
            RealmName = _environment.RealmName,
            RollingFailureUtc = _rollingFailureUtc.ToArray(),
            Records = compacted
        };

        try
        {
            _store.Save(document);
            _records = compacted.ToDictionary(record => record.Key);
            _dirty = false;
        }
        catch (Exception ex)
        {
            _log($"Quest recovery persistence failed: {ex}");
        }
    }

    private static QuestRecoveryRecord[] CompactCompletedRecords(IEnumerable<QuestRecoveryRecord> records)
    {
        var result = new List<QuestRecoveryRecord>();
        foreach (var questGroup in records.GroupBy(record => record.Key.QuestId))
        {
            result.AddRange(questGroup.Where(record => record.State == QuestRecoveryState.ManualBlacklist));
            var nonManual = questGroup.Where(record => record.State != QuestRecoveryState.ManualBlacklist).ToArray();
            if (nonManual.Any(record => record.State == QuestRecoveryState.Completed))
            {
                var key = QuestRecoveryKey.ForQuestStage(questGroup.Key, QuestRecoveryStage.TurnIn);
                result.Add(new QuestRecoveryRecord { Key = key, State = QuestRecoveryState.Completed });
            }
            else
            {
                result.AddRange(nonManual);
            }
        }

        return result.ToArray();
    }

    private static QuestRecoveryRecord WithEvidence(
        QuestRecoveryRecord record,
        QuestFailureReason reason,
        string text,
        DateTime nowUtc)
    {
        var evidence = record.Evidence
            .Append(new QuestRecoveryEvidence { ObservedUtc = nowUtc, Reason = reason, Text = text })
            .TakeLast(MaximumEvidenceRecords)
            .ToArray();
        return Copy(record, evidence: evidence);
    }

    private static bool HasLegacyResetEvidence(QuestRecoveryRecord record, QuestRecoveryContext context) =>
        context.PlayerLevel > record.PlayerLevelAtFailure ||
        Different(record.DatasetVersion, context.DatasetVersion) ||
        Different(record.CoreVersion, context.CoreVersion);

    private static bool HasLiveQuestEvidence(QuestAttemptOutcome outcome) =>
        outcome.ObservedQuestId != 0 || outcome.OfferedQuestIds.Count > 0;

    private static bool Different(string previous, string current) =>
        !IsUnknown(previous) && !IsUnknown(current) && !string.Equals(previous, current, StringComparison.Ordinal);

    private static bool HasDifferentIdentity(QuestRecoveryDocument document, QuestRecoveryEnvironment environment) =>
        (!string.IsNullOrEmpty(document.CharacterName) &&
         !string.Equals(document.CharacterName, environment.CharacterName, StringComparison.Ordinal)) ||
        (!string.IsNullOrEmpty(document.RealmName) &&
         !string.Equals(document.RealmName, environment.RealmName, StringComparison.Ordinal));

    private static QuestRecoveryEnvironment Sanitize(QuestRecoveryEnvironment environment) =>
        new(
            Path.GetFullPath(environment.SettingsRoot),
            SanitizePathPart(environment.CharacterName),
            SanitizePathPart(environment.RealmName),
            environment.DatasetVersion,
            environment.CoreVersion,
            environment.NavigationFingerprint);

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return sanitized.Length == 0 ? "unknown" : sanitized;
    }

    private static bool IsSameStore(QuestRecoveryEnvironment first, QuestRecoveryEnvironment second) =>
        string.Equals(first.SettingsRoot, second.SettingsRoot, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.CharacterName, second.CharacterName, StringComparison.Ordinal) &&
        string.Equals(first.RealmName, second.RealmName, StringComparison.Ordinal);

    private static QuestRecoveryEnvironment MergeRicherConfiguration(
        QuestRecoveryEnvironment current,
        QuestRecoveryEnvironment next) =>
        new(
            current.SettingsRoot,
            current.CharacterName,
            current.RealmName,
            Richer(next.DatasetVersion, current.DatasetVersion),
            Richer(next.CoreVersion, current.CoreVersion),
            Richer(next.NavigationFingerprint, current.NavigationFingerprint));

    private static string Richer(string candidate, string fallback) =>
        IsUnknown(candidate) && !IsUnknown(fallback) ? fallback : candidate;

    private static bool IsUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);

    private void EnsureConfiguredCore()
    {
        if (_environment is null || _store is null)
        {
            throw new InvalidOperationException("Quest recovery manager has not been configured.");
        }
    }

    private void LogTransition(QuestRecoveryRecord previous, QuestRecoveryRecord current)
    {
        if (previous.State == current.State && previous.Reason == current.Reason)
        {
            return;
        }

        _log(
            $"quest={current.Key.QuestId}, stage={current.Key.Stage}, old={previous.State}, new={current.State}, " +
            $"reason={current.Reason}, episode={current.EpisodeCount}, attempt={current.AttemptCountInEpisode}, " +
            $"retry={current.CooldownUntilUtc ?? current.NextHalfOpenUtc}");
    }

    private static QuestRecoveryRecord Copy(
        QuestRecoveryRecord current,
        QuestRecoveryState? state = null,
        QuestFailureReason? reason = null,
        DateTime? cooldownUntilUtc = null,
        bool replaceCooldownUntilUtc = false,
        DateTime? nextHalfOpenUtc = null,
        bool replaceNextHalfOpenUtc = false,
        DateTime? lastProgressUtc = null,
        IReadOnlyList<int>? objectiveCounts = null,
        QuestRecoveryContext? failureContext = null,
        IReadOnlyList<QuestRecoveryEvidence>? evidence = null)
    {
        return new QuestRecoveryRecord
        {
            Key = current.Key,
            State = state ?? current.State,
            Reason = reason ?? current.Reason,
            FirstFailureUtc = current.FirstFailureUtc,
            LastFailureUtc = current.LastFailureUtc,
            CooldownUntilUtc = replaceCooldownUntilUtc ? cooldownUntilUtc : current.CooldownUntilUtc,
            NextHalfOpenUtc = replaceNextHalfOpenUtc ? nextHalfOpenUtc : current.NextHalfOpenUtc,
            EpisodeCount = current.EpisodeCount,
            AttemptCountInEpisode = current.AttemptCountInEpisode,
            DeathCountInEpisode = current.DeathCountInEpisode,
            LastProgressUtc = lastProgressUtc ?? current.LastProgressUtc,
            ObjectiveCounts = objectiveCounts ?? current.ObjectiveCounts,
            PlayerLevelAtFailure = failureContext?.PlayerLevel ?? current.PlayerLevelAtFailure,
            EquipmentFingerprint = failureContext?.EquipmentFingerprint ?? current.EquipmentFingerprint,
            DatasetVersion = failureContext?.DatasetVersion ?? current.DatasetVersion,
            CoreVersion = failureContext?.CoreVersion ?? current.CoreVersion,
            NavigationFingerprint = failureContext?.NavigationFingerprint ?? current.NavigationFingerprint,
            Evidence = evidence ?? current.Evidence
        };
    }

    private sealed class SystemQuestRecoveryClock : IQuestRecoveryClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
