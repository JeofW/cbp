using Singular.ClassSpecific.Paladin;
using Singular.Managers;
using Styx;
using TreeSharp;
using Shared = Singular.Helpers.Common;

// Real complete Helpers.Common, real Ret entry trees and real TreeSharp. No
// simulated interrupt policy: only native/world boundaries in the fixture.
internal static class SharedPaladinInterruptRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Body)>();
        foreach (var entry in new (string Name, Func<Composite> Build)[] {
            ("Normal", Retribution.CreateRetributionPaladinNormalPullAndCombat),
            ("Instance", Retribution.CreateRetributionPaladinInstancePullAndCombat),
            ("Battleground", Retribution.CreateRetributionPaladinPvPPullAndCombat) })
        {
            var f = entry;
            cases.Add((f.Name + " Ret rejects a stale learned Protection interrupt", () => {
                Setup(); Ready("Avenger's Shield"); Tick(f.Build()); Expect("movement");
            }));
            cases.Add((f.Name + " Ret uses its legitimate Hammer of Justice instead", () => {
                Setup(); Ready("Avenger's Shield", "Hammer of Justice"); Tick(f.Build()); Expect("Hammer of Justice");
            }));
            cases.Add((f.Name + " ordinary Ret interrupt remains available", () => {
                Setup(); Ready("Hammer of Justice"); Tick(f.Build()); Expect("Hammer of Justice");
            }));
        }
        foreach (TalentSpec spec in Enum.GetValues<TalentSpec>())
            foreach (bool fallback in new[] { false, true })
            {
                var role = spec; bool useFallback = fallback;
                cases.Add(($"direct {role} shared helper, fallback={useFallback}", () => {
                    Setup(); TalentManager.CurrentSpec = role; Ready("Avenger's Shield");
                    if (useFallback) Ready("Hammer of Justice");
                    Tick(Shared.CreateInterruptSpellCast(_ => StyxWoW.Me.CurrentTarget));
                    Expect(role == TalentSpec.ProtectionPaladin ? "Avenger's Shield" : useFallback ? "Hammer of Justice" : null);
                }));
            }
        foreach (string name in new[] { "Avenger's Shield", "Hammer of Justice" })
            foreach (string mutation in new[] { "target-reference", "target-guid", "player-reference", "player-guid", "spec", "cast-ended", "uninterruptible", "dead", "unchanged" })
            {
                string spell = name, change = mutation;
                cases.Add(($"{spell} cast setup revalidates {change}", () => {
                    Setup(); TalentManager.CurrentSpec = TalentSpec.ProtectionPaladin; Ready(spell);
                    var target = StyxWoW.Me.CurrentTarget!;
                    Fixture.BeforeDispatch = () => {
                        switch (change)
                        {
                            case "target-reference": StyxWoW.Me.CurrentTarget = new UnitState { Guid=99, IsCasting=true, CanInterruptCurrentSpellCast=true }; break;
                            case "target-guid": target.Guid++; break;
                            case "player-reference": StyxWoW.Me = new Player { Guid=99, CurrentTarget=target }; break;
                            case "player-guid": StyxWoW.Me.Guid++; break;
                            case "spec": TalentManager.CurrentSpec = TalentSpec.RetributionPaladin; break;
                            case "cast-ended": target.IsCasting=false; break;
                            case "uninterruptible": target.CanInterruptCurrentSpellCast=false; break;
                            case "dead": target.IsAlive=false; break;
                        }
                    };
                    Tick(Shared.CreateInterruptSpellCast(_ => StyxWoW.Me.CurrentTarget));
                    Expect(change == "unchanged" ? spell : null);
                }));
            }
        cases.Add(("missing target cannot throw in the shared helper", () => {
            Setup(); StyxWoW.Me.CurrentTarget=null; Ready("Hammer of Justice");
            Tick(Shared.CreateInterruptSpellCast(_ => StyxWoW.Me.CurrentTarget)); Expect(null);
        }));
        cases.Add(("selector loss between observations does not dereference null", () => {
            Setup(); Ready("Hammer of Justice"); var target=StyxWoW.Me.CurrentTarget; int observations=0;
            Tick(Shared.CreateInterruptSpellCast(_ => ++observations == 1 ? target : null)); Expect(null);
        }));
        cases.Add(("selector receives the original caller context on revalidation", () => {
            Setup(); Ready("Hammer of Justice"); var context=new object();
            Tick(Shared.CreateInterruptSpellCast(seen => { Check(ReferenceEquals(seen,context),"selector context was replaced"); return StyxWoW.Me.CurrentTarget; }), context);
            Expect("Hammer of Justice");
        }));
        int passed=0, assertions=0, unexpected=0;
        foreach (var item in cases)
        {
            try { item.Body(); passed++; Console.WriteLine("PASS shared Paladin interrupt: "+item.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL shared Paladin interrupt assertion: "+item.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR shared Paladin interrupt fixture: "+item.Name+": "+e); }
        }
        Console.WriteLine($"Shared Paladin interrupt scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual complete shared helper; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Shared Paladin interrupt regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static void Setup()
    {
        Fixture.Reset(); StyxWoW.Me.Guid=1; StyxWoW.Me.CurrentTarget!.Guid=2;
        StyxWoW.Me.CurrentTarget.IsCasting=true; StyxWoW.Me.CurrentTarget.CanInterruptCurrentSpellCast=true;
    }
    private static void Ready(params string[] names) { Fixture.Known.UnionWith(names); Fixture.Ready.UnionWith(names); }
    private static void Tick(Composite root, object? context=null)
    {
        root.Start(context!);
        try { Check(root.Tick(context!)!=RunStatus.Running,"unexpected blocking"); Check(Fixture.Exceptions.Count==0,"shared helper swallowed "+string.Join(';',Fixture.Exceptions.Select(e=>e.GetType().Name))); }
        finally { root.Stop(context!); }
    }
    private static void Expect(string? value) => Check(Fixture.Selected==value,$"expected {value??"no dispatch"}, got {Fixture.Selected}; "+string.Join(',',Fixture.Trace));
    private static void Check(bool ok,string text) { if(!ok) throw new AssertionFailure(text); }
}
