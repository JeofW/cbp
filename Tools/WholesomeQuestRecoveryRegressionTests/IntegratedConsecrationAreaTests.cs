using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Connect Ret's actual selector with actual AoE and hostility enumeration. The
// engagement and reaction reads are explicit native leaves with a query budget.
internal static class IntegratedConsecrationAreaTests
{
    internal static void Run()
    {
        const string unitPath = "runtime-snapshot/Routines/Singular wotlk/Helpers/Unit.cs";
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(IntegratedRegressionFixture.Root, unitPath))).GetRoot();
        string properties = string.Join("\n", syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(property => new[] { "NearbyUnfriendlyUnits", "IsDungeonCombatBotTargetingRestricted" }.Contains(property.Identifier.ValueText)));
        string methods = IntegratedRegressionFixture.Methods(unitPath, "ValidUnit", "IsEligibleDungeonCombatTarget",
            "IsAreaEffectSafe", "HasUnengagedEnemyNear", "IsBasicHostileUnit", "UnfriendlyUnitsWithin");
        string select = IntegratedRegressionFixture.Methods("runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Retribution.cs", "SelectConsecration");
        IntegratedRegressionFixture.Run("Consecration area", Boundary
            + "public static class Unit {public static HashSet<uint> IgnoreMobs=new();" + properties + methods + "}\n"
            + "public static class Ret {public static string Select()=>SelectConsecration();" + select + "}\n" + Scenarios,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/DungeonEngagementPolicy.cs");
    }

    private const string Boundary = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using Singular.Helpers;using Styx.Helpers;
public readonly record struct WoWPoint(float X,float Y,float Z){public float DistanceSqr(WoWPoint other)=>(X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y)+(Z-other.Z)*(Z-other.Z);}
public sealed class WoWUnit {
 public ulong Guid;public uint Entry;public bool IsValid=true,IsAlive=true,Mounted,IsOnTransport,IsMoving,IsCasting,IsChanneling,CanSelect=true,Attackable=true,IsPet,IsNonCombatPet,IsCritter,Boss,Dummy,Engaged=true,Friendly,UnknownReaction;
 public double ManaPercent=100;public bool Dead=>!IsAlive;public WoWPoint Location;public WoWUnit CurrentTarget,OwnedByRoot;
 public bool IsMe=>ReferenceEquals(this,StyxWoW.Me);public float DistanceSqr=>Location.DistanceSqr(StyxWoW.Me.Location);public float Distance=>MathF.Sqrt(DistanceSqr);
 public bool IsFriendly {get{Cases.ReactionReads++;if(UnknownReaction)throw new ObservationUnavailableException("reaction","out-of-area participant unavailable");return Friendly;}}
 public bool IsBoss()=>Boss;public bool IsTrainingDummy()=>Dummy;
}
public static class StyxWoW {public static WoWUnit Me;}
public static class ObjectManager {public static List<WoWUnit> Units=new();public static IEnumerable<T> GetObjectsOfType<T>(bool a,bool b)=>Units.Cast<T>();}
public static class GroupCombatSafety {public static bool IsRestricted;public static bool MayAttack(WoWUnit unit){Cases.EngagementReads++;return !IsRestricted||unit.Engaged;}}
public static class Spell {public const float MeleeRange=5;}
public static class SpellManager {public static bool Learned=true;public static bool HasSpell(string name)=>Learned;}
public sealed class SingularSettings {public static SingularSettings Instance=new();public PaladinSettings Paladin=new();}
public sealed class PaladinSettings {public int DivinePleaMana=30,ConsecrationCount=3;}
""";
    private const string Scenarios = """
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 public static int ReactionReads,EngagementReads;
 private static void Reset(bool instance){ReactionReads=EngagementReads=0;GroupCombatSafety.IsRestricted=instance;SpellManager.Learned=true;SingularSettings.Instance=new();StyxWoW.Me=new(){Guid=1};
  var target=new WoWUnit{Guid=2,Location=new(5,0,0)};StyxWoW.Me.CurrentTarget=target;ObjectManager.Units=new(){target,new(){Guid=3,Location=new(4,1,0)},new(){Guid=4,Location=new(4,-1,0)}};}
 private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
 private static void Selected(){try{Check(Ret.Select()=="Consecration","safe in-range pack was rejected");}catch(ObservationUnavailableException e){throw new Failure("irrelevant observation suppressed Consecration: "+e.Message);}}
 public static void Run(){int total=0,passed=0,failures=0,errors=0;
  void Case(bool instance,string name,System.Action test){total++;Reset(instance);try{test();passed++;}catch(Failure e){failures++;Console.Error.WriteLine($"FAIL Consecration area {(instance?"instance":"world")}: {name}: {e.Message}");}catch(Exception e){errors++;Console.Error.WriteLine("ERROR Consecration area: "+name+": "+e);}}
  foreach(bool instance in new[]{false,true}){
   Case(instance,"three engaged targets admit",Selected);
   Case(instance,"far unknown participants cannot affect eight-yard coverage",()=>{for(int i=0;i<100;i++)ObjectManager.Units.Add(new(){Guid=(ulong)(100+i),Location=new(50+i,0,0),UnknownReaction=true});Selected();Check(ReactionReads<=9,"far participants consumed hostility queries");});
   Case(instance,"unknown at twelve yards is outside the effect",()=>{ObjectManager.Units.Add(new(){Guid=5,Location=new(12,0,0),UnknownReaction=true});Selected();});
   Case(instance,"unengaged mob beyond player radius but near selected target is irrelevant",()=>{ObjectManager.Units.Add(new(){Guid=5,Location=new(12,0,0),Engaged=false});Selected();});
   Case(instance,"relevant unknown remains unavailable",()=>{ObjectManager.Units[1].UnknownReaction=true;bool unknown=false;try{Ret.Select();}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"nearby UNKNOWN authorized ground damage");});
   Case(instance,"zero damage mana budget",()=>{StyxWoW.Me.ManaPercent=30;Check(Ret.Select()==null&&ReactionReads==0,"denied resource budget queried or selected AoE");});
   Case(instance,"unlearned ground spell",()=>{SpellManager.Learned=false;Check(Ret.Select()==null&&ReactionReads==0,"unlearned spell queried area");});
   Case(instance,"two ordinary targets retain threshold",()=>{ObjectManager.Units.RemoveAt(2);Check(Ret.Select()==null,"count threshold weakened");});
   Case(instance,"target moving out of the patch",()=>{StyxWoW.Me.CurrentTarget.IsMoving=true;Check(Ret.Select()==null,"moving target admitted speculative patch");});
   Case(instance,"fresh threat loss revokes a previous selection",()=>{Selected();ObjectManager.Units[1].Engaged=false;Check(!instance||Ret.Select()==null,"new unengaged target failed to revoke area permission");});
  }
  Case(true,"unengaged enemy inside radius vetoes",()=>{ObjectManager.Units.Add(new(){Guid=5,Location=new(7,0,0),Engaged=false});Check(Ret.Select()==null,"unsafe extra pack was admitted");});
  Case(true,"exact radius boundary retains safety",()=>{ObjectManager.Units.Add(new(){Guid=5,Location=new(8,0,0),Engaged=false});Check(Ret.Select()==null,"radius edge excluded an unsafe enemy");});
  Console.WriteLine($"Integrated Consecration area: {passed}/{total}; assertions={failures}; unexpected={errors}; actual Ret selector and AoE readers; controlled original-client reaction/engagement leaves.");
  if(failures+errors!=0)throw new InvalidOperationException("Consecration area regressions");
 }
}
""";
}
