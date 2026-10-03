using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;

internal static class GroundMountRequestTests
{
    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Case(string name,Action test){total++;try{test();passed++;Console.WriteLine("PASS ground mount lifetime: "+name);}catch(Exception e){Console.Error.WriteLine("FAIL ground mount lifetime: "+name+": "+e.Message);}}
        Case("submission yields until an observed mount",()=>{
            var r=new GroundMountRequest();int calls=0;
            bool Tick(double t,bool mounted=false)=>r.Waiting(t,mounted,false,false,()=>true,()=>35022,_=>{calls++;return true;},()=>{});
            Check(Tick(0)&&Tick(1)&&Tick(4)&&calls==1,"pending mount did not retain one nonblocking submission");
            Check(!Tick(5,true)&&calls==1,"observed mount did not release movement without another cast");
        });
        Case("stop is a preparation step not a mount acknowledgement",()=>{
            var r=new GroundMountRequest();int casts=0,stops=0;
            Check(r.Waiting(0,false,false,true,()=>true,()=>35022,_=>{casts++;return true;},()=>stops++)&&stops==1&&casts==0,"moving actor skipped stop acknowledgement");
            Check(r.Waiting(.2,false,false,false,()=>true,()=>35022,_=>{casts++;return true;},()=>stops++)&&casts==1,"stationary continuation did not submit");
        });
        Case("rejected mount falls through instead of fabricating success",()=>{
            var r=new GroundMountRequest();int n=0;
            Check(!r.Waiting(0,false,false,false,()=>true,()=>35022,_=>{n++;return false;},()=>{})&&n==1,"known rejection did not yield ground fallback");
        });
        Case("leaving a rejected mount spot retries after one second",()=>{
            var r=new GroundMountRequest();int requests=0;bool clear=false;
            bool Tick(double t)=>r.Waiting(t,false,false,false,()=>true,()=>35022,_=>{requests++;return clear;},()=>{});
            Check(!Tick(0)&&requests==1,"rejected preparation did not yield walking");
            clear=true;
            Check(!Tick(.2)&&requests==1,"known rejection retried on every pulse");
            Check(Tick(1.01)&&requests==2,"an unsubmitted mount inherited the thirty-second request throttle");
            Check(Tick(2)&&requests==2,"successful retry was not retained until observation");
        });
        Case("repeated preparation rejection stays bounded without parking travel",()=>{
            var r=new GroundMountRequest();int requests=0;
            for(int pulse=0;pulse<50;pulse++)
                Check(!r.Waiting(pulse/10.0,false,false,false,()=>true,()=>35022,_=>{requests++;return false;},()=>{}),"rejection fabricated a pending mount");
            Check(requests==5,"preparation retries did not use their one-second budget: "+requests);
        });
        Case("observed mount clears the old submission delay before a later remount",()=>{
            var r=new GroundMountRequest();int requests=0;
            bool Tick(double t,bool mounted)=>r.Waiting(t,mounted,false,false,()=>true,()=>35022,_=>{requests++;return true;},()=>{});
            Check(Tick(0,false)&&!Tick(3,true)&&requests==1,"initial mounted observation was not accepted");
            Check(Tick(4,false)&&requests==2,"a completed mount kept the next interaction's remount throttled");
        });
        Case("missing candidate leaves navigation available",()=>{
            var r=new GroundMountRequest();Check(!r.Waiting(0,false,false,false,()=>true,()=>0,_=>throw new Exception("unexpected cast"),()=>throw new Exception("unexpected stop")),"unknown mount blocked walking");
        });
        Case("timeout permits walking but throttles further requests",()=>{
            var r=new GroundMountRequest();int n=0;
            bool Tick(double t)=>r.Waiting(t,false,false,false,()=>true,()=>35022,_=>{n++;return true;},()=>{});
            Check(Tick(0)&&!Tick(9)&&!Tick(15)&&n==1,"unacknowledged request stalled forever or spammed retries");
            Check(Tick(31)&&n==2,"bounded retry never became eligible");
        });
        foreach(string point in new[]{"selection","stop","submission"})Case("owner revoked during "+point,()=>{
            var r=new GroundMountRequest();bool current=true;int n=0;
            bool result=r.Waiting(0,false,false,point=="stop",()=>current,()=>{if(point=="selection")current=false;return 35022;},_=>{n++;current=false;return true;},()=>current=false);
            Check(!result&&n==(point=="submission"?1:0),"obsolete mount lifetime retained authority");
        });
        Case("cancellation identity propagates",()=>{
            var r=new GroundMountRequest();var signal=new OperationCanceledException("stop");Exception? caught=null;
            try{r.Waiting(0,false,false,false,()=>true,()=>throw signal,_=>true,()=>{});}catch(Exception e){caught=e;}
            Check(ReferenceEquals(caught,signal),"cancellation swallowed");
        });
        Console.WriteLine($"Ground mount lifetime: {passed}/{total}; actual lifecycle, controlled observations/submission; no native mount proof.");
        if(passed!=total)Environment.ExitCode=1;
    }
}
