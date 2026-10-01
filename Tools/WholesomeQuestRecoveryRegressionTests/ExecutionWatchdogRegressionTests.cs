using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing;

internal static class ExecutionWatchdogRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        Type? type=typeof(PlayerQuest).Assembly.GetType("Styx.Logic.Questing.QuestExecutionWatchdog");
        Check(type!=null,"state-aware execution watchdog is missing");
        object Create()=>Activator.CreateInstance(type!)!;
        DateTime start=new(2026,10,1,0,0,0,DateTimeKind.Utc);
        object owner=new();
        object Sample(object monitor,int second,int[]? counts=null,bool active=true,object? identity=null,long generation=1)
            =>type!.GetMethod("Sample")!.Invoke(monitor,new object?[]{identity??owner,generation,counts??new[]{0},active,start.AddSeconds(second)})!;
        bool B(object row,string name)=>(bool)row.GetType().GetProperty(name)!.GetValue(row)!;
        double D(object row,string name)=>(double)row.GetType().GetProperty(name)!.GetValue(row)!;
        var cases=new List<(string,System.Action)>
        {
            ("no progress is timed independently of movement",()=>{var m=Create();Sample(m,0);object row=Sample(m,0);for(int i=2;i<=30;i+=2)row=Sample(m,i);Check(B(row,"ShouldLog")&&D(row,"NoProgressSeconds")>=30,"bounded no-progress interval did not surface");}),
            ("reports are coalesced rather than repeated every pulse",()=>{var m=Create();Sample(m,0);for(int i=2;i<=30;i+=2)Sample(m,i);Check(!B(Sample(m,32),"ShouldLog"),"diagnostic repeated immediately");}),
            ("observed quantity increments reset the interval",()=>{var m=Create();Sample(m,0);for(int i=2;i<=28;i+=2)Sample(m,i);var row=Sample(m,30,new[]{1});Check(B(row,"MadeProgress")&&!B(row,"ShouldLog")&&D(row,"NoProgressSeconds")==0,"actual item progress was classified as a stall");}),
            ("same count after source GUID changes is not progress",()=>{var m=Create();Sample(m,0);object row=Sample(m,0);for(int i=2;i<=30;i+=2)row=Sample(m,i);Check(!B(row,"MadeProgress")&&B(row,"ShouldLog"),"source selection or motion fabricated acquisition");}),
            ("inactive combat/rest/death time cannot request recovery",()=>{var m=Create();Sample(m,0);for(int i=2;i<=120;i+=2){var row=Sample(m,i,active:false);Check(!B(row,"MayRecover")&&!B(row,"ShouldLog"),"suspended execution acquired watchdog recovery authority");}}),
            ("a long missing pulse gap cannot accrue active time",()=>{var m=Create();Sample(m,0);var row=Sample(m,600);Check(!B(row,"MayRecover")&&!B(row,"ShouldLog"),"unobserved time was treated as failed active work");}),
            ("unknown counts produce diagnostics but no recovery authority",()=>{var m=Create();Sample(m,0,Array.Empty<int>());bool logged=false;for(int i=2;i<=100;i+=2){var row=Sample(m,i,Array.Empty<int>());logged|=B(row,"ShouldLog");Check(!B(row,"MayRecover")&&!B(row,"MadeProgress"),"unknown stock was used as zero or failure authority");}Check(logged,"unavailable observations were silent");}),
            ("newly available positive counts establish a baseline only",()=>{var m=Create();Sample(m,0,Array.Empty<int>());Check(!B(Sample(m,2,new[]{9}),"MadeProgress"),"unknown stock fabricated a nine-item gain");}),
            ("new objective owner revokes the prior episode",()=>{var m=Create();Sample(m,0);for(int i=2;i<=28;i+=2)Sample(m,i);var row=Sample(m,30,identity:new());Check(!B(row,"ShouldLog")&&!B(row,"MayRecover"),"new owner inherited a stale timeout");}),
            ("new attempt generation revokes the prior episode",()=>{var m=Create();Sample(m,0);for(int i=2;i<=58;i+=2)Sample(m,i);Check(!B(Sample(m,60,generation:2),"MayRecover"),"new attempt inherited recovery");}),
            ("recovery budget is finite per unprogressed episode",()=>{var m=Create();Sample(m,0);int requests=0;for(int i=2;i<=360;i+=2)if(B(Sample(m,i),"MayRecover"))requests++;Check(requests==2,"unprogressed work requested unlimited or no bounded recovery");}),
            ("metadata vector changes are not item gains",()=>{var m=Create();Sample(m,0);Check(!B(Sample(m,2,new[]{0,12}),"MadeProgress"),"layout change fabricated progress");}),
            ("unknown then known observations cannot replenish recovery budget",()=>{var m=Create();Sample(m,0);int requests=0;for(int i=2;i<=400;i+=2){int[] values=i%100==0?Array.Empty<int>():new[]{0};if(B(Sample(m,i,values),"MayRecover"))requests++;}Check(requests==2,"observation loss renewed the unprogressed owner's recovery budget: "+requests);})
        };
        int passed=0,failed=0;
        foreach(var(name,test)in cases){try{test();passed++;}catch(Exception e){failed++;Console.Error.WriteLine("FAIL execution watchdog: "+name+": "+e.Message);}}
        Console.WriteLine($"Execution watchdog scenarios: {passed}/{cases.Count}; failed={failed}; actual episode clock and recovery budget; no client side effects.");
        if(failed!=0)throw new InvalidOperationException("Execution watchdog regressions");
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
