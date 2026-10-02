using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;

// Complete linked Ret owners; exact restrictions from the pinned TC335 scripts.
internal static class RetributionRecoveryObservationRegressionTests
{
 [ModuleInitializer] internal static void Run()
 {
  var cases=new List<(string Name,System.Action Test)>();
  void Add(string name,System.Action test)=>cases.Add((name,()=>{Fixture.Reset();StyxWoW.Me.HealthPercent=10;test();}));
  foreach(int id in new[]{25771,61987,61988}){int marker=id;Add("self LoH respects raw restriction "+id,()=>{Fixture.Known.Add("Lay on Hands");Fixture.Aura(StyxWoW.Me,"",1,marker);Fixture.Tick(Retribution.CreateRetributionPaladinHeal());Check(Fixture.Attempts.Count==0,"restricted self LoH submitted");});}
  foreach(string state in new[]{"mounted","transport","dead","ghost","casting","channeling"}){string mode=state;Add("recovery refuses "+mode,()=>{Fixture.Known.Add("Lay on Hands");var me=StyxWoW.Me;if(mode=="mounted")me.Mounted=true;if(mode=="transport")me.IsOnTransport=true;if(mode=="dead")me.IsAlive=false;if(mode=="ghost")me.IsGhost=true;if(mode=="casting")me.IsCasting=true;if(mode=="channeling")me.IsChanneling=true;Fixture.Tick(Retribution.CreateRetributionPaladinHeal());Check(Fixture.Attempts.Count==0,"ineligible recovery submitted");});}
  Add("raw unknown cannot authorize self LoH",()=>{Fixture.Known.Add("Lay on Hands");StyxWoW.Me.RawUnknown=true;try{Fixture.Tick(Retribution.CreateRetributionPaladinHeal());}catch(InvalidOperationException){}Check(Fixture.Attempts.Count==0,"UNKNOWN became unrestricted");});
  Add("unrelated unknown ID does not suppress allowed self LoH",()=>{Fixture.Known.Add("Lay on Hands");StyxWoW.Me.MetadataUnknown=true;Fixture.Aura(StyxWoW.Me,"",0,65000);Fixture.Tick(Retribution.CreateRetributionPaladinHeal());Check(Fixture.Attempts.Single().Spell=="Lay on Hands","known unrestricted self LoH denied");});
  foreach(int id in new[]{25771,61987}){int marker=id;Add("defense respects raw restriction "+id,()=>{Fixture.Known.Add("Divine Protection");Fixture.Aura(StyxWoW.Me,"",1,marker);Fixture.Tick(Retribution.CreateRetributionPaladinInstancePullAndCombat());Check(!Fixture.Attempts.Any(x=>x.Spell=="Divine Protection"),"restricted defense submitted");});}
  Add("shield marker alone is not a Protection restriction",()=>{Fixture.Known.Add("Divine Protection");Fixture.Aura(StyxWoW.Me,"",1,61988);Fixture.Tick(Retribution.CreateRetributionPaladinInstancePullAndCombat());Check(Fixture.Attempts.Single().Spell=="Divine Protection","unrelated shield marker became Protection restriction");});
  foreach(string spell in new[]{"Divine Plea","Avenging Wrath"}){string name=spell;Add("supported buff survives unrelated metadata/"+name,()=>{var me=StyxWoW.Me;me.HealthPercent=100;me.ManaPercent=20;me.CurrentTarget=new Styx.WoWInternals.WoWObjects.WoWUnit{Guid=2,Distance=4};me.MetadataUnknown=true;Fixture.Known.Add(name);Fixture.Aura(me,"",0,65000);Fixture.Tick(Retribution.CreateRetributionPaladinPvPPullAndCombat());Check(Fixture.Attempts.Any(x=>x.Spell==name),"supported buff blocked by unrelated metadata");Check(Fixture.RecoveryRoutes.Any(x=>x.Spell==name&&x.Item2),"supported buff bypassed aura recovery");});}
  foreach(var spec in new[]{("Divine Plea",54428),("Avenging Wrath",31884),("Avenging Wrath",43430),("Avenging Wrath",50837),("Avenging Wrath",66011)}){var known=spec;Add("supported buff exact existing family/"+known.Item2,()=>{var me=StyxWoW.Me;me.HealthPercent=100;me.ManaPercent=20;me.CurrentTarget=new Styx.WoWInternals.WoWObjects.WoWUnit{Guid=2,Distance=4};me.MetadataUnknown=true;Fixture.Known.Add(known.Item1);Fixture.Aura(me,"",1,known.Item2);Fixture.Tick(Retribution.CreateRetributionPaladinPvPPullAndCombat());Check(!Fixture.Attempts.Any(x=>x.Spell==known.Item1),"existing exact-family buff recast");});}
  int passed=0;foreach(var c in cases){try{c.Test();passed++;Console.WriteLine("PASS Ret recovery observation: "+c.Name);}catch(Exception e){Console.Error.WriteLine("FAIL Ret recovery observation: "+c.Name+": "+e);}}
  Fixture.Reset();Console.WriteLine($"Ret recovery observation: {passed}/{cases.Count}");if(passed!=cases.Count)throw new InvalidOperationException("Ret recovery observation regressions");
 }
 private static void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
}
