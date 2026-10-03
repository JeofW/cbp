using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Bots.Quest.QuestOrder;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using TreeSharp;
using WholesomeAQ;

// Executes actual quest descriptor and complete-inventory observers. Only the
// existing fixed-global memory transport and clock fixtures are controlled.
internal static class QuestItemProgressObservationRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private sealed class Clock : IQuestRecoveryClock
    { public DateTime UtcNow { get; set; } = new(2026,10,1,0,0,0,DateTimeKind.Utc); }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new List<(string,System.Action)>();
        void Add(string name,Action<QuestInventorySnapshotRegressionTests.Fixture> test)=>cases.Add((name,()=>
        {
            using var f=new QuestInventorySnapshotRegressionTests.Fixture();
            f.NativeFixture.SetQuest(10161,"Ground item observation",58,new[]{900,0,0,0},new[]{1,0,0,0},new[]{28116,0,0,0,0,0},new[]{30,0,0,0,0,0});
            test(f);
        }));
        Add("known empty carried stock remains an explicit zero", f=>
        {
            var snapshot=(QuestInventorySnapshot)f.Capture();
            Check(snapshot.IsComplete&&snapshot.IsCurrent(),"fixture complete inventory unavailable: "+snapshot.Status);
            var q=f.NativeFixture.Quest;
            bool raw=q.GetData(out var descriptor);
            var counts=Read(f,snapshot);
            Check(counts.Count>4&&counts[4]==0,"item observation is absent from progress vector; raw="+raw
                +";qid="+descriptor.Id+";flags="+descriptor.Flags+";n="+descriptor.ObjectivesDone?.Length
                +";items="+string.Join(',',q.CollectItemIds)+"/"+string.Join(',',q.CollectItemCounts)
                +";intermediate="+string.Join(',',q.CollectIntermediateItemIds)+"/"+string.Join(',',q.CollectIntermediateItemCounts)
                +";inventoryCurrent="+snapshot.IsCurrent()+";player="+snapshot.PlayerGuid+"/"+f.Player.Guid);
        });
        Add("one item advances while all normal counters stay unchanged",f=>
        { var clock=new Clock();var monitor=new WholesomeProgressMonitor(clock);monitor.Sample(Sample(Read(f)));f.Item(23,28116,1);clock.UtcNow=clock.UtcNow.AddSeconds(2);Check(monitor.Sample(Sample(Read(f))).MadeProgress,"actual acquired item did not reset progress timeout"); });
        Add("unrelated item does not advance objective progress",f=>
        { var before=Read(f);f.Item(23,42,1);Check(before.SequenceEqual(Read(f)),"unrelated stock fabricated objective progress"); });
        Add("normal and item observations keep separate vector positions",f=>
        { f.NativeFixture.SetProgress(new[]{1,0,0,0});f.Item(23,28116,7);var counts=Read(f);Check(counts.Count>4&&counts[0]==1&&counts[4]==7,"normal slot and item quantity were conflated"); });
        Add("multiple stacks are summed and capped at the required amount",f=>
        { f.Item(23,28116,20);f.Item(24,28116,20);var counts=Read(f);Check(counts.Count>4&&counts[4]==30,"required quantity not bounded to objective target"); });
        Add("partial progress is not full completion",f=>
        { f.Item(23,28116,29);Check(Read(f).Skip(4).FirstOrDefault()==29,"partial item progress missing"); });
        Add("unresolved carried slot makes the combined observation unknown",f=>
        { f.MainSlot(23,99119999);Check(Read(f).Count==0,"unresolved item treated as zero progress"); });
        Add("active trade prevents stock authority",f=>
        { f.TradeOwners(777,777);Check(Read(f).Count==0,"trade uncertainty ignored"); });
        Add("failed quest cannot receive inventory progress",f=>
        { f.Item(23,28116,30);f.NativeFixture.SetAccepted(true,failed:true);Check(Read(f).Count==0,"failed quest borrowed stock"); });
        Add("abandoned quest cannot receive inventory progress",f=>
        { f.Item(23,28116,30);f.NativeFixture.SetAccepted(false);Check(Read(f).Count==0,"abandoned quest borrowed stock"); });
        Add("stale inventory snapshot cannot donate a later quantity",f=>
        { var item=f.Item(23,28116,1);var snapshot=(QuestInventorySnapshot)f.Capture();f.Write32(item.Fields+56,2);Check(Read(f,snapshot).Count==0,"stale snapshot stayed authoritative"); });
        Add("unchanged complete inventory stays revalidatable",f=>
        { f.Item(23,28116,3);var snapshot=(QuestInventorySnapshot)f.Capture();Check(Read(f,snapshot).Skip(4).FirstOrDefault()==3,"valid captured quantity rejected"); });
        Add("production caller uses the complete-inventory-aware reader", f=>
        {
            var method=typeof(WholesomeAutoQuest).GetMethod("ReadObjectiveCounts",BindingFlags.NonPublic|BindingFlags.Static)!;
            // Explicitly incomplete layout, not an assumption that fixed WoW
            // addresses are unmapped in every ASLR-enabled x86 test process.
            // The production reader must not return only four normal counters.
            f.Write32(f.Player.BaseAddress+6384,149);
            var counts=(IReadOnlyList<int>)method.Invoke(null,new object[]{f.NativeFixture.Quest})!;
            Check(counts.Count==0,"production caller still ignores complete carried-inventory observation");
        });
        cases.Add(("unknown baseline does not become a fabricated item increment",()=>
        { var clock=new Clock();var m=new WholesomeProgressMonitor(clock);m.Sample(Sample(Array.Empty<int>()));clock.UtcNow=clock.UtcNow.AddSeconds(2);Check(!m.Sample(Sample(new[]{0,0,0,0,12})).MadeProgress,"unknown baseline was treated as zero"); }));
        cases.Add(("unknown samples cannot accrue a no-progress failure",()=>
        { var clock=new Clock();var m=new WholesomeProgressMonitor(clock);m.Sample(Sample(new[]{0}));for(int i=0;i<300;i++){clock.UtcNow=clock.UtcNow.AddSeconds(2);var update=m.Sample(Sample(Array.Empty<int>()));Check(update.Outcomes.Count==0,"missing observations accumulated a failure episode");} }));
        cases.Add(("reacquired material after a loss counts from the new known baseline",()=>
        { var clock=new Clock();var m=new WholesomeProgressMonitor(clock);m.Sample(Sample(new[]{5}));m.Sample(Sample(new[]{2}));Check(m.Sample(Sample(new[]{3})).MadeProgress,"old high-water mark hid reacquisition progress"); }));
        cases.Add(("vector identity change starts a new observation baseline",()=>
        { var m=new WholesomeProgressMonitor(new Clock());m.Sample(Sample(new[]{0}));Check(!m.Sample(Sample(new[]{0,7})).MadeProgress,"new objective layout was counted as item acquisition"); }));
        cases.Add(("diagnostic tree inspection never creates a behavior",()=>
        { using var behavior=new LazyBehavior();var prop=typeof(ForcedBehavior).GetProperty("ExistingBranch");Check(prop!=null,"non-creating behavior observation is missing");Check(prop!.GetValue(behavior)==null&&behavior.Created==0,"diagnostic observation created runtime work");var tree=behavior.Branch;Check(ReferenceEquals(prop.GetValue(behavior),tree)&&behavior.Created==1,"existing branch observation differs"); }));
        int passed=0,assertions=0,unexpected=0;
        foreach(var (name,test) in cases){try{test();passed++;}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL item progress: "+name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR item progress: "+name+": "+e);}}
        Console.WriteLine($"Quest item progress scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual inventory/quest readers and progress monitor; no live realm.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Item progress observation regressions");
    }
    private static QuestWorkSample Sample(IReadOnlyList<int> counts)=>new(){Key=QuestRecoveryKey.ForQuestStage(10161,QuestRecoveryStage.Objective),AttemptGeneration=1,IsActiveWork=true,ObjectiveCounts=counts};
    private static IReadOnlyList<int> Read(QuestInventorySnapshotRegressionTests.Fixture f,QuestInventorySnapshot? snapshot=null)
    {
        var type=typeof(PlayerQuest).Assembly.GetType("Styx.Logic.Questing.QuestProgressObservation");
        var method=type?.GetMethod("Read",new[]{typeof(Quest),typeof(QuestInventorySnapshot)});
        try
        {
            if(method!=null)return (IReadOnlyList<int>)method.Invoke(null,new object?[]{f.NativeFixture.Quest,snapshot??(QuestInventorySnapshot)f.Capture()})!;
            return (IReadOnlyList<int>)typeof(WholesomeAutoQuest).GetMethod("ReadObjectiveCounts",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{f.NativeFixture.Quest})!;
        }
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private sealed class LazyBehavior:ForcedBehavior
    { public int Created;protected override Composite CreateBehavior(){Created++;return new TreeSharp.Action(_=>RunStatus.Failure);}public override bool IsDone=>false; }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
