using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the complete tracked public Mount removal methods and their helpers.
// Player observations, logging callbacks and native command leaves are controlled;
// no game, executor, physical landing or server acknowledgement is represented.
internal static class HostMountDismountRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Tracked checkout required.");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "Styx/Logic/Mount.cs"))).GetRoot();
        var owner = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Mount");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is
            "Dismount" or "ClearShapeshift" or "RaiseOnDismount" or "CanRemoveMount").ToArray();
        if (methods.Count(m => m.Identifier.ValueText == "Dismount") != 2 ||
            methods.Count(m => m.Identifier.ValueText == "ClearShapeshift") != 1 ||
            methods.Count(m => m.Identifier.ValueText == "RaiseOnDismount") != 1)
            throw new InvalidOperationException("Complete public removal and event owners required.");
        string source = Prefix + "\npublic static class Mount { private static LocalPlayer Me => World.Player;\n" +
            "public static event EventHandler<EventArgs> OnDismount;\n" +
            string.Join("\n", methods.Select(m => m.ToString())) + "}\n" + Cases;
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Runtime references required.");
        var references = trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("W110HostMountDismount_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("HostMountCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;
public enum ShapeshiftForm { Normal,Cat,TravelForm,AquaticForm,FlightForm,EpicFlightForm }
public sealed class LocalPlayer {
 public ulong Guid=123;public bool IsValid=true,IsAlive=true,Mounted=true,Available=true;
 public uint Flags;public ulong Transport;public ShapeshiftForm Shapeshift;
 public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=Transport;return Available;}
}
public static class World {
 public static LocalPlayer Player;public static readonly List<string> Commands=new List<string>();
 public static System.Action AfterLog,AfterStop;
 public static void Reset(){Player=new LocalPlayer();Commands.Clear();AfterLog=null;AfterStop=null;}
 public static void Record(string command){Commands.Add(command+":"+(Player?.Guid??0));}
}
public static class Logging {
 public static void WriteDebug(string text,params object[] args){World.AfterLog?.Invoke();}
 public static void WriteException(Exception error){throw new InvalidOperationException("Unexpected event failure",error);}
}
public static class WoWMovement {public static void MoveStop(){World.Record("stop");World.AfterStop?.Invoke();}}
public static class Lua {public static void DoString(string code){World.Record(code);}}
""";

    private const string Cases = """
