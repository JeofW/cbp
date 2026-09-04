using Styx.Bot.Quest_Behaviors;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TreeSharp;

internal static class SafeTurnInRegressionTests
{
    public static void Run()
    {
        TestIncompleteRedirectReleasesExactOwner();
        TestDeniedClaimLeavesUnrelatedPoiAndAppliesSafePressurePolicy();
        TestMissingOrActivePrerequisiteAuthorityRetainsQuest();
        TestAbandonmentGuardsDenyUncertainProgressCompletedAndManualCases();
        TestAtomicAbandonmentRecapturesCompletionAndProgress();
        TestThrowingAbandonmentExecutorFinishesAcceptedBatchOnce();
        TestUnknownAuthorityPausesInitializationAndTimers();
        TestCandidateOrderingDedupAndCap();
        TestAllMatchingLiveEndersBecomeBoundedAlternates();
        TestMissingEnderAndExhaustedEndpointsUseNarrowOwnedFailures();
        TestThreeRealCyclesSpanAlternatesAndFormOneEpisode();
        TestObservedCycleStillAdvancesWhenDialogArrivesOnNextPulse();
        TestDelayedIncompleteRedirectPreservesConcurrentWinnerPoi();
        TestRejectedTerminalReportsPreserveReusedWinnerPoi();
        TestSuccessStaleAndDisposeUseExactPoiAndGeneration();
        TestProductionRecoveryLookupHonorsQuestWideManualTerminal();
        TestTerminalOutcomePersistsAcrossFreshManagerReload();
    }

