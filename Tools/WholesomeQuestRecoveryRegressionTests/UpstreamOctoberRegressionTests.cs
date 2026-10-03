using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Styx.Helpers;

// Execute the changed pointer/tick members with controlled native leaves. The
// LFG cases use the actual loaded manager and WaitTimer, restoring their state.
internal static class UpstreamOctoberRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (IntPtr.Size != 4 || Styx.WoWInternals.ObjectManager.Executor != null)
            throw new InvalidOperationException("Original x86 process without a game executor required");
        var failures = new List<Exception>();
        foreach (var test in new (string Name, Action Run)[] {
            ("x86 addresses", PointerCases), ("frame-lock policy", FrameCases), ("completion lifetime", CompletionCases) })
        {
            try { test.Run(); }
            catch (Exception error) { failures.Add(error); Console.Error.WriteLine("FAIL upstream October " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Upstream October families: {3-failures.Count}/3; controlled pointer/dispatch leaves and actual completion timer; no game attached.");
        if (failures.Count != 0) throw new AggregateException("Upstream October regressions", failures);
    }

    private static MethodDeclarationSyntax[] Methods(string file, string name) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(UpstreamSeptemberRegressionTests.Root(), file)))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == name).ToArray();

    private static void PointerCases()
    {
        string array = Methods("GreenMagic/Memory.cs", "ReadStructArray").Single(m =>
            m.ParameterList.Parameters[0].Type!.ToString() == "uint").ToString();
        string thread = string.Join("\n", Methods("GreenMagic/Memory.cs", "CreateRemoteThread").Select(m => m.ToString()));
        string row = Methods("Styx/WoWInternals/WoWDb.cs", "GetRow").Single().ToString();
        UpstreamSeptemberRegressionTests.Probe("x86 address forwarding", PointerPrefix + array + thread +
            "}\npublic class BoundDb { public uint MinIndex=2,MaxIndex=4; public Header _header=new();\n" + row + PointerSuffix);
    }

    private static void FrameCases()
    {
        string tick = Methods("Styx/Logic/BehaviorTree/TreeRoot.cs", "Tick").Single().ToString();
        string settings = string.Join("\n", new[] { "InitializeDefaultValues", "LoadFromXML", "ConvertValue" }
            .SelectMany(name => Methods("Styx/Helpers/Settings.cs", name)).Where(m => m.TypeParameterList == null).Select(m => m.ToString()));
        UpstreamSeptemberRegressionTests.Probe("complete tick and settings defaults", FramePrefix.Replace("/* ACTUAL_SETTINGS_READERS */", settings) + tick + FrameSuffix,
            "Styx/Helpers/StyxSettings.cs");
    }

    private static void CompletionCases()
    {
        var manager = typeof(Styx.StyxWoW).Assembly.GetType("Bots.DungeonBuddy.LfgManager", true)!;
        var reasonProperty = manager.GetProperty("DungeonCompletedReason")!;
        var timer = (WaitTimer)manager.GetField("ExitDelayTimer")!.GetValue(null)!;
        var set = manager.GetMethod("SetDungeonCompleted")!;
        var startField = typeof(WaitTimer).GetField("_startTime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object beforeReason = reasonProperty.GetValue(null)!;
        TimeSpan beforeWait = timer.WaitTime;
        DateTime beforeStart = timer.StartTime;
        object none = Enum.Parse(reasonProperty.PropertyType, "None"), completed = Enum.Parse(reasonProperty.PropertyType, "Completed");
        object other = Enum.GetValues(reasonProperty.PropertyType).Cast<object>().First(v => !v.Equals(none) && !v.Equals(completed));
        void Set(object reason, int seconds = 10) => set.Invoke(null, new[] { reason, (object)seconds });
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var cases = new (string Name, Action Run)[] {
            ("first completion starts its own deadline", () => { Set(completed); Check(timer.StartTime > DateTime.Now.AddSeconds(-2) && timer.WaitTime.TotalSeconds==10, "first deadline was not started"); }),
            ("duplicate completion preserves elapsed wait", () => { Set(completed); var start=DateTime.Now.AddSeconds(-5); startField.SetValue(timer,start); Set(completed); Check(timer.StartTime==start,"duplicate event restarted the existing deadline"); }),
            ("expired completion stays eligible to exit", () => { Set(completed); startField.SetValue(timer,DateTime.Now.AddMinutes(-1)); Set(completed); Check(timer.IsFinished,"duplicate completion revoked an already-finished exit delay"); }),
            ("repeated pulses cannot starve completion", () => { Set(completed); var start=DateTime.Now.AddMinutes(-1); startField.SetValue(timer,start); for(int i=0;i<100;i++)Set(completed); Check(timer.StartTime==start && timer.IsFinished,"repeated completion ticks postponed exit indefinitely"); }),
            ("changed reason starts a new deadline", () => { Set(completed); startField.SetValue(timer,DateTime.Now.AddMinutes(-1)); Set(other); Check(timer.StartTime>DateTime.Now.AddSeconds(-2) && reasonProperty.GetValue(null)!.Equals(other),"new completion reason inherited the old deadline"); }),
            ("clear then complete starts a new lifetime", () => { Set(completed); startField.SetValue(timer,DateTime.Now.AddMinutes(-1)); Set(none); Set(completed); Check(!timer.IsFinished,"a new completion reused the old expired timer"); }),
            ("same reason retains start when delay changes", () => { Set(completed); var start=DateTime.Now.AddSeconds(-5); startField.SetValue(timer,start); Set(completed,20); Check(timer.StartTime==start && timer.WaitTime.TotalSeconds==20,"delay update restarted the completion owner"); })
        };
        int failed=0;
        try
        {
            foreach(var test in cases)
            {
                reasonProperty.SetValue(null,none); timer.WaitTime=TimeSpan.FromSeconds(10); startField.SetValue(timer,DateTime.Now.AddDays(-1));
                try { test.Run(); Console.WriteLine("PASS upstream completion: "+test.Name); }
                catch(Exception error) { failed++; Console.Error.WriteLine("FAIL upstream completion: "+test.Name+": "+error.Message); }
            }
        }
        finally { reasonProperty.SetValue(null,beforeReason); timer.WaitTime=beforeWait; startField.SetValue(timer,beforeStart); }
        Console.WriteLine($"Upstream completion cases: {cases.Length-failed}/{cases.Length}; actual manager and timer, controlled elapsed starting points.");
        if(failed!=0)throw new InvalidOperationException("Completion failures: "+failed);
    }

    private const string PointerPrefix = """
using System;using System.Collections.Generic;
public class BoundMemory {
 public IntPtr ProcessHandle=new IntPtr(42),Seen;public int Elements;
 public T[] ReadStructArray<T>(IntPtr address,int elements) where T:struct {Seen=address;Elements=elements;return new T[elements];}
""";
    private const string PointerSuffix = """
}
public class Header{public IntPtr RowArrayPtr=new IntPtr(unchecked((int)0x80400000u));}
public class Row{public IntPtr Address;public Row(IntPtr address){Address=address;}}
public static class ObjectManager{public static WorldMemory Wow=new();}
public class WorldMemory{public uint Value,Seen;public T Read<T>(uint address){Seen=address;return (T)(object)Value;}}
public static class Imports {
 public static IntPtr Process,Start,Parameter;public static uint Flags;public static int Calls;
 public static IntPtr CreateRemoteThread(IntPtr process,IntPtr security,uint stack,IntPtr start,IntPtr parameter,uint flags,out IntPtr tid){
  if(security!=IntPtr.Zero||stack!=0)throw new InvalidOperationException("native ABI arguments changed");
  Process=process;Start=start;Parameter=parameter;Flags=flags;Calls++;tid=new IntPtr(unchecked((int)0xF1234567u));return new IntPtr(99);
 }
}
public static class Cases {
 static int total,failed;static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 static uint Bits(IntPtr value)=>unchecked((uint)value.ToInt32());
 static void Test(string name,Action run){total++;try{run();Console.WriteLine("PASS upstream pointer: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL upstream pointer: "+name+": "+e.GetType().Name+" "+e.Message);}}
 public static void Run(){
  if(IntPtr.Size!=4)throw new InvalidOperationException("Tests require actual x86 IntPtr conversion");
  foreach(uint address in new uint[]{0,1,0x1234,0x7fffffff,0x80000000,0x87654321,0xffffff00,0xffffffff}){
   uint value=address;Test("array "+value.ToString("X8"),()=>{var memory=new BoundMemory();var rows=memory.ReadStructArray<int>(value,4);Check(Bits(memory.Seen)==value&&memory.Elements==4&&rows.Length==4,"array pointer/count forwarding changed");});
   Test("DBC row "+value.ToString("X8"),()=>{ObjectManager.Wow=new(){Value=value};var table=new BoundDb();var row=table.GetRow(3);Check(ObjectManager.Wow.Seen==0x80400004u,"row lookup address changed");Check(value==0?row==null:row!=null&&Bits(row.Address)==value,"DBC row pointer bits changed");});
   for(int overload=0;overload<4;overload++){int index=overload;Test("native wrapper "+index+" address "+value.ToString("X8"),()=>{
    var memory=new BoundMemory();uint tid=0,parameter=~value;Imports.Calls=0;IntPtr handle;
    if(index==0)handle=memory.CreateRemoteThread(value,parameter);
    else if(index==1)handle=memory.CreateRemoteThread(new IntPtr(43),value,parameter);
    else if(index==2)handle=memory.CreateRemoteThread(new IntPtr(44),value,parameter,out tid);
    else handle=memory.CreateRemoteThread(new IntPtr(45),value,parameter,4,out tid);
    Check(Imports.Calls==1&&Bits(Imports.Start)==value&&Bits(Imports.Parameter)==parameter,"native start/parameter bits changed or submission missing");
    Check(Imports.Process.ToInt32()==42+index&&Imports.Flags==(index==3?4u:0u)&&handle.ToInt32()==99,"native process/flags/result changed");
    if(index>=2)Check(tid==0xF1234567u,"unsigned returned thread ID changed");
   });}
  }
  foreach(uint id in new uint[]{0,1,5,uint.MaxValue}){uint index=id;Test("invalid row index "+id,()=>{ObjectManager.Wow=new(){Value=0x81234567,Seen=123};Check(new BoundDb().GetRow(index)==null&&ObjectManager.Wow.Seen==123,"out-of-range row submitted a memory read");});}
  Console.WriteLine($"Upstream x86 pointer cases: {total-failed}/{total}; actual conversion methods, controlled native leaves.");
  if(failed!=0)throw new InvalidOperationException("Pointer failures: "+failed);
 }
}
""";

    private const string FramePrefix = """
using System;using System.Collections.Generic;using System.ComponentModel;using System.Diagnostics;using System.Threading;using System.Linq;using System.Reflection;using System.Xml.Linq;
using Styx;using Styx.Helpers;using Styx.WoWInternals;
namespace Styx.Helpers {
 public class SettingAttribute:Attribute{public string Explanation{get;set;}public string ElementName{get;set;}}
 public class Settings {public static string SettingsDirectory="unused-fixture-directory";public static bool? LoadedFrameSetting;
  public Settings(string path){InitializeDefaultValues();if(LoadedFrameSetting is bool value)LoadFromXML(new XElement("StyxSettings",new XElement("UseFrameLock",value)));}
  /* ACTUAL_SETTINGS_READERS */
 }
 public enum LogLevel{Normal}
 public static class Logging{public static LogLevel LoggingLevel;public static void WriteDiagnostic(string text){}public static void WriteDebug(string text,params object[] args){}public static void Write(string text,params object[] args){}}
 public class CharacterSettings{public static CharacterSettings Instance=new();public byte TicksPerSecond=100;}
}
namespace Styx {
 public static class StyxWoW{public static ControlledMemory Memory=new();}
 public class ControlledMemory {
  public int Acquires,Holds,CacheScopes;public IDisposable AcquireFrame(bool hard){Acquires++;Holds++;return new Scope(()=>Holds--);}
  public IDisposable TemporaryCacheState(bool enabled){CacheScopes++;return new Scope(()=>{});}public void ClearCache(){}
 }
 public class Scope:IDisposable{Action action;public Scope(Action action){this.action=action;}public void Dispose(){action();}}
}
namespace Styx.WoWInternals {public static class ObjectManager{public static Executor Executor;}
 public class Executor{public bool IsExecutingContinuously;public object AssemblyLock=new();public int Ends;public void EndExecute(){Ends++;IsExecutingContinuously=false;}}
}
public enum TreeRootState{Stopped,Running,Paused}
public static class TreeRoot {
 public static TreeRootState State=TreeRootState.Running;public static byte TicksPerSecond;
 public static int Bodies,HoldsInside;public static bool Throw;
 private static void RunTickBody(){Bodies++;HoldsInside=StyxWoW.Memory.Holds;if(Throw)throw new InvalidOperationException("controlled body failure");}
 public static void Run()=>Tick();
""";
    private const string FrameSuffix = """
}
public static class Cases {
 static int total,failed;static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 static void Test(string name,Action run){total++;try{run();Console.WriteLine("PASS upstream frame: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL upstream frame: "+name+": "+e.Message);}}
 static void Reset(){StyxWoW.Memory=new();ObjectManager.Executor=null;TreeRoot.Bodies=TreeRoot.HoldsInside=0;TreeRoot.Throw=false;TreeRoot.State=TreeRootState.Running;}
 public static void Run(){
  Test("fresh settings avoid whole-tick hard lock",()=>{Settings.LoadedFrameSetting=null;var settings=new StyxSettings();Check(!settings.UseFrameLock,"fresh default still freezes the render frame across an entire bot tick");});
  Test("default metadata agrees with initial value",()=>{Check(typeof(StyxSettings).GetProperty("UseFrameLock").GetCustomAttributes(typeof(Styx.Helpers.DefaultValueAttribute),false) is object[] values && values.Length==1 && ((Styx.Helpers.DefaultValueAttribute)values[0]).Value.Equals(false),"settings reset metadata still opts in to a whole-tick lock");});
  foreach(bool configured in new[]{false,true}){bool value=configured;Test("saved setting "+value+" remains effective",()=>{Settings.LoadedFrameSetting=value;try{Check(new StyxSettings().UseFrameLock==value,"new default overrode a saved choice");}finally{Settings.LoadedFrameSetting=null;}});}
  Test("default tick permits rendering between independent work",()=>{Reset();Settings.LoadedFrameSetting=null;StyxSettings.Instance.UseFrameLock=new StyxSettings().UseFrameLock;TreeRoot.Run();Check(TreeRoot.Bodies==1&&TreeRoot.HoldsInside==0&&StyxWoW.Memory.Acquires==0,"default tick retained a continuous native execution scope");});
  foreach(bool selected in new[]{false,true}){bool enabled=selected;Test("explicit mode "+enabled+" preserves scope cleanup",()=>{Reset();StyxSettings.Instance.UseFrameLock=enabled;TreeRoot.Run();Check(TreeRoot.Bodies==1&&TreeRoot.HoldsInside==(enabled?1:0)&&StyxWoW.Memory.Acquires==(enabled?1:0)&&StyxWoW.Memory.Holds==0,"explicit mode or scope cleanup changed");});}
  Test("disabled tick starts no work",()=>{Reset();TreeRoot.State=TreeRootState.Stopped;TreeRoot.Run();Check(TreeRoot.Bodies==0&&StyxWoW.Memory.Acquires==0,"stopped tick executed");});
  Test("explicit hard-lock exception releases owned scopes",()=>{Reset();StyxSettings.Instance.UseFrameLock=true;TreeRoot.Throw=true;try{TreeRoot.Run();}catch(InvalidOperationException){}Check(TreeRoot.Bodies==1&&StyxWoW.Memory.Holds==0,"failed tick leaked the global lock");});
  Console.WriteLine($"Upstream frame policy cases: {total-failed}/{total}; complete settings and actual tick, controlled frame scopes.");
  if(failed!=0)throw new InvalidOperationException("Frame failures: "+failed);
 }
}
""";
}
