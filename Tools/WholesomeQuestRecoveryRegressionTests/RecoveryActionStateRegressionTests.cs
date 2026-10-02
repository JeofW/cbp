using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Executes the complete production reservation owner with explicit time/context
// and authoritative observations. Runtime event/dispatch integration is separate.
internal static class RecoveryActionStateRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string path = Path.Combine(root, "Styx/Logic/Combat/RecoveryActionLedger.cs");
        if (!File.Exists(path))
            throw new InvalidOperationException("Missing shared recovery action owner; structural contract is absent. Actual potion conflicts are reproduced separately.");
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(value => MetadataReference.CreateFromFile(value));
        var compilation = CSharpCompilation.Create("RecoveryLedger_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(path)), CSharpSyntaxTree.ParseText(Cases) },
            refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        try { assembly.GetType("Styx.Logic.Combat.RecoveryLedgerCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CopilotBuddy.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Tracked source required");
    }
    private const string Cases = """
using System; using System.Linq; namespace Styx.Logic.Combat {
public static class RecoveryLedgerCases {
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string message){if(!value)throw new Failure(message);}
 private static RecoveryActionLedger ledger;private static object context;private static long now;
 private static RecoveryActionTicket Prepare(RecoveryActionKind kind=RecoveryActionKind.Heal,ulong target=1,int spell=101,ulong item=0,RecoveryResource resources=RecoveryResource.Health){
  Check(ledger.TryPrepare(context,kind,spell,target,item,resources,now,10000,"test",out var ticket),"valid action was not prepared");return ticket;
 }
 private static bool Admit(RecoveryActionKind kind,ulong target=1,int spell=101,ulong item=0,RecoveryResource resources=RecoveryResource.Health)
  =>ledger.TryPrepare(context,kind,spell,target,item,resources,now,10000,"other",out _);
 private static RecoveryActionTicket Submit(RecoveryActionKind kind=RecoveryActionKind.Heal,ulong target=1,int spell=101,ulong item=0,RecoveryResource resources=RecoveryResource.Health){var ticket=Prepare(kind,target,spell,item,resources);Check(ledger.BeginSubmission(ticket,context,now),"prepared action could not submit");return ticket;}
 public static void Run(){int passed=0,failed=0,unexpected=0,total=0;
  void Case(string name,Action action){total++;ledger=new();context=new();now=1000;try{action();passed++;}catch(Failure error){failed++;Console.Error.WriteLine("FAIL recovery ledger: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR recovery ledger: "+name+": "+error);}}
  Case("preparation reserves without claiming dispatch",()=>{var t=Prepare();Check(t.State==RecoveryActionState.Prepared&&!t.WasSubmitted,"preparation was submitted");Check(!Admit(RecoveryActionKind.Consumable,item:2,spell:0),"reentrant recovery bypassed preparation");});
  Case("rejected preparation releases recovery",()=>{var t=Prepare();ledger.RejectUnsubmitted(t,context,now);Check(t.State==RecoveryActionState.Rejected&&Admit(RecoveryActionKind.Consumable,item:2,spell:0),"unsubmitted preparation poisoned future work");});
  Case("native entry is submission only",()=>{var t=Submit();Check(t.WasSubmitted&&t.State==RecoveryActionState.Submitted,"native entry became effect acknowledgement");});
  Case("cleanup cannot reject an uncertain submission",()=>{var t=Submit();ledger.RejectUnsubmitted(t,context,now);Check(t.State==RecoveryActionState.Submitted&&!Admit(RecoveryActionKind.Consumable,item:2,spell:0),"unknown submitted result released health recovery");});
  foreach(bool? ack in new bool?[]{null,false})foreach(bool? interrupted in new bool?[]{null,false})
   Case("incomplete outcome "+ack+"/"+interrupted,()=>{var t=Submit();ledger.Observe(t,context,now,ack,interrupted);Check(t.State==RecoveryActionState.Submitted&&!Admit(RecoveryActionKind.Consumable,item:2,spell:0),"UNKNOWN/false authorized a second recovery");});
  Case("contradictory outcomes remain unavailable",()=>{var t=Submit();ledger.Observe(t,context,now,true,true);Check(t.State==RecoveryActionState.Submitted,"contradictory evidence completed an action");});
  Case("cast start is not heal landing",()=>{var t=Submit();ledger.Casting(t,context,now);Check(t.State==RecoveryActionState.Casting&&!Admit(RecoveryActionKind.Consumable,item:2,spell:0),"cast start released health reservation");});
  Case("cast success still awaits the effect",()=>{var t=Submit();ledger.CastSucceeded(t,context,now);Check(t.State==RecoveryActionState.AwaitingEffect&&!Admit(RecoveryActionKind.Consumable,item:2,spell:0),"cast success was confused with healing");});
  Case("authoritative heal releases health",()=>{var t=Submit();ledger.Observe(t,context,now,true,false);Check(t.State==RecoveryActionState.Acknowledged&&Admit(RecoveryActionKind.Consumable,item:2,spell:0),"acknowledged heal did not release");});
  Case("known interruption releases recovery",()=>{var t=Submit();ledger.Observe(t,context,now,false,true);Check(t.State==RecoveryActionState.Interrupted&&Admit(RecoveryActionKind.Consumable,item:2,spell:0),"interrupted heal retained a reservation");});
  Case("mana-only recovery remains independent",()=>{Submit();Check(Admit(RecoveryActionKind.Consumable,item:2,spell:0,resources:RecoveryResource.Mana),"health heal blocked mana-only item");});
  Case("dual-resource item conflicts with heal",()=>{Submit();Check(!Admit(RecoveryActionKind.Consumable,item:2,spell:0,resources:RecoveryResource.Health|RecoveryResource.Mana),"dual-resource item bypassed health reservation");});
  Case("group heal does not reserve our health",()=>{Submit(target:9);Check(Admit(RecoveryActionKind.Consumable,item:2,spell:0),"another recipient starved self recovery");});
  Case("potion prevents a conflicting self-heal",()=>{Submit(RecoveryActionKind.Consumable,item:2,spell:0);Check(!Admit(RecoveryActionKind.Heal,spell:202),"self heal ignored pending potion");});
  Case("pending consumable serializes both item owners",()=>{Submit(RecoveryActionKind.Consumable,item:2,spell:0);Check(!Admit(RecoveryActionKind.Consumable,item:3,spell:0,resources:RecoveryResource.Mana),"another resource branch spent a conflicting item");});
  Case("same aura blocked beyond the old 2500ms timeout",()=>{var t=Submit(RecoveryActionKind.Aura,resources:RecoveryResource.None);now+=3008;Check(!Admit(RecoveryActionKind.Aura,resources:RecoveryResource.None),"Divine Protection chronology resubmitted without acknowledgement");Check(t.BlockedCount==1,"blocked attempt was not accounted");});
  Case("different aura remains available",()=>{Submit(RecoveryActionKind.Aura,resources:RecoveryResource.None);Check(Admit(RecoveryActionKind.Aura,spell:202,resources:RecoveryResource.None),"unrelated spell was starved");});
  Case("same aura on another recipient remains available",()=>{Submit(RecoveryActionKind.Aura,resources:RecoveryResource.None);Check(Admit(RecoveryActionKind.Aura,target:9,resources:RecoveryResource.None),"recipient ownership was globalized");});
  Case("deadline does not slide on rejected duplicates",()=>{var t=Submit();now=10999;Check(!Admit(RecoveryActionKind.Heal),"deadline expired early");now=11000;ledger.Advance(context,now);Check(t.State==RecoveryActionState.TimedOut&&Admit(RecoveryActionKind.Heal),"duplicate extended deadline indefinitely");});
  Case("unsubmitted deadline cannot be called failed dispatch",()=>{var t=Prepare();now=11000;ledger.Advance(context,now);Check(t.State==RecoveryActionState.Rejected&&!t.WasSubmitted,"unsubmitted timeout became a dispatched failure");});
  Case("new context revokes old work",()=>{var t=Submit();context=new();ledger.Advance(context,now);Check(t.State==RecoveryActionState.Replaced&&Admit(RecoveryActionKind.Heal),"new run inherited old reservation");});
  Case("stale acknowledgement cannot clear replacement",()=>{var t=Submit();var old=context;context=new();var current=Submit();ledger.Observe(t,old,now,true,false);Check(current.State==RecoveryActionState.Submitted&&!Admit(RecoveryActionKind.Heal),"stale callback cleared replacement");});
  Case("stale cleanup cannot change the current context",()=>{var t=Submit();var old=context;context=new();var current=Submit();ledger.RejectUnsubmitted(t,old,now);Check(current.State==RecoveryActionState.Submitted,"old cleanup replaced context");});
  Case("same context replacement has a distinct token",()=>{var old=Submit();ledger.Observe(old,context,now,true,false);var current=Submit();ledger.Observe(old,context,now,false,true);Check(current.State==RecoveryActionState.Submitted,"stale same-spell outcome affected replacement token");});
  Case("clock rollback revokes rather than extending",()=>{var t=Submit();now=999;ledger.Advance(context,now);Check(t.State==RecoveryActionState.Replaced,"clock rollback prolonged action");});
  Case("Stop revokes only its owned context",()=>{var t=Submit();ledger.Revoke(context,now);Check(t.State==RecoveryActionState.Replaced&&ledger.Pending.Count==0,"stop leaked active action");});
  Case("stale Stop cannot revoke a new run",()=>{var old=context;Submit();context=new();var current=Submit();ledger.Revoke(old,now);Check(current.State==RecoveryActionState.Submitted,"stale Stop cleared new run");});
  Case("invalid context and identifiers do not reserve",()=>{Check(!ledger.TryPrepare(null,RecoveryActionKind.Heal,101,1,0,RecoveryResource.Health,now,10000,"test",out _),"unknown context admitted");Check(!Admit(RecoveryActionKind.Heal,target:0)&&!Admit(RecoveryActionKind.Heal,spell:0)&&!Admit(RecoveryActionKind.Consumable,spell:0,item:0),"unknown action identity admitted");Check(ledger.Pending.Count==0,"invalid metadata poisoned ledger");});
  Case("invalid deadline does not reserve",()=>{foreach(long duration in new long[]{0,-1,long.MaxValue})Check(!ledger.TryPrepare(context,RecoveryActionKind.Heal,101,1,0,RecoveryResource.Health,now,duration,"test",out _),"invalid deadline admitted");});
  Case("reservations are capacity bounded",()=>{for(int i=0;i<32;i++)Prepare(RecoveryActionKind.Aura,spell:100+i,resources:RecoveryResource.None);Check(!Admit(RecoveryActionKind.Aura,spell:999,resources:RecoveryResource.None)&&ledger.Pending.Count==32,"unbounded action ownership");});
  Case("transition diagnostics are bounded",()=>{for(int i=0;i<100;i++){var t=Submit();ledger.Observe(t,context,now,true,false);}Check(ledger.DrainTransitions().Count<=64&&ledger.DrainTransitions().Count==0,"diagnostic queue grows without bound");});
  Console.WriteLine($"Recovery action owner cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; complete production ledger with controlled observations; no game.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Recovery action owner regressions failed");
 }
}}
""";
}
