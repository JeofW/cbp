using System.Runtime.CompilerServices;

internal static class IntegratedRacialAdmissionTests
{
    internal static void Run()
    {
        string factory = IntegratedRegressionFixture.Methods("runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Generic.cs", "CreateRacialBehaviour");
        IntegratedRegressionFixture.Run("Racial admission", Boundary + "public static class Generic {" + factory + "}\n" + Scenarios);
    }
    private const string Boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using TreeSharp;using Styx.Helpers;using Styx.Logic.Combat;using Action=TreeSharp.Action;
public sealed class Aura {public SpellInfo Spell=new();}
public sealed class SpellInfo {public WoWSpellMechanic Mechanic;public WoWDispelType DispelType;}
public sealed class WoWPartyMember {public enum GroupRole{Tank,Damage}public ulong Guid;public GroupRole Role;}
public sealed class WoWUnit {
 public ulong Guid=1,CurrentTargetGuid;public bool IsInCombat=true,IsInParty=true,IsInRaid;public double HealthPercent=100;
 public List<WoWPartyMember> PartyMemberInfos=new();
 public IEnumerable<Aura> GetAllAuras(){Cases.AuraReads++;if(Cases.Unknown)throw new ObservationUnavailableException("auras","unavailable harmful metadata");return Cases.Controlled?new[]{new Aura{Spell=new(){Mechanic=WoWSpellMechanic.Bleeding}}}:Array.Empty<Aura>();}
 public bool HasAuraWithMechanic(params WoWSpellMechanic[] mechanics){Cases.AuraReads++;if(Cases.Unknown)throw new ObservationUnavailableException("auras","unavailable harmful metadata");return Cases.Controlled;}
 public bool HasHarmfulAuraWithMechanic(params WoWSpellMechanic[] mechanics)=>HasAuraWithMechanic(mechanics);
}
public static class StyxWoW {public static WoWUnit Me=new();}
public static class ObjectManager {public static IEnumerable<T> GetObjectsOfType<T>(bool a,bool b){Cases.WorldReads++;return Array.Empty<T>();}}
public static class Unit {public static bool HasAuraWithMechanic(WoWUnit unit,params WoWSpellMechanic[] mechanics)=>unit.HasAuraWithMechanic(mechanics);public static bool HasHarmfulAuraWithMechanic(WoWUnit unit,params WoWSpellMechanic[] mechanics)=>unit.HasHarmfulAuraWithMechanic(mechanics);}
public static class PVP {public static bool IsCrowdControlled(WoWUnit unit)=>unit.HasAuraWithMechanic();}
public static class SpellManager {public static HashSet<string> Known=new();public static bool HasSpell(string name)=>Known.Contains(name);}
public static class Spell {
 public static Composite BuffSelf(string name,Func<object,bool> required)=>Cast(name,_=>StyxWoW.Me,required);
 public static Composite Cast(string name,Func<object,WoWUnit> select,Func<object,bool> required)=>new Action(context=>{
  try {if(!required(context)||!SpellManager.HasSpell(name))return RunStatus.Failure;Cases.Selected=name;return RunStatus.Success;}
  catch(ObservationUnavailableException){Cases.UnknownBranches++;return RunStatus.Failure;}
 });
}
public sealed class SingularSettings {public static SingularSettings Instance=new();public bool UseRacials=true,ShadowmeldThreatDrop=true;public int GiftNaaruHP=35;}
""";
    private const string Scenarios = """
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 public static int AuraReads,WorldReads,UnknownBranches;public static bool Unknown,Controlled;public static string Selected;
 private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
 private static void Tick(){var root=Generic.CreateRacialBehaviour();root.Start(null);try{root.Tick(null);}finally{root.Stop(null);}}
 public static void Run(){int total=0,passed=0,failures=0,errors=0;
  void Case(string name,System.Action test){total++;AuraReads=WorldReads=UnknownBranches=0;Unknown=Controlled=false;Selected=null;SpellManager.Known.Clear();StyxWoW.Me=new();SingularSettings.Instance=new();try{test();passed++;}catch(Failure e){failures++;Console.Error.WriteLine("FAIL racial admission: "+name+": "+e.Message);}catch(Exception e){errors++;Console.Error.WriteLine("ERROR racial admission: "+name+": "+e);}}
  Case("Blood Elf does not evaluate unlearned racial aura queries",()=>{Unknown=true;Tick();Check(AuraReads==0&&WorldReads==0&&UnknownBranches==0&&Selected==null,"unlearned racial queried unavailable aura/world state");});
  foreach(string name in new[]{"Every Man for Himself","Will of the Forsaken","Escape Artist","Stoneform"}){
   Case(name+" known control",()=>{SpellManager.Known.Add(name);Controlled=true;Tick();Check(Selected==name&&AuraReads==1,"learned racial did not use only its relevant control query");});
   Case(name+" relevant UNKNOWN is not absence",()=>{SpellManager.Known.Add(name);Unknown=true;Tick();Check(Selected==null&&UnknownBranches==1&&AuraReads==1,"relevant missing mechanics were ignored or unlearned racials also queried them");});
  }
  foreach(string name in new[]{"Blood Fury","Berserking"})Case(name+" remains available after irrelevant aura UNKNOWN",()=>{SpellManager.Known.Add(name);Unknown=true;Tick();Check(Selected==name&&AuraReads==0,"irrelevant racial reads delayed the available damage racial");});
  Case("user disable avoids all work",()=>{SingularSettings.Instance.UseRacials=false;Unknown=true;Tick();Check(AuraReads==0&&WorldReads==0&&Selected==null,"disabled racials still evaluated state");});
  Console.WriteLine($"Integrated racial admission: {passed}/{total}; assertions={failures}; unexpected={errors}; actual racial factory and requirement-first cast boundary.");
  if(failures+errors!=0)throw new InvalidOperationException("Racial admission regressions");
 }
}
""";
}
