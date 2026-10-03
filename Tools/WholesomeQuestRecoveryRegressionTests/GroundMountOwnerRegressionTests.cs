using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete managed mount owner, request lifetime and actual economics, joined to
// the exact submission script under stock Lua5.1. Only actor/catalog/DBC/native
// transport observations are controlled. A request is never observed mounting.
internal static class GroundMountOwnerRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) directory = directory.Parent;
        string root = directory?.FullName ?? throw new InvalidOperationException("Tracked checkout required");
        string temp = Path.Combine(Path.GetTempPath(), "cb-full-ground-mount-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (string name in new[] { "GroundTravelMount", "GroundMountRequest" })
                File.Copy(Path.Combine(root, "Styx/Logic/Pathing/" + name + ".cs"), Path.Combine(temp, name + ".cs"));
            var speed = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/Pathing/TravelTimeEstimator.cs")))
                .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "MountedSpeed");
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), Boundary + "\nnamespace Styx.Logic.Pathing { internal " + "static class TravelTimeEstimator { private const double BaseTravelSpeed=7; " + speed + "}}\n" + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            using var lua = new RewardLua51Boundary.StockLua51(root);
            RewardLua51Boundary.StockLua51.Session? session = null;
            var driver = assembly.GetType("MountOwnerCases", true)!;
            driver.GetField("ResetClient")!.SetValue(null, new Action(() => { session?.Dispose(); session = lua.BeginSession(Client); }));
            driver.GetField("ClientQuery")!.SetValue(null, new Func<string, string[]>(code =>
            {
                var response = session!.Execute(code, (uint)Encoding.UTF8.GetByteCount(code));
                if (response.Load != 0 || response.Call != 0) throw new InvalidOperationException("Original companion API fixture: " + response.Error);
                return response.Values.ToArray();
            }));
            try { driver.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            finally { session?.Dispose(); }
        }
        finally { Directory.Delete(temp, true); }
    }

    private const string Boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using Styx.Logic.Combat;using Styx.Logic.Pathing;
/* Controlled external boundary. */ namespace Styx.Helpers {
 public sealed class ObservationUnavailableException(string owner,string reason):InvalidOperationException(owner+":"+reason){}
 public class CharacterSettings {public static CharacterSettings Instance=new();public string MountName="Black Hawkstrider";public bool UseMount=true,FindMountAutomatically=true;public float MountDistance=20;}
 public static class Logging {public static void Write(string text,params object[] values){}public static void WriteDiagnostic(string text,params object[] values){MountOwnerCases.Diagnostics.Add(string.Format(text,values));}}
}
/* Controlled external boundary. */ namespace Styx.Logic.Combat {
 public class SpellEffect {public int EffectType=6,AuraType=32,BasePoints=59,DieSides=1;public float RealPointsPerLevel;}
 public class WoWSpell {public int Id=35022;public string Name="Black Hawkstrider";public bool IsValid=true,Metadata=true;public SpellEffect[] SpellEffects={new()};
  public bool TryGetCurrentSpellInfo(out uint cast,out bool funnel,out float min,out float max){cast=1500;funnel=false;min=max=0;return Metadata;}}
 public static class RecoveryActions {public static void ReportDeferral(Exception e,string owner){}public static void RethrowControlFlow(Exception e){if(e is OperationCanceledException)throw e;}}
}
/* Controlled external boundary. */ namespace Styx.Logic {
 public static class Mount {public static bool AreMountTimersReady=true,Allowed=true;public static bool CanMount()=>Allowed;public static bool AllowMountAttempt(bool flying,string name,WoWPoint destination)=>Allowed;public static void ResetMountTimer(){AreMountTimersReady=false;}}
 public static class MountHelper {public sealed class MountWrapper {public string Name="Black Hawkstrider";public int CreatureSpellId=35022;public WoWSpell CreatureSpell=new();}public static List<MountWrapper> GroundMounts=new(){new()};}
}
/* Controlled external boundary. */ namespace Styx.WoWInternals {public static class Lua {public static Action Before;public static List<string> GetObservedReturnValues(string code,Func<bool> admitted){var before=Before;Before=null;before?.Invoke();return admitted()?MountOwnerCases.ClientQuery(code).ToList():new();}}}
/* Controlled external boundary. */ namespace Styx.WoWInternals.World {
 public readonly record struct State(bool Mounted,bool OnTaxi,bool Rooted,bool Stunned);
 public static class WorldQueryObservation {public static State ReadGroundUnitState(Actor actor)=>new(actor.Mounted,actor.OnTaxi,actor.Rooted,actor.Stunned);public static bool ReadLocalVehicle(Actor actor)=>actor.Vehicle;}
}
/* Controlled external boundary. */ namespace Styx.Logic.Pathing {
 public readonly record struct WoWPoint(float X,float Y,float Z){public float Distance(WoWPoint p)=>MathF.Sqrt((X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z));}
 public class Movement {public double RunSpeed=8.05;}
 public class Actor {public bool Combat,IsSwimming,OnTaxi,IsOnTransport,IsCasting,IsMoving,Mounted,Vehicle,Rooted,Stunned;public bool IsOutdoors=true;public int ChanneledCastingSpellId;public uint Flags;public ulong Transport;public WoWPoint Location=new(100,10,0);public Movement MovementInfo=new();public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=Transport;return true;}}
 internal class GroundTransitionContext {public Actor Actor=new();public bool Current=true;public WoWPoint Destination=new(700,10,0);public ulong ActorGuid=1;public double? RemainingGroundTravelDistance;}
}
""";
    private const string Cases = """
