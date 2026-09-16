using System;
using System.Collections.Generic;
using Singular.ClassSpecific.Paladin;
using Singular.Managers;
using Singular.Settings;
using Styx;
using TreeSharp;

// Actual linked Ret tree and spell-selection callbacks. A controlled player
// target is not an arena simulation, diminishing-return model or DPS benchmark.
internal static class RetributionPlayerSealRegressionTests
{
    private const string Right="Seal of Righteousness", Stack="Seal of Vengeance", Command="Seal of Command";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string why):base(why){} }
    internal static void Run()
    {
        var cases=new List<(string Name,System.Action Test)>();
        foreach(var entry in new (string Name,Func<Composite> Build)[] {
            ("Normal",Retribution.CreateRetributionPaladinNormalPullAndCombat),
            ("Instance",Retribution.CreateRetributionPaladinInstancePullAndCombat),
            ("Battleground",Retribution.CreateRetributionPaladinPvPPullAndCombat) })
        {
            var f=entry;
            void Add(string name,System.Action test)=>cases.Add((f.Name+": "+name,test));
            Add("player single-target seal avoids stacking ramp and uncontrolled dot",()=>{Setup();Ready(Right,Stack,Command);Tick(f.Build());Expect(Right);});
            Add("player cleave with observed unsafe area retains single-target seal",()=>{Setup(4);Fixture.AreaSafe=false;Ready(Right,Stack,Command);Tick(f.Build());Expect(Right);});
            Add("automatic player-combat seal avoids unproven cleave safety",()=>{Setup(4);Ready(Right,Stack,Command);Tick(f.Build());Expect(Right);});
            Add("unknown Command cannot force stacking over Righteousness",()=>{Setup(4);Ready(Right,Stack);Tick(f.Build());Expect(Right);});
            Add("existing Righteousness remains stable against a player",()=>{Setup();Ready(Right,Stack);StyxWoW.Me.Auras[Right]=new Aura{Name=Right};Tick(f.Build());Expect("movement");});
            Add("NPC single target retains stacking damage seal",()=>{Setup();StyxWoW.Me.CurrentTarget!.IsPlayer=false;Ready(Right,Stack,Command);Tick(f.Build());Expect(Stack);});
            Add("manual Wisdom is still authoritative in player combat",()=>{Setup();SingularSettings.Instance.Paladin.Seal=PaladinSeal.Wisdom;Ready(Right,Stack,"Seal of Wisdom");Tick(f.Build());Expect("Seal of Wisdom");});
            Add("manual Command remains an explicit player-combat choice",()=>{Setup(4);SingularSettings.Instance.Paladin.Seal=PaladinSeal.Command;Ready(Right,Stack,Command);Tick(f.Build());Expect(Command);});
            Add("unknown Righteousness retains known fallback",()=>{Setup();Ready(Stack);Tick(f.Build());Expect(Stack);});
            foreach(string value in new[]{"player-guid","target-guid","spec"})
            {
                string change=value;
                Add("late "+change+" change revokes seal dispatch",()=>{
                    Setup();SingularSettings.Instance.Paladin.Seal=PaladinSeal.Wisdom;Ready("Seal of Wisdom");
                    Fixture.BeforeDispatch=()=>{if(change=="player-guid")StyxWoW.Me.Guid=999;else if(change=="target-guid")StyxWoW.Me.CurrentTarget!.Guid=999;else TalentManager.CurrentSpec=TalentSpec.ProtectionPaladin;};
                    Tick(f.Build());Expect("movement");
                });
            }
        }
        int passed=0,assertions=0,unexpected=0;
        foreach(var c in cases)
        {
            try{c.Test();passed++;Console.WriteLine("PASS Ret player seal: "+c.Name);}
            catch(AssertionFailure e){assertions++;Console.Error.WriteLine("FAIL Ret player seal assertion: "+c.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR Ret player seal fixture: "+c.Name+": "+e);}
        }
        Console.WriteLine($"Ret player-seal scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual linked rotation; controlled observations/dispatch; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException($"Ret player-seal regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static void Setup(int count=1){Fixture.Reset(count);StyxWoW.Me.CurrentTarget!.IsPlayer=true;}
    private static void Ready(params string[] spells){Fixture.Known.UnionWith(spells);Fixture.Ready.UnionWith(spells);}
    private static void Tick(Composite root)
    {
        root.Start(null!);try{Check(root.Tick(null!)!=RunStatus.Running,"unexpected running decision");Check(Fixture.Exceptions.Count==0,"swallowed boundary exception");}finally{root.Stop(null!);}
    }
    private static void Expect(string spell)=>Check(Fixture.Selected==spell,$"expected {spell},actual={Fixture.Selected}; {string.Join(',',Fixture.Trace)}");
    private static void Check(bool value,string why){if(!value)throw new AssertionFailure(why);}
}
