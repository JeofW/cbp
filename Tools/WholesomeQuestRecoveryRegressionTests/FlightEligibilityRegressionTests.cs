using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked flight eligibility/speed and player skill lookup. Only the
// descriptor, companion, spellbook and client-area observations are controlled.
internal static class FlightEligibilityRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"CopilotBuddy.csproj"))) root=root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source required.");
        var flight=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"Styx/Logic/Pathing/Flightor.cs"))).GetRoot();
        var properties=flight.DescendantNodes().OfType<ClassDeclarationSyntax>().First(x=>x.Identifier.ValueText=="Flightor")
            .Members.OfType<PropertyDeclarationSyntax>().Where(x=>x.Identifier.ValueText is "CanFly" or "FlySpeedMultiplier").ToArray();
        if(properties.Length!=2)throw new InvalidOperationException("Complete flight owners required.");
        var skill=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"Styx/WoWInternals/WoWObjects/LocalPlayer.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(x=>x.Identifier.ValueText=="GetSkill"
                && x.ParameterList.Parameters.Count==1 && x.ParameterList.Parameters[0].Type!.ToString()=="uint");
        var source=Prefix+"\npublic class LocalPlayer:WoWUnit{public const int MaxSkills=128;public WoWSkill?[] Skills=new WoWSkill?[128];\n"+
            "public WoWSkill? GetSkillByIndex(uint i)=>Skills[i];public WoWSkill? GetSkill(SkillLine line)=>GetSkill((uint)line);\n"+skill+"}\n"+
            "public static class Flightor {"+string.Join("\n",properties.Select(x=>x.ToString()))+
            "public static float Speed()=>FlySpeedMultiplier;}\n"+Cases;
        var trusted=(string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")??throw new InvalidOperationException("References missing.");
        var compilation=CSharpCompilation.Create("FlightAdmission_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(source)},
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(x=>MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(x=>x.Severity==DiagnosticSeverity.Error)));
        try{Assembly.Load(output.ToArray()).GetType("FlightCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private const string Prefix="""
#nullable enable
using System;using System.Collections.Generic;
public enum WoWClass{Paladin,Druid}public enum SkillLine:uint{Riding=762}
public sealed class WoWSkill{public bool IsValid=true;public int Id=762,SkillLineId=762,CurrentValue=225;}
public sealed class Movement{public bool IsFlying;}
public class WoWUnit{public bool IsMe=true,IsValid=true,IsAlive=true,IsGhost,InVehicle,IsSwimming,IsOutdoors=true,OnTaxi,IsOnTransport;public ulong Guid=1;public uint MapId=530;public int Level=60;public WoWClass Class;public Movement MovementInfo=new();}
public static class StyxWoW{public static LocalPlayer Me=new();}public static class ObjectManager{public static object? Wow=new();}
public static class WoWMovement{public static WoWUnit? ActiveMover;}
public static class SpellManager{public static HashSet<string> Known=new();public static bool HasSpell(string name)=>Known.Contains(name);}
public static class MountHelper{public static object? FlyingMount=new();}
public static class Lua{public static bool Flyable=true;public static Action? During;public static T GetReturnVal<T>(string code,uint index){During?.Invoke();return (T)(object)Flyable;}}
""";
    private const string Cases="""
public static class FlightCases{
 static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
 static void Reset(){StyxWoW.Me=new();StyxWoW.Me.Skills[0]=new();WoWMovement.ActiveMover=StyxWoW.Me;ObjectManager.Wow=new();SpellManager.Known.Clear();MountHelper.FlyingMount=new();Lua.Flyable=true;Lua.During=null;}
 public static void Run(){int passed=0;var errors=new List<string>();
 void Case(string name,Action test){Reset();try{test();passed++;Console.WriteLine("PASS flight eligibility: "+name);}catch(Exception e){errors.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL flight eligibility: "+errors[^1]);}}
 Case("trained skill225 needs no riding spellbook entry",()=>Check(Flightor.CanFly,"valid Outland flight rejected"));
 Case("trained skill300 needs no riding spellbook entry",()=>{StyxWoW.Me.Skills[0]!.CurrentValue=300;Check(Flightor.CanFly,"artisan flight rejected");});
 Case("descriptor skill identity survives unavailable localized metadata",()=>{StyxWoW.Me.Skills[0]!.Id=0;Check(Flightor.CanFly,"localized skill name became a capability prerequisite");});
 Case("skill150 cannot use a known companion as flight authority",()=>{StyxWoW.Me.Skills[0]!.CurrentValue=150;Check(!Flightor.CanFly,"insufficient riding admitted");});
 Case("learning spell name cannot replace missing skill225",()=>{StyxWoW.Me.Skills[0]!.CurrentValue=150;SpellManager.Known.Add("Expert Riding");Check(!Flightor.CanFly,"learning spell bypassed riding requirement");});
 Case("Outland nonflyable subzone falls back",()=>{Lua.Flyable=false;Check(!Flightor.CanFly,"client area restriction ignored");});
 foreach(uint map in new uint[]{0,1,30,489}){uint m=map;Case("nonflyable map/"+m,()=>{StyxWoW.Me.MapId=m;Check(!Flightor.CanFly,"old-world or battleground flight admitted");});}
 Case("Northrend needs Cold Weather Flying",()=>{StyxWoW.Me.MapId=571;Check(!Flightor.CanFly,"cold-weather requirement missing");});
 Case("Northrend trained control",()=>{StyxWoW.Me.MapId=571;SpellManager.Known.Add("Cold Weather Flying");Check(Flightor.CanFly,"trained Northrend flight rejected");});
 foreach(string flag in new[]{"indoors","swimming","vehicle","taxi","transport","dead","ghost"}){string f=flag;Case(f,()=>{var p=StyxWoW.Me;switch(f){case "indoors":p.IsOutdoors=false;break;case "swimming":p.IsSwimming=true;break;case "vehicle":p.InVehicle=true;break;case "taxi":p.OnTaxi=true;break;case "transport":p.IsOnTransport=true;break;case "dead":p.IsAlive=false;break;case "ghost":p.IsGhost=true;break;}SpellManager.Known.Add("Expert Riding");Check(!Flightor.CanFly,"invalid mount environment admitted");});}
 Case("missing flying companion",()=>{MountHelper.FlyingMount=null;Check(!Flightor.CanFly,"missing flight provider admitted");});
 Case("already airborne keeps aerial recovery even outside mount eligibility",()=>{StyxWoW.Me.MovementInfo.IsFlying=true;StyxWoW.Me.MapId=0;MountHelper.FlyingMount=null;Check(Flightor.CanFly,"airborne recovery was forced into ground navigation");});
 Case("vehicle cannot use airborne fast path",()=>{StyxWoW.Me.MovementInfo.IsFlying=true;StyxWoW.Me.InVehicle=true;Check(!Flightor.CanFly,"vehicle was mistaken for the player");});
 Case("druid trained form provider",()=>{StyxWoW.Me.Class=WoWClass.Druid;StyxWoW.Me.Level=58;StyxWoW.Me.Skills[0]=null;SpellManager.Known.Add("Flight Form");Check(Flightor.CanFly,"druid flight provider rejected");});
 Case("actor replacement during area observation",()=>{SpellManager.Known.Add("Expert Riding");Lua.During=()=>StyxWoW.Me=new();Check(!Flightor.CanFly,"area reply authorized another actor");});
 Case("map replacement during area observation",()=>{SpellManager.Known.Add("Expert Riding");Lua.During=()=>StyxWoW.Me.MapId=0;Check(!Flightor.CanFly,"area reply authorized another map");});
 Case("normal flight speed225",()=>Check(Math.Abs(Flightor.Speed()-2.5f)<0.001f,"150-percent flying multiplier missing"));
 Case("epic flight speed300",()=>{StyxWoW.Me.Skills[0]!.CurrentValue=300;Check(Math.Abs(Flightor.Speed()-3.8f)<0.001f,"280-percent flying multiplier missing");});
 Case("post-WotLK Master Riding is not capability evidence",()=>{StyxWoW.Me.Skills[0]!.CurrentValue=150;SpellManager.Known.Add("Master Riding");Check(Flightor.Speed()==0&&!Flightor.CanFly,"later-expansion skill inferred flight");});
 Console.WriteLine($"Flight eligibility scenarios: {passed}/{passed+errors.Count}; tracked property/skill owners; external observations controlled.");
 if(errors.Count!=0)throw new InvalidOperationException(string.Join("; ",errors));}
}
""";
}
