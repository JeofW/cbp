using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked Dismount and DisMount entry points and their shared helper,
// with the real TreeSharp action. Only actor observations, command dispatch and
// lag are controlled. This is not native execution or successful game dismount.
internal static class FlightorDismountRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        DirectoryInfo? directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null && !File.Exists(Path.Combine(directory.FullName,"CopilotBuddy.csproj"))) directory=directory.Parent;
        if(directory==null) throw new InvalidOperationException("Tracked checkout required.");
        var syntax=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName,"Styx/Logic/Pathing/Flightor.cs"))).GetRoot();
        var helper=syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="MountHelper");
        var members=helper.Members.Where(m=>m is MethodDeclarationSyntax method && method.Identifier.ValueText is "Dismount" or "TryDismount"
            || m is ClassDeclarationSyntax type && type.Identifier.ValueText=="DisMount").ToArray();
        if(!members.OfType<MethodDeclarationSyntax>().Any(m=>m.Identifier.ValueText=="Dismount")
            || members.OfType<ClassDeclarationSyntax>().Count()!=1) throw new InvalidOperationException("Both complete dismount owners required.");
        string source=Prefix+"\npublic static class MountHelper {public static bool Mounted=>World.Mounted;\n"+
            string.Join("\n",members.Select(m=>m.ToString()))+"}\n"+Cases;
        var trusted=AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
        var references=trusted.Split(Path.PathSeparator).Append(typeof(TreeSharp.Composite).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("W110FlightorDismount_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(source)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();
        var result=compilation.Emit(output);
        if(!result.Success) throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        var assembly=Assembly.Load(output.ToArray());
        try { assembly.GetType("DismountCases",true)!.GetMethod("Run")!.Invoke(null,null); }
        catch(TargetInvocationException e) when(e.InnerException!=null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private const string Prefix="""
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using TreeSharp;
public sealed class LocalPlayer {
 public ulong Guid=123;public bool IsValid=true,IsAlive=true,IsMoving;public uint Flags;public ulong Transport;
 public bool Available=true;public string Form;public bool HasAura(string name){World.AuraRead?.Invoke();return name==Form;}
 public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=Transport;return Available;}
}
public static class World {
 public static bool Mounted=true;public static LocalPlayer Player=new LocalPlayer();
 public static readonly List<string> Commands=new List<string>();public static System.Action AfterWait,AuraRead;
 public static void Reset(){Mounted=true;Player=new LocalPlayer();Commands.Clear();AfterWait=null;AuraRead=null;}
}
public static class StyxWoW {
 public static LocalPlayer Me=>World.Player;
 public static void SleepForLagDuration(){World.Commands.Add("wait-lag");World.AfterWait?.Invoke();}
 public static void Sleep(int milliseconds){World.Commands.Add("wait-"+milliseconds);World.AfterWait?.Invoke();}
}
public static class Lua {public static void DoString(string code){World.Commands.Add(code);}}
public static class WoWMovement {public static void MoveStop(){World.Commands.Add("stop");}}
""";
    private const string Cases="""
public static class DismountCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static bool Removed=>World.Commands.Any(c=>c=="Dismount()"||c=="CancelShapeshiftForm()");
 private static void Invoke(bool tree,bool expected){
  if(!tree){MountHelper.Dismount();return;}
  var action=new MountHelper.DisMount();action.Start(null);
  try{Check(action.Tick(null)==(expected?RunStatus.Success:RunStatus.Failure),"TreeSharp action reported incorrect completion");}
  finally{action.Stop(null);}
 }
 private static void Change(string state){
  switch(state){
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
   case "dismounted":World.Mounted=false;break;
  }
 }
 public static void Run(){
  var cases=new List<(string Name,System.Action Body)>();
  foreach(bool action in new[]{false,true}){
   bool tree=action;string owner=tree?"tree/":"direct/";
   foreach(string form in new[]{"","Flight Form","Swift Flight Form","Aquatic Form"}){
    string selected=form;
    cases.Add((owner+"ground form "+form,()=>{
     World.Reset();World.Player.Form=selected;Invoke(tree,true);
     string removal=selected==""?"Dismount()":"CancelShapeshiftForm()";
     Check(World.Commands.Count(c=>c==removal)==1,"healthy form removal lost or duplicated");
     if(selected!="")Check(World.Commands.Last()==(tree?"wait-250":"wait-lag"),"form wait compatibility changed");
    }));
   }
   foreach(string state in new[]{"flying","short-fall","long-fall","both-fall","transport","unavailable","invalid","dead","unknown-guid","dismounted"}){
    string change=state;
    cases.Add((owner+"admission "+state,()=>{World.Reset();Change(change);Invoke(tree,false);Check(!Removed,"unsafe admission issued removal");}));
   }
   foreach(string state in new[]{"flying","short-fall","transport","unavailable","invalid","dead","changed-guid","changed-player","dismounted"}){
    string change=state;
    cases.Add((owner+"after wait "+state,()=>{
     World.Reset();World.Player.IsMoving=true;World.AfterWait=()=>Change(change);Invoke(tree,false);
     Check(!Removed,"state change during lag retained removal permission");
    }));
   }
   cases.Add((owner+"aura observation changes actor",()=>{
    World.Reset();World.AuraRead=()=>{World.AuraRead=null;Change("changed-player");};Invoke(tree,false);
    Check(!Removed,"aura read transferred permission to another actor");
   }));
   cases.Add((owner+"moving grounded actor stops before removal",()=>{
    World.Reset();World.Player.IsMoving=true;Invoke(tree,true);
     Check(World.Commands.SequenceEqual(new[]{"stop","wait-lag","Dismount()"}),"valid moving-ground command order changed");
   }));
   cases.Add((owner+"root alone is not falling",()=>{
    World.Reset();World.Player.Flags=0x800;Invoke(tree,true);
    Check(Removed,"root flag was mistaken for an airborne actor");
   }));
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in cases){try{test.Body();passed++;Console.WriteLine("PASS Flightor dismount: "+test.Name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL Flightor dismount: "+test.Name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR Flightor dismount: "+test.Name+": "+e);}}
  Console.WriteLine($"Flightor dismount scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked direct/action owners, real TreeSharp, controlled observations/dispatch/lag; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Flightor dismount regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
