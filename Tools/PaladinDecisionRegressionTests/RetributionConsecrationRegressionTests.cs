using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Paladin;
using Styx;
using TreeSharp;

// Ret's actual linked entry trees. World and dispatch are the existing controlled
// boundaries; these test decision policy, not server acceptance or measured DPS.
internal static class RetributionConsecrationRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Body)>();
        foreach (var entry in new (string Name, Func<Composite> Build)[] {
            ("Normal", Retribution.CreateRetributionPaladinNormalPullAndCombat),
            ("Instance", Retribution.CreateRetributionPaladinInstancePullAndCombat),
            ("Battleground", Retribution.CreateRetributionPaladinPvPPullAndCombat) })
        {
            var f = entry;
            void Add(string label, System.Action body) => cases.Add((f.Name + ": " + label, body));
            Add("stationary boss admits an available damage filler", () => {
                Setup(); Tick(f.Build()); Expect("Consecration");
            });
            Add("ordinary one-target grinding retains mana", () => {
                Setup(boss:false); Tick(f.Build()); Expect("movement");
            });
            Add("ordinary safe pack retains the configured AoE control", () => {
                Setup(3,false); Tick(f.Build()); Expect("Consecration");
            });
            Add("low mana cannot spend the damage-filler budget", () => {
                Setup(3); StyxWoW.Me.ManaPercent=10; Tick(f.Build()); Expect("movement");
            });
            Add("exact recovery threshold reserves mana", () => {
                Setup(3); StyxWoW.Me.ManaPercent=30; Tick(f.Build()); Expect("movement");
            });
            Add("above recovery threshold retains an available filler", () => {
                Setup(); StyxWoW.Me.ManaPercent=31; Tick(f.Build()); Expect("Consecration");
            });
            Add("moving boss does not receive a speculative ground patch", () => {
                Setup(); StyxWoW.Me.CurrentTarget!.IsMoving=true; Tick(f.Build()); Expect("movement");
            });
            Add("moving player preserves the filler budget", () => {
                Setup(3); StyxWoW.Me.IsMoving=true; Tick(f.Build()); Expect("movement");
            });
            Add("out-of-melee target retains movement", () => {
                Setup(); StyxWoW.Me.CurrentTarget!.Distance=20; Tick(f.Build()); Expect("movement");
            });
            Add("unsafe area never receives Consecration", () => {
                Setup(3); Fixture.AreaSafe=false; Tick(f.Build()); Expect("movement");
            });
            Add("dead target cannot authorize ground damage", () => {
                Setup(3); StyxWoW.Me.CurrentTarget!.IsAlive=false; Tick(f.Build()); Expect("movement");
            });
            Add("unknown spell keeps the existing fallback", () => {
                Setup(); Fixture.Known.Clear(); Tick(f.Build()); Expect("movement");
            });
            Add("unavailable spell keeps the existing fallback", () => {
                Setup(); Fixture.Ready.Clear(); Tick(f.Build()); Expect("movement");
            });
            Add("ready Crusader Strike remains ahead of the filler", () => {
                Setup(3); Fixture.Known.Add("Crusader Strike"); Fixture.Ready.Add("Crusader Strike");
                Tick(f.Build()); Expect("Crusader Strike");
            });
            Add("available Plea retains the low-mana recovery slot", () => {
                Setup(3); StyxWoW.Me.ManaPercent=20; Fixture.Known.Add("Divine Plea"); Fixture.Ready.Add("Divine Plea");
                Tick(f.Build()); Expect("Divine Plea");
            });
            Add("late target loss revokes the pending filler", () => {
                Setup(3); Fixture.BeforeDispatch=()=>StyxWoW.Me.CurrentTarget=null;
                Tick(f.Build()); Expect("movement");
            });
            Add("late target replacement cannot borrow its predecessor's decision", () => {
                Setup(3); Fixture.BeforeDispatch=()=>StyxWoW.Me.CurrentTarget=new UnitState { Boss=true, Guid=99 };
                Tick(f.Build()); Expect("movement");
            });
            Add("late mana loss revokes the pending filler", () => {
                Setup(3); Fixture.BeforeDispatch=()=>StyxWoW.Me.ManaPercent=5;
                Tick(f.Build()); Expect("movement");
            });
            Add("late area hazard revokes the pending filler", () => {
                Setup(3); Fixture.BeforeDispatch=()=>Fixture.AreaSafe=false;
                Tick(f.Build()); Expect("movement");
            });
            Add("late movement revokes the pending filler", () => {
                Setup(3); Fixture.BeforeDispatch=()=>StyxWoW.Me.IsMoving=true;
                Tick(f.Build()); Expect("movement");
            });
            Add("raid Ret may use safe Consecration without acquiring a tank role", () => {
                Setup(); StyxWoW.Me.IsInRaid=true; Tick(f.Build()); Expect("Consecration");
            });
            Add("mounted travel cannot be interrupted by ground damage", () => {
                Setup(3); StyxWoW.Me.Mounted=true; Tick(f.Build()); Expect("movement");
            });
        }
        int passed=0, assertions=0, unexpected=0;
        foreach (var item in cases)
        {
            try { item.Body(); passed++; Console.WriteLine("PASS Ret Consecration: "+item.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL Ret Consecration assertion: "+item.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR Ret Consecration fixture: "+item.Name+": "+e); }
        }
        Console.WriteLine($"Ret Consecration scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual linked Ret entries; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Ret Consecration regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static void Setup(int count=1,bool boss=true)
    {
        Fixture.Reset(count); StyxWoW.Me.CurrentTarget!.Boss=boss;
        Fixture.Known.Add("Consecration"); Fixture.Ready.Add("Consecration");
    }
    private static void Tick(Composite root)
    {
        root.Start(null!);
        try { Check(root.Tick(null!)!=RunStatus.Running,"unexpected blocking decision"); Check(Fixture.Exceptions.Count==0,"swallowed exception"); }
        finally { root.Stop(null!); }
    }
    private static void Expect(string wanted) => Check(Fixture.Selected==wanted,
        $"expected {wanted}, observed {Fixture.Selected}; "+string.Join(',',Fixture.Trace));
    private static void Check(bool condition,string text) { if(!condition) throw new AssertionFailure(text); }
}
