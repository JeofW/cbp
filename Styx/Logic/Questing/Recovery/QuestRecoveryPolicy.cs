namespace Styx.Logic.Questing.Recovery;

public static class QuestRecoveryPolicy
{
    private static readonly TimeSpan QuarantineProbeDelay = TimeSpan.FromHours(6);

    public static QuestRecoveryRecord ApplyFailure(
        QuestRecoveryRecord current,
        QuestFailureReason reason,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.State is QuestRecoveryState.ManualBlacklist or QuestRecoveryState.Completed)
        {
            return Copy(current);
        }

        if (reason == QuestFailureReason.TurnInQuestIncomplete)
        {
            return Copy(current, state: QuestRecoveryState.Eligible, reason: reason,
                cooldownUntilUtc: null, replaceCooldownUntilUtc: true,
                nextHalfOpenUtc: null, replaceNextHalfOpenUtc: true,
                attemptCountInEpisode: 0);
        }

        var newEpisode = IsNewEpisode(current, nowUtc);
        var episodeCount = current.EpisodeCount + (newEpisode ? 1 : 0);
        var attemptCount = newEpisode ? 1 : current.AttemptCountInEpisode + 1;
        var deathCount = reason == QuestFailureReason.RepeatedDeaths
            ? (newEpisode ? 1 : current.DeathCountInEpisode + 1)
            : current.DeathCountInEpisode;
        var immediateQuarantine = reason is QuestFailureReason.UnsupportedObjective or QuestFailureReason.InvalidQuestData;
        var quarantine = immediateQuarantine || episodeCount >= 3;

