using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked forced-behavior factory and its removal owner, executed with
// real TreeSharp/coroutine bridge. Actor observations and already-covered shared
// landing/cancel commands are controlled. No game or physical landing is tested.
internal static class ForcedDismountBoundaryRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Tracked checkout required.");
        var owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "runtime-snapshot/Quest Behaviors/ForcedDismount.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ForcedDismount");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is "CreateBehavior" or "ExecuteDismount").ToArray();
        if (methods.Count(m => m.Identifier.ValueText == "CreateBehavior") != 1)
            throw new InvalidOperationException("Complete forced dismount factory required.");
        string source = Prefix + "\npublic sealed class ForcedOwner:Behavior {private bool _isBehaviorDone;private Composite _root;\n" +
            "private LocalPlayer Me=>World.Player;public bool Done=>_isBehaviorDone;public Composite Build()=>CreateBehavior();\n" +
            string.Join("\n", methods.Select(m => m.ToString())) + "}\n" + Cases;
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Runtime references required.");
        var references = trusted.Split(Path.PathSeparator).Append(typeof(TreeSharp.Composite).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("W110ForcedDismount_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("ForcedDismountCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;
using TreeSharp;using CommonBehaviors.Actions;using Action=TreeSharp.Action;
public abstract class Behavior {protected abstract Composite CreateBehavior();}
public enum ShapeshiftForm {Normal,Cat,TravelForm,AquaticForm,FlightForm,EpicFlightForm}
public sealed class LocalPlayer {public ulong Guid=123;public bool IsValid=true,IsAlive=true,Mounted=true,IsMoving,IsFlying;public ShapeshiftForm Shapeshift;}
public static class World {
 public static LocalPlayer Player;public static readonly List<string> Commands=new List<string>();
 public static bool LandResult=true,LandRemoves=true,ClearRemoves=true,RawRemoves=true;
 public static System.Action AfterStatus,AfterLand,AfterClear,BeforeWait,AfterSleep;
 public static void Reset(){Player=new LocalPlayer();Commands.Clear();LandResult=true;LandRemoves=true;ClearRemoves=true;RawRemoves=true;
  AfterStatus=null;AfterLand=null;AfterClear=null;BeforeWait=null;AfterSleep=null;}
 public static void Record(string command)=>Commands.Add(command+":"+(Player?.Guid??0));
}
public static class TreeRoot {public static string StatusText {set{World.AfterStatus?.Invoke();}}}
public static class Thread {public static void Sleep(int milliseconds){World.Record("sleep");World.AfterSleep?.Invoke();}}
public static class WoWMovement {public static void MoveStop(){World.Record("stop");}}
public static class Lua {public static void DoString(string code){World.Record("raw-"+code);if(World.RawRemoves&&World.Player!=null){World.Player.Mounted=false;World.Player.Shapeshift=ShapeshiftForm.Normal;}}}
public static class Mount {public static void ClearShapeshift(){World.Record("clear");if(World.ClearRemoves)World.Player.Shapeshift=ShapeshiftForm.Normal;World.AfterClear?.Invoke();}}
public static class CommonCoroutines {public static Task<bool> LandAndDismount(string reason){World.Record("land");
 if(World.LandResult&&World.LandRemoves){World.Player.Mounted=false;World.Player.Shapeshift=ShapeshiftForm.Normal;}
 World.AfterLand?.Invoke();return Task.FromResult(World.LandResult);}}
public static class Coroutine {public static Task<bool> Wait(int milliseconds,Func<bool> condition){World.BeforeWait?.Invoke();return Task.FromResult(condition());}}
""";

    private const string Cases = """
public static class ForcedDismountCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static bool Run(ForcedOwner owner){var root=owner.Build();root.Start(null);try{return root.Tick(null)==RunStatus.Success;}
  catch(NullReferenceException){throw new Failure("missing actor was dereferenced");}finally{root.Stop(null);}}
 private static void Change(string state){switch(state){case "missing":World.Player=null;break;case "invalid":World.Player.IsValid=false;break;
  case "dead":World.Player.IsAlive=false;break;case "unknown-guid":World.Player.Guid=0;break;case "changed-guid":World.Player.Guid++;break;
  case "changed-player":World.Player=new LocalPlayer{Guid=789,Mounted=false};break;case "changed-form":World.Player.Shapeshift=ShapeshiftForm.Cat;break;}}
 public static void Run(){
  var tests=new List<(string Name,System.Action Body)>();
  tests.Add(("already unmounted normal is complete",()=>{World.Reset();World.Player.Mounted=false;var owner=new ForcedOwner();Check(Run(owner)&&owner.Done&&World.Commands.Count==0,"normal no-op changed");}));
  foreach(var selected in new[]{ShapeshiftForm.Normal,ShapeshiftForm.FlightForm,ShapeshiftForm.EpicFlightForm}){
   var form=selected;tests.Add(("shared landing route/"+form,()=>{World.Reset();World.Player.Shapeshift=form;World.Player.IsFlying=true;
    var owner=new ForcedOwner();Check(Run(owner)&&owner.Done,"successful landing did not complete");Check(World.Commands.SequenceEqual(new[]{"land:123"}),"forced behavior bypassed shared landing ownership");}));
   foreach(string state in new[]{"refused","no-acknowledgement"}){
    string outcome=state;tests.Add(("landing failure/"+form+"/"+state,()=>{World.Reset();World.Player.Shapeshift=form;World.LandResult=outcome!="refused";World.LandRemoves=false;World.RawRemoves=false;
     var owner=new ForcedOwner();Run(owner);Check(!owner.Done,"failed/unobserved removal consumed behavior");Check(World.Commands.SequenceEqual(new[]{"land:123"}),"landing refusal was bypassed");}));
   }
  }
  foreach(var selected in new[]{ShapeshiftForm.Cat,ShapeshiftForm.TravelForm,ShapeshiftForm.AquaticForm}){
   var form=selected;tests.Add(("nonflight clear route/"+form,()=>{World.Reset();World.Player.Mounted=false;World.Player.Shapeshift=form;var owner=new ForcedOwner();
    Check(Run(owner)&&owner.Done&&World.Commands.SequenceEqual(new[]{"clear:123"}),"nonflight form bypassed observed shared cancellation");}));
   tests.Add(("nonflight refusal/"+form,()=>{World.Reset();World.Player.Mounted=false;World.Player.Shapeshift=form;World.ClearRemoves=false;World.RawRemoves=false;
    var owner=new ForcedOwner();Run(owner);Check(!owner.Done,"unobserved cancellation consumed behavior");}));
  }
  foreach(string state in new[]{"missing","invalid","dead","unknown-guid"}){
   string observed=state;tests.Add(("admission/"+state,()=>{World.Reset();Change(observed);var owner=new ForcedOwner();Run(owner);Check(!owner.Done&&World.Commands.Count==0,"invalid actor authorized forced removal");}));
  }
  foreach(string phase in new[]{"status","land","clear","wait"}){
   string boundary=phase;foreach(string state in new[]{"changed-player","changed-guid","invalid","dead"}){
    string observed=state;tests.Add((boundary+"/"+state,()=>{World.Reset();if(boundary is "clear" or "wait"){World.Player.Mounted=false;World.Player.Shapeshift=ShapeshiftForm.Cat;}
     if(boundary=="status")World.AfterStatus=()=>Change(observed);if(boundary=="land")World.AfterLand=()=>Change(observed);
     if(boundary=="clear")World.AfterClear=()=>Change(observed);if(boundary=="wait")World.BeforeWait=()=>Change(observed);
     var owner=new ForcedOwner();Run(owner);Check(!owner.Done,"replacement or invalid actor consumed prior behavior");
     Check(!World.Commands.Any(c=>c.EndsWith(":789")||c.EndsWith(":124")),"old request commanded replacement actor");}));
   }
  }
  tests.Add(("status form change does not clear the new form",()=>{World.Reset();World.Player.Shapeshift=ShapeshiftForm.FlightForm;World.AfterStatus=()=>Change("changed-form");
   var owner=new ForcedOwner();Run(owner);Check(!owner.Done&&World.Commands.Count==0,"new form inherited old removal request");}));
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS forced dismount: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL forced dismount: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR forced dismount: "+test.Name+": "+error);}}
  Console.WriteLine($"Forced dismount scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked factory/owner, real TreeSharp bridge, controlled shared landing leaves; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Forced dismount regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
