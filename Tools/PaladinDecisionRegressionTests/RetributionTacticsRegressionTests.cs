using System;
using System.Collections.Generic;
using System.Linq;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;
using TreeSharp;

// Real linked Retribution/TreeSharp; observations and dispatch are controlled.
// These are decision/negative-action tests, not a simulated server or DPS benchmark.
internal static class RetributionTacticsRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Test)>();
        foreach (var entry in new (string Name, Func<Composite> Build)[] {
            ("Normal", Retribution.CreateRetributionPaladinNormalPullAndCombat),
            ("Instance", Retribution.CreateRetributionPaladinInstancePullAndCombat),
            ("Battleground", Retribution.CreateRetributionPaladinPvPPullAndCombat) })
        {
            var f = entry;
            void Add(string name, System.Action test) => cases.Add((f.Name + ": " + name, test));
            Add("level-33 Command is a learned damage seal", () => { Setup(1,33); Ready("Seal of Command"); Tick(f.Build()); Expect("Seal of Command"); });
            Add("unlearned high-level seals fall back to Righteousness", () => { Setup(1,33); Ready("Seal of Righteousness"); Tick(f.Build()); Expect("Seal of Righteousness"); });
            foreach (PaladinSeal seal in Enum.GetValues<PaladinSeal>().Where(x => x != PaladinSeal.Auto))
            {
                var wanted = seal;
                Add("explicit " + seal + " is not overwritten by the combat tree", () => {
                    Setup(4); Ready("Seal of Vengeance", "Seal of Corruption", "Seal of Righteousness", "Seal of Command", "Seal of Wisdom", "Seal of Light", "Seal of Justice");
                    SingularSettings.Instance.Paladin.Seal = wanted; Tick(f.Build()); Expect("Seal of " + wanted);
                });
            }
            Add("three safe enemies select Command rather than Righteousness", () => { Setup(3); Ready("Seal of Vengeance","Seal of Command","Seal of Righteousness"); Tick(f.Build()); Expect("Seal of Command"); });
            Add("four safe enemies still select Command", () => { Setup(4); Ready("Seal of Vengeance","Seal of Command","Seal of Righteousness"); Tick(f.Build()); Expect("Seal of Command"); });
            Add("boss keeps the stacking damage seal with incidental adds", () => { Setup(4); StyxWoW.Me.CurrentTarget!.Boss=true; Ready("Seal of Vengeance","Seal of Command","Seal of Righteousness"); Tick(f.Build()); Expect("Seal of Vengeance"); });
            Add("unsafe cleave does not activate Command", () => { Setup(4); Fixture.AreaSafe=false; Ready("Seal of Command","Seal of Vengeance","Seal of Righteousness"); Tick(f.Build()); Expect("Seal of Vengeance"); });
            Add("two-target boundary retains an observed Command", () => { Setup(2); Ready("Seal of Command","Seal of Vengeance"); Aura("Seal of Command"); Tick(f.Build()); Expect("movement"); });
            Add("manual Wisdom remains stable at full mana", () => { Setup(); Ready("Seal of Vengeance","Seal of Wisdom"); SingularSettings.Instance.Paladin.Seal=PaladinSeal.Wisdom; Aura("Seal of Wisdom"); Tick(f.Build()); Expect("movement"); });
            Add("grouped low mana does not replace a damage seal with Wisdom", () => { Setup(); StyxWoW.Me.IsInParty=true; StyxWoW.Me.ManaPercent=5; Ready("Seal of Wisdom","Seal of Vengeance"); Aura("Seal of Vengeance"); Tick(f.Build()); Expect("movement"); });
            Add("Wisdom judgement works with a damage seal", () => { Setup(); StyxWoW.Me.IsInParty=true; Ready("Judgement of Wisdom","Judgement of Light"); Aura("Seal of Vengeance"); Tick(f.Build()); Expect("Judgement of Wisdom"); });
            Add("external Wisdom makes Light a complementary judgement", () => { Setup(); StyxWoW.Me.IsInParty=true; Ready("Judgement of Wisdom","Judgement of Light"); TargetAura("Judgement of Wisdom",99); Tick(f.Build()); Expect("Judgement of Light"); });
            Add("own Wisdom is not mistaken for external coverage", () => { Setup(); StyxWoW.Me.IsInRaid=true; Ready("Judgement of Wisdom","Judgement of Light"); TargetAura("Judgement of Wisdom",StyxWoW.Me.Guid); Tick(f.Build()); Expect("Judgement of Wisdom"); });
            Add("expired external Wisdom does not remove mana coverage", () => { Setup(); StyxWoW.Me.IsInRaid=true; Ready("Judgement of Wisdom","Judgement of Light"); TargetAura("Judgement of Wisdom",99,0); Tick(f.Build()); Expect("Judgement of Wisdom"); });
            Add("external Light keeps Wisdom complementary", () => { Setup(); StyxWoW.Me.IsInParty=true; Ready("Judgement of Wisdom","Judgement of Light"); TargetAura("Judgement of Light",99); Tick(f.Build()); Expect("Judgement of Wisdom"); });
            Add("only-learned Light retains low-level fallback", () => { Setup(1,8); Ready("Judgement of Light"); Tick(f.Build()); Expect("Judgement of Light"); });
            Add("solo low health can prefer Light without replacing its seal", () => { Setup(); StyxWoW.Me.HealthPercent=55; Ready("Judgement of Wisdom","Judgement of Light"); Tick(f.Build()); Expect("Judgement of Light"); });
            Add("critical mana prioritizes a judgement over a ready melee strike", () => { Setup(); StyxWoW.Me.ManaPercent=10; Ready("Judgement of Wisdom","Judgement of Light","Crusader Strike"); Tick(f.Build()); Expect("Judgement of Wisdom"); });
            Add("Divine Plea is not starved behind melee attacks", () => { Setup(); StyxWoW.Me.ManaPercent=20; Ready("Divine Plea","Crusader Strike"); Tick(f.Build()); Expect("Divine Plea"); });
            Add("Plea is held while emergency healing is necessary", () => { Setup(); StyxWoW.Me.ManaPercent=20; StyxWoW.Me.HealthPercent=20; Ready("Divine Plea"); Tick(f.Build()); Expect("movement"); });
            Add("Holy Wrath is not spent on humanoids", () => { Setup(4); Ready("Holy Wrath"); Tick(f.Build()); Expect("movement"); });
            Add("Holy Wrath has a real undead target control", () => { Setup(1); Singular.Helpers.Unit.NearbyUnfriendlyUnits[0].UndeadOrDemon=true; Ready("Holy Wrath"); Tick(f.Build()); Expect("Holy Wrath"); });
            Add("known taunts and Protection attacks never enter Ret", () => { Setup(); Ready("Hand of Reckoning","Righteous Defense","Righteous Fury","Avenger's Shield","Hammer of the Righteous","Shield of Righteousness","Holy Shield"); Tick(f.Build()); Expect("movement"); });
            Add("Divine Protection remains a legitimate Ret defensive", () => { Setup(); StyxWoW.Me.HealthPercent=15; Ready("Divine Protection"); Tick(f.Build()); Expect("Divine Protection"); });
            Add("Forbearance prevents the Ret defensive retry", () => { Setup(); StyxWoW.Me.HealthPercent=15; Aura("Forbearance"); Ready("Divine Protection"); Tick(f.Build()); Expect("movement"); });
            Add("seal maintenance never dismounts a travelling player", () => { Setup(); StyxWoW.Me.Mounted=true; Ready("Seal of Vengeance"); Tick(f.Build()); Expect("movement"); });
            Add("lost target during judgement setup revokes that cast", () => { Setup(); Ready("Judgement of Wisdom"); Fixture.BeforeDispatch=()=>StyxWoW.Me.CurrentTarget=null; Tick(f.Build()); Expect("movement"); });
            Add("coverage change during judgement setup revokes the old choice", () => { Setup(); StyxWoW.Me.IsInParty=true; Ready("Judgement of Wisdom"); Fixture.Known.Add("Judgement of Light"); Fixture.BeforeDispatch=()=>TargetAura("Judgement of Wisdom",99); Tick(f.Build()); Expect("movement"); });
        }
        cases.Add(("PvP moving player uses Justice judgement, not a mandatory Justice seal",()=>{
            Setup(); StyxWoW.Me.CurrentTarget!.IsPlayer=true; StyxWoW.Me.CurrentTarget.IsMoving=true;
            Aura("Seal of Righteousness"); Ready("Judgement of Justice","Judgement of Wisdom","Judgement of Light");
            Tick(Retribution.CreateRetributionPaladinPvPPullAndCombat()); Expect("Judgement of Justice");
        }));
        cases.Add(("grouped Ret does not hard-heal at the Holy 90-percent threshold",()=>{
            Setup(); StyxWoW.Me.IsInParty=true; StyxWoW.Me.Combat=true; StyxWoW.Me.HealthPercent=80;
            SingularSettings.Instance.Paladin.HolyLightHealth=90; Ready("Holy Light","Flash of Light");
            Tick(Retribution.CreateRetributionPaladinHeal()); Check(Fixture.Selected==null,"ordinary group damage triggered a hard heal");
        }));
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS Ret tactics: "+item.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL Ret tactics assertion: "+item.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR Ret tactics fixture: "+item.Name+": "+e); }
        }
        Console.WriteLine($"Retribution tactics scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; linked real rotation; controlled world/dispatch; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Ret tactics regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static void Setup(int count=1,int level=80) { Fixture.Reset(count,level); Fixture.ApplyEffects=true; }
    private static void Ready(params string[] names) { Fixture.Known.UnionWith(names); Fixture.Ready.UnionWith(names); }
    private static void Aura(string name) => StyxWoW.Me.Auras[name]=new Aura{Name=name,CreatorGuid=StyxWoW.Me.Guid};
    private static void TargetAura(string name,ulong owner,int seconds=20) => StyxWoW.Me.CurrentTarget!.Auras[name]=new Aura{Name=name,CreatorGuid=owner,TimeLeft=TimeSpan.FromSeconds(seconds)};
    private static void Tick(Composite root) { root.Start(null!); try { Check(root.Tick(null!)!=RunStatus.Running,"unexpected blocking decision"); Check(Fixture.Exceptions.Count==0,"swallowed exception"); } finally { root.Stop(null!); } }
    private static void Expect(string name) => Check(Fixture.Selected==name,$"expected {name}, got {Fixture.Selected}; {string.Join(',',Fixture.Trace)}");
    private static void Check(bool ok,string why) { if(!ok) throw new AssertionFailure(why); }
}
