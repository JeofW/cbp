using System.IO;
using System.Globalization;
using System.Text;
using Styx.Helpers;

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
    private long _lastAttemptGeneration;
    private bool _dirty;

    public static QuestRecoveryManager Instance { get; } =
        new(log: message => Logging.WriteDiagnostic($"[QuestRecovery] {message}"));

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

            if (!FlushCore())
            {
                throw new InvalidOperationException(
                    "Cannot switch quest recovery identity while the current state is not persisted.");
            }

            var identityDirectory =
                $"{EncodePathComponent(sanitized.CharacterName)}-{EncodePathComponent(sanitized.RealmName)}";
            var storePath = Path.Combine(
                sanitized.SettingsRoot,
                "QuestRecovery",
                identityDirectory,
                "quest-recovery.json");
            var store = new QuestRecoveryStore(storePath, _log);

            var document = store.Load();
            if (HasDifferentIdentity(document, sanitized))
            {
                _log($"Quest recovery store identity mismatch at '{storePath}'; ignoring its records.");
                document = new QuestRecoveryDocument();
            }

            var recoveredStaleAttempt = document.Records.Any(record =>
                record.State == QuestRecoveryState.Attempting);
            var records = document.Records
                .Where(record => record.Key is not null)
                .Select(record => record.State == QuestRecoveryState.Attempting
                    ? Copy(record, state: QuestRecoveryState.HalfOpen)
                    : record)
                .GroupBy(record => record.Key)
                .ToDictionary(group => group.Key, group => group.Last());
            var rollingFailureUtc = document.RollingFailureUtc.ToList();
            long loadedAttemptGeneration = records.Count == 0
                ? 0
                : records.Values.Max(record => record.AttemptGeneration);
            loadedAttemptGeneration = Math.Max(
                loadedAttemptGeneration,
                document.LastAttemptGeneration);

            _environment = sanitized;
            _store = store;
            _records = records;
            _rollingFailureUtc = rollingFailureUtc;
            _lastAttemptGeneration = Math.Max(_lastAttemptGeneration, loadedAttemptGeneration);
            _dirty = recoveredStaleAttempt;
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

    public bool OwnsAttempt(QuestRecoveryKey key, long attemptGeneration)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (attemptGeneration <= 0)
            return false;

        lock (_sync)
        {
            EnsureConfiguredCore();
            return _records.TryGetValue(key, out var current) &&
                current.State == QuestRecoveryState.Attempting &&
                current.AttemptGeneration == attemptGeneration;
        }
    }

    public bool AbandonAttempt(QuestRecoveryKey key, long attemptGeneration)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (attemptGeneration <= 0)
            return false;

        lock (_sync)
        {
            EnsureConfiguredCore();
            if (!_records.TryGetValue(key, out var current) ||
                current.State != QuestRecoveryState.Attempting ||
                current.AttemptGeneration != attemptGeneration)
                return false;

            var released = Copy(current, state: QuestRecoveryState.Eligible);
            _records[key] = released;
            _dirty = true;
            LogTransition(current, released);
            return true;
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
            current = MaterializeLegacyRecordCore(current, key);
            long attemptGeneration = checked(
                Math.Max(_lastAttemptGeneration, current.AttemptGeneration) + 1);
            _lastAttemptGeneration = attemptGeneration;
            _records[current.Key] = Copy(
                current,
                state: QuestRecoveryState.Attempting,
                attemptGeneration: attemptGeneration);
            _dirty = true;
            return new QuestRecoveryDecision
            {
                State = QuestRecoveryState.Attempting,
                MayAttempt = true,
                AttemptGeneration = attemptGeneration,
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
            if (IsOwnedTerminalOutcome(outcome) &&
                outcome.AttemptKey is not null &&
                outcome.Key.Equals(outcome.AttemptKey) &&
                outcome.AttemptGeneration > 0)
                return TryReportOwnedOutcomeCore(outcome, context).Decision;
            return ReportCore(outcome, context, countRollingFailure: true);
        }
    }

    public QuestRecoveryReportResult TryReportOwnedOutcome(
        QuestAttemptOutcome outcome,
        QuestRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(outcome.Key);
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            return TryReportOwnedOutcomeCore(outcome, context);
        }
    }

    public QuestRecoveryReportResult TryReportOwnedRedirect(
        QuestAttemptOutcome outcome,
        QuestRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(outcome.Key);
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            if (!IsOwnedIncompleteRedirect(outcome))
            {
                EnsureConfiguredCore();
                return new QuestRecoveryReportResult
                {
                    Accepted = false,
                    Decision = EvaluateCore(outcome.Key, NormalizeContext(context))
                };
            }
            return TryReportOwnedOutcomeCore(outcome, context);
        }
    }

    private QuestRecoveryReportResult TryReportOwnedOutcomeCore(
        QuestAttemptOutcome outcome,
        QuestRecoveryContext context)
    {
        EnsureConfiguredCore();
        var normalizedContext = NormalizeContext(context);
        QuestRecoveryKey? attemptKey = outcome.AttemptKey;
        if (!IsOwnedTerminalOutcome(outcome) ||
            attemptKey == null ||
            !outcome.Key.Equals(attemptKey) ||
            IsOwnedIncompleteRedirect(outcome) &&
                (attemptKey.Stage != QuestRecoveryStage.TurnIn ||
                 attemptKey.Scope != QuestRecoveryScope.QuestStage) ||
            outcome.AttemptGeneration <= 0 ||
            FindQuestTerminalCore(attemptKey.QuestId) is not null ||
            !_records.TryGetValue(attemptKey, out var owner) ||
            owner.State != QuestRecoveryState.Attempting ||
            owner.AttemptGeneration != outcome.AttemptGeneration)
        {
            return new QuestRecoveryReportResult
            {
                Accepted = false,
                Decision = EvaluateCore(outcome.Key, normalizedContext)
            };
        }

        return new QuestRecoveryReportResult
        {
            Accepted = true,
            Decision = ReportCore(outcome, context, countRollingFailure: true)
        };
    }

    private QuestRecoveryDecision ReportCore(
        QuestAttemptOutcome outcome,
        QuestRecoveryContext context,
        bool countRollingFailure)
    {
            EnsureConfiguredCore();
            var normalizedContext = NormalizeContext(context);
            QuestRecoveryRecord? activeOwner = null;
            QuestRecoveryKey? activeOwnerKey = null;
            bool generatedFailure = outcome.IsFailureEpisode && outcome.AttemptGeneration > 0;
            if (generatedFailure)
            {
                activeOwnerKey = outcome.AttemptKey ?? outcome.Key;
                if (!QuestAttemptOutcome.IsAuthorizedGeneratedFailureTarget(activeOwnerKey, outcome.Key) ||
                    !_records.TryGetValue(activeOwnerKey, out activeOwner) ||
                    activeOwner.State != QuestRecoveryState.Attempting ||
                    activeOwner.AttemptGeneration != outcome.AttemptGeneration)
                {
                    return EvaluateCore(activeOwnerKey, normalizedContext);
                }
                if (!activeOwnerKey.Equals(outcome.Key) &&
                    _records.TryGetValue(outcome.Key, out var targetOwner) &&
                    targetOwner.State == QuestRecoveryState.Attempting)
                    return EvaluateCore(activeOwnerKey, normalizedContext);
            }

            var terminal = FindQuestTerminalCore(outcome.Key.QuestId);
            if (terminal is not null)
            {
                return QuestRecoveryPolicy.Evaluate(terminal, normalizedContext, RollingFailureCountCore(), _clock.UtcNow);
            }

            var current = FindRecordCore(outcome.Key) ?? QuestRecoveryRecord.Create(outcome.Key);
            current = MaterializeLegacyRecordCore(current, outcome.Key);
            if (outcome.IsFailureEpisode && current.State == QuestRecoveryState.Attempting && !generatedFailure)
            {
                return QuestRecoveryPolicy.Evaluate(
                    current,
                    normalizedContext,
                    RollingFailureCountCore(),
                    _clock.UtcNow);
            }
            var policyInput = current.State == QuestRecoveryState.Attempting
                ? Copy(current, state: QuestRecoveryState.Eligible)
                : current;
            QuestRecoveryRecord updated;

            if (outcome.Kind == QuestAttemptOutcomeKind.Success)
            {
                if (current.State != QuestRecoveryState.Attempting ||
                    outcome.AttemptGeneration != current.AttemptGeneration)
                {
                    return QuestRecoveryPolicy.Evaluate(
                        current,
                        normalizedContext,
                        RollingFailureCountCore(),
                        _clock.UtcNow);
                }

                updated = ApplySuccessfulAttempt(current);
            }
            else if (!outcome.IsFailureEpisode &&
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
                if (countRollingFailure && outcome.IsFailureEpisode && updated.EpisodeCount > current.EpisodeCount)
                {
                    _rollingFailureUtc.Add(_clock.UtcNow);
                }
            }
            else
            {
                updated = current;
            }

            updated = WithEvidence(
                updated,
                outcome.Reason,
                outcome.Evidence,
                _clock.UtcNow,
                coalesce: !outcome.IsFailureEpisode);
            _records[current.Key] = updated;
            _dirty = true;
            LogTransition(current, updated);
            return QuestRecoveryPolicy.Evaluate(updated, normalizedContext, RollingFailureCountCore(), _clock.UtcNow);
    }

    private static bool IsOwnedIncompleteRedirect(QuestAttemptOutcome outcome) =>
        outcome.Kind == QuestAttemptOutcomeKind.Redirect &&
        outcome.Reason == QuestFailureReason.TurnInQuestIncomplete &&
        !outcome.IsFailureEpisode;

    private static bool IsOwnedTerminalOutcome(QuestAttemptOutcome outcome) =>
        outcome.Kind == QuestAttemptOutcomeKind.Success && !outcome.IsFailureEpisode ||
        outcome.Kind == QuestAttemptOutcomeKind.Failure && outcome.IsFailureEpisode ||
        IsOwnedIncompleteRedirect(outcome);

    public bool TryReportGeneratedFailures(
        IReadOnlyList<QuestAttemptOutcome> outcomes,
        QuestRecoveryContext context,
        out IReadOnlyList<QuestRecoveryDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(context);
        decisions = Array.Empty<QuestRecoveryDecision>();
        if (outcomes.Count == 0)
            throw new ArgumentException("At least one generated failure is required.", nameof(outcomes));

        lock (_sync)
        {
            QuestAttemptOutcome final = outcomes[^1];
            QuestRecoveryKey attemptKey = final.AttemptKey
                ?? throw new ArgumentException("Generated failures require an attempt key.", nameof(outcomes));
            long generation = final.AttemptGeneration;
            if (generation <= 0 || !final.Key.Equals(attemptKey))
                throw new ArgumentException("The final generated failure must target its source attempt.", nameof(outcomes));

            for (int index = 0; index < outcomes.Count; index++)
            {
                QuestAttemptOutcome outcome = outcomes[index];
                if (outcome == null || !outcome.IsFailureEpisode ||
                    outcome.AttemptGeneration != generation ||
                    !attemptKey.Equals(outcome.AttemptKey) ||
                    !QuestAttemptOutcome.IsAuthorizedGeneratedFailureTarget(attemptKey, outcome.Key) ||
                    index < outcomes.Count - 1 && outcome.Key.Equals(attemptKey))
                    throw new ArgumentException("Generated failures must share one exact authorized owner.", nameof(outcomes));
            }
            if (!_records.TryGetValue(attemptKey, out var sourceOwner) ||
                sourceOwner.State != QuestRecoveryState.Attempting ||
                sourceOwner.AttemptGeneration != generation)
                return false;
            foreach (QuestAttemptOutcome outcome in outcomes.Take(outcomes.Count - 1))
            {
                if (_records.TryGetValue(outcome.Key, out var targetOwner) &&
                    targetOwner.State == QuestRecoveryState.Attempting)
                    return false;
            }

            _rollingFailureUtc.Add(_clock.UtcNow);
            _dirty = true;
            var accepted = new List<QuestRecoveryDecision>(outcomes.Count);
            foreach (QuestAttemptOutcome outcome in outcomes)
                accepted.Add(ReportCore(outcome, context, countRollingFailure: false));
            decisions = accepted;
            return true;
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
                updated = QuestRecoveryPolicy.ApplyProgress(
                    QuestRecoveryRecord.Create(key),
                    objectiveCounts,
                    _clock.UtcNow);
            }
            else
            {
                current = MaterializeLegacyRecordCore(current, key);
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
            foreach (var record in matching)
            {
                _records[record.Key] = Copy(
                    record,
                    state: QuestRecoveryState.Completed,
                    reason: QuestFailureReason.None,
                    cooldownUntilUtc: null,
                    replaceCooldownUntilUtc: true,
                    nextHalfOpenUtc: null,
                    replaceNextHalfOpenUtc: true);
            }

            var completionKey = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.TurnIn);
            if (!_records.TryGetValue(completionKey, out var completion))
            {
                _records[completionKey] = new QuestRecoveryRecord
                {
                    Key = completionKey,
                    State = QuestRecoveryState.Completed,
                    RecoveryCycleId = matching.Select(record => record.RecoveryCycleId).DefaultIfEmpty().Max(),
                    AttemptGeneration = matching.Select(record => record.AttemptGeneration).DefaultIfEmpty().Max()
                };
            }
            else if (completion.State != QuestRecoveryState.Completed)
            {
                _records[completionKey] = Copy(
                    completion,
                    state: QuestRecoveryState.Completed,
                    reason: QuestFailureReason.None,
                    cooldownUntilUtc: null,
                    replaceCooldownUntilUtc: true,
                    nextHalfOpenUtc: null,
                    replaceNextHalfOpenUtc: true);
            }

            _dirty = true;
        }
    }

    public void SetManualBlacklist(uint questId, bool blacklisted)
    {
        TrySetManualBlacklist(questId, blacklisted);
    }

    public bool TrySetManualBlacklist(uint questId, bool blacklisted)
    {
        lock (_sync)
        {
            EnsureConfiguredCore();
            return TrySetManualBlacklistCore(questId, blacklisted);
        }
    }

    public bool TrySetManualBlacklistIfCurrentAutomatic(
        QuestRecoveryKey key,
        QuestRecoveryState expectedState,
        long expectedAttemptGeneration)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_sync)
        {
            EnsureConfiguredCore();
            if (expectedState is not (QuestRecoveryState.CoolingDown or
                    QuestRecoveryState.HalfOpen or
                    QuestRecoveryState.Quarantined) ||
                !_records.TryGetValue(key, out var current) ||
                current.State != expectedState ||
                current.AttemptGeneration != expectedAttemptGeneration ||
                FindQuestTerminalCore(key.QuestId) is not null)
            {
                return false;
            }

            return TrySetManualBlacklistCore(key.QuestId, true);
        }
    }

    private bool TrySetManualBlacklistCore(uint questId, bool blacklisted)
    {
        if (blacklisted)
        {
            if (_records.Values.Any(pair =>
                    pair.Key.QuestId == questId && pair.State == QuestRecoveryState.Completed))
                return false;

            bool changed = false;

            foreach (var pair in _records
                .Where(pair => pair.Key.QuestId == questId && pair.Value.State == QuestRecoveryState.Attempting)
                .ToArray())
            {
                _records[pair.Key] = Copy(pair.Value, state: QuestRecoveryState.Eligible);
                changed = true;
            }

            var key = QuestRecoveryKey.ForManualTerminal(questId);
            var existing = _records.TryGetValue(key, out var current) ? current : null;
            if (existing is null)
            {
                var legacy = _records.Values.FirstOrDefault(record =>
                    record.Key.QuestId == questId &&
                    record.State == QuestRecoveryState.ManualBlacklist);
                _records[key] = legacy is null
                    ? new QuestRecoveryRecord
                    {
                        Key = key,
                        State = QuestRecoveryState.ManualBlacklist,
                        Reason = QuestFailureReason.UserExcluded
                    }
                    : Copy(
                        legacy,
                        key: key,
                        state: QuestRecoveryState.ManualBlacklist,
                        reason: QuestFailureReason.UserExcluded);
                changed = true;
            }
            else if (existing.State != QuestRecoveryState.ManualBlacklist ||
                     existing.Reason != QuestFailureReason.UserExcluded)
            {
                _records[key] = Copy(
                    existing,
                    state: QuestRecoveryState.ManualBlacklist,
                    reason: QuestFailureReason.UserExcluded);
                changed = true;
            }
            foreach (var pair in _records
                .Where(pair => pair.Key.QuestId == questId &&
                    pair.Value.State == QuestRecoveryState.ManualBlacklist &&
                    !pair.Key.Equals(key))
                .ToArray())
            {
                _records.Remove(pair.Key);
                changed = true;
            }

            _dirty |= changed;
            return changed;
        }

        bool removed = false;
        foreach (var pair in _records
            .Where(pair => pair.Key.QuestId == questId &&
                pair.Value.State == QuestRecoveryState.ManualBlacklist)
            .ToArray())
        {
            removed |= _records.Remove(pair.Key);
        }

        _dirty |= removed;
        return removed;
    }

    public void RetryNow(QuestRecoveryKey key)
    {
        TryRetryNow(key);
    }

    public bool TryRetryNow(QuestRecoveryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_sync)
        {
            EnsureConfiguredCore();
            if (FindQuestTerminalCore(key.QuestId) is not null ||
                !_records.TryGetValue(key, out var current) ||
                current.State is not (QuestRecoveryState.CoolingDown or QuestRecoveryState.Quarantined))
            {
                return false;
            }

            _records[key] = Copy(
                current,
                state: QuestRecoveryState.HalfOpen,
                cooldownUntilUtc: null,
                replaceCooldownUntilUtc: true,
                nextHalfOpenUtc: null,
                replaceNextHalfOpenUtc: true);
            _dirty = true;
            return true;
        }
    }

    public void ClearExclusion(QuestRecoveryKey key)
    {
        TryClearExclusion(key);
    }

    public bool TryClearExclusion(QuestRecoveryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_sync)
        {
            EnsureConfiguredCore();
            if (FindQuestTerminalCore(key.QuestId) is { State: QuestRecoveryState.Completed })
            {
                return false;
            }

            bool removed = false;
            foreach (var pair in _records
                .Where(pair => pair.Key.QuestId == key.QuestId &&
                    pair.Value.State == QuestRecoveryState.ManualBlacklist)
                .ToArray())
            {
                removed |= _records.Remove(pair.Key);
            }

            if (_records.TryGetValue(key, out var current) &&
                current.State is QuestRecoveryState.CoolingDown or
                    QuestRecoveryState.HalfOpen or
                    QuestRecoveryState.Quarantined)
            {
                removed = _records.Remove(key);
            }

            _dirty |= removed;
            return removed;
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

    public QuestRecoveryRecord? GetRecord(QuestRecoveryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_sync)
        {
            EnsureConfiguredCore();
            return FindRecordCore(key);
        }
    }

    public QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
        QuestRecoveryKey key,
        Func<QuestAbandonmentLiveSnapshot> recapture,
        Action abandonAction)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(recapture);
        ArgumentNullException.ThrowIfNull(abandonAction);

        lock (_sync)
        {
            EnsureConfiguredCore();
            QuestAbandonmentLiveSnapshot live = recapture()
                ?? throw new InvalidOperationException("Live quest abandonment recapture returned no snapshot.");
            QuestRecoveryRecord? current = FindRecordCore(key);
            QuestAbandonmentDecision decision = QuestAbandonmentPolicy.Evaluate(
                new QuestAbandonmentContext
                {
                    IsAccepted = live.IsAccepted,
                    IsCompleted = live.IsCompleted,
                    StateIsCertain = live.StateIsCertain,
                    HasObjectiveProgress = live.HasObjectiveProgress ||
                        HasHistoricalObjectiveProgressCore(key.QuestId),
                    PrerequisiteStatus = live.PrerequisiteStatus,
                    FreeQuestLogSlots = live.FreeQuestLogSlots,
                    RecoveryState = current?.State ?? QuestRecoveryState.Eligible,
                    Reason = current?.Reason ?? QuestFailureReason.None
                });
            if (!decision.MayAbandon)
                return decision;
            if (!FlushCore())
            {
                return new QuestAbandonmentDecision
                {
                    MayAbandon = false,
                    Reason = "Automatic abandonment denied: recovery state could not be persisted."
                };
            }

            try
            {
                abandonAction();
                return decision;
            }
            catch (Exception ex)
            {
                _log($"Quest recovery automatic abandonment action failed for quest {key.QuestId}: {ex}");
                return new QuestAbandonmentDecision
                {
                    MayAbandon = false,
                    Reason = $"Automatic abandonment failed: abandon action threw {ex.GetType().Name}: {ex.Message}"
                };
            }
        }
    }

    public void Flush()
    {
        TryFlush();
    }

    private bool HasHistoricalObjectiveProgressCore(uint questId)
    {
        return _records.Values.Any(record =>
            record.Key.QuestId == questId &&
            record.ObjectiveCounts.Any(count => count > 0));
    }

    public bool TryFlush()
    {
        lock (_sync)
        {
            EnsureConfiguredCore();
            return FlushCore();
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
        FindQuestTerminalCore(key.QuestId)
        ?? (_records.TryGetValue(key, out var exact) ? exact : FindQuestControlCore(key.QuestId));

    private QuestRecoveryRecord? FindQuestControlCore(uint questId) =>
        FindQuestTerminalCore(questId)
        ?? _records.Values.FirstOrDefault(record =>
            record.Key.QuestId == questId && record.Reason == QuestFailureReason.LegacyUnknown);

    private QuestRecoveryRecord? FindQuestTerminalCore(uint questId) =>
        _records.Values.FirstOrDefault(record =>
            record.Key.QuestId == questId && record.State == QuestRecoveryState.Completed)
        ?? _records.Values.FirstOrDefault(record =>
            record.Key.QuestId == questId && record.State == QuestRecoveryState.ManualBlacklist);

    private QuestRecoveryRecord MaterializeLegacyRecordCore(
        QuestRecoveryRecord current,
        QuestRecoveryKey requestedKey)
    {
        if (current.Key.Equals(requestedKey) || current.Reason != QuestFailureReason.LegacyUnknown)
        {
            return current;
        }

        _records.Remove(current.Key);
        return Copy(current, key: requestedKey);
    }

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
            var backupPath = Path.Combine(
                Path.GetDirectoryName(legacyPath) ?? "",
                "quest_blacklist.legacy.bak");
            int importedCount = 0;
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
                            Text = "Imported from quest_blacklist.txt.",
                            SourceKey = key
                        }
                    }
                };
                importedCount++;
            }

            _dirty = true;
            FlushCore();
            if (!_dirty)
            {
                File.WriteAllText(markerPath, "1");
                _log($"Legacy migration: backup='{backupPath}', imported={importedCount}.");
            }
        }
        catch (Exception ex)
        {
            _log($"Quest recovery legacy migration failed: {ex}");
        }
    }

    private bool FlushCore()
    {
        if (!_dirty || _store is null || _environment is null)
        {
            return true;
        }

        var now = _clock.UtcNow;
        _rollingFailureUtc.RemoveAll(timestamp => timestamp <= now.Subtract(RollingFailureWindow));
        var compacted = CompactCompletedRecords(_records.Values);
        var document = new QuestRecoveryDocument
        {
            SchemaVersion = 1,
            CharacterName = _environment.CharacterName,
            RealmName = _environment.RealmName,
            LastAttemptGeneration = _lastAttemptGeneration,
            RollingFailureUtc = _rollingFailureUtc.ToArray(),
            Records = compacted
        };

        try
        {
            _store.Save(document);
            _records = compacted.ToDictionary(record => record.Key);
            _dirty = false;
            return true;
        }
        catch (Exception ex)
        {
            _log($"Quest recovery persistence failed: {ex}");
            return false;
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
                var evidence = nonManual
                    .Where(record => record.State == QuestRecoveryState.Completed)
                    .SelectMany(record => record.Evidence)
                    .OrderBy(item => item.ObservedUtc)
                    .TakeLast(MaximumEvidenceRecords)
                    .ToArray();
                result.Add(new QuestRecoveryRecord
                {
                    Key = key,
                    State = QuestRecoveryState.Completed,
                    RecoveryCycleId = nonManual.Max(record => record.RecoveryCycleId),
                    AttemptGeneration = nonManual.Max(record => record.AttemptGeneration),
                    Evidence = evidence
                });
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
        DateTime nowUtc,
        bool coalesce)
    {
        if (coalesce &&
            record.Evidence.Any(evidence =>
                evidence.SourceKey?.Equals(record.Key) == true &&
                evidence.RecoveryCycleId == record.RecoveryCycleId &&
                evidence.EpisodeCount == record.EpisodeCount &&
                evidence.Reason == reason &&
                string.Equals(evidence.Text, text, StringComparison.Ordinal)))
        {
            return record;
        }

        var evidence = record.Evidence
            .Append(new QuestRecoveryEvidence
            {
                ObservedUtc = nowUtc,
                Reason = reason,
                Text = text,
                EpisodeCount = record.EpisodeCount,
                RecoveryCycleId = record.RecoveryCycleId,
                SourceKey = record.Key
            })
            .TakeLast(MaximumEvidenceRecords)
            .ToArray();
        return Copy(record, evidence: evidence);
    }

    private static QuestRecoveryRecord ApplySuccessfulAttempt(QuestRecoveryRecord current)
    {
        return new QuestRecoveryRecord
        {
            Key = current.Key,
            State = QuestRecoveryState.Eligible,
            Reason = QuestFailureReason.None,
            RecoveryCycleId = current.RecoveryCycleId + 1,
            AttemptGeneration = current.AttemptGeneration,
            LastProgressUtc = current.LastProgressUtc,
            ObjectiveCounts = current.ObjectiveCounts,
            Evidence = current.Evidence
        };
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
         !string.Equals(document.CharacterName, environment.CharacterName, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(document.RealmName) &&
         !string.Equals(document.RealmName, environment.RealmName, StringComparison.OrdinalIgnoreCase));

    private static QuestRecoveryEnvironment Sanitize(QuestRecoveryEnvironment environment) =>
        new(
            Path.GetFullPath(environment.SettingsRoot),
            environment.CharacterName,
            environment.RealmName,
            environment.DatasetVersion,
            environment.CoreVersion,
            environment.NavigationFingerprint);

    private static string EncodePathComponent(string value)
    {
        if (value.Length == 0)
        {
            return "%EMPTY";
        }

        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToHashSet();
        var trimmedLength = value.TrimEnd(' ', '.').Length;
        var reservedDeviceName = IsReservedDeviceName(value);
        var encoded = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var trailingAlias = index >= trimmedLength && (character is ' ' or '.');
            if ((reservedDeviceName && index == 0) ||
                character is '-' or '%' ||
                char.IsControl(character) ||
                invalid.Contains(character) ||
                trailingAlias)
            {
                encoded.Append('%');
                encoded.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                encoded.Append(character);
            }
        }

        return encoded.ToString();
    }

    private static bool IsReservedDeviceName(string value)
    {
        var trimmed = value.TrimEnd(' ', '.');
        var dotIndex = trimmed.IndexOf('.');
        var stem = dotIndex < 0 ? trimmed : trimmed[..dotIndex];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                stem[3] is (>= '1' and <= '9') or '\u00B9' or '\u00B2' or '\u00B3');
    }

    private static bool IsSameStore(QuestRecoveryEnvironment first, QuestRecoveryEnvironment second) =>
        string.Equals(first.SettingsRoot, second.SettingsRoot, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.CharacterName, second.CharacterName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.RealmName, second.RealmName, StringComparison.OrdinalIgnoreCase);

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
        QuestRecoveryKey? key = null,
        QuestRecoveryState? state = null,
        QuestFailureReason? reason = null,
        DateTime? cooldownUntilUtc = null,
        bool replaceCooldownUntilUtc = false,
        DateTime? nextHalfOpenUtc = null,
        bool replaceNextHalfOpenUtc = false,
        DateTime? lastProgressUtc = null,
        IReadOnlyList<int>? objectiveCounts = null,
        QuestRecoveryContext? failureContext = null,
        IReadOnlyList<QuestRecoveryEvidence>? evidence = null,
        long? attemptGeneration = null)
    {
        return new QuestRecoveryRecord
        {
            Key = key ?? current.Key,
            State = state ?? current.State,
            Reason = reason ?? current.Reason,
            FirstFailureUtc = current.FirstFailureUtc,
            LastFailureUtc = current.LastFailureUtc,
            CooldownUntilUtc = replaceCooldownUntilUtc ? cooldownUntilUtc : current.CooldownUntilUtc,
            NextHalfOpenUtc = replaceNextHalfOpenUtc ? nextHalfOpenUtc : current.NextHalfOpenUtc,
            EpisodeCount = current.EpisodeCount,
            RecoveryCycleId = current.RecoveryCycleId,
            AttemptGeneration = attemptGeneration ?? current.AttemptGeneration,
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
