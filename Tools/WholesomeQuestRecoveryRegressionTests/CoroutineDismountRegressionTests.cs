using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the complete tracked async dismount, alias and movement-stop owners.
// Await outcomes and native/actor observations are controlled; actual C# await,
// exception propagation and finally cleanup execute. No game or wall-clock wait.
internal static class CoroutineDismountRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Tracked checkout required.");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "Styx/CommonBot/Coroutines/CommonCoroutines.cs"))).GetRoot();
        var owner = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "CommonCoroutines");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is
            "Dismount" or "LandAndDismount" or "StopMoving").ToArray();
        if (methods.Length != 3) throw new InvalidOperationException("All complete coroutine movement owners required.");
        string source = Prefix + "\npublic static class CommonCoroutines {\n" + string.Join("\n", methods.Select(m => m.ToString())) + "}\n" + Cases;
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Runtime references required.");
        var compilation = CSharpCompilation.Create("W110CoroutineDismount_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("CoroutineDismountCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
public enum ShapeshiftForm { Normal,Cat,FlightForm,EpicFlightForm }
public class WoWUnit {public ulong Guid=123;public bool IsMoving,IsValid=true,IsAlive=true;}
public sealed class LocalPlayer:WoWUnit {
 public bool Mounted=true,Available=true;public uint Flags;public ulong Transport;public ShapeshiftForm Shapeshift;
 public bool IsFlying=>(Flags&0x02000000)!=0;
 public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=Transport;return Available;}
}
public static class World {
 public static LocalPlayer Player;public static WoWUnit MoverOverride;public static bool MissingMover;
 public static readonly List<string> Commands=new List<string>();public static bool StopWorks=true,RemovalWorks=true;
 public static System.Action<string> AfterCommand,AfterLog;public static System.Action<int> BeforeWait,AfterSleep;
 public static Func<int,bool?> WaitResult;public static int CancelWait;public static bool CancelSleep;
 public static void Reset(){Player=new LocalPlayer();MoverOverride=null;MissingMover=false;Commands.Clear();StopWorks=true;RemovalWorks=true;
  AfterCommand=null;AfterLog=null;BeforeWait=null;AfterSleep=null;WaitResult=null;CancelWait=0;CancelSleep=false;}
 public static void Record(string command){Commands.Add(command+":"+(WoWMovement.ActiveMover?.Guid??0));AfterCommand?.Invoke(command);}
}
public static class StyxWoW {public static LocalPlayer Me=>World.Player;}
public static class WoWMovement {
 public enum MovementDirection {Descend}
 public static WoWUnit ActiveMover=>World.MissingMover?null:World.MoverOverride??World.Player;
 public static void MoveStop(){var mover=ActiveMover;World.Record("stop");if(World.StopWorks&&mover!=null)mover.IsMoving=false;}
 public static void MoveStop(MovementDirection direction)=>World.Record("stop-descend");
 public static void Move(MovementDirection direction)=>World.Record("descend");
}
public static class Logging {public static void WriteDiagnostic(string message){World.AfterLog?.Invoke(message);}}
public static class Lua {
 public static void DoString(string code){World.Record(code);if(World.RemovalWorks&&World.Player!=null){World.Player.Mounted=false;World.Player.Shapeshift=ShapeshiftForm.Normal;}}
}
/* Controlled event sink. */ namespace Styx.Logic {public static class Mount {public static void RaiseOnDismount(string reason)=>World.Record("event");}}
public static class Coroutine {
 public static Task Sleep(int milliseconds){World.AfterSleep?.Invoke(milliseconds);return World.CancelSleep?Task.FromCanceled(new CancellationToken(true)):Task.CompletedTask;}
 public static Task<bool> Wait(int milliseconds,Func<bool> condition){World.BeforeWait?.Invoke(milliseconds);
  if(World.CancelWait==milliseconds)return Task.FromCanceled<bool>(new CancellationToken(true));
  return Task.FromResult(World.WaitResult?.Invoke(milliseconds)??condition());}
}
""";

    private const string Cases = """