    private static void TestIncompleteRedirectReleasesExactOwner()
    {
        var runtime = new FakeTurnInRuntime
        {
            Snapshot = Snapshot(true, QuestCompletionState.KnownIncomplete, true, 8, 0, 0, 0, 0)
        };
        var behavior = NewBehavior(runtime);
        behavior.OnStart();

        QuestAttemptOutcome redirect = runtime.Reports.Single();
        Assert(behavior.IsDone
               && runtime.EnsureCount == 1
               && runtime.ClaimKey.Equals(QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn))
               && runtime.CapturedCounts.SequenceEqual(new[] { 0, 0, 0, 0 })
               && redirect.Kind == QuestAttemptOutcomeKind.Redirect
               && redirect.Key.Equals(runtime.ClaimKey)
               && redirect.AttemptKey.Equals(runtime.ClaimKey)
               && redirect.AttemptGeneration == 41
               && redirect.Reason == QuestFailureReason.TurnInQuestIncomplete
               && !redirect.IsFailureEpisode
               && runtime.Batches.Count == 0
               && runtime.AbandonAttemptCount == 0
               && runtime.AbandonQuestCount == 0
               && runtime.FlushCount > 0,
            "the real incomplete branch must redirect and release the exact turn-in owner without failure or abandonment");
    }

    private static void TestAllMatchingLiveEndersBecomeBoundedAlternates()
    {
        var runtime = new FakeTurnInRuntime
        {
            Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8)
        };
        runtime.LiveEnders.Add(Candidate(3338, 1, 1, "live-a"));
        runtime.LiveEnders.Add(Candidate(3338, 161, 1, "live-b"));
        var behavior = NewBehavior(runtime);
        behavior.OnStart();
        runtime.Now = runtime.Now.AddSeconds(31);
        behavior.TickForTesting();

        Assert(runtime.CreatedCandidates.Select(candidate => candidate.Source)
                .SequenceEqual(new[] { "live-a", "live-b" }),
            "all distinct matching live ender spawns must be bounded alternates instead of pinning to the first unreachable spawn");
    }

    private static void TestDeniedClaimLeavesUnrelatedPoiAndAppliesSafePressurePolicy()
    {
        var winner = TurnInPoi(876, 3338, new WoWPoint(5, 5, 0));
        var denied = new FakeTurnInRuntime
        {
            ClaimAllowed = false,
            ClaimState = QuestRecoveryState.CoolingDown,
            CurrentPoi = winner,
            Snapshot = Snapshot(true, QuestCompletionState.KnownIncomplete, true, 1, 0, 0, 0, 0),
            RecoveryRecord = Record(QuestRecoveryState.CoolingDown, QuestFailureReason.NoObjectiveProgress)
        };
        var deniedBehavior = NewBehavior(denied);
        deniedBehavior.OnStart();
        Assert(deniedBehavior.IsDone
               && ReferenceEquals(denied.CurrentPoi, winner)
               && denied.ClearCount == 0
               && denied.AbandonQuestCount == 0
               && denied.Reports.Count == 0,
            "a denied turn-in claim must finish without clearing a concurrent winner or abandoning during cooldown");

        var pressured = new FakeTurnInRuntime
        {
            ClaimAllowed = false,
            ClaimState = QuestRecoveryState.Quarantined,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            Snapshot = Snapshot(true, QuestCompletionState.KnownIncomplete, true, 2, 0, 0, 0, 0),
            RecoveryRecord = Record(QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress)
        };
        var pressuredBehavior = NewBehavior(pressured);
        pressuredBehavior.OnStart();
        Assert(pressuredBehavior.IsDone
               && pressured.AbandonQuestCount == 1
               && pressured.ClientMutationEvents.SequenceEqual(new[] { "persist", "abandon" }),
            "a certain accepted incomplete zero-progress automatic quarantine must persist before abandoning under two-slot pressure");
    }

    private static void TestMissingOrActivePrerequisiteAuthorityRetainsQuest()
    {
        foreach (QuestPrerequisiteStatus status in new[]
        {
            QuestPrerequisiteStatus.Unknown,
            QuestPrerequisiteStatus.Active
        })
        {
            var runtime = new FakeTurnInRuntime
            {
                ClaimAllowed = false,
                ClaimState = QuestRecoveryState.Quarantined,
                PrerequisiteStatus = status,
                Snapshot = Snapshot(
                    true, QuestCompletionState.KnownIncomplete, true, 2, 0, 0, 0, 0),
                RecoveryRecord = Record(
                    QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress)
            };

            NewBehavior(runtime).OnStart();

            Assert(runtime.AbandonQuestCount == 0
                   && runtime.ClientMutationEvents.Count == 0
                   && runtime.LastAbandonmentDecision?.Reason.Contains(
                       "prerequisite", StringComparison.OrdinalIgnoreCase) == true,
                "SafeTurnIn must retain a quest when prerequisite authority is " + status);
        }
    }

    private static void TestAbandonmentGuardsDenyUncertainProgressCompletedAndManualCases()
    {
        var cases = new[]
        {
            (Snapshot(true, QuestCompletionState.KnownIncomplete, false, 2, 0, 0, 0, 0), Record(QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress), "uncertain"),
            (Snapshot(true, QuestCompletionState.KnownIncomplete, true, 2, 0, 1, 0, 0), Record(QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress), "objective progress"),
            (Snapshot(true, QuestCompletionState.KnownIncomplete, true, 2, 0, 0, 0, 2), Record(QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress), "required item progress"),
            (Snapshot(true, QuestCompletionState.KnownComplete, true, 2, 0, 0, 0, 0), Record(QuestRecoveryState.Quarantined, QuestFailureReason.TurnInTargetNotOffered), "completed"),
            (Snapshot(true, QuestCompletionState.KnownIncomplete, true, 2, 0, 0, 0, 0), Record(QuestRecoveryState.ManualBlacklist, QuestFailureReason.UserExcluded), "manual"),
            (Snapshot(true, QuestCompletionState.KnownIncomplete, true, 3, 0, 0, 0, 0), Record(QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress), "no pressure")
        };

        foreach (var item in cases)
        {
            var runtime = new FakeTurnInRuntime
            {
                ClaimAllowed = false,
                ClaimState = item.Item2.State,
                PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
                Snapshot = item.Item1,
                RecoveryRecord = item.Item2
            };
            var behavior = NewBehavior(runtime);
            behavior.OnStart();
            Assert(runtime.AbandonQuestCount == 0,
                "automatic abandonment must deny the " + item.Item3 + " guard case");
        }

        var unpersisted = new FakeTurnInRuntime
        {
            ClaimAllowed = false,
            ClaimState = QuestRecoveryState.Quarantined,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            PersistAllowed = false,
            Snapshot = Snapshot(true, QuestCompletionState.KnownIncomplete, true, 2, 0, 0, 0, 0),
            RecoveryRecord = Record(QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress)
        };
        NewBehavior(unpersisted).OnStart();
        Assert(unpersisted.AbandonQuestCount == 0
               && unpersisted.ClientMutationEvents.SequenceEqual(new[] { "persist" }),
            "automatic abandonment must be withheld when recovery state cannot be persisted first");
    }

    private static void TestAtomicAbandonmentRecapturesCompletionAndProgress()
    {
        var recaptures = new[]
        {
            (Snapshot(true, QuestCompletionState.KnownComplete, true, 2, 0, 0, 0, 0), "completed"),
            (Snapshot(true, QuestCompletionState.KnownIncomplete, true, 2, 1, 0, 0, 0), "progressed")
        };
        foreach (var item in recaptures)
        {
            var runtime = new FakeTurnInRuntime
            {
                ClaimAllowed = false,
                ClaimState = QuestRecoveryState.Quarantined,
                PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
                RecoveryRecord = Record(
                    QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress)
            };
            runtime.Snapshots.Enqueue(
                Snapshot(true, QuestCompletionState.KnownIncomplete, true, 2, 0, 0, 0, 0));
            runtime.Snapshots.Enqueue(item.Item1);

            NewBehavior(runtime).OnStart();

            Assert(runtime.AbandonQuestCount == 0
                   && runtime.SnapshotReadCount == 2
                   && runtime.ClientMutationEvents.Count == 0,
                "atomic abandonment must recapture and retain a newly " + item.Item2 + " quest");
        }
    }

    private static void TestUnknownAuthorityPausesInitializationAndTimers()
    {
        var runtime = new FakeTurnInRuntime
        {
            Snapshot = Snapshot(false, QuestCompletionState.Unknown, false, 8),
            DatabaseEnder = Candidate(3338, 100, 0)
        };
        var behavior = NewBehavior(runtime);
        behavior.OnStart();
        runtime.Now = runtime.Now.AddMinutes(10);
        behavior.TickForTesting();
        Assert(runtime.CreatedCandidates.Count == 0
               && runtime.Reports.Count == 0
               && runtime.Batches.Count == 0,
            "unknown completion authority must defer child work and every timeout/report");

        runtime.Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8);
        behavior.TickForTesting();
        var child = runtime.Children.Single();
        int ticksBeforeDeferral = child.TickCount;
        child.ExecutionDeferred = true;
        behavior.TickForTesting();
        runtime.Now = runtime.Now.AddMinutes(10);
        behavior.TickForTesting();
        Assert(child.TickCount == ticksBeforeDeferral && runtime.Batches.Count == 0,
            "a deferred turn-in child must pause interaction and navigation timers");

        child.ExecutionDeferred = false;
        behavior.TickForTesting();
        Assert(child.TickCount == ticksBeforeDeferral + 1 && runtime.Batches.Count == 0,
            "resuming authority must restore the time budget instead of expiring it");
    }

    private static void TestThrowingAbandonmentExecutorFinishesAcceptedBatchOnce()
    {
        var runtime = RuntimeWithOneLiveCandidate();
        runtime.RecoveryRecord = Record(
            QuestRecoveryState.Quarantined, QuestFailureReason.NoObjectiveProgress);
        runtime.PrerequisiteStatus = QuestPrerequisiteStatus.NotActive;
        runtime.ThrowOnAutomaticAbandonment = true;
        var behavior = NewBehavior(runtime);
        behavior.OnStart();
        var ownedPoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        runtime.Children[0].OnTick = () => runtime.CurrentPoi = ownedPoi;
        behavior.TickForTesting();
        runtime.OnBatch = () =>
        {
            runtime.CurrentPoi = ownedPoi;
            runtime.Snapshot = Snapshot(
                true, QuestCompletionState.KnownIncomplete, true, 2, 0, 0, 0, 0);
        };
        runtime.Children[0].Outcomes.Enqueue(Dialog(1, 867));
        runtime.Children[0].Outcomes.Enqueue(Dialog(2, 867));
        runtime.Children[0].Outcomes.Enqueue(Dialog(3, 867));
        behavior.TickForTesting();
        behavior.TickForTesting();
        behavior.TickForTesting();
        behavior.TickForTesting();

        Assert(behavior.IsDone
               && runtime.Batches.Count == 1
               && runtime.ClientMutationEvents.SequenceEqual(new[] { "persist", "abandon" })
               && runtime.ClearCount == 1
               && runtime.AbandonAttemptCount == 0,
            "a throwing abandonment executor after an accepted batch must be contained, finish once, and clear only its proven owned POI");
    }

    private static void TestCandidateOrderingDedupAndCap()
    {
        var runtime = new FakeTurnInRuntime { Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8) };
        runtime.LiveEnders.Add(Candidate(3338, 1, 1, "live"));
        runtime.DatabaseEnder = Candidate(3338, 81, 1, "db");
        var behavior = NewBehavior(runtime,
            "4001,2,2,0;4002,161,1,0;4003,241,1,0;4004,321,1,0;4005,401,1,0;4006,481,1,0");
        behavior.OnStart();

        for (var index = 0; index < 5 && !behavior.IsDone; index++)
        {
            runtime.Now = runtime.Now.AddSeconds(31);
            behavior.TickForTesting();
        }

        Assert(runtime.CreatedCandidates.Select(candidate => candidate.Entry)
                .SequenceEqual(new uint[] { 3338, 3338, 4002, 4003, 4004 })
               && runtime.CreatedCandidates.Count == 5,
            "turn-in candidates must be live, database, then strict alternates, deduped and capped at five stable cells");
    }

    private static void TestMissingEnderAndExhaustedEndpointsUseNarrowOwnedFailures()
    {
        var missing = new FakeTurnInRuntime { Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8) };
        var missingBehavior = NewBehavior(missing);
        missingBehavior.OnStart();
        Assert(missingBehavior.IsDone && missing.Batches.Count == 1,
            "missing live, database, and alternate ender evidence must end one outer episode");
        Assert(missing.Batches[0].Count == 2
               && missing.Batches[0][0].Key.Scope == QuestRecoveryScope.NpcRelation
               && missing.Batches[0][0].Reason == QuestFailureReason.NpcMissingFromDatabase
               && missing.Batches[0][1].Key.Equals(QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn)),
            "missing ender evidence must remain relation-scoped until the one final turn-in stage outcome");

        var runtime = new FakeTurnInRuntime { Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8) };
        runtime.LiveEnders.Add(Candidate(3338, 1, 1, "live"));
        runtime.DatabaseEnder = Candidate(3338, 81, 1, "db");
        var behavior = NewBehavior(runtime, "3391,161,1,0");
        behavior.OnStart();
        for (var index = 0; index < 3; index++)
        {
            runtime.Now = runtime.Now.AddSeconds(31);
            behavior.TickForTesting();
        }
        Assert(behavior.IsDone && runtime.Batches.Count == 1
               && runtime.Batches[0].Count(outcome => outcome.Key.Scope == QuestRecoveryScope.Endpoint) == 3
               && runtime.Batches[0].Last().Reason == QuestFailureReason.EndpointUnreachable,
            "each failed location must stay endpoint-scoped and all locations must produce only one outer stage episode");
        Assert(runtime.Batches[0].All(outcome => outcome.AttemptKey.Equals(QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn))
                                                  && outcome.AttemptGeneration == 41),
            "every generated turn-in failure must carry the exact stage owner generation");
    }

    private static void TestThreeRealCyclesSpanAlternatesAndFormOneEpisode()
    {
        var runtime = new FakeTurnInRuntime
        {
            Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8),
            PlayerLocation = new WoWPoint(1, 1, 0)
        };
        runtime.LiveEnders.Add(Candidate(3338, 1, 1, "live"));
        var behavior = NewBehavior(runtime, "3391,161,1,0;3392,321,1,0");
        behavior.OnStart();

        runtime.Children[0].Outcomes.Enqueue(Dialog(1, 867));
        behavior.TickForTesting();
        runtime.Children[1].Outcomes.Enqueue(Dialog(1, 875));
        behavior.TickForTesting();
        runtime.Children[2].Outcomes.Enqueue(Dialog(1, 0));
        behavior.TickForTesting();

        Assert(behavior.IsDone
               && runtime.CreatedCandidates.Select(candidate => candidate.Entry)
                    .SequenceEqual(new uint[] { 3338, 3391, 3392 })
               && runtime.Reports.Count(outcome => outcome.Kind == QuestAttemptOutcomeKind.Observation) == 3
               && runtime.Batches.Count == 1,
            "three distinct real cycles must span child replacements while natural mismatches advance ordered alternates");
        Assert(runtime.Batches[0].Count(outcome => outcome.Key.Scope == QuestRecoveryScope.NpcRelation) == 3
               && runtime.Batches[0].Last().Reason == QuestFailureReason.TurnInTargetNotOffered,
            "three tried ender relations must feed one and only one turn-in stage failure episode");
    }

    private static void TestObservedCycleStillAdvancesWhenDialogArrivesOnNextPulse()
    {
        var runtime = new FakeTurnInRuntime
        {
            Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8),
            PlayerLocation = new WoWPoint(1, 1, 0)
        };
        runtime.LiveEnders.Add(Candidate(3338, 1, 1, "live"));
        var behavior = NewBehavior(runtime, "3391,161,1,0");
        behavior.OnStart();

        runtime.Children[0].CycleId = 1;
        behavior.TickForTesting();
        runtime.Children[0].Outcomes.Enqueue(Dialog(1, 867));
        behavior.TickForTesting();

        Assert(runtime.CreatedCandidates.Select(candidate => candidate.Entry)
                .SequenceEqual(new uint[] { 3338, 3391 })
               && runtime.Reports.Count(outcome => outcome.Kind == QuestAttemptOutcomeKind.Observation) == 1,
            "a real cycle observed before its dialog result must still classify the mismatch and advance naturally");
    }

    private static void TestDelayedIncompleteRedirectPreservesConcurrentWinnerPoi()
    {
        var runtime = RuntimeWithOneLiveCandidate();
        var behavior = NewBehavior(runtime);
        behavior.OnStart();
        var ownedPoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        runtime.Children[0].OnTick = () => runtime.CurrentPoi = ownedPoi;
        behavior.TickForTesting();

        runtime.Snapshot = Snapshot(true, QuestCompletionState.KnownIncomplete, true, 8);
        runtime.RedirectAccepted = false;
        runtime.RedirectDecision = new QuestRecoveryDecision
        {
            State = QuestRecoveryState.Attempting,
            MayAttempt = false,
            AttemptGeneration = 42,
            Status = "newer owner retained"
        };
        runtime.OnReport = _ => runtime.CurrentPoi = ownedPoi;
        behavior.TickForTesting();
        behavior.Dispose();

        QuestAttemptOutcome redirect = runtime.Reports.Last();
        Assert(behavior.IsDone
               && redirect.Kind == QuestAttemptOutcomeKind.Redirect
               && redirect.AttemptGeneration == 41
               && ReferenceEquals(runtime.CurrentPoi, ownedPoi)
               && runtime.ClearCount == 0
               && runtime.AbandonAttemptCount == 1
               && runtime.AbandonedKey.Equals(
                   QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn))
               && runtime.AbandonedGeneration == 41,
            "a rejected delayed redirect must retain local owner A until disposal's exact-generation neutral release without clearing B's reused POI object");
    }

    private static void TestSuccessStaleAndDisposeUseExactPoiAndGeneration()
    {
        var successRuntime = RuntimeWithOneLiveCandidate();
        var success = NewBehavior(successRuntime);
        success.OnStart();
        BotPoi ownedPoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        successRuntime.Children[0].OnTick = () => successRuntime.CurrentPoi = ownedPoi;
        success.TickForTesting();
        successRuntime.Snapshot = Snapshot(false, QuestCompletionState.KnownComplete, true, 8);
        success.TickForTesting();
        QuestAttemptOutcome completed = successRuntime.Reports.Last();
        Assert(success.IsDone
               && completed.Kind == QuestAttemptOutcomeKind.Success
               && completed.Key.Equals(QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn))
               && completed.AttemptGeneration == 41
               && successRuntime.ClearCount == 1
               && successRuntime.AbandonAttemptCount == 0
               && successRuntime.FlushCount > 0,
            "confirmed turn-in must report exact-generation success and clear only its exact POI");

        var staleRuntime = RuntimeWithOneLiveCandidate();
        var stale = NewBehavior(staleRuntime);
        stale.OnStart();
        var stalePoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        staleRuntime.Children[0].OnTick = () => staleRuntime.CurrentPoi = stalePoi;
        stale.TickForTesting();
        var winner = TurnInPoi(876, 3338, new WoWPoint(2, 2, 0));
        staleRuntime.OnBatch = () => staleRuntime.CurrentPoi = winner;
        staleRuntime.AcceptBatch = false;
        staleRuntime.Children[0].Outcomes.Enqueue(Dialog(1, 867));
        staleRuntime.Children[0].Outcomes.Enqueue(Dialog(2, 867));
        staleRuntime.Children[0].Outcomes.Enqueue(Dialog(3, 867));
        stale.TickForTesting();
        stale.TickForTesting();
        stale.TickForTesting();
        Assert(stale.IsDone && ReferenceEquals(staleRuntime.CurrentPoi, winner) && staleRuntime.ClearCount == 0,
            "a stale terminal rejection must not clear a concurrent same-quest replacement POI");

        var disposedRuntime = RuntimeWithOneLiveCandidate();
        var disposed = NewBehavior(disposedRuntime);
        disposed.OnStart();
        var disposedPoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        disposedRuntime.Children[0].OnTick = () => disposedRuntime.CurrentPoi = disposedPoi;
        disposed.TickForTesting();
        disposed.Dispose();
        Assert(disposedRuntime.ClearCount == 1
               && disposedRuntime.AbandonAttemptCount == 1
               && disposedRuntime.AbandonedKey.Equals(QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn))
               && disposedRuntime.AbandonedGeneration == 41
               && disposedRuntime.Children[0].DisposeCount == 1
               && disposedRuntime.FlushCount > 0,
            "neutral disposal must release the exact owner generation and exact POI without abandoning the quest");
    }

    private static void TestRejectedTerminalReportsPreserveReusedWinnerPoi()
    {
        var batchRuntime = RuntimeWithOneLiveCandidate();
        var batch = NewBehavior(batchRuntime);
        batch.OnStart();
        var batchPoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        batchRuntime.Children[0].OnTick = () => batchRuntime.CurrentPoi = batchPoi;
        batch.TickForTesting();
        batchRuntime.AcceptBatch = false;
        batchRuntime.OnBatch = () => batchRuntime.CurrentPoi = batchPoi;
        batchRuntime.Children[0].Outcomes.Enqueue(Dialog(1, 867));
        batchRuntime.Children[0].Outcomes.Enqueue(Dialog(2, 867));
        batchRuntime.Children[0].Outcomes.Enqueue(Dialog(3, 867));
        batch.TickForTesting();
        batch.TickForTesting();
        batch.TickForTesting();
        batch.Dispose();
        Assert(batch.IsDone
               && ReferenceEquals(batchRuntime.CurrentPoi, batchPoi)
               && batchRuntime.ClearCount == 0
               && batchRuntime.AbandonAttemptCount == 1
               && batchRuntime.AbandonedGeneration == 41,
            "a rejected generated batch must not clear B's reused POI and must neutrally dispose stale owner A");

        var failureRuntime = RuntimeWithOneLiveCandidate();
        var failure = NewBehavior(failureRuntime);
        failure.OnStart();
        var failurePoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        failureRuntime.Children[0].OnTick = () => failureRuntime.CurrentPoi = failurePoi;
        failure.TickForTesting();
        failureRuntime.OwnedOutcomeAccepted = false;
        failureRuntime.OnReport = _ => failureRuntime.CurrentPoi = failurePoi;
        InvokeStageFailure(failure, "delayed owner A stage failure");
        failure.Dispose();
        Assert(failure.IsDone
               && ReferenceEquals(failureRuntime.CurrentPoi, failurePoi)
               && failureRuntime.ClearCount == 0
               && failureRuntime.AbandonAttemptCount == 1
               && failureRuntime.AbandonedGeneration == 41,
            "a rejected stage failure must not clear B's reused POI and must neutrally dispose stale owner A");

        var successRuntime = RuntimeWithOneLiveCandidate();
        var success = NewBehavior(successRuntime);
        success.OnStart();
        var successPoi = TurnInPoi(876, 3338, new WoWPoint(1, 1, 0));
        successRuntime.Children[0].OnTick = () => successRuntime.CurrentPoi = successPoi;
        success.TickForTesting();
        successRuntime.OwnedOutcomeAccepted = false;
        successRuntime.OnReport = _ => successRuntime.CurrentPoi = successPoi;
        successRuntime.Snapshot = Snapshot(false, QuestCompletionState.KnownComplete, true, 8);
        success.TickForTesting();
        success.Dispose();
        Assert(success.IsDone
               && ReferenceEquals(successRuntime.CurrentPoi, successPoi)
               && successRuntime.ClearCount == 0
               && successRuntime.AbandonAttemptCount == 1
               && successRuntime.AbandonedGeneration == 41,
            "a rejected success must not clear B's reused POI and must neutrally dispose stale owner A");
    }

    private static void InvokeStageFailure(SafeTurnIn behavior, string evidence)
    {
        MethodInfo method = typeof(SafeTurnIn).GetMethod(
            "ReportStageFailure", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SafeTurnIn.ReportStageFailure was not found.");
        method.Invoke(behavior, new object[] { QuestFailureReason.InternalBehaviorError, evidence });
    }

    private static void TestProductionRecoveryLookupHonorsQuestWideManualTerminal()
    {
        string settingsRoot = Path.Combine(
            AppContext.BaseDirectory, "safe-turnin-terminal-precedence");
        if (Directory.Exists(settingsRoot))
            Directory.Delete(settingsRoot, recursive: true);

        try
        {
            var turnIn = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn);
            var manualPickup = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.Pickup);
            string storePath = Path.Combine(
                settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
            new QuestRecoveryStore(storePath).Save(new QuestRecoveryDocument
            {
                CharacterName = "Jeof",
                RealmName = "Lordaeron",
                Records = new[]
                {
                    new QuestRecoveryRecord
                    {
                        Key = turnIn,
                        State = QuestRecoveryState.Quarantined,
                        Reason = QuestFailureReason.NoObjectiveProgress,
                        EpisodeCount = 3,
                        AttemptGeneration = 41
                    },
                    new QuestRecoveryRecord
                    {
                        Key = manualPickup,
                        State = QuestRecoveryState.ManualBlacklist,
                        Reason = QuestFailureReason.UserExcluded,
                        AttemptGeneration = 40
                    }
                }
            });
            QuestRecoveryManager.Instance.Configure(new QuestRecoveryEnvironment(
                settingsRoot, "Jeof", "Lordaeron", "quest-data-v1", "core-v1", "nav-v1"));

            QuestAbandonmentDecision selected = new ProductionSafeTurnInRuntime()
                .TryExecuteAutomaticAbandonment(
                    turnIn,
                    () => new QuestAbandonmentLiveSnapshot
                    {
                        IsAccepted = true,
                        IsCompleted = false,
                        StateIsCertain = true,
                        HasObjectiveProgress = false,
                        PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
                        FreeQuestLogSlots = 2
                    });

            Assert(!selected.MayAbandon
                   && QuestRecoveryManager.Instance.GetRecord(turnIn)?.State ==
                       QuestRecoveryState.ManualBlacklist
                   && QuestRecoveryManager.Instance.GetRecord(turnIn)?.Key.Equals(manualPickup) == true,
                "production SafeTurnIn must use quest-wide manual precedence and deny automatic abandonment under log pressure");
        }
        finally
        {
            if (Directory.Exists(settingsRoot))
                Directory.Delete(settingsRoot, recursive: true);
        }
    }

    private static void TestTerminalOutcomePersistsAcrossFreshManagerReload()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "turnin-terminal-reload");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        var environment = new QuestRecoveryEnvironment(
            root, "Jeof", "Lordaeron", "quest-data-v1", "core-v1", "nav-v1");
        var manager = new QuestRecoveryManager(new AdapterTestClock());
        manager.Configure(environment);
        var runtime = new ManagerBackedTurnInRuntime(manager)
        {
            Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8)
        };
        var behavior = NewBehavior(runtime);
        behavior.OnStart();
        Assert(behavior.IsDone && runtime.FlushCount > 0,
            "SafeTurnIn must flush an accepted terminal missing-ender outcome before returning");

        var reloaded = new QuestRecoveryManager(new AdapterTestClock());
        reloaded.Configure(environment);
        QuestRecoveryRecord record = reloaded.GetRecord(
            QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn));
        Assert(record != null && record.Reason == QuestFailureReason.NpcMissingFromDatabase,
            "a fresh manager must observe SafeTurnIn's terminal stage outcome after adapter return");
    }

    private static FakeTurnInRuntime RuntimeWithOneLiveCandidate()
    {
        var runtime = new FakeTurnInRuntime { Snapshot = Snapshot(true, QuestCompletionState.KnownComplete, true, 8) };
        runtime.LiveEnders.Add(Candidate(3338, 1, 1, "live"));
        return runtime;
    }

    private static SafeTurnIn NewBehavior(FakeTurnInRuntime runtime, string alternates = "")
    {
        var args = new Dictionary<string, string>
        {
            ["QuestId"] = "876",
            ["QuestName"] = "Test quest",
            ["TurnInId"] = "3338",
            ["TurnInName"] = "Test ender",
            ["TimeoutSeconds"] = "5",
            ["NavigationTimeoutSeconds"] = "30",
            ["ArrivalDistance"] = "15"
        };
        if (!string.IsNullOrEmpty(alternates))
            args["AlternateEnders"] = alternates;
        return new SafeTurnIn(args, runtime);
    }

    private static SafeTurnInEnderCandidate Candidate(uint entry, float x, float y, string source = "alternate") =>
        new(entry, "ender " + entry, new WoWPoint(x, y, 0), source);

    private static SafeTurnInQuestSnapshot Snapshot(
        bool accepted,
        QuestCompletionState state,
        bool progressCertain,
        int freeSlots,
        params int[] counts) =>
        new(new QuestCompletionSnapshot(accepted, state), counts, progressCertain, freeSlots);

    private static QuestRecoveryRecord Record(QuestRecoveryState state, QuestFailureReason reason) => new()
    {
        Key = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn),
        State = state,
        Reason = reason,
        AttemptGeneration = 41
    };

    private static SafeTurnInDialogOutcome Dialog(long cycle, uint shown) => new()
    {
        InteractionCycleId = cycle,
        ShownQuestId = shown,
        OfferedQuestIds = shown == 0 ? Array.Empty<uint>() : new uint[] { shown }
    };

    private static BotPoi TurnInPoi(uint questId, uint entry, WoWPoint location) => new(PoiType.QuestTurnIn)
    {
        Entry = entry,
        Location = location,
        Name = "quest " + questId
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private class FakeTurnInRuntime : SafeTurnInRuntime
    {
        public DateTime Now { get; set; } = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
        public bool ClaimAllowed { get; set; } = true;
        public QuestRecoveryState ClaimState { get; set; } = QuestRecoveryState.Attempting;
        public bool AcceptBatch { get; set; } = true;
        public bool PersistAllowed { get; set; } = true;
        public bool ThrowOnAutomaticAbandonment { get; set; }
        public QuestPrerequisiteStatus PrerequisiteStatus { get; set; } =
            QuestPrerequisiteStatus.Unknown;
        public SafeTurnInQuestSnapshot Snapshot { get; set; } = Snapshot(true, QuestCompletionState.KnownComplete, true, 8);
        public Queue<SafeTurnInQuestSnapshot> Snapshots { get; } = new();
        public QuestRecoveryRecord RecoveryRecord { get; set; }
        public WoWPoint PlayerLocation { get; set; } = new(1000, 1000, 0);
        public int MapId { get; set; } = 1;
        public BotPoi CurrentPoi { get; set; } = new(PoiType.None);
        public int EnsureCount { get; private set; }
        public int ClearCount { get; private set; }
        public int AbandonAttemptCount { get; private set; }
        public int AbandonQuestCount { get; private set; }
        public int FlushCount { get; private set; }
        public int SnapshotReadCount { get; private set; }
        public QuestRecoveryKey ClaimKey { get; private set; }
        public QuestRecoveryKey AbandonedKey { get; private set; }
        public QuestAbandonmentDecision LastAbandonmentDecision { get; private set; }
        public long AbandonedGeneration { get; private set; }
        public IReadOnlyList<int> CapturedCounts { get; private set; } = Array.Empty<int>();
        public List<SafeTurnInEnderCandidate> LiveEnders { get; } = new();
        public SafeTurnInEnderCandidate DatabaseEnder { get; set; }
        public List<SafeTurnInEnderCandidate> CreatedCandidates { get; } = new();
        public List<FakeTurnInChild> Children { get; } = new();
        public List<QuestAttemptOutcome> Reports { get; } = new();
        public List<IReadOnlyList<QuestAttemptOutcome>> Batches { get; } = new();
        public List<string> ClientMutationEvents { get; } = new();
        public System.Action OnBatch { get; set; }
        public System.Action<QuestAttemptOutcome> OnReport { get; set; }
        public QuestRecoveryDecision ReportDecision { get; set; } = new()
        {
            MayAttempt = true,
            State = QuestRecoveryState.Eligible
        };
        public bool RedirectAccepted { get; set; } = true;
        public bool OwnedOutcomeAccepted { get; set; } = true;
        public QuestRecoveryDecision RedirectDecision { get; set; } = new()
        {
            MayAttempt = true,
            State = QuestRecoveryState.Eligible
        };

        public override DateTime UtcNow => Now;
        public override WoWPoint CurrentPlayerLocation => PlayerLocation;
        public override int CurrentMapId => MapId;
        public override BotPoi CurrentBotPoi => CurrentPoi;
        public override void EnsureConfigured() => EnsureCount++;
        public override QuestRecoveryContext CaptureContext(IReadOnlyList<int> objectiveAndItemCounts)
        {
            CapturedCounts = objectiveAndItemCounts.ToArray();
            return new QuestRecoveryContext { PlayerLevel = 34, ObjectiveCounts = CapturedCounts };
        }
        public override QuestRecoveryDecision TryBeginAttempt(QuestRecoveryKey key, QuestRecoveryContext context)
        {
            ClaimKey = key;
            return new QuestRecoveryDecision
            {
                MayAttempt = ClaimAllowed,
                State = ClaimAllowed ? QuestRecoveryState.Attempting : ClaimState,
                AttemptGeneration = ClaimAllowed ? 41 : 0,
                Status = ClaimAllowed ? "owned" : "denied"
            };
        }
        public override SafeTurnInQuestSnapshot GetQuestSnapshot(uint questId)
        {
            SnapshotReadCount++;
            return Snapshots.Count == 0 ? Snapshot : Snapshots.Dequeue();
        }
        public override QuestPrerequisiteStatus GetPrerequisiteStatus(uint questId) =>
            PrerequisiteStatus;
        public override IReadOnlyList<SafeTurnInEnderCandidate> FindLiveEnders(uint entry) => LiveEnders;
        public override SafeTurnInEnderCandidate FindDatabaseEnder(uint entry, string name) => DatabaseEnder;
        public override ISafeTurnInChild CreateChild(uint questId, string questName, SafeTurnInEnderCandidate candidate)
        {
            CreatedCandidates.Add(candidate);
            var child = new FakeTurnInChild();
            Children.Add(child);
            return child;
        }
        public override QuestRecoveryDecision Report(QuestAttemptOutcome outcome, QuestRecoveryContext context)
        {
            Reports.Add(outcome);
            OnReport?.Invoke(outcome);
            return ReportDecision;
        }
        public override QuestRecoveryReportResult TryReportOwnedRedirect(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context)
        {
            Reports.Add(outcome);
            OnReport?.Invoke(outcome);
            return new QuestRecoveryReportResult
            {
                Accepted = RedirectAccepted,
                Decision = RedirectDecision
            };
        }
        public override QuestRecoveryReportResult TryReportOwnedOutcome(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context)
        {
            Reports.Add(outcome);
            OnReport?.Invoke(outcome);
            return new QuestRecoveryReportResult
            {
                Accepted = OwnedOutcomeAccepted,
                Decision = ReportDecision
            };
        }
        public override bool TryReportGeneratedFailures(IReadOnlyList<QuestAttemptOutcome> outcomes, QuestRecoveryContext context, out IReadOnlyList<QuestRecoveryDecision> decisions)
        {
            Batches.Add(outcomes.ToArray());
            OnBatch?.Invoke();
            decisions = outcomes.Select(_ => new QuestRecoveryDecision { State = QuestRecoveryState.CoolingDown }).ToArray();
            return AcceptBatch;
        }
        public override bool OwnsAttempt(QuestRecoveryKey key, long generation) => ClaimAllowed && Batches.Count == 0;
        public override void AbandonAttempt(QuestRecoveryKey key, long generation)
        {
            AbandonAttemptCount++;
            AbandonedKey = key;
            AbandonedGeneration = generation;
        }
        public override bool TryFlush()
        {
            FlushCount++;
            return true;
        }
        public override QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
            QuestRecoveryKey key,
            Func<QuestAbandonmentLiveSnapshot> recapture)
        {
            QuestAbandonmentLiveSnapshot live = recapture();
            QuestAbandonmentDecision decision = QuestAbandonmentPolicy.Evaluate(
                new QuestAbandonmentContext
                {
                    IsAccepted = live.IsAccepted,
                    IsCompleted = live.IsCompleted,
                    StateIsCertain = live.StateIsCertain,
                    HasObjectiveProgress = live.HasObjectiveProgress,
                    PrerequisiteStatus = live.PrerequisiteStatus,
                    FreeQuestLogSlots = live.FreeQuestLogSlots,
                    RecoveryState = RecoveryRecord?.State ?? QuestRecoveryState.Eligible,
                    Reason = RecoveryRecord?.Reason ?? QuestFailureReason.None
                });
            LastAbandonmentDecision = decision;
            if (!decision.MayAbandon)
                return decision;
            ClientMutationEvents.Add("persist");
            if (!PersistAllowed)
            {
                return new QuestAbandonmentDecision
                {
                    MayAbandon = false,
                    Reason = "persistence failed"
                };
            }
            ClientMutationEvents.Add("abandon");
            if (ThrowOnAutomaticAbandonment)
                throw new InvalidOperationException("abandon executor boom");
            AbandonQuestCount++;
            return decision;
        }
        public override void ClearBotPoi(string reason)
        {
            ClearCount++;
            CurrentPoi = new BotPoi(PoiType.None);
        }
    }

    private sealed class ManagerBackedTurnInRuntime : FakeTurnInRuntime
    {
        private readonly QuestRecoveryManager _manager;

        public ManagerBackedTurnInRuntime(QuestRecoveryManager manager) => _manager = manager;
        public override QuestRecoveryDecision TryBeginAttempt(
            QuestRecoveryKey key, QuestRecoveryContext context) => _manager.TryBeginAttempt(key, context);
        public override QuestRecoveryReportResult TryReportOwnedOutcome(
            QuestAttemptOutcome outcome, QuestRecoveryContext context) =>
            _manager.TryReportOwnedOutcome(outcome, context);
        public override QuestRecoveryReportResult TryReportOwnedRedirect(
            QuestAttemptOutcome outcome, QuestRecoveryContext context) =>
            _manager.TryReportOwnedRedirect(outcome, context);
        public override bool TryReportGeneratedFailures(
            IReadOnlyList<QuestAttemptOutcome> outcomes,
            QuestRecoveryContext context,
            out IReadOnlyList<QuestRecoveryDecision> decisions)
        {
            Batches.Add(outcomes.ToArray());
            return _manager.TryReportGeneratedFailures(outcomes, context, out decisions);
        }
        public override bool OwnsAttempt(QuestRecoveryKey key, long generation) =>
            _manager.OwnsAttempt(key, generation);
        public override void AbandonAttempt(QuestRecoveryKey key, long generation) =>
            _manager.AbandonAttempt(key, generation);
        public override QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
            QuestRecoveryKey key,
            Func<QuestAbandonmentLiveSnapshot> recapture) =>
            _manager.TryExecuteAutomaticAbandonment(key, recapture, () => { });
        public override bool TryFlush()
        {
            base.TryFlush();
            return _manager.TryFlush();
        }
    }

    private sealed class FakeTurnInChild : ISafeTurnInChild
    {
        public Queue<SafeTurnInDialogOutcome> Outcomes { get; } = new();
        public bool Done { get; set; }
        public bool ExecutionDeferred { get; set; }
        public int TickCount { get; private set; }
        public int DisposeCount { get; private set; }
        public long CycleId { get; set; }
        public System.Action OnTick { get; set; }
        public bool IsDone => Done;
        public bool IsExecutionDeferred => ExecutionDeferred;
        public long InteractionCycleId => CycleId;
        public bool HasVisibleDialog => false;
        public void OnStart() { }
        public RunStatus Tick(object context)
        {
            TickCount++;
            OnTick?.Invoke();
            return RunStatus.Running;
        }
        public bool TryConsumeOutcome(out SafeTurnInDialogOutcome outcome)
        {
            outcome = Outcomes.Count == 0 ? null : Outcomes.Dequeue();
            return outcome != null;
        }
        public void Dispose() => DisposeCount++;
    }
}
