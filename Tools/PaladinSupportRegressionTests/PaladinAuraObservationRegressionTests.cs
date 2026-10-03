using System.Reflection;
using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;
using Styx.Logic.Combat;
using TreeSharp;
using PaladinCommon=Singular.ClassSpecific.Paladin.Common;

// The real linked selectors/trees, with observation and Spell adapter leaves controlled.
// Core raw-reader and actual TryCast acknowledgement are independently exercised by host fixtures.
internal static class PaladinAuraObservationRegressionTests
{
 [ModuleInitializer] internal static void Run()
 {
  var cases=new List<(string Name,System.Action Test)> {
   ("known blessing survives unresolved metadata",()=>{Unknown();Fixture.Known.Add("Blessing of Kings");SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Kings;Check(Select("SelectNormalBlessing",StyxWoW.Me)=="Blessing of Kings","unrelated missing row denied supported negative");}),
   ("known positive blessing survives unresolved metadata",()=>{Unknown();Fixture.Known.Add("Blessing of Kings");Fixture.Aura(StyxWoW.Me,"Blessing of Kings",1,20217);Check(Select("SelectNormalBlessing",StyxWoW.Me)==null,"known own blessing lost");}),
   ("known aura coverage survives unresolved metadata",()=>{Unknown();Fixture.Known.Add("Devotion Aura");SingularSettings.Instance.Paladin.Aura=PaladinAura.Devotion;Check(Select("SelectAura",StyxWoW.Me)=="Devotion Aura","missing row denied aura");}),
   ("unreadable raw coverage remains unknown",()=>{StyxWoW.Me.RawUnknown=true;Fixture.Known.Add("Blessing of Kings");bool rejected=false;try{rejected=Select("SelectNormalBlessing",StyxWoW.Me)==null;}catch(TargetInvocationException){rejected=true;}Check(rejected,"raw unknown became supported absence");}),
   ("seal owner requests aura acknowledgement",()=>{Fixture.Known.Add("Seal of Righteousness");SingularSettings.Instance.Paladin.Seal=PaladinSeal.Righteousness;Fixture.Tick(Retribution.CreateRetributionSealBehavior());Check(Fixture.RecoveryRoutes.Any(x=>x.Spell=="Seal of Righteousness"&&x.Aura),"seal bypassed aura recovery");}),
   ("blessing owner requests aura acknowledgement",()=>{Fixture.Known.Add("Blessing of Kings");SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Kings;Fixture.Tick(PaladinCommon.CreatePaladinPreCombatBuffs());Check(Fixture.RecoveryRoutes.Any(x=>x.Spell=="Blessing of Kings"&&x.Aura),"blessing bypassed aura recovery");}),
   ("aura owner requests aura acknowledgement",()=>{Fixture.Known.Add("Devotion Aura");SingularSettings.Instance.Paladin.Aura=PaladinAura.Devotion;Fixture.Tick(PaladinCommon.CreatePaladinAuraBehavior());Check(Fixture.RecoveryRoutes.Any(x=>x.Spell=="Devotion Aura"&&x.Aura),"aura bypassed aura recovery");}),
   ("Greater rejects an omitted group object",()=>{Fixture.Add(Styx.Combat.CombatRoutine.WoWClass.Paladin);Fixture.RosterObservation=new(){"group-v1","0","2","0x1","0xA","0xB"};Greater();Check(Fixture.Attempts.Single().Spell=="Blessing of Kings","partial object roster authorized Greater");}),
   ("Greater rejects unavailable roster instead of inferring solo",()=>{Fixture.RosterObservation=new();Greater();Check(Fixture.Attempts.Single().Spell=="Blessing of Kings","empty roster authorized Greater");}),
   ("seal supported negative survives unresolved marker",()=>{Unknown();Fixture.Known.Add("Seal of Righteousness");SingularSettings.Instance.Paladin.Seal=PaladinSeal.Righteousness;Fixture.Tick(Retribution.CreateRetributionSealBehavior());Check(Fixture.Attempts.Any(x=>x.Spell=="Seal of Righteousness"),"marker blocked seal");}),
   ("exact seal ID remains covering without its name",()=>{Unknown();Fixture.Known.Add("Seal of Righteousness");SingularSettings.Instance.Paladin.Seal=PaladinSeal.Righteousness;Fixture.Aura(StyxWoW.Me,"",1,21084);Fixture.Tick(Retribution.CreateRetributionSealBehavior());Check(Fixture.Attempts.Count==0,"known seal lost coverage");}),
   ("judgement external coverage survives unresolved marker",()=>{var p=Fixture.Add();p.MetadataUnknown=true;StyxWoW.Me.CurrentTarget=p;Fixture.Known.UnionWith(new[]{"Judgement of Light","Judgement of Wisdom"});Fixture.Aura(p,"",99,20186);Fixture.Aura(p,"",0,61988);Check(RetSelect("SelectRetributionJudgement")=="Judgement of Light","missing marker lost external Wisdom");}),
   ("proc exact ID works without complete metadata",()=>{Unknown();StyxWoW.Me.CurrentTarget=new Styx.WoWInternals.WoWObjects.WoWUnit{Guid=20};Fixture.Known.Add("Exorcism");Fixture.Aura(StyxWoW.Me,"",1,59578);var tree=(Composite)typeof(Retribution).GetMethod("CreateExorcismBehavior",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object?[]{true,null})!;Fixture.Tick(tree);Check(Fixture.Attempts.Single().Spell=="Exorcism","known proc lost");}),
   ("expired proc does not authorize proc window",()=>{Unknown();StyxWoW.Me.CurrentTarget=new Styx.WoWInternals.WoWObjects.WoWUnit{Guid=20};Fixture.Known.Add("Exorcism");Fixture.Aura(StyxWoW.Me,"",1,59578);StyxWoW.Me.ObservedAuras.Last().TimeLeft=TimeSpan.Zero;var tree=(Composite)typeof(Retribution).GetMethod("CreateExorcismBehavior",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object?[]{true,null})!;Fixture.Tick(tree);Check(Fixture.Attempts.Count==0,"expired proc admitted");}),
   ("raw-unreadable target cannot fabricate judgement coverage",()=>{var p=Fixture.Add();p.RawUnknown=true;StyxWoW.Me.CurrentTarget=p;bool rejected=false;try{RetSelect("SelectRetributionJudgement");}catch(TargetInvocationException){rejected=true;}Check(rejected,"target UNKNOWN admitted");}),
   ("unknown harmful dispel metadata explicitly defers",()=>{Unknown();SingularSettings.Instance.Paladin.DispelDebuffs=true;Fixture.Known.Add("Cleanse");Check(Select("SelectDispel",StyxWoW.Me)==null&&Fixture.Attempts.Count==0,"unknown mechanics authorized dispel");})
  };
  int passed=0;
  foreach(var c in cases){Fixture.Reset();try{c.Test();passed++;Console.WriteLine("PASS Paladin aura observation: "+c.Name);}catch(Exception e){Console.Error.WriteLine("FAIL Paladin aura observation: "+c.Name+": "+e);}}
  Fixture.Reset();Console.WriteLine($"Paladin aura observation: {passed}/{cases.Count}");if(passed!=cases.Count)throw new InvalidOperationException("Paladin aura observation regressions");
 }
 private static string? RetSelect(string name)=>(string?)typeof(Retribution).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
 private static void Greater(){SingularSettings.Instance.Paladin.UseGreaterBlessings=true;SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Kings;Fixture.Known.UnionWith(new[]{"Blessing of Kings","Greater Blessing of Kings"});var spell=new WoWSpell();spell.InternalInfo.Reagent![0]=777;spell.InternalInfo.ReagentCount![0]=1;Fixture.Metadata["Greater Blessing of Kings"]=spell;StyxWoW.Me.ItemCounts[777]=1;Fixture.Tick(PaladinCommon.CreatePaladinPreCombatBuffs());}
 private static void Unknown(){StyxWoW.Me.MetadataUnknown=true;Fixture.Aura(StyxWoW.Me,"",0,61988);}
 private static string? Select(string name,params object[] args)=>(string?)typeof(PaladinCommon).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args);
 private static void Check(bool condition,string why){if(!condition)throw new InvalidOperationException(why);}
}
