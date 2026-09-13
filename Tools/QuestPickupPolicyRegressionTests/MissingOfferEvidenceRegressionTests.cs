using System.Runtime.CompilerServices;
using Bots.Quest.QuestOrder;
using Styx.Logic.Questing.Recovery;

internal static class MissingOfferEvidenceRegressionTests
{
    private static QuestPickupDialogDecision Missing(uint[] offered, uint target = 876, uint giver = 123,
        uint shown = 0, string title = "") => QuestPickupDialogPolicy.Decide(
        target, "Target quest", shown, title, giver, offered,
        false, false, false, false, false, false, false, offeredQuestListLoaded: true);

    private static QuestPickupDialogDecision Target(bool canAccept) => QuestPickupDialogPolicy.Decide(
        876, "Target quest", 876, "Target quest", 123, new uint[] { 876 },
        canAccept, false, false, false, false, false, false, offeredQuestListLoaded: true);

    internal static void Run()
    {
        var tests = new List<(string Name, Action Run)>();
        void Test(string name, Action run) => tests.Add((name, run));
        void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        void Three(params QuestPickupDialogDecision[] observations)
        {
            var tracker = new QuestPickupMismatchTracker();
            for (int i = 0; i < observations.Length; i++) tracker.Observe(observations[i], i + 1);
            Check(tracker.ConfirmedCycles == 3 && tracker.PickupUnavailable,
                "three distinct confirmed absences for the same NPC/quest must terminate the attempt");
            Check(tracker.LastOutcome.Evidence == observations[^1].Evidence &&
                tracker.LastOutcome.OfferedQuestIds.SequenceEqual(observations[^1].OfferedQuestIds),
                "the terminal outcome must retain the actual latest diagnostic evidence");
        }
        Test("offer ordering cannot restart missing-quest evidence", () => Three(
            Missing(new uint[] { 1, 2 }), Missing(new uint[] { 2, 1 }), Missing(new uint[] { 1, 2 })));
        Test("unrelated offer changes cannot hide the still-absent target", () => Three(
            Missing(new uint[] { 1 }), Missing(new uint[] { 2, 3 }), Missing(Array.Empty<uint>())));
        Test("duplicate offers cannot restart confirmed absence", () => Three(
            Missing(new uint[] { 1 }), Missing(new uint[] { 1, 1 }), Missing(new uint[] { 1 })));
        Test("unrelated shown quest cannot restart a confirmed loaded-list absence", () => Three(
            Missing(new uint[] { 1 }, shown: 1, title: "First"),
            Missing(new uint[] { 2 }, shown: 2, title: "Second"), Missing(Array.Empty<uint>())));
        Test("same interaction cannot be counted twice after offer changes", () =>
        {
            var tracker = new QuestPickupMismatchTracker();
            var first = tracker.Observe(Missing(new uint[] { 1 }), 10);
            var again = tracker.Observe(Missing(new uint[] { 2 }), 10);
            Check(tracker.ConfirmedCycles == 1 && ReferenceEquals(first, again), "same cycle must remain idempotent");
            tracker.Observe(Missing(new uint[] { 3 }), 11);
            Check(!tracker.PickupUnavailable, "two cycles cannot terminate pickup");
            tracker.Observe(Missing(new uint[] { 4 }), 12);
            Check(tracker.PickupUnavailable, "three cycles must terminate pickup");
        });
        Test("different NPC has independent evidence", () =>
        {
            var tracker = new QuestPickupMismatchTracker();
            tracker.Observe(Missing(new uint[] { 1 }), 1); tracker.Observe(Missing(new uint[] { 1 }), 2);
            tracker.Observe(Missing(new uint[] { 1 }, giver: 124), 3);
            Check(tracker.ConfirmedCycles == 1 && !tracker.PickupUnavailable, "NPC evidence was conflated");
        });
        Test("different requested quest has independent evidence", () =>
        {
            var tracker = new QuestPickupMismatchTracker();
            tracker.Observe(Missing(new uint[] { 1 }), 1); tracker.Observe(Missing(new uint[] { 1 }), 2);
            tracker.Observe(Missing(new uint[] { 1 }, target: 877), 3);
            Check(tracker.ConfirmedCycles == 1 && !tracker.PickupUnavailable, "quest evidence was conflated");
        });
        Test("unloaded dialogue does not count as another confirmed absence", () =>
        {
            var tracker = new QuestPickupMismatchTracker();
            var first = tracker.Observe(Missing(new uint[] { 1 }), 1);
            var unknown = QuestPickupDialogPolicy.Decide(876, "Target quest", 0, "", 123,
                Array.Empty<uint>(), false, false, false, false, false, false, false);
            Check(ReferenceEquals(first, tracker.Observe(unknown, 2)) && tracker.ConfirmedCycles == 1,
                "an unloaded frame must not manufacture absence");
        });
        Test("a positively shown target awaiting its button invalidates absence", () =>
        {
            var tracker = new QuestPickupMismatchTracker();
            tracker.Observe(Missing(new uint[] { 1 }), 1); tracker.Observe(Missing(new uint[] { 1 }), 2);
            tracker.Observe(Target(false), 3);
            Check(tracker.ConfirmedCycles == 0 && tracker.LastOutcome == null, "visible target did not invalidate old absence");
            tracker.Observe(Missing(new uint[] { 1 }), 4);
            Check(tracker.ConfirmedCycles == 1 && !tracker.PickupUnavailable, "stale absence ended a fresh attempt");
        });
        Test("an acceptable target clears mismatch evidence", () =>
        {
            var tracker = new QuestPickupMismatchTracker(); tracker.Observe(Missing(new uint[] { 1 }), 1);
            tracker.Observe(Target(true), 2);
            Check(tracker.ConfirmedCycles == 0 && tracker.LastOutcome == null, "acceptance must clear mismatch history");
        });
        Test("different failure reasons do not share a confirmation budget", () =>
        {
            var tracker = new QuestPickupMismatchTracker(); tracker.Observe(Missing(new uint[] { 1 }), 1);
            tracker.Observe(Missing(new uint[] { 1 }), 2);
            var wrong = QuestPickupDialogPolicy.Decide(876, "Target quest", 1, "Wrong", 123,
                new uint[] { 876, 1 }, true, false, false, false, false, false, false, true);
            tracker.Observe(wrong, 3);
            Check(tracker.ConfirmedCycles == 1 && !tracker.PickupUnavailable, "wrong-dialog evidence was conflated with absence");
        });
        Test("explicit reset clears a completed absence episode", () =>
        {
            var tracker = new QuestPickupMismatchTracker();
            for (int i = 1; i <= 3; i++) tracker.Observe(Missing(new uint[] { 1 }), i);
            tracker.Reset(); Check(!tracker.PickupUnavailable && tracker.LastOutcome == null && tracker.ConfirmedCycles == 0,
                "explicit reset retained stale evidence");
        });
        foreach (var observation in new (string Name, bool UniqueTitle, bool ListLoaded, uint[] Offered, bool Clear)[]
        {
            ("unique exact target title invalidates old absence", true, false, Array.Empty<uint>(), true),
            ("target returning in a loaded offer list invalidates old absence", false, true, new uint[] { 876 }, true),
            ("ambiguous title without ID is not positive evidence", false, false, Array.Empty<uint>(), false),
            ("an unloaded empty frame is not positive evidence", false, false, Array.Empty<uint>(), false)
        })
        {
            var sample = observation;
            Test(sample.Name, () =>
            {
                var tracker = new QuestPickupMismatchTracker();
                tracker.Observe(Missing(Array.Empty<uint>()), 1);
                tracker.Observe(Missing(Array.Empty<uint>()), 2);
                string shown = sample.UniqueTitle || sample.Name.StartsWith("ambiguous") ? "Target quest" : "";
                var next = QuestPickupDialogPolicy.Decide(876, "Target quest", 0, shown, 123, sample.Offered,
                    false, false, false, false, false, false, sample.UniqueTitle, sample.ListLoaded);
                tracker.Observe(next, 3);
                Check(sample.Clear ? tracker.ConfirmedCycles == 0 && tracker.LastOutcome == null : tracker.LastOutcome != null,
                    "positive target evidence and unknown/ambiguous observations must remain distinct");
            });
        }
        var failures = new List<string>();
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS missing-offer: " + test.Name); }
            catch (Exception e) { failures.Add(test.Name + ": " + e.Message); Console.Error.WriteLine("FAIL missing-offer: " + failures[^1]); }
        }
        Console.WriteLine($"Missing-offer evidence cases: {tests.Count - failures.Count}/{tests.Count}; actual policy and tracker; no client attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
}
