using System.Runtime.CompilerServices;
using Styx.Logic.Questing;

internal static class CompletedHistoryFreshnessRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        void Case(string name, Action test)
        {
            QuestLog.GetCompletedQuestCacheStatusForIdentity(null!, null!);
            test(); passed++; Console.WriteLine("PASS completed history freshness: " + name);
        }
        bool Read(Func<List<uint>> query) => QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity(
            "history-fixture", "realm", query, out _);
        long Invalidate() => QuestLog.RequestCompletedQuestHistoryRefreshForIdentity("history-fixture\u001frealm");
        Case("young valid cache does not prove a later reward", () =>
        {
            Check(Read(() => new()), "baseline failed");
            var old = QuestLog.SnapshotCompletedQuestHistory();
            int queries = 0;
            Check(Read(() => { queries++; return new() { 10161 }; }) && queries == 0,
                "young baseline did not exercise the original cached path");
            long requested = Invalidate();
            Check(!old.IsAuthoritativeAfter(requested), "old snapshot proved a later reward");
            Check(Read(() => { queries++; return new() { 10161 }; }) && queries == 1,
                "post-action invalidation did not force a fresh query");
            var fresh = QuestLog.SnapshotCompletedQuestHistory();
            Check(fresh.IsAuthoritativeAfter(requested) && fresh.QuestIds.Contains(10161)
                && fresh.Revision > old.Revision, "fresh positive history lost its epoch");
            Check(old.QuestIds.Count == 0, "refresh mutated an earlier observation");
        });
        Case("fresh negative remains negative", () =>
        {
            Read(() => new()); long requested = Invalidate(); Read(() => new());
            var observed = QuestLog.SnapshotCompletedQuestHistory();
            Check(observed.IsAuthoritativeAfter(requested) && !observed.QuestIds.Contains(10161),
                "request or successful refresh fabricated quest completion");
        });
        Case("failed refresh cannot reuse old positive authority", () =>
        {
            Read(() => new() { 10161 }); long requested = Invalidate();
            Check(!Read(() => null!), "failed query became authoritative");
            Check(!QuestLog.SnapshotCompletedQuestHistory().IsAuthoritativeAfter(requested),
                "retained cached ID acknowledged the action after refresh failure");
        });
        Case("old-generation callback cannot publish after same-character invalidation", () =>
        {
            long replacement = 0;
            Check(!Read(() => { replacement = Invalidate(); return new() { 10161 }; }),
                "old callback reacquired history authority");
            var old = QuestLog.SnapshotCompletedQuestHistory();
            Check(!old.IsAuthoritativeAfter(replacement) && !old.QuestIds.Contains(10161),
                "old callback injected completed history");
            Check(Read(() => new()), "replacement query failed");
            Check(QuestLog.SnapshotCompletedQuestHistory().IsAuthoritativeAfter(replacement),
                "replacement query did not acquire its own generation");
        });
        Case("another character cannot inherit the observation identity", () =>
        {
            Read(() => new() { 10161 }); var old = QuestLog.SnapshotCompletedQuestHistory();
            QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity("other", "realm", () => new(), out _);
            var fresh = QuestLog.SnapshotCompletedQuestHistory();
            Check(old.Identity != fresh.Identity && fresh.QuestIds.Count == 0 && fresh.Generation > old.Generation,
                "new identity borrowed the previous reward evidence");
        });
        Case("request alone never fabricates completed history", () =>
        {
            Read(() => new()); long requested = Invalidate();
            var pending = QuestLog.SnapshotCompletedQuestHistory();
            Check(pending.Status == CompletedQuestCacheStatus.Unknown && !pending.IsAuthoritativeAfter(requested)
                && pending.QuestIds.Count == 0, "observation request was reported as acknowledgement");
        });
        foreach (Exception control in new Exception[] { new OperationCanceledException("history cancelled"), new System.Threading.ThreadInterruptedException("history interrupted") })
        {
            Case("history refresh preserves " + control.GetType().Name, () =>
            {
                Styx.WoWInternals.Lua.Failure = control;
                try
                {
                    var method = typeof(QuestLog).GetMethod("TryRefreshCompletedQuestCache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                    try { method.Invoke(null, null); throw new InvalidOperationException("history refresh swallowed control flow"); }
                    catch (System.Reflection.TargetInvocationException error) { Check(ReferenceEquals(error.InnerException, control), "control signal changed identity"); }
                }
                finally { Styx.WoWInternals.Lua.Failure = null; }
            });
            Case("history memory traversal preserves " + control.GetType().Name, () =>
            {
                try { QuestLog.TryTraverseCompletedQuestNodes(4, _ => throw control, out _); throw new InvalidOperationException("history traversal swallowed control flow"); }
                catch (Exception error) { Check(ReferenceEquals(error, control), "control signal did not propagate unchanged"); }
            });
        }
        Console.WriteLine($"Completed history freshness scenarios: {passed}/{passed}; actual QuestLog cache owner, controlled query leaves.");
    }
}
