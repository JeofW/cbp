using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Complete production parsers/cast correlation under explicit original-era
// replies. The private Lua collector and native dispatch have separate tests.
internal static class RecoveryActionEvidenceRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string? root = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CopilotBuddy.csproj"))) { root = dir.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked source required");
        string path = Path.Combine(root, "Styx/Logic/Combat/RecoveryActionEvidence.cs");
        if (!File.Exists(path)) throw new InvalidOperationException("Recovery acknowledgement protocol is absent; structural gate only.");
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(value => MetadataReference.CreateFromFile(value));
        var compilation = CSharpCompilation.Create("RecoveryEvidence_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(path)), CSharpSyntaxTree.ParseText(Cases) }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        try { assembly.GetType("Styx.Logic.Combat.RecoveryEvidenceCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private const string Cases = """
using System;using System.Linq;using System.Collections.Generic; namespace Styx.Logic.Combat {
public static class RecoveryEvidenceCases {
 private sealed class Failure(string value):Exception(value){}
 private static void Check(bool value,string message){if(!value)throw new Failure(message);}
 private static RecoverySpellEvidence Bind(bool instant=false)=>new(101,"Holy Light","Rank 1",1,1,5,100,instant);
 private static RecoveryEvent Event(string kind,long seq=6,long cast=42,int spell=101,ulong actor=1,ulong target=1,double time=100.1,string name="Holy Light",string rank="Rank 1")=>new(seq,time,kind,name,rank,cast,actor,target,spell,500,0);
 private static string[] Batch()=>new[]{"recovery-events","abc123","102","8","0","3", "6","100.1","START","Holy Light","Rank 1","42","0x0000000000000001","","0","0","0", "7","101.5","SUCCEEDED","Holy Light","Rank 1","42","0x0000000000000001","","0","0","0", "8","101.5","HEAL","","","0","0x0000000000000001","0x0000000000000001","101","500","0"};
 private static string[] Item()=>new[]{"recovery-item","123","0x0000000000000001","4","0","0","1","100"};
 public static void Run(){int passed=0,failed=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;}catch(Failure error){failed++;Console.Error.WriteLine("FAIL recovery evidence: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR recovery evidence: "+name+": "+error);}}
  Case("complete cast and heal acknowledgement",()=>{Check(RecoveryActionEvidence.TryParseBatch(Batch(),"abc123",out var batch),"known original protocol rejected");var b=Bind();foreach(var e in batch.Events)b.Observe(e);Check(b.Started&&b.Succeeded&&b.Acknowledged&&!b.Interrupted,"matching cast and effect not acknowledged");});
  Case("cast success alone is not healing",()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("SUCCEEDED",7));Check(b.Succeeded&&!b.Acknowledged,"cast success became health recovery");});
  Case("heal alone cannot acknowledge a cast",()=>{var b=Bind();b.Observe(Event("HEAL"));Check(!b.Acknowledged,"uncorrelated heal released reservation");});
  Case("heal before cast start is not borrowed",()=>{var b=Bind();b.Observe(Event("HEAL"));b.Observe(Event("START",7));b.Observe(Event("SUCCEEDED",8));Check(!b.Acknowledged,"earlier effect was assigned to later cast");});
  Case("effect may precede the success callback",()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("HEAL",7));b.Observe(Event("SUCCEEDED",8));Check(b.Acknowledged,"valid original callback ordering lost");});
  foreach(string change in new[]{"actor","target","spell","time","sequence"})
   Case("unrelated heal / "+change,()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("SUCCEEDED",7));b.Observe(Event("HEAL",change=="sequence"?5:8,spell:change=="spell"?202:101,actor:change=="actor"?9UL:1UL,target:change=="target"?9UL:1UL,time:change=="time"?99:101.5));Check(!b.Acknowledged,"unrelated effect acknowledged active cast");});
  foreach(string change in new[]{"name","rank","counter","time","sequence"})
   Case("unrelated cast success / "+change,()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("SUCCEEDED",change=="sequence"?5:7,cast:change=="counter"?99:42,time:change=="time"?99:101.5,name:change=="name"?"Other":"Holy Light",rank:change=="rank"?"Rank 2":"Rank 1"));b.Observe(Event("HEAL",8));Check(!b.Acknowledged,"unrelated cast success was accepted");});
  Case("SENT target is never a cast counter",()=>{var b=Bind();b.Observe(Event("SENT"));b.Observe(Event("SUCCEEDED",7));b.Observe(Event("HEAL",8));Check(!b.Acknowledged,"SENT was accepted as START");});
  Case("zero cast counter remains uncorrelated",()=>{var b=Bind();b.Observe(Event("START",cast:0));b.Observe(Event("SUCCEEDED",7,cast:0));b.Observe(Event("HEAL",8));Check(!b.Acknowledged,"zero counter manufactured cast identity");});
  Case("another same-spell cast makes the effect ambiguous",()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("START",7,99));b.Observe(Event("SUCCEEDED",8));b.Observe(Event("HEAL",9));Check(b.Ambiguous&&!b.Acknowledged,"overlapping cast effects were arbitrarily assigned");});
  Case("matching failure permits recovery",()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("FAILED",7));Check(b.Interrupted&&!b.Acknowledged,"matching failure not recognized");});
  Case("wrong failure cannot release a pending cast",()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("FAILED",7,99));Check(!b.Interrupted,"stale failure released current cast");});
  Case("STOP does not prove either heal or failure",()=>{var b=Bind();b.Observe(Event("START"));b.Observe(Event("STOP",7));Check(!b.Interrupted&&!b.Acknowledged,"STOP was promoted to outcome");});
  Case("lost observations block causal promotion",()=>{var b=Bind();b.Observe(Event("START"));b.MarkUnavailable();b.Observe(Event("SUCCEEDED",7));b.Observe(Event("HEAL",8));Check(!b.Acknowledged&&b.Ambiguous,"lost coverage was silently complete");});
  Case("instant success without an observed start stays uncorrelated",()=>{var b=Bind(true);b.Observe(Event("SUCCEEDED"));b.Observe(Event("HEAL",7));Check(!b.Acknowledged,"late success was borrowed as the current instant cast identity");});
  Case("instant metadata does not weaken a complete cast sequence",()=>{var b=Bind(true);b.Observe(Event("START"));b.Observe(Event("SUCCEEDED",7));b.Observe(Event("HEAL",8));Check(b.Acknowledged,"complete original cast/effect sequence lost");});
  Case("instant without a positive cast identity stays unknown",()=>{var b=Bind(true);b.Observe(Event("SUCCEEDED",cast:0));b.Observe(Event("HEAL",7));Check(!b.Acknowledged,"zero instant identity became acknowledged");});
  foreach(string value in new[]{"NaN","Infinity","-1","","wat"})
   foreach(int index in new[]{2,3,4,5,6,7,11,25,36,37,38})
    Case("malformed event scalar "+index+"="+value,()=>{var values=Batch();values[index]=value;Check(!RecoveryActionEvidence.TryParseBatch(values,"abc123",out _),"malformed event scalar accepted");});
  Case("wrong collector identity is rejected",()=>Check(!RecoveryActionEvidence.TryParseBatch(Batch(),"other",out _),"foreign collector accepted"));
  Case("missing reply is unavailable",()=>Check(!RecoveryActionEvidence.TryParseBatch(null,"abc123",out _),"missing events became empty observed queue"));
  Case("incomplete reply is unavailable",()=>Check(!RecoveryActionEvidence.TryParseBatch(Batch().SkipLast(1).ToArray(),"abc123",out _),"partial event batch accepted"));
  Case("unknown event kind is unavailable",()=>{var values=Batch();values[8]="SENT";Check(!RecoveryActionEvidence.TryParseBatch(values,"abc123",out _),"unsupported event entered ownership" );});
  Case("duplicate event sequence is unavailable",()=>{var values=Batch();values[17]="6";Check(!RecoveryActionEvidence.TryParseBatch(values,"abc123",out _),"duplicate sequence accepted");});
  Case("reported sequence cannot precede included events",()=>{var values=Batch();values[3]="7";Check(!RecoveryActionEvidence.TryParseBatch(values,"abc123",out _),"future event accepted" );});
  Case("complete empty batch remains known",()=>Check(RecoveryActionEvidence.TryParseBatch(new[]{"recovery-events","abc123","102","8","0","0"},"abc123",out var b)&&b.Events.Count==0,"complete empty queue rejected"));
  Case("event batch stays within the observed Lua return bound",()=>{var values=new List<string>{"recovery-events","abc123","102","6","0","6"};for(int i=1;i<=6;i++)values.AddRange(new[]{i.ToString(),"101","HEAL","","","0","0x0000000000000001","0x0000000000000001","101","500","0"});Check(!RecoveryActionEvidence.TryParseBatch(values,"abc123",out _),"event batch exceeded the64-value observed return contract");});
  Case("complete ready item observation",()=>{Check(RecoveryActionEvidence.TryParseItem(Item(),123,1,out var item)&&item.IsReady&&item.Count==4,"ready item unavailable");});
  Case("disabled item is complete but not ready",()=>{var values=Item();values[6]="0";Check(RecoveryActionEvidence.TryParseItem(values,123,1,out var item)&&!item.IsReady,"disabled item was ready");});
  Case("cooldown and quantity change jointly acknowledge",()=>{RecoveryActionEvidence.TryParseItem(Item(),123,1,out var before);var values=Item();values[3]="3";values[4]="100";values[5]="120";values[7]="101";Check(RecoveryActionEvidence.TryParseItem(values,123,1,out var after)&&after.Acknowledges(before),"known use transition not acknowledged");});
  foreach(string only in new[]{"count","cooldown"})
   Case("partial item transition / "+only,()=>{RecoveryActionEvidence.TryParseItem(Item(),123,1,out var before);var values=Item();if(only=="count")values[3]="3";else{values[4]="100";values[5]="120";}values[7]="101";RecoveryActionEvidence.TryParseItem(values,123,1,out var after);Check(!after.Acknowledges(before),"partial item evidence became use acknowledgement");});
  Case("foreign item/source cannot acknowledge",()=>{Check(!RecoveryActionEvidence.TryParseItem(Item(),999,1,out _)&&!RecoveryActionEvidence.TryParseItem(Item(),123,9,out _),"foreign item reply accepted");});
  foreach(string value in new[]{"NaN","Infinity","-1","","wat"})foreach(int index in new[]{1,3,4,5,6,7})
   Case("malformed item scalar "+index+"="+value,()=>{var values=Item();values[index]=value;Check(!RecoveryActionEvidence.TryParseItem(values,123,1,out _),"malformed item observation accepted");});
  Console.WriteLine($"Recovery acknowledgement evidence cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; complete parsers/cast correlation; explicit original-era observations, no game.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Recovery acknowledgement evidence regressions failed");
 }
}}
""";
}