public static class HostMountCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static bool Removed=>World.Commands.Any(c=>c.StartsWith("Dismount()")||c.StartsWith("CancelShapeshiftForm()"));
 private static void Invoke(bool reason){if(reason)Mount.Dismount("controlled reason");else Mount.Dismount();}
 private static void Change(string state){
  switch(state){
   case "missing":World.Player=null;break;
   case "flying":World.Player.Flags=0x02000000;break;
   case "short-fall":World.Player.Flags=0x1000;break;
   case "long-fall":World.Player.Flags=0x2000;break;
   case "both-fall":World.Player.Flags=0x3000;break;
   case "transport":World.Player.Transport=456;break;
   case "unavailable":World.Player.Available=false;break;
   case "invalid":World.Player.IsValid=false;break;
   case "dead":World.Player.IsAlive=false;break;
   case "unknown-guid":World.Player.Guid=0;break;
   case "changed-guid":World.Player.Guid++;break;
   case "changed-player":World.Player=new LocalPlayer{Guid=789};break;
   case "changed-form":World.Player.Shapeshift=ShapeshiftForm.Cat;break;
   case "dismounted":World.Player.Mounted=false;break;
  }
 }
 public static void Run(){
  Mount.OnDismount+=(_,_)=>World.Record("event");
  var tests=new List<(string Name,System.Action Body)>();
  foreach(bool withReason in new[]{false,true}){
   bool reason=withReason;string prefix=reason?"reason/":"parameterless/";
   foreach(var selected in new[]{ShapeshiftForm.Normal,ShapeshiftForm.FlightForm,ShapeshiftForm.EpicFlightForm}){
    var form=selected;
    tests.Add((prefix+"ground "+form,()=>{
     World.Reset();World.Player.Shapeshift=form;Invoke(reason);
     string command=form==ShapeshiftForm.Normal?"Dismount()":"CancelShapeshiftForm()";
     Check(World.Commands.SequenceEqual(new[]{"stop:123",command+":123","event:123"}),"grounded removal or command/event order changed");
    }));
   }
   foreach(string state in new[]{"missing","flying","short-fall","long-fall","both-fall","transport","unavailable","invalid","dead","unknown-guid","dismounted"}){
    string observed=state;tests.Add((prefix+"admission "+state,()=>{
     World.Reset();Change(observed);Invoke(reason);Check(World.Commands.Count==0,"unproven admission issued movement/removal/event");
    }));
   }
   foreach(bool afterStop in new[]{false,true}){
    bool stop=afterStop;
    foreach(string state in new[]{"flying","transport","unavailable","dead","changed-guid","changed-player","changed-form","dismounted"}){
     string observed=state;tests.Add((prefix+(stop?"after stop ":"after log ")+state,()=>{
      World.Reset();if(stop)World.AfterStop=()=>Change(observed);else World.AfterLog=()=>Change(observed);
      Invoke(reason);Check(!Removed&&!World.Commands.Any(c=>c.StartsWith("event:")),"setup change retained removal/event permission");
      Check(World.Commands.Count==(stop?1:0),"stale setup commanded movement or duplicated cleanup");
     }));
    }
   }
  }
  foreach(var selected in new[]{ShapeshiftForm.FlightForm,ShapeshiftForm.EpicFlightForm}){
   var form=selected;
   tests.Add(("unmounted flag still permits grounded "+form,()=>{
    World.Reset();World.Player.Mounted=false;World.Player.Shapeshift=form;Mount.Dismount();
    Check(World.Commands.SequenceEqual(new[]{"stop:123","CancelShapeshiftForm():123","event:123"}),"flight-form removal wrongly depends on mount flag");
   }));
   foreach(string state in new[]{"flying","short-fall","long-fall","both-fall","transport","unavailable"}){
    string observed=state;tests.Add(("clear flight form/"+form+"/"+state,()=>{
     World.Reset();World.Player.Mounted=false;World.Player.Shapeshift=form;Change(observed);Mount.ClearShapeshift();
     Check(World.Commands.Count==0,"ClearShapeshift removed unproven flight");
    }));
   }
   tests.Add(("clear grounded "+form,()=>{
    World.Reset();World.Player.Mounted=false;World.Player.Shapeshift=form;Mount.ClearShapeshift();
    Check(World.Commands.SequenceEqual(new[]{"CancelShapeshiftForm():123"}),"grounded ClearShapeshift changed");
   }));
  }
  foreach(string state in new[]{"missing","unknown-guid","invalid","dead","changed-player","changed-guid","changed-form"}){
   string observed=state;tests.Add(("clear form ownership/"+state,()=>{
    World.Reset();World.Player.Shapeshift=ShapeshiftForm.Cat;
    if(observed.StartsWith("changed-"))World.AfterLog=()=>{if(observed=="changed-form")World.Player.Shapeshift=ShapeshiftForm.TravelForm;else Change(observed);};
    else Change(observed);
    Mount.ClearShapeshift();Check(World.Commands.Count==0,"ClearShapeshift acted on missing/stale actor or form");
   }));
  }
  foreach(var selected in new[]{ShapeshiftForm.Cat,ShapeshiftForm.TravelForm,ShapeshiftForm.AquaticForm}){
   var form=selected;tests.Add(("nonflight form remains clearable/"+form,()=>{
    World.Reset();World.Player.Shapeshift=form;World.Player.Flags=0x1000;Mount.ClearShapeshift();
    Check(World.Commands.SequenceEqual(new[]{"CancelShapeshiftForm():123"}),"nonflight cancellation incorrectly borrowed flight landing restrictions");
   }));
  }
  tests.Add(("normal ClearShapeshift is a no-op",()=>{World.Reset();Mount.ClearShapeshift();Check(World.Commands.Count==0,"normal form issued a command");}));
  tests.Add(("ordinary movement flags permit ground dismount",()=>{World.Reset();World.Player.Flags=1;Mount.Dismount();Check(Removed,"ordinary forward movement was treated as flight");}));
  tests.Add(("root alone permits ground dismount",()=>{World.Reset();World.Player.Flags=0x800;Mount.Dismount();Check(Removed,"root was mistaken for falling");}));
  tests.Add(("root alone permits grounded flight-form cancellation",()=>{World.Reset();World.Player.Flags=0x800;World.Player.Shapeshift=ShapeshiftForm.FlightForm;Mount.ClearShapeshift();Check(Removed,"root was mistaken for airborne flight form");}));
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS host mount removal: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL host mount removal: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR host mount removal: "+test.Name+": "+error);}}
  Console.WriteLine($"Host mount removal scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked public owners and event dispatch, controlled actor/native leaves; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Host mount removal regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
