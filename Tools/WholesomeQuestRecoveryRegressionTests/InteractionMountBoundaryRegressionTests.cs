using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Executes the complete tracked pre-interaction stage through actual submission.
// The legacy baseline is exactly its first three Sequence children; later frame,
// merchant, gossip and quest-credit behavior is deliberately outside this fixture.
// Additional cases execute the real final target-cleanup action after submission.
// Real TreeSharp/coroutine bridge; controlled observation and native/shared leaves.
internal static class InteractionMountBoundaryRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Tracked checkout required.");
        var owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "runtime-snapshot/Quest Behaviors/InteractWith.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "InteractWith");
        var factory = owner.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "CreateBehavior");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is
            "CreatePreInteractionBehavior" or "PrepareInteraction" or "SubmitInteraction" or "CanContinueInteraction" or "HasInteractionActors" or "ClearSubmittedInteractionTarget").ToArray();
        var types = owner.Members.OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.ValueText == "InteractionRequest");
        var fields = owner.Members.OfType<FieldDeclarationSyntax>().Where(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "_submittedInteraction"));
        bool hasCleanup = methods.Any(m => m.Identifier.ValueText == "ClearSubmittedInteractionTarget");
        var cleanupAction = factory.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single(n => n.Type.ToString() == "Action"
            && n.ArgumentList?.Arguments.Count == 1 && n.ArgumentList.Arguments[0].ToString().Contains(
                hasCleanup ? "ClearSubmittedInteractionTarget()" : ".ClearTarget()", StringComparison.Ordinal));
        // Preserve the real enclosing admission predicate in the legacy branch.
        // Executing only its Action would falsely bypass KeepTargetSelected.
        var cleanup = hasCleanup ? cleanupAction : cleanupAction.Ancestors().OfType<ObjectCreationExpressionSyntax>()
            .First(n => n.Type.ToString() == "DecoratorContinue");
        string body;
        if (methods.Any(m => m.Identifier.ValueText == "CreatePreInteractionBehavior"))
        {
            if (factory.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(i => i.Expression.ToString() == "CreatePreInteractionBehavior") != 1)
                throw new InvalidOperationException("Pre-interaction helper must remain wired into the real factory exactly once.");
            body = "public Composite Build()=>CreatePreInteractionBehavior();\n" + string.Join("\n", methods.Select(m => m.ToString())) +
                "\n" + string.Join("\n", types.Select(t => t.ToString())) + "\n" + string.Join("\n", fields.Select(f => f.ToString()));
        }
        else
        {
            var sequence = factory.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single(n => n.Type.ToString() == "Sequence"
                && n.ArgumentList?.Arguments.Count > 3 && n.ArgumentList.Arguments[1].ToString().Contains("PreInteractMountStrategy", StringComparison.Ordinal)
                && n.ArgumentList.Arguments[2].ToString().Contains("Counter++", StringComparison.Ordinal));
            var prefix = sequence.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(sequence.ArgumentList!.Arguments.Take(3))));
            body = "public Composite Build()=>" + prefix + ";";
        }
        string enumSource = owner.Members.OfType<EnumDeclarationSyntax>().Single(e => e.Identifier.ValueText == "MountStrategyType").ToString();
        string source = Prefix + "\npublic sealed class InteractionOwner {\n" + enumSource + "\n" +
            "public MountStrategyType PreInteractMountStrategy; public bool KeepTargetSelected=true,IgnoreLoSToTarget; public double Range=4;\n" +
            "public int InteractByUsingItemId,Counter;private LocalPlayer Me=>World.Player;private WoWObject CurrentObject=>World.Target;\n" +
            "private readonly List<ulong> _npcBlacklist=new List<ulong>();public int Blacklisted=>_npcBlacklist.Count;public bool BlacklistedGuid(ulong guid)=>_npcBlacklist.Contains(guid);\n" +
            "private void LogMessage(string level,string message,params object[] args){}\n" + body +
            "\npublic Composite BuildCompletion()=>" + cleanup + ";\n}\n" + Cases;
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
        var compilation = CSharpCompilation.Create("W110InteractionMount_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Append(typeof(TreeSharp.Composite).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream(); var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("InteractionMountCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;
using TreeSharp;using CommonBehaviors.Actions;using Action=TreeSharp.Action;
public enum ShapeshiftForm {Normal,Cat,FlightForm,EpicFlightForm}
public enum WoWObjectType {Unit,GameObject}
public struct Point {public double X,Y,Z;public double DistanceSqr(Point other)=>(X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y)+(Z-other.Z)*(Z-other.Z);}
public class WoWObject {
 public ulong Guid=321;public bool IsValid=true,InLineOfSight=true;public Point Location;public string Name="controlled target";public WoWObjectType Type=WoWObjectType.Unit;
 public WoWUnit ToUnit()=>this as WoWUnit;
 public void Interact(){World.Record("interact",Guid);World.AfterDispatch?.Invoke();}
}
public sealed class WoWUnit:WoWObject {
 public void Target(){World.Record("target",Guid);if(World.TargetAck){World.Player.CurrentTargetGuid=Guid;World.Player.CurrentTarget=this;}World.AfterTarget?.Invoke();}
 public bool TryInteract(){if(!World.InteractReceipt)return false;Interact();return true;}
}
public sealed class WoWItem {public uint Entry=99;public ulong Guid=654;public bool IsValid=true;public void Use(ulong guid){World.Record("use",guid);World.AfterDispatch?.Invoke();}}
public sealed class LocalPlayer {
 public ulong Guid=123,CurrentTargetGuid;public bool IsValid=true,IsAlive=true,Mounted=true,IsMoving;public ShapeshiftForm Shapeshift;public Point Location;
 public WoWUnit CurrentTarget;public void ClearTarget(){World.Record("clear-target",CurrentTargetGuid);CurrentTarget=null;CurrentTargetGuid=0;}
 public List<WoWItem> CarriedItems=new List<WoWItem>{new WoWItem()};
}
public static class World {
 public static LocalPlayer Player;public static WoWObject Target;public static readonly List<string> Commands=new List<string>();
 public static bool LandResult=true,LandRemoves=true,ClearRemoves=true,MountWorks=true,StopWorks=true,RawRemoves=true,InteractReceipt=true,TargetAck=true;
 public static System.Action AfterStatus,AfterLand,AfterClear,AfterMount,AfterStop,AfterTarget,AfterDispatch,BeforeWait,AfterSleep;
 public static void Reset(){Player=new LocalPlayer();Target=new WoWUnit();Commands.Clear();LandResult=true;LandRemoves=true;ClearRemoves=true;MountWorks=true;
  StopWorks=true;RawRemoves=true;InteractReceipt=true;TargetAck=true;AfterStatus=null;AfterLand=null;AfterClear=null;AfterMount=null;AfterStop=null;
  AfterTarget=null;AfterDispatch=null;BeforeWait=null;AfterSleep=null;}
 public static void Record(string command,ulong target=0)=>Commands.Add(command+":"+(Player?.Guid??0)+":"+target);
}
public static class StyxWoW {public static LocalPlayer Me=>World.Player;public static void SleepForLagDuration(){World.AfterStop?.Invoke();}}
public static class TreeRoot {public static string StatusText {set{World.AfterStatus?.Invoke();}}}
public static class Thread {public static void Sleep(int milliseconds){if(milliseconds==2000)World.AfterSleep?.Invoke();}}
public static class Lua {public static void DoString(string code){World.Record("raw-remove");if(World.RawRemoves)World.Player.Mounted=false;}}
public static class WoWMovement {public static void MoveStop(){World.Record("stop");if(World.StopWorks)World.Player.IsMoving=false;}}
public static class Mount {
 public static void ClearShapeshift(){World.Record("clear");if(World.ClearRemoves)World.Player.Shapeshift=ShapeshiftForm.Normal;World.AfterClear?.Invoke();}
 public static void MountUp(){World.Record("mount");if(World.MountWorks)World.Player.Mounted=true;World.AfterMount?.Invoke();}
}
public static class CommonCoroutines {
 public static Task<bool> StopMoving(string reason){World.Record("stop");if(World.StopWorks)World.Player.IsMoving=false;World.AfterStop?.Invoke();return Task.FromResult(World.StopWorks);}
 public static Task<bool> LandAndDismount(string reason){World.Record("land");if(World.LandResult&&World.LandRemoves){World.Player.Mounted=false;World.Player.Shapeshift=ShapeshiftForm.Normal;}
  World.AfterLand?.Invoke();return Task.FromResult(World.LandResult);}
}
public static class Coroutine {public static Task<bool> Wait(int milliseconds,Func<bool> condition){World.BeforeWait?.Invoke();return Task.FromResult(condition());}}
""";

    private const string Cases = """
public static class InteractionMountCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static bool Submitted=>World.Commands.Any(c=>c.StartsWith("interact:")||c.StartsWith("use:"));
 private static bool Run(InteractionOwner owner){var root=owner.Build();root.Start(null);try{return root.Tick(null)==RunStatus.Success;}
  catch(NullReferenceException){throw new Failure("missing actor/target was dereferenced");}finally{root.Stop(null);}}
 private static InteractionOwner Owner(InteractionOwner.MountStrategyType mode)=>new InteractionOwner{PreInteractMountStrategy=mode};
 private static void Change(string state){switch(state){
  case "missing-player":World.Player=null;break;case "dead":World.Player.IsAlive=false;break;case "invalid-player":World.Player.IsValid=false;break;
  case "unknown-player":World.Player.Guid=0;break;case "changed-player":World.Player=new LocalPlayer{Guid=789};break;case "changed-player-guid":World.Player.Guid++;break;
  case "missing-target":World.Target=null;break;case "invalid-target":World.Target.IsValid=false;break;case "unknown-target":World.Target.Guid=0;break;
  case "changed-target":World.Target=new WoWUnit{Guid=987};break;case "changed-target-guid":World.Target.Guid++;break;
  case "out-of-range":World.Target.Location=new Point{Z=5};break;case "no-los":World.Target.InLineOfSight=false;break;
 }}
 public static void Run(){
  var tests=new List<(string Name,System.Action Body)>();
  foreach(var selected in new[]{InteractionOwner.MountStrategyType.None,InteractionOwner.MountStrategyType.Dismount,InteractionOwner.MountStrategyType.CancelShapeshift,
   InteractionOwner.MountStrategyType.DismountOrCancelShapeshift,InteractionOwner.MountStrategyType.Mount}){
   var mode=selected;
   foreach(string start in new[]{"mounted","normal","cat","flight"}){
    string state=start;tests.Add((mode+"/"+state,()=>{
     World.Reset();World.Player.Mounted=state=="mounted";World.Player.Shapeshift=state=="cat"?ShapeshiftForm.Cat:state=="flight"?ShapeshiftForm.FlightForm:ShapeshiftForm.Normal;
     var owner=Owner(mode);Check(Run(owner)&&owner.Counter==1&&Submitted,"valid mount preparation did not submit exactly one attempt");
     bool shouldLand=(mode is InteractionOwner.MountStrategyType.Dismount or InteractionOwner.MountStrategyType.DismountOrCancelShapeshift)&&(state is "mounted" or "flight")
      || mode==InteractionOwner.MountStrategyType.CancelShapeshift&&state=="flight";
     bool shouldClear=(mode is InteractionOwner.MountStrategyType.CancelShapeshift or InteractionOwner.MountStrategyType.DismountOrCancelShapeshift)&&state=="cat";
     bool shouldMount=mode==InteractionOwner.MountStrategyType.Mount&&(state is "normal" or "cat");
     Check(World.Commands.Count(c=>c.StartsWith("land:"))==(shouldLand?1:0)&&World.Commands.Count(c=>c.StartsWith("clear:"))==(shouldClear?1:0)
      &&World.Commands.Count(c=>c.StartsWith("mount:"))==(shouldMount?1:0)&&!World.Commands.Any(c=>c.StartsWith("raw-remove:")),"configured mount choice used the wrong removal/mount route");
    }));
   }
  }
  foreach(string state in new[]{"missing-player","dead","invalid-player","unknown-player","missing-target","invalid-target","unknown-target","out-of-range","no-los"}){
   string observed=state;tests.Add(("admission/"+state,()=>{World.Reset();Change(observed);var owner=Owner(InteractionOwner.MountStrategyType.Dismount);
    Check(!Run(owner)&&!Submitted&&owner.Counter==0&&World.Commands.Count==0,"unproven admission issued movement/removal/submission");}));
  }
  foreach(string phase in new[]{"stop","land","clear","mount","status","target"}){
   string boundary=phase;foreach(string state in new[]{"changed-player","changed-player-guid","changed-target","changed-target-guid","out-of-range","no-los"}){
    string observed=state;tests.Add((boundary+"/"+state,()=>{
     World.Reset();var owner=Owner(boundary=="clear"?InteractionOwner.MountStrategyType.CancelShapeshift:boundary=="mount"?InteractionOwner.MountStrategyType.Mount:InteractionOwner.MountStrategyType.Dismount);
     if(boundary=="stop"){World.Player.IsMoving=true;World.AfterStop=()=>Change(observed);}
     if(boundary=="land")World.AfterLand=()=>Change(observed);
     if(boundary=="clear"){World.Player.Mounted=false;World.Player.Shapeshift=ShapeshiftForm.Cat;World.AfterClear=()=>Change(observed);}
     if(boundary=="mount"){World.Player.Mounted=false;World.AfterMount=()=>Change(observed);}
     if(boundary=="status")World.AfterStatus=()=>Change(observed);if(boundary=="target")World.AfterTarget=()=>Change(observed);
     Check(!Run(owner)&&!Submitted&&owner.Counter==0&&owner.Blacklisted==0,"changed preparation owner or reachability retained submission permission");
    }));
   }
  }
  foreach(string state in new[]{"landing-refused","landing-no-ack","clear-no-ack","mount-no-ack","stop-timeout","selection-unacknowledged","interact-refused","item-missing","item-invalid","item-guid-zero","unknown-mode"}){
   string observed=state;tests.Add(("failure/"+state,()=>{
    World.Reset();var owner=Owner(InteractionOwner.MountStrategyType.Dismount);
    switch(observed){case "landing-refused":World.LandResult=false;World.RawRemoves=false;break;
     case "landing-no-ack":World.LandRemoves=false;World.RawRemoves=false;break;
     case "clear-no-ack":owner.PreInteractMountStrategy=InteractionOwner.MountStrategyType.CancelShapeshift;World.Player.Mounted=false;World.Player.Shapeshift=ShapeshiftForm.Cat;World.ClearRemoves=false;break;
     case "mount-no-ack":owner.PreInteractMountStrategy=InteractionOwner.MountStrategyType.Mount;World.Player.Mounted=false;World.MountWorks=false;break;
     case "stop-timeout":World.Player.IsMoving=true;World.StopWorks=false;break;
     case "selection-unacknowledged":World.TargetAck=false;break;
     case "interact-refused":World.InteractReceipt=false;break;
     case "item-missing":owner.InteractByUsingItemId=99;World.Player.CarriedItems.Clear();break;
     case "item-invalid":owner.InteractByUsingItemId=99;World.Player.CarriedItems[0].IsValid=false;break;
     case "item-guid-zero":owner.InteractByUsingItemId=99;World.Player.CarriedItems[0].Guid=0;break;
     case "unknown-mode":owner.PreInteractMountStrategy=(InteractionOwner.MountStrategyType)999;break;}
    Check(!Run(owner)&&!Submitted&&owner.Counter==0&&owner.Blacklisted==0,"failed readiness/selection/local receipt consumed an interaction attempt");
   }));
  }
  tests.Add(("item uses captured target",()=>{World.Reset();var owner=Owner(InteractionOwner.MountStrategyType.None);owner.InteractByUsingItemId=99;
   Check(Run(owner)&&owner.Counter==1&&World.Commands.Contains("use:123:321")&&!World.Commands.Any(c=>c.StartsWith("interact:")),"item route or captured recipient changed");}));
  tests.Add(("game object normal interaction",()=>{World.Reset();World.Target=new WoWObject{Type=WoWObjectType.GameObject};var owner=Owner(InteractionOwner.MountStrategyType.None);
   Check(Run(owner)&&Submitted&&owner.Counter==1&&!World.Commands.Any(c=>c.StartsWith("target:")),"game-object dispatch incorrectly required unit targeting");}));
  tests.Add(("explicit LOS override",()=>{World.Reset();World.Target.InLineOfSight=false;var owner=Owner(InteractionOwner.MountStrategyType.None);owner.IgnoreLoSToTarget=true;
   Check(Run(owner)&&Submitted,"explicit LOS override was ignored");}));
  tests.Add(("no target-selection request",()=>{World.Reset();World.TargetAck=false;var owner=Owner(InteractionOwner.MountStrategyType.None);owner.KeepTargetSelected=false;
   Check(Run(owner)&&Submitted&&!World.Commands.Any(c=>c.StartsWith("target:")),"optional selection was forced");}));
  tests.Add(("post-dispatch target change cannot blacklist its successor",()=>{World.Reset();World.AfterDispatch=()=>Change("changed-target");var owner=Owner(InteractionOwner.MountStrategyType.None);
   Check(Run(owner)&&owner.Counter==1&&owner.Blacklisted==1&&owner.BlacklistedGuid(321)&&!owner.BlacklistedGuid(987),"owned submission did not retain its attempted recipient");}));
  foreach(string next in new[]{"same","changed-nearest","missing-nearest"}){string observation=next;
   tests.Add(("cleanup of actual submitted target/"+observation,()=>{
    World.Reset();var original=(WoWUnit)World.Target;World.Player.CurrentTarget=original;World.Player.CurrentTargetGuid=original.Guid;
    var owner=Owner(InteractionOwner.MountStrategyType.None);owner.KeepTargetSelected=false;Check(Run(owner),"control submission failed");
    if(observation=="changed-nearest")World.Target=new WoWUnit{Guid=987};if(observation=="missing-nearest")World.Target=null;
    Complete(owner);Check(World.Commands.Count(c=>c.StartsWith("clear-target:"))==1&&World.Player.CurrentTargetGuid==0,"cleanup did not release its actual submitted target");
   }));
  }
  foreach(string state in new[]{"successor-target","replacement-player","replacement-player-same-guid","changed-player-guid","dead-player","invalid-player","invalid-target","changed-target-guid","replacement-target-same-guid","manual-keep","changed-mode"}){
   string observed=state;tests.Add(("cleanup rejects "+observed,()=>{
    World.Reset();var original=(WoWUnit)World.Target;World.Player.CurrentTarget=original;World.Player.CurrentTargetGuid=original.Guid;
    var owner=Owner(InteractionOwner.MountStrategyType.None);owner.KeepTargetSelected=false;Check(Run(owner),"control submission failed");
    switch(observed){
     case "successor-target":World.Target=new WoWUnit{Guid=987};World.Player.CurrentTarget=(WoWUnit)World.Target;World.Player.CurrentTargetGuid=987;break;
     case "replacement-player":World.Player=new LocalPlayer{Guid=789,CurrentTarget=original,CurrentTargetGuid=original.Guid};break;
     case "replacement-player-same-guid":World.Player=new LocalPlayer{Guid=123,CurrentTarget=original,CurrentTargetGuid=original.Guid};break;
     case "changed-player-guid":World.Player.Guid++;break;
     case "dead-player":World.Player.IsAlive=false;break;
     case "invalid-player":World.Player.IsValid=false;break;
     case "invalid-target":original.IsValid=false;break;
     case "changed-target-guid":original.Guid++;World.Player.CurrentTargetGuid=original.Guid;break;
     case "replacement-target-same-guid":World.Target=new WoWUnit{Guid=321};World.Player.CurrentTarget=(WoWUnit)World.Target;break;
     case "manual-keep":owner.KeepTargetSelected=true;break;
     case "changed-mode":owner.PreInteractMountStrategy=InteractionOwner.MountStrategyType.Dismount;break;
    }
    Complete(owner);Check(!World.Commands.Any(c=>c.StartsWith("clear-target:")),"cleanup cleared a successor or unowned target");
   }));
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS interaction mount: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL interaction mount: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR interaction mount: "+test.Name+": "+error);}}
  Console.WriteLine($"Interaction mount scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked preparation/submission stage, controlled native/frame leaves; no game or quest-credit acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Interaction mount regressions: assertions={assertions}; unexpected={unexpected}");
 }
 private static void Complete(InteractionOwner owner){var root=owner.BuildCompletion();root.Start(null);try{root.Tick(null);}finally{root.Stop(null);}}
}
""";
}