public static class MountOwnerCases {
 public static Action ResetClient;public static Func<string,string[]> ClientQuery;
 public static List<string> Diagnostics=new();
 static void Check(bool good,string why){if(!good)throw new InvalidOperationException(why);}
 static int Calls()=>int.Parse(ClientQuery("return clicks")[0]);
 public static void Run(){int passed=0,total=0;var failures=new List<string>();
  void Case(string name,Action<GroundTravelMount,GroundTransitionContext> body){total++;ResetClient();Diagnostics.Clear();Styx.Helpers.CharacterSettings.Instance=new();Styx.Logic.Mount.AreMountTimersReady=Styx.Logic.Mount.Allowed=true;Styx.Logic.MountHelper.GroundMounts=new(){new()};Styx.WoWInternals.Lua.Before=null;try{body(new(),new());passed++;Console.WriteLine("PASS full ground mount: "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL full ground mount: "+failures.Last());}}
  Case("owned companion need not appear in ordinary spellbook",(owner,context)=>{Check(owner.Wait(context,0,()=>true,()=>{}),"original companion was rejected by ordinary-spell readiness");Check(Calls()==1&&!context.Actor.Mounted,"one request did not remain separate from mounting");});
  Case("stop preparation continues to one later submission",(owner,context)=>{int stops=0;context.Actor.IsMoving=true;Check(owner.Wait(context,0,()=>true,()=>stops++),"preparation was not retained");Check(stops==1&&Calls()==0,"moving cast submitted");context.Actor.IsMoving=false;Check(owner.Wait(context,.2,()=>true,()=>stops++)&&Calls()==1,"stopped owner never submitted");Check(owner.Wait(context,.4,()=>true,()=>stops++)&&Calls()==1,"pending request repeated");context.Actor.Mounted=true;Check(!owner.Wait(context,.6,()=>true,()=>stops++)&&Calls()==1,"observed mount remained blocked");});
  Case("known companion cooldown rejects and does not spam",(owner,context)=>{ClientQuery("duration=10;start=now");Check(!owner.Wait(context,0,()=>true,()=>{}),"cooldown acquired pending ownership");for(int i=1;i<20;i++)owner.Wait(context,i*.1,()=>true,()=>{});Check(Calls()==0,"cooldown mount reached CallCompanion");});
  Case("unavailable metadata cannot authorize a mount",(owner,context)=>{Styx.Logic.MountHelper.GroundMounts[0].CreatureSpell.Metadata=false;Check(!owner.Wait(context,0,()=>true,()=>{})&&Calls()==0,"unknown cost became ready");});
  Case("walking denial is explained without per-tick diagnostic spam",(owner,context)=>{Styx.Logic.MountHelper.GroundMounts[0].CreatureSpell.Metadata=false;for(int i=0;i<50;i++)owner.Wait(context,i*.1,()=>true,()=>{});Check(Calls()==0&&Diagnostics.Count==1&&Diagnostics[0].Contains("metadata"),"silent mounting denial or per-tick diagnostic spam");owner.Wait(context,6,()=>true,()=>{});Check(Diagnostics.Count==2,"later unchanged denial lost bounded diagnostics");});
  Case("short walk remains unmounted",(owner,context)=>{context.Destination=new(120,10,0);Check(!owner.Wait(context,0,()=>true,()=>{})&&Calls()==0,"short leg spent mount cast time");});
  Case("short through-point uses the remaining owned journey for mount economics",(owner,context)=>{context.Destination=new(120,10,0);context.RemainingGroundTravelDistance=600;Check(owner.Wait(context,0,()=>true,()=>{})&&Calls()==1,"long patrol was treated as an independent twenty-yard walk");});
  Case("current owner is rechecked at submission boundary",(owner,context)=>{Styx.WoWInternals.Lua.Before=()=>context.Current=false;Check(!owner.Wait(context,0,()=>true,()=>{})&&Calls()==0,"revoked owner issued mount");});
  Case("client vehicle remains a veto",(owner,context)=>{ClientQuery("vehicle=true");Check(!owner.Wait(context,0,()=>true,()=>{})&&Calls()==0,"vehicle state authorized mount");});
  Case("unobserved mount request has a bounded wait",(owner,context)=>{Check(owner.Wait(context,0,()=>true,()=>{}),"control did not submit");Check(!owner.Wait(context,9,()=>true,()=>{})&&Calls()==1,"unobserved mount waited indefinitely or repeated");});
  Console.WriteLine($"Full ground mount owner: {passed}/{total}; complete managed owner/lifetime/economics plus actual script in stock Lua5.1; controlled original companion API, no game action.");if(failures.Count!=0)throw new InvalidOperationException(string.Join("; ",failures));
 }
}
""";
    private const string Client = """
now=100;clicks=0;start=0;duration=0;enabled=1;vehicle=false
function UnitGUID() return '0x0000000000000001' end
function IsMounted() return nil end
function UnitAffectingCombat() return nil end
function IsFlying() return nil end
function IsFalling() return nil end
function IsSwimming() return nil end
function IsOutdoors() return 1 end
function UnitOnTaxi() return nil end
function UnitInVehicle() return vehicle end
function UnitCastingInfo() return nil end
function UnitChannelInfo() return nil end
function GetUnitSpeed() return 0 end
function GetTime() return now end
function GetNumCompanions(kind) assert(kind=='MOUNT');return 1 end
function GetCompanionInfo(kind,slot) assert(kind=='MOUNT' and slot==1);return 20222,'Black Hawkstrider',35022,'icon',nil end
-- Build12340 541680/540670 resolve the ordinary spellbook, not the companion list.
function IsUsableSpell(name) return nil,nil end
function GetSpellCooldown(name) return nil end
-- Build12340 53E490 uses exactly the selected companion type and slot.
function GetCompanionCooldown(kind,slot) assert(kind=='MOUNT' and slot==1);return start,duration,enabled end
function CallCompanion(kind,slot) assert(kind=='MOUNT' and slot==1);clicks=clicks+1 end
""";
}
