using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Paladin;
using Singular.Dynamics;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.WoWInternals.WoWObjects;

// Actual linked support selection and TreeSharp. Only world/context/spell
// dispatch boundaries are controlled. This is not a damage simulator.
internal static class BlessingContributionRegressionTests
{
    private const string Kings="Blessing of Kings", Might="Blessing of Might", Wisdom="Blessing of Wisdom";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text):base(text){} }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new List<(string Name,Action Test)>();
        foreach(bool raid in new[]{false,true})
        {
            var isRaid=raid; string tag=raid?"raid":"party";
            void Add(string name,Action test)=>cases.Add((tag+": "+name,()=>{Setup(isRaid);test();}));
            Add("uncovered Ret damage role prefers learned Might",()=>{Know(Kings,Might,Wisdom);Bless();Expect(Might);});
            Add("external Kings retains complementary Might",()=>{Know(Kings,Might,Wisdom);Aura(Kings,99);Bless();Expect(Might);});
            Add("external Might retains complementary Kings",()=>{Know(Kings,Might,Wisdom);Aura(Might,99);Bless();Expect(Kings);});
            Add("Greater Might is the same contribution category",()=>{Know(Kings,Might,Wisdom);Aura("Greater "+Might,99);Bless();Expect(Kings);});
            Add("Battle Shout makes Kings the complementary contribution",()=>{Know(Kings,Might,Wisdom);Aura("Battle Shout",99);Bless();Expect(Kings);});
            Add("Battle Shout and external Kings permit Wisdom",()=>{Know(Kings,Might,Wisdom);Aura("Battle Shout",99);Aura(Kings,98);Bless();Expect(Wisdom);});
            Add("expired Battle Shout is not current AP coverage",()=>{Know(Kings,Might);Aura("Battle Shout",99,0);Bless();Expect(Might);});
            Add("own useful Kings remains stable",()=>{Know(Kings,Might);Aura(Kings,1);Bless();Expect(null);});
            Add("own useful Might remains stable",()=>{Know(Kings,Might);Aura(Might,1);Bless();Expect(null);});
            Add("duplicated own Might yields to missing Kings",()=>{Know(Kings,Might);Aura(Might,1);Aura(Might,99);Bless();Expect(Kings);});
            Add("expired own Kings does not freeze contribution",()=>{Know(Kings,Might);Aura(Kings,1,0);Bless();Expect(Might);});
            Add("manual Kings remains explicit",()=>{Know(Kings,Might);SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Kings;Bless();Expect(Kings);});
            Add("manual Might is deferred under Shout without substituting Kings",()=>{Know(Kings,Might);Aura("Battle Shout",99);SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Might;Bless();Expect(null);});
            // Explicit preference cannot override an active same-category conflict.
            // Effective rank/talent magnitude is unknown here; preserve coverage.
            Add("manual Might alone does not retry covered AP",()=>{Know(Might);ManualMight();Aura("Battle Shout",99);Bless();Expect(null);});
            Add("unknown Shout caster still establishes observed coverage",()=>{Know(Might);ManualMight();Aura("Battle Shout",0);Bless();Expect(null);});
            Add("short remaining Shout is not cancelled or overwritten early",()=>{Know(Might);ManualMight();Aura("Battle Shout",99,1);Bless();Expect(null);});
            Add("inactive Shout permits manual Might",()=>{Know(Might);ManualMight();Aura("Battle Shout",99);StyxWoW.Me.ObservedAuras[^1].IsActive=false;Bless();Expect(Might);});
            Add("expired Shout permits manual Might",()=>{Know(Might);ManualMight();Aura("Battle Shout",99,0);Bless();Expect(Might);});
            Add("manual Might also respects Greater Might",()=>{Know(Might);ManualMight();Aura("Greater "+Might,99);Bless();Expect(null);});
            Add("forty fresh decisions cannot repeat a rejected Might",()=>{Know(Kings,Might);ManualMight();Aura("Battle Shout",99);for(int i=0;i<40;i++)Bless();Expect(null);});
            Add("Shout arrival and expiry change admission without sticky backoff",()=>{Know(Might);ManualMight();Bless();Expect(Might);Fixture.Attempts.Clear();Aura("Battle Shout",99);Bless();Expect(null);StyxWoW.Me.ObservedAuras[^1].TimeLeft=TimeSpan.Zero;Bless();Expect(Might);});
            Add("teammate Shout is checked on that recipient",()=>{Know(Might);ManualMight();Aura(Might,1);var p=Fixture.Add(WoWClass.Warrior,isRaid);Fixture.Aura(p,"Battle Shout",99);Bless();Expect(null);});
            Add("another recipient Shout cannot suppress uncovered self",()=>{Know(Might);ManualMight();var p=Fixture.Add(WoWClass.Warrior,isRaid);Fixture.Aura(p,"Battle Shout",99);Bless();Expect(Might);});
            Add("unknown Might falls back to learned Kings",()=>{Know(Kings);Bless();Expect(Kings);});
            Add("unavailable Might does not starve Kings",()=>{Know(Kings,Might);Fixture.Unavailable.Add(Might);Bless();Expect(Kings);});
            Add("PvP preserves the Kings survival default",()=>{Know(Kings,Might);Singular.SingularRoutine.CurrentWoWContext=WoWContext.Battlegrounds;Bless();Expect(Kings);});
            foreach(string name in new[]{"Trueshot Aura","Unleashed Rage","Abomination's Might"})
            {
                var buff=name; Add(buff+" is not flat attack-power coverage",()=>{Know(Kings,Might);Aura(buff,99);Bless();Expect(Might);});
            }
        }
        cases.Add(("solo retains the balanced Kings default",()=>{Setup(false);StyxWoW.Me.IsInParty=false;Know(Kings,Might);Bless();Expect(Kings);}));
        cases.Add(("Holy does not borrow the Ret damage preference",()=>{Setup(false);TalentManager.CurrentSpec=TalentSpec.HolyPaladin;Know(Kings,Might);Bless();Expect(Kings);}));
        cases.Add(("Protection does not borrow the Ret damage preference",()=>{Setup(false);TalentManager.CurrentSpec=TalentSpec.ProtectionPaladin;Know(Kings,Might);Bless();Expect(Kings);}));
        cases.Add(("unknown teammate role keeps conservative Kings",()=>{Setup(false);Know(Kings,Might);Aura(Might,1);var p=Fixture.Add(WoWClass.Paladin);Bless();Expect(Kings,p);}));
        cases.Add(("non-mana recipient cannot gain Wisdom from Shout coverage",()=>{Setup(false);StyxWoW.Me.MaxMana=0;Know(Kings,Might,Wisdom);Aura(Kings,99);Aura("Battle Shout",98);Bless();Expect(null);}));
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases)
        {
            try{item.Test();passed++;Console.WriteLine("PASS blessing contribution: "+item.Name);}
            catch(AssertionFailure e){assertions++;Console.Error.WriteLine("FAIL blessing contribution assertion: "+item.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR blessing contribution fixture: "+item.Name+": "+e);}
            finally{Singular.SingularRoutine.CurrentWoWContext=WoWContext.All;}
        }
        Console.WriteLine($"Blessing contribution scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual linked support owner; no game or DPS benchmark.");
        // Preserve the original Main's 44 controls even on a failing new group.
        if(assertions+unexpected!=0)Environment.ExitCode=1;
    }
    private static void Setup(bool raid){Fixture.Reset();StyxWoW.Me.IsInParty=!raid;StyxWoW.Me.IsInRaid=raid;Singular.SingularRoutine.CurrentWoWContext=WoWContext.Instances;}
    private static void ManualMight()=>SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Might;
    private static void Know(params string[] names){Fixture.Known.UnionWith(names);}
    private static void Aura(string name,ulong owner,int seconds=600){Fixture.Aura(StyxWoW.Me,name,owner);StyxWoW.Me.ObservedAuras[^1].TimeLeft=TimeSpan.FromSeconds(seconds);}
    private static void Bless()=>Fixture.Tick(Common.CreatePaladinPreCombatBuffs());
    private static void Expect(string? name,WoWPlayer? player=null)
    {
        bool correct=name==null?Fixture.Attempts.Count==0:Fixture.Attempts.Count==1&&Fixture.Attempts[0]==(name,(player??StyxWoW.Me).Guid);
        if(!correct)throw new AssertionFailure($"expected {name??"no cast"}, observed {string.Join(',',Fixture.Attempts)}");
    }
}
namespace Singular
{
    // Same context boundary used by the production routine; no routine override
    // is linked into this deliberately controlled standalone support harness.
    internal static class SingularRoutine
    {
        internal static WoWContext CurrentWoWContext {get;set;}=WoWContext.All;
    }
}
