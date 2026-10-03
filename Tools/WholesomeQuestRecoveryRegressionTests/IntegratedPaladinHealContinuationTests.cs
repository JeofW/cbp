using System.Runtime.CompilerServices;

// Connect the real Ret/Protection factories and predicates to all actual shared
// Heal overloads and TreeSharp. A real successful start must survive its own
// casting state; the native cast and complete raw aura observations are leaves.
internal static class IntegratedPaladinHealContinuationTests
{
    internal static void Run()
    {
        const string paladin = "runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/";
        string ret = IntegratedRegressionFixture.Methods(paladin + "Retribution.cs",
            "CreateRetributionPaladinHeal", "CanRecover", "CanContinueRecovery", "CanContinueRetributionHeal",
            "CanUseRetributionEmergencyDefense", "GetRetributionHealThreshold");
        string prot = IntegratedRegressionFixture.Methods(paladin + "Protection.cs",
            "CreateProtectionPaladinHeal", "CanRecoverOutOfCombat", "CanContinueRecoveryOutOfCombat",
            "CanUseProtectionEmergencyDefense", "CreateProtectionEmergencyDefenses");
        string heal = IntegratedRegressionFixture.Methods("runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs", "Heal");
        IntegratedRegressionFixture.Run("Paladin heal continuation", Boundary + "\npublic static class Retribution {" + ret
            + "}\npublic static class Protection {" + prot + "}\npublic static class Spell {" + heal + CastBoundary + "}\n" + Scenarios);
    }

