using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The complete actual default weight method, with already-eligible candidates
// supplied at its input. Optional scores never substitute for safety admission.
internal static class TargetRankingContinuityRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        var type = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"Styx/Logic/Targeting.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="Targeting");
        var method = type.Members.OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="DefaultTargetWeight");
        string text = Prefix+"public class Owner {public sealed class TargetPriority{public WoWUnit Object;public double Score;}public void Weigh(List<TargetPriority> targets)=>DefaultTargetWeight(targets);"+method+"}\n"+Cases;
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Distinct().Select(p=>MetadataReference.CreateFromFile(p));
        var build=CSharpCompilation.Create("ActualWeights_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(text)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var emit=build.Emit(stream);
        if(!emit.Success)throw new InvalidOperationException(string.Join(";",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        try{Assembly.Load(stream.ToArray()).GetType("RankingCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private const string Prefix="""
using System;using System.Linq;using System.Collections.Generic;
public enum WoWUnitReaction{Hostile=1,Neutral=3,Friendly=4}
public readonly record struct WoWPoint(float X,float Y,float Z);
public readonly record struct WorldLine(WoWPoint A,WoWPoint B);
public class WoWUnit{
 public ulong Guid=2,CurrentTargetGuid;public bool Elite,IsPet,CanSelect=true;public float Distance=10,Range=20;public double HealthPercent=100,ManaPercent;public uint MaxMana;
 public Exception RangeError,ReactionError;public int RangeReads;public WoWUnitReaction Reaction=WoWUnitReaction.Friendly;
 public WoWUnitReaction MyReaction{get{if(ReactionError!=null)throw ReactionError;return Reaction;}}
 public float MyAggroRange{get{RangeReads++;if(RangeError!=null)throw RangeError;return Range;}}
 public WoWUnit ToUnit()=>this;public WoWPoint GetTraceLinePos()=>default;
}
public sealed class LocalPlayer:WoWUnit{public bool PetInCombat,Combat,GotTarget;public WoWUnit CurrentTarget;}
public static class StyxWoW{public static LocalPlayer Me=new();public static Memory Memory=new();}
public sealed class Memory{public IDisposable AcquireFrame()=>new Frame();private sealed class Frame:IDisposable{public void Dispose(){}}}
public sealed class Profile{public bool TargetElites;}
public static class ProfileManager{public static Profile CurrentProfile=new();}
public static class Battlegrounds{public static bool IsInsideBattleground;}
public static class Blacklist{public static HashSet<ulong> Entries=new();public static bool Contains(ulong id)=>Entries.Contains(id);}
public enum PoiType{None,Kill}
public sealed class BotPoi{public static BotPoi Current=new();public PoiType Type;public ulong Guid;}
public static class GameWorld{public enum CGWorldFrameHitFlags{HitTestLOS}public static Exception Error;public static void MassTraceLine(WorldLine[] lines,CGWorldFrameHitFlags flags,out bool[] hits){if(Error!=null)throw Error;hits=Enumerable.Repeat(true,lines.Length).ToArray();}}
public sealed class ObservationUnavailableException:Exception{public ObservationUnavailableException(string field,string reason):base(field+":"+reason){}}
public sealed class InvalidProcessException:Exception{}
public sealed class InvalidExecutorException:Exception{}
public static class ObservationFailureDiagnostics{public static int Calls;public static void Report(Exception error,string owner){Calls++;}}
public static class RecoveryActions{public static void RethrowControlFlow(Exception error){if(error is OperationCanceledException or System.Threading.ThreadInterruptedException or InvalidProcessException or InvalidExecutorException)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();}}
""";
    private const string Cases="""
public static class RankingCases{
 private static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
 public static void Run(){int passed=0,total=0;var errors=new List<string>();void Case(string name,Action action){total++;StyxWoW.Me=new();BotPoi.Current=new();Blacklist.Entries.Clear();ProfileManager.CurrentProfile=new();GameWorld.Error=null;ObservationFailureDiagnostics.Calls=0;try{action();passed++;}catch(Exception e){errors.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL target ranking: "+errors.Last());}}
 List<Owner.TargetPriority> Targets(WoWUnit unit)=>new(){new(){Object=unit},new(){Object=new WoWUnit{Guid=3,Distance=40,Range=10}}};
 Case("known aggro bonus retains exact weighting",()=>{var u=new WoWUnit();var list=Targets(u);new Owner().Weigh(list);Check(list[0].Score==255&&list[1].Score==95&&u.RangeReads==1,"known scores changed");});
 Case("unknown optional aura-derived range cannot blank eligible targets",()=>{var list=Targets(new WoWUnit{RangeError=new ObservationUnavailableException("auras","61988 metadata absent")});new Owner().Weigh(list);Check(list.Count==2&&list[0].Score==155&&list[1].Score==95,"unknown optional bonus became eligibility failure or fabricated bonus");});
 Case("later known metadata restores optional score",()=>{var u=new WoWUnit{RangeError=new ObservationUnavailableException("auras","missing")};var list=Targets(u);new Owner().Weigh(list);u.RangeError=null;list=Targets(u);new Owner().Weigh(list);Check(list[0].Score==255,"temporary unknown permanently poisoned ranking");});
 Case("elite rejection remains mandatory",()=>{var list=Targets(new WoWUnit{Elite=true,Distance=40,RangeError=new ObservationUnavailableException("auras","missing")});new Owner().Weigh(list);Check(list.Count==1&&list[0].Object.Guid==3,"optional score bypassed elite filter");});
 Case("blacklist rejection remains mandatory",()=>{Blacklist.Entries.Add(2);var list=Targets(new WoWUnit{Distance=40,RangeError=new ObservationUnavailableException("auras","missing")});new Owner().Weigh(list);Check(list.Count==1&&list[0].Object.Guid==3,"optional score bypassed blacklist");});
 Case("reaction unknown still prevents weighing",()=>{var error=new ObservationUnavailableException("reaction","missing");Exception caught=null;try{new Owner().Weigh(Targets(new WoWUnit{ReactionError=error}));}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,error),"mandatory reaction silently became friendly");});
 Case("collision unknown still prevents weighing",()=>{var error=new ObservationUnavailableException("collision","missing");GameWorld.Error=error;Exception caught=null;try{new Owner().Weigh(Targets(new WoWUnit()));}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,error),"mandatory collision silently became clear");});
 Case("in-combat weighting never needs optional detection range",()=>{StyxWoW.Me.Combat=true;var u=new WoWUnit{RangeError=new ObservationUnavailableException("auras","missing")};var list=Targets(u);new Owner().Weigh(list);Check(u.RangeReads==0&&list[0].Score==80,"combat started optional range query");});
 foreach(Exception signal in new Exception[]{new OperationCanceledException(),new System.Threading.ThreadInterruptedException(),new InvalidProcessException(),new InvalidExecutorException(),new InvalidOperationException("unexpected")}){var expected=signal;Case("control/unexpected errors remain fatal/"+expected.GetType().Name,()=>{Exception caught=null;try{new Owner().Weigh(Targets(new WoWUnit{RangeError=expected}));}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,expected),"nonoptional error swallowed");});}
 Console.WriteLine($"Target ranking continuity: {passed}/{total}; complete actual weighting, controlled already-eligible candidate observations; not proof of safety admission or native aggro radius.");if(errors.Count!=0)throw new InvalidOperationException(string.Join(";",errors));
 }}
""";
}
