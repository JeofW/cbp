using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class InventoryAndPickupEdgeRegressionTests
{
    private static readonly MethodInfo Completion = typeof(QuestScheduler).GetMethod("IsObjectiveComplete", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static bool Complete(QuestObjective o, int[]? counts, Dictionary<int,long>? bag) =>
        (bool)Completion.Invoke(null, new object?[] {o, counts, bag})!;
    private static void Check(bool good, string why) { if (!good) throw new InvalidOperationException(why); }
    private static QuestObjective Item(ObjectiveType kind = ObjectiveType.CollectItem, int index = 1) =>
        new() {Index=index, Type=kind, ItemId=5058, MobId=100, GameObjectId=3685, CollectCount=12};
    private static QuestAttemptOutcome Outcome(QuestRecoveryKey key, long cycle, bool failed = false) =>
        new() {Key=key, InteractionCycleId=cycle, Kind=failed?QuestAttemptOutcomeKind.Failure:QuestAttemptOutcomeKind.Observation,
            Reason=QuestFailureReason.PickupTargetNotOffered, IsFailureEpisode=failed};

    internal static void Run()
    {
        var tests = new List<(string Name, Action Run)>();
        foreach (var kind in new[] {ObjectiveType.CollectItem, ObjectiveType.CollectFromGameObject})
        {
            var type = kind;
            tests.Add(($"{kind}: carried quantity wins over unrelated counter index", () =>
                Check(Complete(Item(type), new[] {0,1}, new() {[5058]=12}), "already-carried required items must suppress collection work")));
            tests.Add(($"{kind}: insufficient inventory cannot borrow another objective's counter", () =>
                Check(!Complete(Item(type), new[] {0,99}, new() {[5058]=11}), "an unrelated live counter is not this item")));
            tests.Add(($"{kind}: current empty bags override stale completed counters", () =>
                Check(!Complete(Item(type), new[] {0,99}, new()), "a current empty inventory snapshot is authoritative, not unknown")));
            tests.Add(($"{kind}: alternative source index outside live counters is valid", () =>
                Check(Complete(Item(type, 99), new[] {0}, new() {[5058]=12}), "alternative-source index is not a live quest counter index")));
        }
        tests.Add(("item identity cannot be replaced by a different abundant item", () =>
            Check(!Complete(Item(), new[] {0,99}, new() {[9999]=100}), "count by item ID, not total bag quantity")));
        tests.Add(("removing required items revokes earlier completion evidence", () =>
        {
            var bag = new Dictionary<int,long> {[5058]=12}; Check(Complete(Item(), new[] {0,0}, bag), "setup");
            bag[5058]=11; Check(!Complete(Item(), new[] {0,12}, bag), "a new inventory observation must not reuse completion" );
        }));
        tests.Add(("large quantities are not truncated to a signed int", () =>
            Check(Complete(Item(), null, new() {[5058]=(long)int.MaxValue+50}), "long stack totals must retain their value")));
        tests.Add(("kill progress remains owned by its live objective counter", () =>
        {
            var o = new QuestObjective {Index=0, Type=ObjectiveType.KillMob, MobId=100, KillCount=2};
            Check(!Complete(o, new[] {1}, new() {[5058]=999}) && Complete(o, new[] {2}, new()), "bag contents must not complete kill work");
        }));
        tests.Add(("Wholesome rejects a stale failure episode from an old interaction", () =>
        {
            var key=QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001); var m=new WholesomePickupMonitor();
            m.Observe(key,1,10,Outcome(key,10),true); m.Observe(key,1,11,Outcome(key,11),true);
            Check(m.Observe(key,1,10,Outcome(key,10,true),true)==null, "old cycle must not become a third active interaction");
            Check(m.Observe(key,1,12,Outcome(key,12,true),true)?.IsFailureEpisode==true, "current third rejection must still be reported");
        }));
        tests.Add(("Wholesome replays must not inflate active-cycle accounting", () =>
        {
            var key=QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001); var m=new WholesomePickupMonitor();
            m.Observe(key,1,10,Outcome(key,10),true); m.Observe(key,1,9,Outcome(key,9),true);
            Check(m.Observe(key,1,11,Outcome(key,11,true),true)==null, "only two current interactions occurred");
        }));
        tests.Add(("inactive pickup observations do not count as failed active work", () =>
        {
            var key=QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001); var m=new WholesomePickupMonitor();
            for(int i=1;i<10;i++) Check(m.Observe(key,1,i,Outcome(key,i,true),false)==null,"paused work must not accumulate failures");
        }));
        tests.Add(("a new owned generation may restart interaction numbering", () =>
        {
            var key=QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001); var m=new WholesomePickupMonitor();
            m.Observe(key,1,10,Outcome(key,10),true);
            Check(m.Observe(key,2,1,Outcome(key,1),true)!=null, "new generation must not inherit the previous high-water mark");
        }));
        var failed=new List<string>();
        foreach(var test in tests)
        {
            try {test.Run();Console.WriteLine("PASS Wholesome inventory/pickup: "+test.Name);}
            catch(Exception e){failed.Add(test.Name+": "+e.Message);Console.Error.WriteLine("FAIL Wholesome inventory/pickup: "+failed[^1]);}
        }
        Console.WriteLine($"Wholesome inventory/pickup edge scenarios: {tests.Count-failed.Count}/{tests.Count}; actual scheduler predicate and pickup owner, no client attached.");
        if(failed.Count!=0)throw new InvalidOperationException(string.Join(Environment.NewLine,failed));
    }
}