    private const string Boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using TreeSharp;using CommonBehaviors.Actions;using Action=TreeSharp.Action;
public delegate WoWUnit UnitSelectionDelegate(object context);
public delegate bool SimpleBooleanDelegate(object context);
public sealed class WoWAura {public int SpellId;public bool IsActive=true;public string Name;}
public class WoWUnit {
 public ulong Guid=1;public bool IsValid=true,IsAlive=true,IsGhost,Mounted,IsOnTransport,IsCasting,IsChanneling,IsMoving,Combat,IsInParty,IsInRaid,RawUnknown,MetadataUnknown;
 public int CastingSpellId,ChanneledCastingSpellId;public double HealthPercent=20,ManaPercent=100;
 public List<WoWAura> Auras=new();
 public IEnumerable<WoWAura> GetRawAuras()=>RawUnknown?throw new Styx.Helpers.ObservationUnavailableException("auras","controlled raw unavailable"):Auras;
 public IEnumerable<WoWAura> GetAllAuras()=>MetadataUnknown?throw new Styx.Helpers.ObservationUnavailableException("auras","controlled missing metadata 61988"):GetRawAuras();
 public bool HasAura(string name)=>GetAllAuras().Any(a=>a.Name==name);
}
public sealed class LocalPlayer:WoWUnit{}
public static class StyxWoW {public static LocalPlayer Me;}
public sealed class WoWSpell {public int Id,CastTime=2500;}
public static class SpellManager {
 public static Dictionary<string,WoWSpell> Spells=new();
 public static void StopCasting(){Cases.Stopped++;StyxWoW.Me.IsCasting=false;}
}
public sealed class PaladinSettings {public int HolyLightHealth=65,FlashOfLightHealth=70,RetributionHealHealth=30,LayOnHandsHealth=15,DivineProtectionHealthProt=20,DivinePleaMana=30;}
public sealed class SingularSettings {public static SingularSettings Instance=new();public PaladinSettings Paladin=new();}
public static class Common {public static Composite CreatePaladinDispelBehavior()=>new Action(_=>RunStatus.Failure);public static bool HasSupportedAura(WoWUnit actor,string name)=>actor.GetRawAuras().Any(a=>a.IsActive&&name=="Divine Protection"&&a.SpellId==498);}
namespace Styx.Logic.Common {public static class Rest {public static bool TryObserveActivity(LocalPlayer actor,out bool food,out bool drink){food=actor.Auras.Any(a=>a.SpellId==433);drink=actor.Auras.Any(a=>a.SpellId==430);return !actor.RawUnknown;}}}
""";
    private const string CastBoundary = """
 private static Composite CastWithRecovery(string name,SimpleBooleanDelegate movement,UnitSelectionDelegate select,SimpleBooleanDelegate requirements,bool heal,bool aura)
 =>new Action(context=>{
  var actor=StyxWoW.Me;
  if(actor==null||actor.IsCasting||actor.IsChanneling||actor.IsMoving||!SpellManager.Spells.TryGetValue(name,out var spell)||!requirements(context))return RunStatus.Failure;
  var recipient=select(context);if(recipient==null||!ReferenceEquals(actor,StyxWoW.Me))return RunStatus.Failure;
  Cases.Submitted++;Cases.LastSpell=name;Cases.LastRecipient=recipient;
  actor.CastingSpellId=spell.Id;actor.IsCasting=spell.CastTime>0;return RunStatus.Success;
 });
 public static Composite Cast(string name,UnitSelectionDelegate select,SimpleBooleanDelegate requires)=>CastWithRecovery(name,_=>true,select,requires,false,false);
 public static Composite BuffSelf(string name,SimpleBooleanDelegate requires)=>CastWithRecovery(name,_=>true,_=>StyxWoW.Me,requires,false,true);
 public static Composite Buff(string name,bool mine,UnitSelectionDelegate select,SimpleBooleanDelegate requires,params string[] equivalents)=>CastWithRecovery(name,_=>true,select,requires,false,true);
""";
    private const string Scenarios = """
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 public static int Submitted,Stopped;public static string LastSpell;public static WoWUnit LastRecipient;
 private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
 public static void Run(){int total=0,passed=0,failures=0,errors=0;
  void Case(string name,System.Action test){total++;Submitted=Stopped=0;LastSpell=null;LastRecipient=null;StyxWoW.Me=new();SingularSettings.Instance=new();SpellManager.Spells.Clear();try{test();passed++;}catch(Failure e){failures++;Console.Error.WriteLine("FAIL integrated Paladin heal: "+name+": "+e.Message);}catch(Exception e){errors++;Console.Error.WriteLine("ERROR integrated Paladin heal: "+name+": "+e);}}
  foreach(bool protection in new[]{false,true})foreach(bool grouped in new[]{false,true})foreach(string name in new[]{"Holy Light","Flash of Light"}){
   Composite Tree()=>protection?Protection.CreateProtectionPaladinHeal():Retribution.CreateRetributionPaladinHeal();
   void Setup(){StyxWoW.Me.IsInParty=grouped;SpellManager.Spells[name]=new(){Id=name=="Holy Light"?27135:27137};}
   string label=$"{(protection?"Protection":"Ret")}/{(grouped?"instance":"world")}/{name}";
   Case(label+" continues its observed cast",()=>{Setup();var tree=Tree();tree.Start(null);try{var status=tree.Tick(null);Check(Submitted==1&&LastSpell==name,"expected one eligible self cast");Check(status==RunStatus.Running&&Stopped==0,"the caller cancelled its own newly observed cast");Check(tree.Tick(null)==RunStatus.Running&&Stopped==0,"the caller cancelled its own continuation");StyxWoW.Me.IsCasting=false;Check(tree.Tick(null)==RunStatus.Success&&Stopped==0,"observed completion did not release wait");}finally{tree.Stop(null);}});
   Case(label+" cancels only when health recovers",()=>{Setup();var tree=Tree();tree.Start(null);try{Check(tree.Tick(null)==RunStatus.Running&&Stopped==0,"cast was not retained");StyxWoW.Me.HealthPercent=100;Check(tree.Tick(null)==RunStatus.Success&&Stopped==1,"no-longer-needed current heal was not cancelled once");}finally{tree.Stop(null);}});
   Case(label+" does not cancel a replacement actor",()=>{Setup();var tree=Tree();tree.Start(null);try{Check(tree.Tick(null)==RunStatus.Running,"cast was not retained");StyxWoW.Me=new(){Guid=2,IsCasting=true,CastingSpellId=27135};tree.Tick(null);Check(Stopped==0&&StyxWoW.Me.IsCasting,"obsolete wait cancelled replacement actor");}finally{tree.Stop(null);}});
   foreach(string state in new[]{"casting","channeling","food","raw-unknown","mounted","transport","dead","ghost"}){
    Case(label+" start denied by "+state,()=>{Setup();var me=StyxWoW.Me;if(state=="casting")me.IsCasting=true;if(state=="channeling")me.IsChanneling=true;if(state=="food")me.Auras.Add(new(){SpellId=433,Name="Food"});if(state=="raw-unknown")me.RawUnknown=true;if(state=="mounted")me.Mounted=true;if(state=="transport")me.IsOnTransport=true;if(state=="dead")me.IsAlive=false;if(state=="ghost")me.IsGhost=true;var tree=Tree();tree.Start(null);try{tree.Tick(null);Check(Submitted==0&&Stopped==0,"unsafe start submitted or cancelled a cast");}finally{tree.Stop(null);}});
   }
   Case(label+" marker metadata is unnecessary for self healing",()=>{Setup();StyxWoW.Me.Auras.Add(new(){SpellId=61988});StyxWoW.Me.MetadataUnknown=true;var tree=Tree();tree.Start(null);try{Check(tree.Tick(null)==RunStatus.Running&&Submitted==1&&Stopped==0,"restriction marker blocked or cancelled ordinary self healing");}finally{tree.Stop(null);}});
  }
  foreach(string context in new[]{"solo","party","raid"})foreach(string name in new[]{"Holy Light","Flash of Light"}){
   void SetupCombat(){StyxWoW.Me.Combat=true;StyxWoW.Me.IsInParty=context=="party";StyxWoW.Me.IsInRaid=context=="raid";SpellManager.Spells[name]=new(){Id=name=="Holy Light"?27135:27137};}
   Case($"Ret/{context}/combat/{name} retains the current heal",()=>{SetupCombat();var tree=Retribution.CreateRetributionPaladinHeal();tree.Start(null);try{Check(tree.Tick(null)==RunStatus.Running&&Submitted==1&&Stopped==0,"combat self heal was cancelled at start");Check(tree.Tick(null)==RunStatus.Running&&Stopped==0,"combat self heal lost its continuation");StyxWoW.Me.IsCasting=false;Check(tree.Tick(null)==RunStatus.Success&&Submitted==1&&Stopped==0,"combat completion repeated or cancelled the cast");}finally{tree.Stop(null);}});
   Case($"Ret/{context}/combat/{name} honors its configured group threshold",()=>{SetupCombat();StyxWoW.Me.HealthPercent=50;var tree=Retribution.CreateRetributionPaladinHeal();tree.Start(null);try{var status=tree.Tick(null);Check(context=="solo"?Submitted==1&&status==RunStatus.Running:Submitted==0&&status==RunStatus.Failure,"solo/group combat threshold was confused");}finally{tree.Stop(null);}});
   Case($"Ret/{context}/combat/{name} yields to emergency Lay on Hands",()=>{SetupCombat();StyxWoW.Me.HealthPercent=10;StyxWoW.Me.MetadataUnknown=true;SpellManager.Spells["Lay on Hands"]=new(){Id=633,CastTime=0};var tree=Retribution.CreateRetributionPaladinHeal();tree.Start(null);try{Check(tree.Tick(null)==RunStatus.Success&&Submitted==1&&LastSpell=="Lay on Hands"&&ReferenceEquals(LastRecipient,StyxWoW.Me)&&Stopped==0,"critical self heal did not precede ordinary healing");}finally{tree.Stop(null);}});
  }
  Console.WriteLine($"Integrated Paladin heal continuation: {passed}/{total}; assertions={failures}; unexpected={errors}; actual Ret/Protection factories, shared Heal and TreeSharp; controlled native submission and raw state; includes solo/party/raid combat.");
  if(failures+errors!=0)throw new InvalidOperationException("Integrated Paladin heal continuation failures");
 }
}
""";
}