        return Copy(current,
            state: quarantine ? QuestRecoveryState.Quarantined : QuestRecoveryState.CoolingDown,
            reason: reason,
            firstFailureUtc: current.FirstFailureUtc ?? nowUtc,
            lastFailureUtc: nowUtc,
            cooldownUntilUtc: quarantine ? null : nowUtc.Add(GetCooldown(current.Key, reason, episodeCount)),
            replaceCooldownUntilUtc: true,
            nextHalfOpenUtc: quarantine ? nowUtc.Add(QuarantineProbeDelay) : null,
            replaceNextHalfOpenUtc: true,
            episodeCount: episodeCount,
            attemptCountInEpisode: attemptCount,
            deathCountInEpisode: deathCount);
    }

    public static QuestRecoveryRecord ApplyProgress(
        QuestRecoveryRecord current,
        IReadOnlyList<int> objectiveCounts,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(objectiveCounts);

        var madeProgress = objectiveCounts.Select((count, index) => count > (index < current.ObjectiveCounts.Count ? current.ObjectiveCounts[index] : 0)).Any(progressed => progressed);
        var resetsEscalation = madeProgress &&
            (current.Key.Stage == QuestRecoveryStage.Objective || current.Reason == QuestFailureReason.RepeatedDeaths);

        return Copy(current,
            state: resetsEscalation ? QuestRecoveryState.Eligible : current.State,
            reason: resetsEscalation ? QuestFailureReason.None : current.Reason,
            cooldownUntilUtc: resetsEscalation ? null : current.CooldownUntilUtc,
            replaceCooldownUntilUtc: resetsEscalation,
            nextHalfOpenUtc: resetsEscalation ? null : current.NextHalfOpenUtc,
            replaceNextHalfOpenUtc: resetsEscalation,
            episodeCount: resetsEscalation ? 0 : current.EpisodeCount,
            attemptCountInEpisode: resetsEscalation ? 0 : current.AttemptCountInEpisode,
            deathCountInEpisode: resetsEscalation ? 0 : current.DeathCountInEpisode,
            lastProgressUtc: nowUtc,
            objectiveCounts: objectiveCounts);
    }

    public static QuestRecoveryDecision Evaluate(
        QuestRecoveryRecord? current,
        QuestRecoveryContext context,
        int failuresInRollingHour,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (current is null)
        {
            return Allow(QuestRecoveryState.Eligible, "No recovery record.");
        }

        if (current.State == QuestRecoveryState.ManualBlacklist)
        {
            return Deny(QuestRecoveryState.ManualBlacklist, null, "Manual blacklist.");
        }

        if (current.State == QuestRecoveryState.Completed)
        {
            return Deny(QuestRecoveryState.Completed, null, "Quest completed.");
        }

        if (current.State == QuestRecoveryState.Attempting)
        {
            return Deny(QuestRecoveryState.Attempting, null, "An attempt is already active.");
        }

        if (current.State == QuestRecoveryState.CoolingDown)
        {
            if (current.CooldownUntilUtc is { } cooldownUntilUtc && nowUtc < cooldownUntilUtc)
            {
                return Deny(QuestRecoveryState.CoolingDown, cooldownUntilUtc, "Cooldown active.");
            }

            return HalfOpenDecision(failuresInRollingHour, "Cooldown elapsed.");
        }

        if (current.State == QuestRecoveryState.Quarantined)
        {
            var resetReason = GetRelevantResetReason(current, context);
            if (resetReason.Length > 0)
            {
                return HalfOpenDecision(failuresInRollingHour, resetReason);
            }

            if (current.NextHalfOpenUtc is { } nextHalfOpenUtc && nowUtc >= nextHalfOpenUtc)
            {
                return HalfOpenDecision(failuresInRollingHour, "Quarantine probe due.");
            }

            return Deny(QuestRecoveryState.Quarantined, current.NextHalfOpenUtc, "Quarantined.");
        }

        if (current.State == QuestRecoveryState.HalfOpen)
        {
            return HalfOpenDecision(failuresInRollingHour, "Half-open probe.");
        }

        return Allow(QuestRecoveryState.Eligible, "Eligible.");
    }

    private static bool IsNewEpisode(QuestRecoveryRecord current, DateTime nowUtc) =>
        current.EpisodeCount == 0 ||
        (current.State == QuestRecoveryState.CoolingDown &&
         (!current.CooldownUntilUtc.HasValue || nowUtc >= current.CooldownUntilUtc.Value)) ||
        (current.State == QuestRecoveryState.Quarantined &&
         (!current.NextHalfOpenUtc.HasValue || nowUtc >= current.NextHalfOpenUtc.Value)) ||
        current.State == QuestRecoveryState.HalfOpen ||
        current.State == QuestRecoveryState.Eligible;

    private static TimeSpan GetCooldown(QuestRecoveryKey key, QuestFailureReason reason, int episodeCount)
    {
        if (episodeCount >= 2)
        {
            return TimeSpan.FromMinutes(60);
        }

        if (key.Scope == QuestRecoveryScope.Endpoint && reason == QuestFailureReason.PathGenerationFailed)
        {
            return TimeSpan.FromMinutes(10);
        }

        return key.Stage == QuestRecoveryStage.Pickup ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(30);
    }

    private static string GetRelevantResetReason(QuestRecoveryRecord current, QuestRecoveryContext context)
    {
        if (current.Reason is QuestFailureReason.UnsupportedObjective or QuestFailureReason.InvalidQuestData)
        {
            return Different(current.DatasetVersion, context.DatasetVersion) || Different(current.CoreVersion, context.CoreVersion)
                ? "Dataset or core version changed."
                : "";
        }

        if (current.Reason is QuestFailureReason.PathGenerationFailed or QuestFailureReason.EndpointUnreachable or QuestFailureReason.NoNavigableHotspot)
        {
            return Different(current.DatasetVersion, context.DatasetVersion) ||
                   Different(current.CoreVersion, context.CoreVersion) ||
                   Different(current.NavigationFingerprint, context.NavigationFingerprint)
                ? "Navigation context changed."
                : "";
        }

        if (current.Reason == QuestFailureReason.RepeatedDeaths)
        {
            return context.PlayerLevel > current.PlayerLevelAtFailure ||
                   (!string.IsNullOrEmpty(current.EquipmentFingerprint) && Different(current.EquipmentFingerprint, context.EquipmentFingerprint))
                ? "Combat capability changed."
                : "";
        }

        return "";
    }

    private static bool Different(string previous, string current) =>
        !string.IsNullOrEmpty(previous) &&
        !string.IsNullOrEmpty(current) &&
        !string.Equals(previous, current, StringComparison.Ordinal);

    private static QuestRecoveryDecision HalfOpenDecision(int failuresInRollingHour, string resetReason) =>
        failuresInRollingHour >= 6
            ? Deny(QuestRecoveryState.HalfOpen, null, "Rolling-hour retry budget exhausted.")
            : new QuestRecoveryDecision
            {
                State = QuestRecoveryState.HalfOpen,
                MayAttempt = true,
                ResetReason = resetReason,
                Status = "One controlled probe is allowed."
            };

    private static QuestRecoveryDecision Allow(QuestRecoveryState state, string status) =>
        new() { State = state, MayAttempt = true, Status = status };

    private static QuestRecoveryDecision Deny(QuestRecoveryState state, DateTime? retryUtc, string status) =>
        new() { State = state, MayAttempt = false, RetryUtc = retryUtc, Status = status };

    private static QuestRecoveryRecord Copy(
        QuestRecoveryRecord current,
        QuestRecoveryState? state = null,
        QuestFailureReason? reason = null,
        DateTime? firstFailureUtc = null,
        DateTime? lastFailureUtc = null,
        DateTime? cooldownUntilUtc = null,
        bool replaceCooldownUntilUtc = false,
        DateTime? nextHalfOpenUtc = null,
        bool replaceNextHalfOpenUtc = false,
        int? episodeCount = null,
        int? attemptCountInEpisode = null,
        int? deathCountInEpisode = null,
        DateTime? lastProgressUtc = null,
        IReadOnlyList<int>? objectiveCounts = null)
    {
        return new QuestRecoveryRecord
        {
            Key = current.Key,
            State = state ?? current.State,
            Reason = reason ?? current.Reason,
            FirstFailureUtc = firstFailureUtc ?? current.FirstFailureUtc,
            LastFailureUtc = lastFailureUtc ?? current.LastFailureUtc,
            CooldownUntilUtc = replaceCooldownUntilUtc ? cooldownUntilUtc : current.CooldownUntilUtc,
            NextHalfOpenUtc = replaceNextHalfOpenUtc ? nextHalfOpenUtc : current.NextHalfOpenUtc,
            EpisodeCount = episodeCount ?? current.EpisodeCount,
            AttemptCountInEpisode = attemptCountInEpisode ?? current.AttemptCountInEpisode,
            DeathCountInEpisode = deathCountInEpisode ?? current.DeathCountInEpisode,
            LastProgressUtc = lastProgressUtc ?? current.LastProgressUtc,
            ObjectiveCounts = objectiveCounts ?? current.ObjectiveCounts,
            PlayerLevelAtFailure = current.PlayerLevelAtFailure,
            EquipmentFingerprint = current.EquipmentFingerprint,
            DatasetVersion = current.DatasetVersion,
            CoreVersion = current.CoreVersion,
            NavigationFingerprint = current.NavigationFingerprint,
            Evidence = current.Evidence
        };
    }
}
