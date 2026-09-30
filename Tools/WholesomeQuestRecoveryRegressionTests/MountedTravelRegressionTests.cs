using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class MountedTravelRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"CopilotBuddy.csproj")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Tracked source required.");
        var owner=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"Styx/Logic/Mount.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(x=>x.Identifier.ValueText=="ShouldDismount");
        string policyPath=Path.Combine(root.FullName,"Styx/Logic/MountedTravelProgress.cs");
        string policy=File.Exists(policyPath)?CSharpSyntaxTree.ParseText(File.ReadAllText(policyPath)).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(x=>x.Identifier.ValueText=="MountedTravelProgress").ToString()
            :"internal sealed class MountedTravelProgress{public void Reset(){}}";
        string source=Prefix+policy+"\npublic static class Mount {private static LocalPlayer? Me=>ObjectManager.Me;"+
            "private static readonly MountedTravelProgress _mountedTravelProgress=new();public static void Reset()=>_mountedTravelProgress.Reset();\n"+
            owner+"}\n"+Cases;
        var trusted=(string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")??throw new InvalidOperationException("References missing.");
        var compilation=CSharpCompilation.Create("MountedTravel_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(source)},
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(x=>MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(x=>x.Severity==DiagnosticSeverity.Error)));
        try{Assembly.Load(output.ToArray()).GetType("MountedCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private const string Prefix="""
#nullable enable
using System;using System.Collections.Generic;
public static class Environment{public static long TickCount64=1;}
public readonly record struct WoWPoint(float X,float Y,float Z){public static readonly WoWPoint Empty=default,Zero=default;public float Distance(WoWPoint p)=>MathF.Sqrt(DistanceSqr(p));public float DistanceSqr(WoWPoint p)=>(X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z);}
public enum PoiType{None,Quest,QuestPickUp,QuestTurnIn,Hotspot,Kill,Loot,Skin,Harvest,Sell,Buy,Repair,Train,Mail,Fly}
public class WoWUnit{public bool IsValid=true,IsAlive=true;}
public class LocalPlayer:WoWUnit{public bool Mounted=true,Combat=true,IsMoving=true,IsFlying,Rooted,Stunned,IsGhost,IsOnTransport,OnTaxi,InVehicle;public double HealthPercent=100;public ulong Guid=1;public uint MapId=530;public WoWPoint Location=new(1,2,3);}
public static class ObjectManager{public static LocalPlayer? Me=new();public static object? Wow=new();}
public sealed class BotPoi{public static BotPoi Current=new();public PoiType Type=PoiType.QuestTurnIn;public WoWPoint Location=new(201,2,3);}
public sealed class Targeting{public static Targeting Instance=new();public WoWUnit? FirstUnit;}
public sealed class CharacterSettings{public static CharacterSettings Instance=new();public float PullDistance=30;}
public static class Logging{public static Action? After;public static void WriteDebug(string text,params object[] args)=>After?.Invoke();}
/* Controlled target-intent boundary. */ namespace Levelbot.Decorators.Combat{public static class DecoratorNeedToFindTarget{public static bool Requested;public static bool IsRequestedMountedTarget(WoWUnit? unit)=>unit!=null&&Requested;}}
""";
    private const string Cases="""
public static class MountedCases{
 static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
 static bool Observe()=>Mount.ShouldDismount(BotPoi.Current.Location);
 static void Reset(){ObjectManager.Me=new();ObjectManager.Wow=new();BotPoi.Current=new();Targeting.Instance=new();Logging.After=null;Environment.TickCount64=1;Levelbot.Decorators.Combat.DecoratorNeedToFindTarget.Requested=false;Mount.Reset();}
 public static void Run(){int passed=0;var errors=new List<string>();
 void Case(string name,Action test){Reset();try{test();passed++;Console.WriteLine("PASS mounted travel: "+name);}catch(Exception e){errors.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL mounted travel: "+errors[^1]);}}
 Case("brief navigation pause retains mount",()=>{ObjectManager.Me!.IsMoving=false;Check(!Observe(),"one stationary combat tick forced combat");});
 Case("stationary route expires after bounded recovery",()=>{ObjectManager.Me!.IsMoving=false;Observe();Environment.TickCount64+=3100;Check(Observe(),"blocked escape did not hand over to combat");});
 Case("moving flag without positional progress cannot escape forever",()=>{Check(!Observe(),"initial escape rejected");Environment.TickCount64+=3100;Check(Observe(),"moving into a wall kept suppressing combat");});
 Case("healthy actual progress tolerates passing aggro",()=>{for(int i=0;i<8;i++){ObjectManager.Me!.Location=new(1+15*i,2,3);Environment.TickCount64+=1000;Check(!Observe(),"progressing travel voluntarily dismounted");}});
 Case("slow snared but progressing travel remains mounted",()=>{for(int i=0;i<7;i++){ObjectManager.Me!.Location=new(1+3*i,2,3);Environment.TickCount64+=1000;Check(!Observe(),"movement reduction alone forced combat");}});
 Case("movement away from the destination has a bounded escape",()=>{bool stopped=false;for(int i=0;i<8;i++){ObjectManager.Me!.Location=new(1-10*i,2,3);Environment.TickCount64+=1000;stopped|=Observe();}Check(stopped,"movement without route progress suppressed combat indefinitely");});
 Case("rooted ground rider yields immediately",()=>{ObjectManager.Me!.Rooted=true;Check(Observe(),"rooted actor cannot flee");});
 Case("stunned ground rider yields immediately",()=>{ObjectManager.Me!.Stunned=true;Check(Observe(),"stunned actor cannot flee");});
 Case("critical health while moving yields to defenses",()=>{ObjectManager.Me!.HealthPercent=30;Check(Observe(),"critical health did not yield");});
 Case("rapid incoming damage yields before critical health",()=>{Observe();ObjectManager.Me!.HealthPercent=75;ObjectManager.Me.Location=new(16,2,3);Environment.TickCount64+=500;Check(Observe(),"rapidly deteriorating escape was retained");});
 Case("an airborne rider is never dropped by combat admission",()=>{ObjectManager.Me!.IsFlying=true;ObjectManager.Me.IsMoving=false;ObjectManager.Me.Rooted=true;ObjectManager.Me.HealthPercent=20;Check(!Observe(),"combat admission removed a flying mount");});
 Case("forced dismount hands control to the existing ground combat owner",()=>{Observe();ObjectManager.Me!.Mounted=false;Check(!Observe(),"already dismounted actor got a duplicate removal request");});
 Case("no combat does not inherit an escape timeout",()=>{Observe();Environment.TickCount64+=10000;ObjectManager.Me!.Combat=false;Check(!Observe(),"ended combat retained timeout");ObjectManager.Me.Combat=true;Check(!Observe(),"new encounter inherited old grace");});
 Case("explicit reset cannot retain old escape ownership",()=>{Observe();Environment.TickCount64+=10000;Mount.Reset();Check(!Observe(),"stopped run retained authority");});
 Case("actor replacement starts its own progress window",()=>{Observe();Environment.TickCount64+=10000;ObjectManager.Me=new();Check(!Observe(),"replacement actor inherited timeout");});
 Case("map replacement starts its own progress window",()=>{Observe();Environment.TickCount64+=10000;ObjectManager.Me!.MapId=571;Check(!Observe(),"replacement map inherited timeout");});
 Case("a nearby incidental hotspot target is not a pull request",()=>{BotPoi.Current.Type=PoiType.Hotspot;BotPoi.Current.Location=new(80,2,3);Targeting.Instance.FirstUnit=new();Check(!Observe(),"incidental target forced a hotspot dismount");});
 Case("a nearby explicit objective still requests combat",()=>{BotPoi.Current.Type=PoiType.Hotspot;BotPoi.Current.Location=new(20,2,3);Targeting.Instance.FirstUnit=new();Levelbot.Decorators.Combat.DecoratorNeedToFindTarget.Requested=true;Check(Observe(),"explicit objective lost pull authority");});
 Case("explicit nearby Kill POI remains authoritative",()=>{BotPoi.Current.Type=PoiType.Kill;BotPoi.Current.Location=new(20,2,3);Check(Observe(),"explicit kill did not dismount");});
 Case("remote Kill POI retains travel",()=>{BotPoi.Current.Type=PoiType.Kill;Check(!Observe(),"remote kill was pulled early");});
 foreach(PoiType type in new[]{PoiType.Loot,PoiType.Harvest,PoiType.Sell,PoiType.Repair,PoiType.Train,PoiType.Mail}){var t=type;Case("arrival/"+t,()=>{ObjectManager.Me!.Combat=false;BotPoi.Current.Type=t;BotPoi.Current.Location=new(5,2,3);Check(Observe(),"ordinary interaction arrival stopped dismounting");});}
 Case("missing travel destination cannot strand combat",()=>{BotPoi.Current.Location=WoWPoint.Empty;ObjectManager.Me!.IsMoving=false;Check(Observe(),"no escape route suppressed combat");});
 Case("logging replacement revokes the old decision",()=>{ObjectManager.Me!.HealthPercent=20;ObjectManager.Me.IsMoving=false;Logging.After=()=>ObjectManager.Me=new();Check(!Observe(),"old decision transferred dismount to a replacement actor");});
 Case("transport is not a voluntary mount removal target",()=>{ObjectManager.Me!.IsOnTransport=true;ObjectManager.Me.IsMoving=false;Check(!Observe(),"transport actor admitted dismount");});
 Console.WriteLine($"Mounted travel scenarios: {passed}/{passed+errors.Count}; actual dismount and progress owners; synthetic clock and observations.");
 if(errors.Count!=0)throw new InvalidOperationException(string.Join("; ",errors));}
}
""";
}