public static class CoroutineDismountCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static bool Removed=>World.Commands.Any(c=>c.StartsWith("Dismount()")||c.StartsWith("RunMacroText"));
 private static bool Event=>World.Commands.Any(c=>c.StartsWith("event:"));
 private static Task<bool> TaskFor(bool alias,bool descend=true)=>alias?CommonCoroutines.LandAndDismount("controlled"):CommonCoroutines.Dismount("controlled",descend);
 private static bool Invoke(bool alias=false,bool descend=true){try{return TaskFor(alias,descend).GetAwaiter().GetResult();}
  catch(NullReferenceException){throw new Failure("missing actor was dereferenced instead of refusing the request");}}
 private static void Change(string state){switch(state){
  case "missing":World.Player=null;break;case "invalid":World.Player.IsValid=false;break;case "dead":World.Player.IsAlive=false;break;
  case "unknown-guid":World.Player.Guid=0;break;case "changed-guid":World.Player.Guid++;break;
  case "changed-player":World.Player=new LocalPlayer{Guid=789};break;case "changed-form":World.Player.Shapeshift=ShapeshiftForm.Cat;break;
  case "unavailable":World.Player.Available=false;break;case "transport":World.Player.Transport=456;break;
  case "flying":World.Player.Flags=0x02000000;break;case "short-fall":World.Player.Flags=0x1000;break;
  case "long-fall":World.Player.Flags=0x2000;break;case "both-fall":World.Player.Flags=0x3000;break;
  case "other-mover":World.MoverOverride=new WoWUnit{Guid=456};break;case "missing-mover":World.MissingMover=true;break;
  case "already-dismounted":World.Player.Mounted=false;break;
 }}
 public static void Run(){
  var tests=new List<(string Name,System.Action Body)>();
  foreach(bool selected in new[]{false,true}){
   bool alias=selected;string prefix=alias?"alias/":"direct/";
   foreach(var selectedForm in new[]{ShapeshiftForm.Normal,ShapeshiftForm.FlightForm,ShapeshiftForm.EpicFlightForm}){
    var form=selectedForm;tests.Add((prefix+"ground "+form,()=>{
     World.Reset();World.Player.Shapeshift=form;Check(Invoke(alias),"ground removal did not acknowledge this actor");
     string command=form==ShapeshiftForm.Normal?"Dismount():123":"RunMacroText('/cancelform'):123";
     Check(World.Commands.SequenceEqual(new[]{command,"event:123"}),"ground dispatch/event order changed");
    }));
   }
   foreach(string state in new[]{"missing","invalid","dead","unknown-guid","unavailable","transport","short-fall","long-fall","both-fall","other-mover","missing-mover","already-dismounted"}){
    string observed=state;tests.Add((prefix+"admission "+state,()=>{
     World.Reset();Change(observed);Check(!Invoke(alias)&&World.Commands.Count==0,"invalid observation issued a command or returned success");
    }));
   }
   tests.Add((prefix+"landing timeout preserves flight and cleans input",()=>{
    World.Reset();World.Player.Flags=0x02000000;World.WaitResult=ms=>ms==40000?false:null;
    Check(!Invoke(alias)&&!Removed&&!Event,"timeout authorized removal or completion");
    Check(World.Commands.SequenceEqual(new[]{"descend:123","stop-descend:123"}),"owned descent cleanup missing or duplicated");
   }));
   tests.Add((prefix+"successful landing removes once",()=>{
    World.Reset();World.Player.Flags=0x02000000;World.BeforeWait=ms=>{if(ms==40000)World.Player.Flags=0;};
    Check(Invoke(alias),"landed dismount failed");Check(World.Commands.SequenceEqual(new[]{"descend:123","stop-descend:123","Dismount():123","event:123"}),"landing dispatch/cleanup order changed");
   }));
   tests.Add((prefix+"failed stop does not authorize removal",()=>{
    World.Reset();World.Player.IsMoving=true;World.StopWorks=false;
    Check(!Invoke(alias)&&!Removed&&!Event&&World.Commands.SequenceEqual(new[]{"stop:123"}),"movement-stop timeout was treated as readiness");
   }));
   tests.Add((prefix+"root alone permits ground removal",()=>{
    World.Reset();World.Player.Flags=0x800;
    Check(Invoke(alias)&&Removed&&Event,"root flag was mistaken for falling");
   }));
  }
  tests.Add(("descend=false refuses airborne removal",()=>{World.Reset();World.Player.Flags=0x02000000;Check(!Invoke(false,false)&&World.Commands.Count==0,"disabled descent removed flight");}));
  foreach(string phase in new[]{"log","stop-wait","sleep","landing-wait","release"}){
   string boundary=phase;
   foreach(string state in new[]{"changed-player","changed-guid","changed-form","invalid","dead","unavailable","transport","short-fall","other-mover"}){
    string observed=state;tests.Add((boundary+" invalidation "+state,()=>{
     World.Reset();bool airborne=boundary is "sleep" or "landing-wait" or "release";
     World.Player.Flags=airborne?0x02000000u:0;
     if(boundary=="log")World.AfterLog=message=>{World.AfterLog=null;Change(observed);};
     if(boundary=="stop-wait"){World.Player.IsMoving=true;World.BeforeWait=ms=>{World.BeforeWait=null;Change(observed);};}
     if(boundary=="sleep")World.AfterSleep=ms=>Change(observed);
     if(boundary=="landing-wait")World.BeforeWait=ms=>{if(ms==40000)Change(observed);};
     if(boundary=="release"){
      World.BeforeWait=ms=>{if(ms==40000)World.Player.Flags=0;};
      World.AfterCommand=command=>{if(command=="stop-descend")Change(observed);};
     }
     Check(!Invoke()&&!Removed&&!Event,"stale coroutine continued removal or success");
     Check(!World.Commands.Any(c=>c.EndsWith(":789")||c.EndsWith(":456")||c.EndsWith(":124")),"old request issued input to a changed actor/mover");
    }));
   }
  }
  foreach(bool duringSleep in new[]{true,false}){
   bool sleep=duringSleep;tests.Add(((sleep?"sleep":"wait")+" cancellation releases own descent",()=>{
    World.Reset();World.Player.Flags=0x02000000;World.CancelSleep=sleep;World.CancelWait=sleep?0:40000;
    bool cancelled=false;try{TaskFor(false).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
    Check(cancelled&&!Removed&&World.Commands.SequenceEqual(new[]{"descend:123","stop-descend:123"}),"cancellation lost input cleanup or cancellation outcome");
   }));
   tests.Add(((sleep?"sleep":"wait")+" cancellation never stops replacement",()=>{
    World.Reset();World.Player.Flags=0x02000000;World.CancelSleep=sleep;World.CancelWait=sleep?0:40000;
    if(sleep)World.AfterSleep=ms=>Change("changed-player");else World.BeforeWait=ms=>{if(ms==40000)Change("changed-player");};
    bool cancelled=false;try{TaskFor(false).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
    Check(cancelled&&!Removed&&World.Commands.SequenceEqual(new[]{"descend:123"}),"cancel cleanup targeted replacement actor");
   }));
  }
  tests.Add(("acknowledgement timeout reports false",()=>{World.Reset();World.RemovalWorks=false;Check(!Invoke()&&Removed&&!Event,"unobserved dismount was reported complete");}));
  tests.Add(("acknowledgement belongs to original actor",()=>{
   World.Reset();World.BeforeWait=ms=>{World.Player=new LocalPlayer{Guid=789,Mounted=false};};
   Check(!Invoke()&&Removed&&!Event,"replacement actor satisfied old dismount acknowledgement");
  }));
  foreach(var selected in new[]{ShapeshiftForm.FlightForm,ShapeshiftForm.EpicFlightForm}){
   var form=selected;tests.Add(("grounded unmounted flag "+form,()=>{
    World.Reset();World.Player.Mounted=false;World.Player.Shapeshift=form;Check(Invoke()&&Removed&&Event,"flight-form removal wrongly depended on normal mount flag");
   }));
  }
  foreach(string state in new[]{"missing-mover","invalid","unknown-guid","other-mover","changed-guid","changed-player","stubborn","normal","stationary"}){
   string observed=state;tests.Add(("StopMoving receipt "+state,()=>{
    World.Reset();World.Player.IsMoving=observed!="stationary";
    if(observed is "missing-mover" or "invalid" or "unknown-guid")Change(observed);
    if(observed=="stubborn")World.StopWorks=false;
    if(observed is "other-mover" or "changed-guid" or "changed-player")World.BeforeWait=ms=>Change(observed);
    bool result=CommonCoroutines.StopMoving("controlled").GetAwaiter().GetResult();
    Check(result==(observed=="normal"),"stop returned success for timeout/changed or unknown mover");
    if(observed is "missing-mover" or "invalid" or "unknown-guid" or "stationary")Check(World.Commands.Count==0,"invalid or stationary mover was commanded");
   }));
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS coroutine dismount: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL coroutine dismount: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR coroutine dismount: "+test.Name+": "+error);}}
  Console.WriteLine($"Coroutine dismount scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked async owners, controlled await/actor/native leaves; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Coroutine dismount regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
