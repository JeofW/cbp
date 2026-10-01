using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual Singular Pulse with external actor/manager observations controlled at
// the leaves. Shared exception classification and Logging are the real host.
internal static class SingularPulseObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source is required");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,
            "runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs"))).GetRoot();
        string pulse = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method.Identifier.ValueText == "Pulse").ToString();
        string folder = Path.Combine(Path.GetTempPath(), "cb-singular-pulse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Probe.cs"), Prefix + pulse + Suffix);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            var compiler = Activator.CreateInstance(compilerType, new object[] { folder })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { typeof(Styx.StyxWoW).Assembly.Location });
            var result = (CompilerResults)compilerType.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual Singular Pulse compilation: " +
                string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("Cases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally
        {
            string full = Path.GetFullPath(folder);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("cb-singular-pulse-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected owned fixture cleanup path");
            Directory.Delete(full, true);
        }
    }
    private const string Prefix = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Threading;
using Styx.Helpers;
public abstract class Routine{public abstract void Pulse();}
public class Probe:Routine {
 private ulong _lastTargetGuid;
 public static WoWContext CurrentWoWContext=WoWContext.Normal;
 public static Actor Me=>StyxWoW.Me;
""";
    private const string Suffix = """
}
public enum WoWContext{Normal,Battlegrounds}
public enum WoWClass{Hunter,DeathKnight,Warlock,Mage,Paladin}
public class Actor {
 public ulong CurrentTargetGuid;public Actor CurrentTarget;public bool IsValid=true,IsInParty=true,IsInRaid;public WoWClass Class=WoWClass.Hunter;
 public double HealthPercent{get{Cases.Observe("target");return 100;}}public int Level=>60;
}
public static class StyxWoW{public static Actor Me=new Actor();}
public static class Spell{public static double MeleeRange=>5;public static Dictionary<string,DateTime> DoubleCastPreventionDict=new();}
public static class Extensions{public static void RemoveAll(this Dictionary<string,DateTime> values,Func<DateTime,bool> filter){foreach(var key in values.Where(pair=>filter(pair.Value)).Select(pair=>pair.Key).ToArray())values.Remove(key);}}
public static class PetManager{public static void Pulse(){Cases.Observe("pet");Cases.Pets++;}}
public class HealerManager{public static bool NeedHealTargeting=true;public static HealerManager Instance=new();public void Pulse(){Cases.Observe("heal");Cases.Heals++;}}
public class TankManager{public static TankManager Instance=new();public void Pulse(){Cases.Observe("tank");Cases.Tanks++;}}
public static class Group{public static bool MeIsTank=>true;}
public static class Logger{public static void Write(string message)=>Logging.Write(message);public static void WriteDebug(string message)=>Logging.WriteDebug(message);}
public static class Cases{
 public static int Pets,Heals,Tanks;public static string FaultOwner;public static Exception Fault;
 private static readonly List<string> Messages=new();
 public static void Observe(string owner){if(FaultOwner==owner)throw Fault;}
 private static void Reset(){FaultOwner=null;Fault=null;Pets=Heals=Tanks=0;StyxWoW.Me=new Actor{CurrentTarget=new Actor()};Messages.Clear();Spell.DoubleCastPreventionDict.Clear();}
 private static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 public static void Run(){
  bool fileLogging=Logging.FileLogging;Logging.FileLogging=false;
  Logging.LogMessageDelegate capture=batch=>{foreach(var line in batch)Messages.Add(line.Message);};Logging.OnLogMessage+=capture;
  int passed=0,failed=0;
  try{
   var cases=new List<(string,Action)>();
   cases.Add(("healthy pulse reaches all configured managers",()=>{Reset();new Probe().Pulse();Check(Pets==1&&Heals==1&&Tanks==1,"healthy managers were not reached");}));
   foreach(string owner in new[]{"target","pet","heal","tank"})foreach(bool wrapped in new[]{false,true}){
    string stage=owner;bool reflection=wrapped;
    cases.Add(($"{stage} unknown is bounded wrapped={reflection}",()=>{Reset();var probe=new Probe();var signal=new ObservationUnavailableException("auras","active aura 61988 unavailable at "+stage+" wrapped="+reflection);FaultOwner=stage;Fault=reflection?new TargetInvocationException(signal):signal;
     for(int i=0;i<500;i++){if(stage=="target")StyxWoW.Me.CurrentTargetGuid=(ulong)i+1;probe.Pulse();}
     int count=Messages.Count(message=>message.Contains("active aura 61988"));Check(count>0&&count<20,$"unknown observation generated {count} redundant exception records");
     FaultOwner=null;probe.Pulse();Check(Pets>0&&Heals>0&&Tanks>0,"known later pulse did not recover");
    }));
    foreach(bool interrupted in new[]{false,true}){
     bool stop=interrupted;cases.Add(($"{stage} cancellation propagates wrapped={reflection} interruption={stop}",()=>{Reset();if(stage=="target")StyxWoW.Me.CurrentTargetGuid=1;Exception signal=stop?new ThreadInterruptedException("controlled Singular Stop"):new OperationCanceledException("controlled Singular cancellation");FaultOwner=stage;Fault=reflection?new TargetInvocationException(signal):signal;Exception observed=null;try{new Probe().Pulse();}catch(Exception error){observed=error;}Check(ReferenceEquals(observed,signal),"Singular swallowed or replaced the original cancellation");Check(Tanks==0,"cancellation allowed later manager side effects");}));
    }
   }
   cases.Add(("ordinary optional failure remains nonfatal",()=>{Reset();FaultOwner="pet";Fault=new InvalidOperationException("controlled optional manager failure");new Probe().Pulse();FaultOwner=null;new Probe().Pulse();Check(Pets==1&&Heals==1&&Tanks==1,"optional error prevented later pulse recovery");}));
   foreach(var test in cases){try{test.Item2();passed++;}catch(Exception error){failed++;Console.Error.WriteLine("FAIL Singular pulse observation: "+test.Item1+": "+error.Message);}}
   Console.WriteLine($"Singular pulse observations: {passed}/{cases.Count}; failed={failed}; actual Pulse/shared diagnostics; controlled actor/manager leaves; no game attached.");
  }finally{Logging.OnLogMessage-=capture;Logging.FileLogging=fileLogging;}
  if(failed!=0)throw new InvalidOperationException("Singular pulse observation regressions");
 }
}
""";
}
