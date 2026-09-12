using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Logic.Questing.Recovery;

namespace WholesomeAQ
{
    public static class QuestSchedulingPolicy
    {
        private const int MaximumEndpointAttempts = 5;

        public static QuestScheduleResult Select(
            IEnumerable<QuestWorkCandidate> candidates,
            int maximum,
            DateTime utcNow,
            string validatedGrindProfilePath = null!)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            var candidateList = candidates.ToList();
            var earliestRetryUtc = candidateList
                .Select(candidate => candidate.Recovery.RetryUtc)
                .Where(retryUtc => retryUtc.HasValue)
                .OrderBy(retryUtc => retryUtc)
                .FirstOrDefault();

            var ordinary = Order(candidateList.Where(candidate =>
                    candidate.Stage != QuestWorkStage.HalfOpen && candidate.Recovery.MayAttempt))
                .Take(Math.Max(0, maximum))
                .ToArray();

            IReadOnlyList<QuestWorkCandidate> selected = ordinary;
            if (ordinary.Length == 0 && maximum > 0)
            {
                var halfOpen = Order(candidateList.Where(candidate =>
                        candidate.Stage == QuestWorkStage.HalfOpen
                        && candidate.Recovery.MayAttempt
                        && (!candidate.Recovery.RetryUtc.HasValue
                            || candidate.Recovery.RetryUtc.Value <= utcNow)))
                    .FirstOrDefault();
                selected = halfOpen == null
                    ? Array.Empty<QuestWorkCandidate>()
                    : new[] { halfOpen };
            }

            if (selected.Count > 0)
            {
                return new QuestScheduleResult
                {
                    Selected = selected,
                    EarliestRetryUtc = earliestRetryUtc,
                    FallbackMode = QuestFallbackMode.None,
                    Status = $"Selected {selected.Count} quest work candidate(s)."
                };
            }

            if (!string.IsNullOrWhiteSpace(validatedGrindProfilePath))
            {
                return new QuestScheduleResult
                {
                    EarliestRetryUtc = earliestRetryUtc,
                    FallbackMode = QuestFallbackMode.ValidatedGrind,
                    ValidatedGrindProfilePath = validatedGrindProfilePath,
                    Status = "No eligible quest work; using the caller-validated grind profile."
                };
            }

            return new QuestScheduleResult
            {
                EarliestRetryUtc = earliestRetryUtc,
                FallbackMode = QuestFallbackMode.TimedIdle,
                Status = earliestRetryUtc.HasValue
                    ? $"No eligible quest work; idle until {earliestRetryUtc.Value:O}."
                    : "No eligible quest work; waiting for recovery context to change."
            };
        }

        public static IReadOnlyList<QuestEndpointCandidate> Select(
            IEnumerable<QuestEndpointCandidate> candidates,
            int maximum)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            var limit = Math.Min(Math.Max(0, maximum), MaximumEndpointAttempts);
            var eligible = candidates
                .Where(candidate => candidate.IsKnownReachable != false
                                    && candidate.IsKnownSafe != false
                                    && candidate.Recovery.MayAttempt)
                .ToArray();
            var ordinary = OrderEndpoints(eligible.Where(candidate =>
                    !candidate.RequiresHalfOpen && candidate.Recovery.State != QuestRecoveryState.HalfOpen))
                .DistinctBy(candidate => candidate.Key)
                .Take(limit)
                .ToArray();
            if (ordinary.Length > 0)
                return ordinary;

            return OrderEndpoints(eligible.Where(candidate =>
                    candidate.RequiresHalfOpen || candidate.Recovery.State == QuestRecoveryState.HalfOpen))
                .DistinctBy(candidate => candidate.Key)
                .Take(Math.Min(limit, 1))
                .ToArray();
        }

        private static IOrderedEnumerable<QuestEndpointCandidate> OrderEndpoints(
            IEnumerable<QuestEndpointCandidate> candidates) =>
            candidates
                .OrderByDescending(candidate => candidate.IsKnownReachable == true
                                                && candidate.IsKnownSafe == true)
                .ThenByDescending(candidate => candidate.SafetyScore)
                .ThenBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Key.QuestId)
                .ThenBy(candidate => candidate.Key.MapId)
                .ThenBy(candidate => candidate.Key.Endpoint, StringComparer.Ordinal);

        private static IOrderedEnumerable<QuestWorkCandidate> Order(
            IEnumerable<QuestWorkCandidate> candidates) =>
            candidates
                .OrderBy(candidate => candidate.Stage)
                .ThenByDescending(candidate => candidate.SafetyScore)
                .ThenByDescending(candidate => candidate.ChainValue)
                .ThenBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.QuestId);
    }
}
