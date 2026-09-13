using System.Runtime.CompilerServices;
using Bots.Quest.QuestOrder;
using Styx.Logic.Questing.Recovery;

internal static class PickupObservationRegressionTests
{
    private static QuestPickupDialogDecision Missing(params uint[] offered) =>
        QuestPickupDialogPolicy.Decide(867, "Requested quest", 0, "", 1001, offered,
            false, false, false, false, false, false, false, offeredQuestListLoaded: true);
    private static QuestPickupDialogDecision Accept() =>
        QuestPickupDialogPolicy.Decide(867, "Requested quest", 867, "Requested quest", 1001,
            new uint[] {867}, true, false, false, false, false, false, false, true);
    private static void Check(bool good, string why) { if (!good) throw new InvalidOperationException(why); }

    internal static void Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("offer ordering is not new missing-quest evidence", () =>
            {
                var tracker = new QuestPickupMismatchTracker();
                tracker.Observe(Missing(101, 102), 1);
                tracker.Observe(Missing(102, 101), 2);
                tracker.Observe(Missing(101, 102), 3);
                Check(tracker.PickupUnavailable, "the same absent quest must not retry forever because the offer list reordered");
            }),
            ("duplicate offer records do not reset an otherwise identical mismatch", () =>
            {
                var tracker = new QuestPickupMismatchTracker();
                tracker.Observe(Missing(101, 102), 1);
                tracker.Observe(Missing(101, 101, 102), 2);
                tracker.Observe(Missing(102, 101), 3);
                Check(tracker.PickupUnavailable, "duplicate metadata must not erase confirmed absence");
            }),
            ("ordered offer snapshot is preserved for interaction consumers", () =>
            {
                uint[] source = {102, 101, 102};
                var decision = Missing(source); source[0] = 999;
                Check(decision.OfferedQuestIds.SequenceEqual(new uint[] {102, 101, 102}),
                    "canonical evidence must not reorder or alias the actual UI snapshot");
            }),
            ("stale interaction cannot count as a third distinct attempt", () =>
            {
                var tracker = new QuestPickupMismatchTracker(); var d = Missing(101);
                tracker.Observe(d, 10); var second = tracker.Observe(d, 11);
                var replay = tracker.Observe(d, 10);
                Check(tracker.ConfirmedCycles == 2 && !tracker.PickupUnavailable && ReferenceEquals(second, replay),
                    "a replayed old interaction is not another attempt");
                tracker.Observe(d, 12); Check(tracker.PickupUnavailable, "the genuine third interaction must still terminate");
            }),
            ("stale changed evidence cannot erase a current sequence", () =>
            {
                var tracker = new QuestPickupMismatchTracker();
                tracker.Observe(Missing(101), 10); tracker.Observe(Missing(101), 11);
                tracker.Observe(Missing(102), 9);
                Check(tracker.ConfirmedCycles == 2, "an older dialog must not replace current evidence");
            }),
            ("stale acceptance cannot reset newer missing-offer evidence", () =>
            {
                var tracker = new QuestPickupMismatchTracker();
                tracker.Observe(Missing(101), 10); tracker.Observe(Missing(101), 11);
                tracker.Observe(Accept(), 9);
                Check(tracker.ConfirmedCycles == 2, "only a current successful observation may reset the sequence");
            }),
            ("current target appearance resets absence", () =>
            {
                var tracker = new QuestPickupMismatchTracker(); tracker.Observe(Missing(101), 10);
                Check(tracker.Observe(Accept(), 11) == null && tracker.ConfirmedCycles == 0 && !tracker.PickupUnavailable,
                    "the requested quest appearing must remain eligible");
            }),
            ("unrelated current offers do not erase the requested quest absence", () =>
            {
                var tracker = new QuestPickupMismatchTracker(); tracker.Observe(Missing(101), 10);
                tracker.Observe(Missing(101), 11); tracker.Observe(Missing(102), 12);
                Check(tracker.ConfirmedCycles == 3 && tracker.PickupUnavailable, "the requested quest is still absent despite unrelated offer changes");
            }),
            ("an unloaded dialog is not a confirmed missing quest", () =>
            {
                var d = QuestPickupDialogPolicy.Decide(867, "Requested quest", 0, "", 1001, Array.Empty<uint>(),
                    false, false, false, false, false, false, false, false);
                var tracker = new QuestPickupMismatchTracker();
                for (int i = 1; i <= 100; i++) tracker.Observe(d, i);
                Check(d.Action == QuestPickupDialogAction.Wait && tracker.ConfirmedCycles == 0,
                    "loading latency is not NPC rejection");
            }),
            ("explicit lifecycle reset permits a new cycle numbering", () =>
            {
                var tracker = new QuestPickupMismatchTracker(); tracker.Observe(Missing(101), 20); tracker.Reset();
                tracker.Observe(Missing(101), 1);
                Check(tracker.ConfirmedCycles == 1, "a new tracked interaction lifecycle must not inherit old cycle IDs");
            })
        };
        var failures = new List<string>();
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS pickup observation: " + test.Name); }
            catch (Exception e) { failures.Add(test.Name + ": " + e.Message); Console.Error.WriteLine("FAIL pickup observation: " + failures[^1]); }
        }
        Console.WriteLine($"Pickup observation scenarios: {tests.Length-failures.Count}/{tests.Length}; actual policy/tracker, no client attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
}
