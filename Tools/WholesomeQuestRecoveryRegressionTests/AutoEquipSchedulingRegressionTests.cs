using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class AutoEquipSchedulingRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "CopilotBuddy.csproj"))) path = path.Parent;
        string root = path?.FullName ?? throw new InvalidOperationException("Checkout required");
        var owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "AutoEquip"
                && c.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ValueText == "Pulse"));
        string members = string.Join("\n", owner.Members.Where(m =>
            m is MethodDeclarationSyntax method && method.Identifier.ValueText is "Initialize" or "Dispose" or "Pulse" or "RequestItemCheck"
            || m is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => v.Identifier.ValueText is "_isDisposed" or "_itemCheckTimer" or "_itemCheckRequested")));
        string temp = Path.Combine(Path.GetTempPath(), "cb-equipment-scheduling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp,"Probe.cs"), Prefix + "public class AutoEquip:HBPlugin {" + members + Leaves + "}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temp })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("EquipScheduleCases",true)!.GetMethod("Run")!.Invoke(null,null); }
            catch(TargetInvocationException e) when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally { Directory.Delete(temp,true); }
    }
    private const string Prefix="""
#nullable disable
using System;using System.Linq;using System.Collections.Generic;
public class HBPlugin {public virtual void Initialize(){}public virtual void Dispose(){}public virtual void Pulse(){}}
public class WaitTimer {public static WaitTimer TenSeconds=>new();public bool IsFinished=true;public void Reset(){IsFinished=false;State.TimerResets++;}}
public class Actor {public bool IsMoving,IsCasting,IsChanneling;}
public static class ObjectManager {public static Actor Me=new();}
public static class StyxWoW {public static Actor Me=>ObjectManager.Me;}
public enum WeaponStyle{None,TwoHanded}
public class AutoEquipSettings {public static AutoEquipSettings Instance=new();public WeaponStyle WeaponStyle=WeaponStyle.TwoHanded;}
public class LuaEventArgs{}
public static class Lua {public static class Events {public static Dictionary<string,Action<object,LuaEventArgs>> Handlers=new();public static void AttachEvent(string name,Action<object,LuaEventArgs> action)=>Handlers[name]=action;public static void DetachEvent(string name,Action<object,LuaEventArgs> action){if(Handlers.TryGetValue(name,out var old)&&old==action)Handlers.Remove(name);}}}
public static class State {public static int Checks,Ammo,Pending,TimerResets;public static void Reset(){Checks=Ammo=Pending=TimerResets=0;ObjectManager.Me=new();Lua.Events.Handlers.Clear();}}
""";
    private const string Leaves="""
 public bool HasPendingEquip;
 private void TickPendingEquip(){State.Pending++;}
 private void CheckAndEquipAmmo(){State.Ammo++;}
 private void DoCheck(object sender,LuaEventArgs args){State.Checks++;}
 private void HandleLootRoll(object sender,LuaEventArgs args){}
 private void HandleConfirmLootRoll(object sender,LuaEventArgs args){}
 private void ResetPendingEquip(){HasPendingEquip=false;}
 private void Log(string message){}private void LogDebug(string message){}
""";
    private const string Cases="""
public static class EquipScheduleCases {
 static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 public static void Run(){int total=0,passed=0;
  void Case(string name,Action<AutoEquip> body){total++;State.Reset();var owner=new AutoEquip();try{body(owner);passed++;Console.WriteLine("PASS equipment scheduling: "+name);}catch(Exception e){Console.Error.WriteLine("FAIL equipment scheduling: "+name+": "+e.Message);}}
  foreach(string activity in new[]{"moving","casting","channeling"})Case("new scans defer during "+activity,owner=>{ObjectManager.Me.IsMoving=activity=="moving";ObjectManager.Me.IsCasting=activity=="casting";ObjectManager.Me.IsChanneling=activity=="channeling";owner.Pulse();Check(State.Checks==0&&State.Ammo==0&&State.TimerResets==0,"optional scan interrupted activity or postponed retry");ObjectManager.Me=new();owner.Pulse();Check(State.Checks==1&&State.Ammo==1,"stationary owner did not immediately recover its due scan");});
  Case("pending transaction is still observed while moving",owner=>{owner.HasPendingEquip=true;ObjectManager.Me.IsMoving=true;owner.Pulse();Check(State.Pending==1&&State.Checks==0,"activity gate stranded a pending transaction");});
  foreach(string eventName in new[]{"UNIT_INVENTORY_CHANGED","LOOT_CLOSED"})Case("event queues rather than scans/"+eventName,owner=>{owner.Initialize();ObjectManager.Me.IsMoving=true;Lua.Events.Handlers[eventName](null,null);Check(State.Checks==0,"client event performed synchronous inventory scan");owner.Pulse();Check(State.Checks==0,"queued scan ignored motion");ObjectManager.Me.IsMoving=false;owner.Pulse();Check(State.Checks==1,"queued event lost its later scan");owner.Dispose();Check(Lua.Events.Handlers.Count==0,"dispose retained event callbacks");});
  Case("disposed owner cannot scan",owner=>{owner.Dispose();owner.Pulse();Check(State.Checks==0&&State.Ammo==0,"disposed scan ran");});
  Console.WriteLine($"Equipment scan scheduling: {passed}/{total}; complete production pulse and event lifecycle; only expensive scan/clock/client leaves controlled.");if(passed!=total)throw new InvalidOperationException("equipment scheduling failures");
 }
}
""";
}
