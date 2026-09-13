using System.Reflection;
using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Paladin;
using Styx;
using TreeSharp;

internal static class ExorcismDecisionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var factories = new (string Name, Func<Composite> Make)[]
        {
            ("Normal", Retribution.CreateRetributionPaladinNormalPullAndCombat),
            ("Battleground", Retribution.CreateRetributionPaladinPvPPullAndCombat),
            ("Instance", Retribution.CreateRetributionPaladinInstancePullAndCombat)
        };
        var failures = new List<string>();
        int cases = 0, rows = 0;
        void Case(string name, System.Action run)
        {
            cases++;
            try { Fixture.Reset(1, 33); run(); Console.WriteLine("PASS Exorcism: " + name); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); Console.Error.WriteLine("FAIL Exorcism: " + failures[^1]); }
        }
        foreach (var factory in factories)
        {
            Case(factory.Name + " active melee does not lose its fallback to an unprocced hard cast", () =>
            { Ready("Exorcism"); Tick(factory.Make()); Expect("movement"); });
            Case(factory.Name + " auto-attack enabled at range does not remove the stationary opener", () =>
            { Ready("Exorcism"); StyxWoW.Me.CurrentTarget!.Distance = 20; Tick(factory.Make()); Expect("Exorcism"); });
            Case(factory.Name + " moving ranged player yields instead of attempting a hard cast", () =>
            { Ready("Exorcism"); StyxWoW.Me.IsMoving = true; StyxWoW.Me.CurrentTarget!.Distance = 20; Tick(factory.Make()); Expect("movement"); });
            Case(factory.Name + " confirmed proc permits the moving melee instant", () =>
            { Ready("Exorcism"); Proc(); StyxWoW.Me.IsMoving = true; Tick(factory.Make()); Expect("Exorcism"); });
            Case(factory.Name + " trained talent without a proc does not hard cast", () =>
            { Ready("Exorcism"); Fixture.Known.Add("The Art of War"); StyxWoW.Me.CurrentTarget!.Distance = 20; Tick(factory.Make()); Expect("movement"); });
            Case(factory.Name + " actual proc is usable even if talent discovery is incomplete", () =>
            { Ready("Exorcism"); Proc(); Tick(factory.Make()); Expect("Exorcism"); });
            Case(factory.Name + " ready melee strike precedes non-proc Exorcism", () =>
            { Ready("Exorcism", "Crusader Strike"); Tick(factory.Make()); Expect("Crusader Strike"); });
            Case(factory.Name + " ready judgement precedes a non-proc ranged filler", () =>
            { Ready("Exorcism", "Judgement of Light"); StyxWoW.Me.CurrentTarget!.Distance = 20; Tick(factory.Make()); Expect("Judgement of Light"); });
            Case(factory.Name + " undead classification alone cannot authorize a melee hard cast", () =>
            { Ready("Exorcism"); StyxWoW.Me.CurrentTarget!.UndeadOrDemon = true; Tick(factory.Make()); Expect("movement"); });
            Case(factory.Name + " missing target never causes an exception", () =>
            { Ready("Exorcism"); StyxWoW.Me.CurrentTarget = null; Tick(factory.Make()); Expect("movement"); });
            Case(factory.Name + " unavailable cast yields to movement", () =>
            { Fixture.Known.Add("Exorcism"); StyxWoW.Me.CurrentTarget!.Distance = 20; Tick(factory.Make()); Expect("movement"); });
            Case(factory.Name + " changed movement state is evaluated on the next decision", () =>
            {
                Ready("Exorcism"); StyxWoW.Me.CurrentTarget!.Distance = 20; StyxWoW.Me.IsMoving = true;
                Composite root = factory.Make(); Tick(root); Expect("movement");
                StyxWoW.Me.IsMoving = false; Fixture.Selected = null; Fixture.Trace.Clear();
                Tick(root); Expect("Exorcism");
            });
            Case(factory.Name + " level 33 and 80 policy matrix", () =>
            {
                var errors = new List<string>();
                foreach (int level in new[] { 33, 80 })
                    for (int flags = 0; flags < 32; flags++)
                    {
                        bool known = (flags & 1) != 0, proc = (flags & 2) != 0,
                            melee = (flags & 4) != 0, auto = (flags & 8) != 0, moving = (flags & 16) != 0;
                        Fixture.Reset(1, level); Ready("Exorcism");
                        if (known) Fixture.Known.Add("The Art of War");
                        if (proc) Proc();
                        StyxWoW.Me.CurrentTarget!.Distance = melee ? 3 : 20;
                        StyxWoW.Me.IsAutoAttacking = auto; StyxWoW.Me.IsMoving = moving;
                        Tick(factory.Make()); rows++;
                        // This fixture controls observation inputs; it does not model swing
                        // resets or claim that a real Common.AutoAttack leaves melee disabled.
                        bool allowed = proc || (!known && !moving && !(melee && auto));
                        string expected = allowed ? "Exorcism" : "movement";
                        if (Fixture.Selected != expected) errors.Add($"level={level} flags={flags}: {Fixture.Selected} != {expected}");
                    }
                Check(errors.Count == 0, string.Join("; ", errors));
            });
        }
        Console.WriteLine($"Exorcism decisions: {cases - failures.Count}/{cases}; matrix rows={rows}; linked production rotation and TreeSharp; controlled observations/dispatch; no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
    private static void Ready(params string[] spells) { Fixture.Known.UnionWith(spells); Fixture.Ready.UnionWith(spells); }
    private static void Proc() => StyxWoW.Me.Auras["The Art of War"] = new Aura { Name = "The Art of War" };
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Expect(string expected) => Check(Fixture.Selected == expected,
        $"expected {expected}, observed {Fixture.Selected}; trace={string.Join(",", Fixture.Trace)}");
    private static void Tick(Composite root)
    {
        root.Start(null!);
        try
        {
            Check(root.Tick(null!) != RunStatus.Running, "controlled dispatch must finish one decision");
            Check(Fixture.Exceptions.Count == 0, "swallowed exception: " + string.Join(";", Fixture.Exceptions.Select(e => e.Message)));
        }
        finally { root.Stop(null!); }
    }
}
