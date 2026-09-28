using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the complete tracked airborne subtree and its request/cleanup owners.
// Native start/stop/removal and player/node observations are recording leaves;
// actual TreeSharp cancellation, yielding and finally cleanup remain intact.
internal static class GatherLandingLifecycleRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        DirectoryInfo? directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null && !File.Exists(Path.Combine(directory.FullName,"CopilotBuddy.csproj"))) directory=directory.Parent;
        if(directory==null) throw new InvalidOperationException("Tracked checkout required.");
        var syntax=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName,"Bots/Gatherbuddy/GatherbuddyBot.cs"))).GetRoot();
        var owner=syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="GatherbuddyBot");
        var method=owner.Members.OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="CreateGatherBehavior");
        var subtree=method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single(n=>n.Type.ToString()=="Decorator"
            && n.ArgumentList!.Arguments.Count==2 && n.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax lambda
            && lambda.Body.ToString()=="StyxWoW.Me.MovementInfo.IsFlying");
        var helpers=owner.Members.OfType<ClassDeclarationSyntax>().Where(c=>c.Identifier.ValueText is "GatherLandingRequest" or "GatherLandingSequence");
        string source=Prefix+"\npublic static class GatherProbe {private static WoWObject _currentNode=>World.Node;private static WoWPoint _approachPoint;\n"+
            string.Join("\n",helpers.Select(c=>c.ToString()))+"\npublic static Composite Create()=>"+subtree+";}\n"+Cases;
        var trusted=AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
        var references=trusted.Split(Path.PathSeparator).Append(typeof(TreeSharp.Composite).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("W110GatherLifecycle_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(source)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        var assembly=Assembly.Load(output.ToArray());
        try{assembly.GetType("GatherLifecycleCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private const string Prefix="""
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using TreeSharp;using CommonBehaviors.Actions;using Action=TreeSharp.Action;
public struct WoWPoint{public static WoWPoint Zero=>default;}
public class WoWObject{public ulong Guid=999;public bool IsValid=true,WithinInteractRange;}
public sealed class LocalPlayer:WoWObject{
 public bool IsAlive=true,Available=true;public uint Flags=0x02000000;public ulong Transport;
 public LocalPlayer MovementInfo=>this;public bool IsFlying=>(Flags&0x02000000)!=0;
 public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=Transport;return Available;}
}
public static class World{
 public static LocalPlayer Player;public static WoWObject Node;public static readonly List<string> Commands=new List<string>();
 public static System.Action<string> AfterCommand;
 public static void Reset(){Player=new LocalPlayer{Guid=123};Node=new WoWObject();Commands.Clear();AfterCommand=null;}
 public static void Record(string action){Commands.Add(action+":"+Player.Guid);AfterCommand?.Invoke(action);}
}
public static class StyxWoW{public static LocalPlayer Me=>World.Player;}
public static class WoWMovement{
 public enum MovementDirection{Descend}
 public static void Move(MovementDirection direction)=>World.Record("descend");
 public static void MoveStop(MovementDirection direction)=>World.Record("stop-descend");
 public static void MoveStop()=>World.Record("stop-all");
}
public static class Flightor{public static class MountHelper{public static void Dismount()=>World.Record("remove");}}
""";
    private const string Cases="""
public static class GatherLifecycleCases{
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string why){if(!value)throw new Failure(why);}
 private static Composite Start(){var root=GatherProbe.Create();root.Start(null);Check(root.Tick(null)==RunStatus.Running,"descent did not yield");return root;}
 private static bool Removed=>World.Commands.Any(c=>c.StartsWith("remove:"));
 private static IEnumerable<Composite> Nodes(Composite root){yield return root;if(root is GroupComposite group)foreach(var child in group.Children)foreach(var node in Nodes(child))yield return node;}
 public static void Run(){
  var tests=new List<(string Name,System.Action Body)>{
   ("cancellation releases owned descent exactly once",()=>{World.Reset();var root=Start();root.Stop(null);root.Stop(null);Check(World.Commands.SequenceEqual(new[]{"descend:123","stop-descend:123"}),"cancelled descent was retained or released twice");}),
   ("actor replacement receives no old cleanup",()=>{World.Reset();var root=Start();World.Player=new LocalPlayer{Guid=456};root.Stop(null);Check(World.Commands.SequenceEqual(new[]{"descend:123"}),"cancellation commanded a replacement actor");}),
   ("same wrapper with new GUID receives no old cleanup",()=>{World.Reset();var root=Start();World.Player.Guid++;root.Stop(null);Check(World.Commands.SequenceEqual(new[]{"descend:123"}),"cancellation commanded changed native identity");}),
   ("lost node still releases this actor's descent",()=>{World.Reset();var root=Start();World.Node=null;root.Stop(null);Check(World.Commands.SequenceEqual(new[]{"descend:123","stop-descend:123"}),"node loss stranded the owned descent");}),
   ("timeout releases descent without removal",()=>{World.Reset();var root=GatherProbe.Create();foreach(var wait in Nodes(root).OfType<Wait>())wait.Timeout=TimeSpan.Zero;root.Start(null);try{Check(root.Tick(null)==RunStatus.Success,"bounded attempt failed");Check(!Removed&&World.Commands.Count(c=>c=="stop-descend:123")==1,"timeout retained descent or removed flight");}finally{root.Stop(null);}}),
   ("landed continuation releases once and removes once",()=>{World.Reset();var root=Start();World.Player.Flags=0;try{Check(root.Tick(null)==RunStatus.Success,"landing did not continue");Check(World.Commands.Count(c=>c=="stop-descend:123")==1&&World.Commands.Count(c=>c=="remove:123")==1,"landing cleanup/removal count changed");}finally{root.Stop(null);}Check(World.Commands.Count(c=>c=="stop-descend:123")==1,"completed cleanup repeated on Stop");}),
   ("replacement during wait is not stopped or dismounted",()=>{World.Reset();var root=Start();World.Player=new LocalPlayer{Guid=456,Flags=0};try{root.Tick(null);Check(World.Commands.SequenceEqual(new[]{"descend:123"}),"old wait commanded a replacement actor");}finally{root.Stop(null);}}),
   ("replacement during descent release prevents later stop-all",()=>{World.Reset();var root=Start();World.Player.Flags=0;World.AfterCommand=action=>{if(action=="stop-descend")World.Player=new LocalPlayer{Guid=456,Flags=0};};try{root.Tick(null);Check(World.Commands.SequenceEqual(new[]{"descend:123","stop-descend:123"}),"cleanup continued against replacement actor");}finally{root.Stop(null);}}),
   ("node replacement releases only the old descent",()=>{World.Reset();var root=Start();World.Player.Flags=0;World.Node=new WoWObject{Guid=1000};try{root.Tick(null);Check(World.Commands.SequenceEqual(new[]{"descend:123","stop-descend:123"}),"old node request continued stop-all or removal");}finally{root.Stop(null);}})
  };
  foreach(string state in new[]{"dead","invalid","transport","unavailable","unknown-guid","missing-node"}){
   string change=state;tests.Add(("admission "+state,()=>{
    World.Reset();switch(change){case "dead":World.Player.IsAlive=false;break;case "invalid":World.Player.IsValid=false;break;case "transport":World.Player.Transport=88;break;case "unavailable":World.Player.Available=false;break;case "unknown-guid":World.Player.Guid=0;break;case "missing-node":World.Node=null;break;}
    var root=GatherProbe.Create();root.Start(null);try{Check(root.Tick(null)==RunStatus.Failure&&World.Commands.Count==0,"unsafe actor/node started descent");}finally{root.Stop(null);}
   }));
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS gather landing lifetime: "+test.Name);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL gather landing lifetime: "+test.Name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR gather landing lifetime: "+test.Name+": "+e);}}
  Console.WriteLine($"Gather landing lifetime scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked subtree/owners, real TreeSharp, controlled native leaves; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Gather landing lifetime regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
