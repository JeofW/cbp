using System.Reflection;
using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;
using Styx.Helpers;
using Styx.Logic.Combat;
using PaladinCommon = Singular.ClassSpecific.Paladin.Common;

internal static class LiveSupportPipelineRegressionTests
{
    [ModuleInitializer] internal static void Run()
    {
        int count=0, passed=0;
        void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
        void Case(string name,System.Action test)
        {
            count++;Fixture.Reset();
            try {test();passed++;Console.WriteLine("PASS live support pipeline: "+name);}
            catch(Exception error){Console.Error.WriteLine("FAIL live support pipeline: "+name+": "+error.Message);}
        }
        void Unknown(bool harmful)
        {
            StyxWoW.Me.MetadataUnknown=true;
            StyxWoW.Me.ObservedAuras.Add(new WoWAura{SpellId=61988,IsActive=true,IsHarmful=harmful,Spell=null});
        }
        Case("unknown optional dispel does not prevent full precombat blessings",()=>
        {
            Unknown(true);Fixture.Known.UnionWith(new[]{"Cleanse","Purify","Blessing of Kings"});
            Fixture.Tick(PaladinCommon.CreatePaladinPreCombatBuffs());
            Check(Fixture.Attempts.Count==1&&Fixture.Attempts[0].Spell=="Blessing of Kings",
                "the complete precombat tree did not progress from uncertain dispel to an independent known blessing");
        });
        Case("helpful unknown metadata is outside the harmful dispel mask",()=>
        {
            Unknown(false);Fixture.Known.Add("Purify");
            Fixture.Aura(StyxWoW.Me,"known poison",99,100,WoWDispelType.Poison);
            Fixture.Tick(PaladinCommon.CreatePaladinDispelBehavior());
            Check(Fixture.Attempts.Count==1&&Fixture.Attempts[0].Spell=="Purify","helpful marker prevented a fully observed harmful dispel");
        });
        Case("unknown harmful mask cannot authorize removal beside a known poison",()=>
        {
            Unknown(true);Fixture.Known.UnionWith(new[]{"Purify","Cleanse"});
            Fixture.Aura(StyxWoW.Me,"known poison",99,100,WoWDispelType.Poison);
            Fixture.Tick(PaladinCommon.CreatePaladinDispelBehavior());
            Check(Fixture.Attempts.Count==0,"unknown harmful metadata was treated as safe-to-remove absence");
        });
        Case("sole learned Auto damage seal does not enumerate unrelated hostiles",()=>
        {
            Fixture.Known.UnionWith(new[]{"Seal of Command","Seal of Righteousness"});
            Fixture.NearbyError=new ObservationUnavailableException("reaction","unrelated hostile observation unavailable");
            string? choice=null;Exception? escaped=null;
            try {choice=(string?)typeof(Retribution).GetMethod("SelectRetributionSeal",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null);}
            catch(Exception error){escaped=error;}
            Check(escaped==null&&choice=="Seal of Command"&&Fixture.NearbyReads==0,
                "a seal choice independent of nearby count paid for or failed on that observation");
        });
        Case("one selected blessing does not repeat readiness during policy revalidation",()=>
        {
            Fixture.Known.Add("Blessing of Kings");
            SingularSettings.Instance.Paladin.UseGreaterBlessings=false;
            Fixture.Tick(PaladinCommon.CreatePaladinPreCombatBuffs());
            Check(Fixture.Attempts.Count==1&&Fixture.Attempts[0].Spell=="Blessing of Kings", "blessing control failed");
            Check(Fixture.SupportReadinessReads<=2,"one support selection repeated expensive readiness across callback revalidations");
        });
        Fixture.Reset();Console.WriteLine($"Live support pipeline: {passed}/{count}; linked full precombat/dispel/seal owners; controlled client and dispatch boundaries.");
        if(passed!=count)throw new InvalidOperationException("Live support pipeline regressions");
    }
}
