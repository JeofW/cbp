using System.Reflection;
using Singular.ClassSpecific.Paladin;
using Singular.Dynamics;
using Styx;
using TreeSharp;

var factories = new (string Name, Func<Composite> Make)[]
{
    ("Normal", Retribution.CreateRetributionPaladinNormalPullAndCombat),
    ("Battleground", Retribution.CreateRetributionPaladinPvPPullAndCombat),
    ("Instance", Retribution.CreateRetributionPaladinInstancePullAndCombat)
};
var failures = new List<string>();
int scenarios = 0, matrixRows = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void Case(string name, System.Action test)
{
    scenarios++;
    try { Fixture.Reset(); test(); Console.WriteLine("PASS paladin decision: " + name); }
    catch (Exception error) { failures.Add(name + ": " + error.Message); Console.Error.WriteLine("FAIL paladin decision: " + failures[^1]); }
}
void Run(Func<Composite> factory)
{
    Composite root = factory();
    root.Start(null!);
    try
    {
        RunStatus result = root.Tick(null!);
        Check(result != RunStatus.Running, "controlled ready/rejected actions must complete one decision tick");
        Check(Fixture.Exceptions.Count == 0, "target loss produced swallowed exception(s): " + string.Join(";", Fixture.Exceptions.Select(e => e.Message)));
    }
    finally { root.Stop(null!); }
}
void Spells(params string[] ready) { foreach (string name in ready) { Fixture.Known.Add(name); Fixture.Ready.Add(name); } }
void Expect(string expected) => Check(Fixture.Selected == expected,
    $"expected {expected}, observed {Fixture.Selected}; trace={string.Join(",", Fixture.Trace)}");
foreach (var factory in factories)
{
    Case(factory.Name + " single-target Divine Storm remains a ready melee action", () =>
    { Spells("Divine Storm"); Run(factory.Make); Expect("Divine Storm"); });
    Case(factory.Name + " unavailable Divine Storm cannot disable Crusader Strike on four targets", () =>
    { Fixture.Reset(4); Spells("Crusader Strike"); Fixture.Known.Add("Divine Storm"); Run(factory.Make); Expect("Crusader Strike"); });
    Case(factory.Name + " unknown Divine Storm retains Crusader Strike", () =>
    { Fixture.Reset(4); Spells("Crusader Strike"); Run(factory.Make); Expect("Crusader Strike"); });
    Case(factory.Name + " level-33 availability falls through to Judgement", () =>
    { Fixture.Reset(1, 33); Spells("Judgement of Light"); Run(factory.Make); Expect("Judgement of Light"); });
    Case(factory.Name + " rejected melee actions yield to movement", () =>
    { Fixture.Known.UnionWith(new[] { "Crusader Strike", "Divine Storm" }); Run(factory.Make); Expect("movement"); });
    Case(factory.Name + " area safety rejection is preserved", () =>
    { Fixture.Reset(4); Spells("Divine Storm"); Fixture.AreaSafe = false; Run(factory.Make); Expect("movement"); });
    Case(factory.Name + " unavailable melee range yields to movement", () =>
    { Spells("Crusader Strike", "Divine Storm"); StyxWoW.Me.CurrentTarget!.Distance = 20; Run(factory.Make); Expect("movement"); });
    foreach (string flag in new[] { "Horde Flag", "Alliance Flag" })
        Case(factory.Name + " retains " + flag + " instead of selecting Divine Shield", () =>
        { Spells("Divine Shield"); StyxWoW.Me.HealthPercent = 10; StyxWoW.Me.Auras.Add(flag, new Aura { Name = flag }); Run(factory.Make); Expect("movement"); });
    Case(factory.Name + " emergency shield remains available without a flag", () =>
    { Spells("Divine Shield"); StyxWoW.Me.HealthPercent = 10; Run(factory.Make); Expect("Divine Shield"); });
    Case(factory.Name + " Forbearance veto remains intact", () =>
    { Spells("Divine Shield"); StyxWoW.Me.HealthPercent = 10; StyxWoW.Me.Auras.Add("Forbearance", new Aura()); Run(factory.Make); Expect("movement"); });
    Case(factory.Name + " combat composition is not registered as a healing behavior", () =>
        Check(!factory.Make.Method.GetCustomAttributes<BehaviorAttribute>().Any(a => a.Type == BehaviorType.Heal),
            "offensive factory advertises Heal and competes with the dedicated healing owner"));
    Case(factory.Name + " disappearing target never throws from a self-buff predicate", () =>
    { StyxWoW.Me.CurrentTarget = null; Run(factory.Make); });
    Case(factory.Name + " exhaustive melee availability matrix", () =>
    {
        var errors = new List<string>();
        foreach (int count in new[] { 1, 2, 3, 4, 5 })
            for (int known = 0; known < 4; known++)
                for (int ready = 0; ready < 4; ready++)
                {
                    if ((ready & ~known) != 0) continue;
                    matrixRows++;
                    Fixture.Reset(count);
                    if ((known & 1) != 0) Fixture.Known.Add("Crusader Strike");
                    if ((known & 2) != 0) Fixture.Known.Add("Divine Storm");
                    if ((ready & 1) != 0) Fixture.Ready.Add("Crusader Strike");
                    if ((ready & 2) != 0) Fixture.Ready.Add("Divine Storm");
                    Run(factory.Make);
                    // Existing source order is CS then DS. Availability of one
                    // independent action must not make another ready action vanish.
                    string expected = (ready & 1) != 0 ? "Crusader Strike" : (ready & 2) != 0 ? "Divine Storm" : "movement";
                    if (Fixture.Selected != expected) errors.Add($"n={count},known={known},ready={ready}: {Fixture.Selected} != {expected}");
                }
        Check(errors.Count == 0, string.Join(";", errors));
    });
}
Console.WriteLine($"Paladin decisions: {scenarios - failures.Count}/{scenarios}; exhaustive matrix rows={matrixRows}. Actual linked rotation/TreeSharp, controlled world/dispatch, no game attached.");
if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));

RetributionTacticsRegressionTests.Run();
SharedPaladinInterruptRegressionTests.Run();
