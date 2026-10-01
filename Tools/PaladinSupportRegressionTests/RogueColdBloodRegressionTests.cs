// Complete production Assassination trees run against controlled external unit,
// spell admission, movement and talent observations. No replacement rotation.
using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Rogue;
using Singular.Managers;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

internal static class RogueColdBloodRegressionTests
{
    private static int _cases;
    private static int _passed;
    private static int _assertions;
    private static int _unexpected;

    [ModuleInitializer]
    internal static void Run()
    {
        var owners = new (string Name, Func<Composite> Create)[]
        {
            ("Normal", Assassination.CreateAssaRogueNormalCombat),
            ("Battleground", Assassination.CreateAssaRoguePvPCombat),
            ("Instance", Assassination.CreateAssaRogueInstanceCombat)
        };
        foreach (var owner in owners)
        {
            for (int points = 0; points <= 5; points++)
            {
                int combo = points;
                foreach (bool envenomKnown in new[] { false, true })
                {
                    bool known = envenomKnown;
                    Case($"{owner.Name} admission cp={combo} envenom={known}", () =>
                    {
                        Prepare(combo, "Cold Blood", "Sinister Strike");
                        if (known) Fixture.Known.Add("Envenom");
                        Tick(owner.Create, combo >= 4 ? "Cold Blood" : "Sinister Strike");
                    });
                }
                foreach (string builder in new[] { "Mutilate", "Sinister Strike", "Mutilate-unavailable" })
                {
                    string choice = builder;
                    Case($"{owner.Name} retained buff cp={combo} builder={choice}", () =>
                    {
                        Prepare(combo, "Cold Blood", "Sinister Strike");
                        Fixture.Aura(StyxWoW.Me, "Cold Blood", StyxWoW.Me.Guid, 14177);
                        if (choice != "Sinister Strike") Fixture.Known.Add("Mutilate");
                        if (choice == "Mutilate-unavailable") Fixture.Unavailable.Add("Mutilate");
                        Tick(owner.Create, choice == "Mutilate" ? "Mutilate" : "Sinister Strike");
                    });
                }
            }
            foreach (int combo in new[] { 4, 5 })
            foreach (double health in new[] { 20.0, 34.999, 35.0, 80.0 })
            foreach (bool envenomKnown in new[] { false, true })
            {
                Case($"{owner.Name} preserved threshold cp={combo} hp={health} envenom={envenomKnown}", () =>
                {
                    Prepare(combo, "Cold Blood", "Sinister Strike");
                    StyxWoW.Me.CurrentTarget!.HealthPercent = health;
                    // A learned but presently unavailable finisher preserves the
                    // original threshold decision without masking it by a cast.
                    if (envenomKnown)
                    {
                        Fixture.Known.Add("Envenom");
                        Fixture.Unavailable.Add("Envenom");
                    }
                    bool expectedColdBlood = combo == 5 || health >= 35 || !envenomKnown;
                    Tick(owner.Create, expectedColdBlood ? "Cold Blood" : "Sinister Strike");
                });
            }
            foreach (int combo in new[] { 0, 1, 2, 3 })
            {
                Case($"{owner.Name} unavailable cooldown cp={combo}", () =>
                {
                    Prepare(combo, "Cold Blood", "Sinister Strike");
                    Fixture.Unavailable.Add("Cold Blood");
                    Tick(owner.Create, "Sinister Strike");
                });
            }
            foreach (int combo in new[] { 4, 5 })
            foreach (string finisher in new[] { "Eviscerate", "Envenom" })
            {
                Case($"{owner.Name} retained buff prioritizes {finisher} cp={combo}", () =>
                {
                    Prepare(combo, "Cold Blood", "Mutilate", "Sinister Strike", finisher);
                    StyxWoW.Me.CurrentTarget!.Elite = true;
                    Fixture.Aura(StyxWoW.Me, "Cold Blood", StyxWoW.Me.Guid, 14177);
                    Tick(owner.Create, finisher);
                });
            }
            Case(owner.Name + " all builders unavailable preserves failure", () =>
            {
                Prepare(0, "Cold Blood", "Mutilate", "Sinister Strike");
                Fixture.Aura(StyxWoW.Me, "Cold Blood", StyxWoW.Me.Guid, 14177);
                Fixture.Unavailable.UnionWith(new[] { "Mutilate", "Sinister Strike" });
                Tick(owner.Create, null);
            });
            Case(owner.Name + " no learned builder does not submit premature cooldown", () =>
            {
                Prepare(0, "Cold Blood");
                Tick(owner.Create, null);
            });
            Case(owner.Name + " declared Vanish opener remains with Cold Blood", () =>
            {
                Prepare(0, "Cold Blood", "Mutilate", "Sinister Strike", "Garrote");
                Fixture.Aura(StyxWoW.Me, "Cold Blood", StyxWoW.Me.Guid, 14177);
                Fixture.Aura(StyxWoW.Me, "Vanish", StyxWoW.Me.Guid, 1856);
                Tick(owner.Create, owner.Name == "Normal" ? null : "Garrote");
            });
            Case(owner.Name + " declared Vanish opener is independent of Cold Blood", () =>
            {
                Prepare(0, "Mutilate", "Sinister Strike", "Garrote");
                Fixture.Aura(StyxWoW.Me, "Vanish", StyxWoW.Me.Guid, 1856);
                Tick(owner.Create, owner.Name == "Normal" ? null : "Garrote");
            });
            Case(owner.Name + " global cooldown suspension remains", () =>
            {
                Prepare(0, "Cold Blood", "Mutilate", "Sinister Strike");
                Fixture.GlobalCooldown = true;
                Tick(owner.Create, null);
            });
            Case(owner.Name + " persisted cooldown survives a combo-point target reset", () =>
            {
                Prepare(4, "Cold Blood", "Mutilate", "Sinister Strike", "Eviscerate");
                Composite tree = owner.Create();
                Tick(() => tree, "Cold Blood");
                Fixture.Aura(StyxWoW.Me, "Cold Blood", StyxWoW.Me.Guid, 14177);
                StyxWoW.Me.ComboPoints = 0;
                StyxWoW.Me.CurrentTarget = Target(3);
                Fixture.Attempts.Clear();
                Tick(() => tree, "Mutilate");
            });
            Case(owner.Name + " unavailable damaging finishers permit a learned builder", () =>
            {
                Prepare(4, "Cold Blood", "Mutilate", "Sinister Strike", "Envenom", "Eviscerate");
                StyxWoW.Me.CurrentTarget!.Elite = true;
                Fixture.Aura(StyxWoW.Me, "Cold Blood", StyxWoW.Me.Guid, 14177);
                Fixture.Unavailable.UnionWith(new[] { "Envenom", "Eviscerate" });
                Tick(owner.Create, "Mutilate");
            });
            Case(owner.Name + " intended cooldown then Eviscerate sequence remains", () =>
            {
                Prepare(4, "Cold Blood", "Sinister Strike", "Eviscerate");
                Composite tree = owner.Create();
                Tick(() => tree, "Cold Blood");
                Fixture.Aura(StyxWoW.Me, "Cold Blood", StyxWoW.Me.Guid, 14177);
                Fixture.Attempts.Clear();
                Tick(() => tree, "Eviscerate");
            });
            Case(owner.Name + " low-point builder advances across two observations", () =>
            {
                Prepare(0, "Cold Blood", "Sinister Strike", "Eviscerate");
                Composite tree = owner.Create();
                Tick(() => tree, "Sinister Strike");
                StyxWoW.Me.ComboPoints = 1;
                Fixture.Attempts.Clear();
                Tick(() => tree, "Sinister Strike");
            });
        }
        Fixture.Reset();
        Console.WriteLine($"Rogue Cold Blood scenarios: {_passed}/{_cases}; assertions={_assertions}; unexpected={_unexpected}; complete Assassination owners, real TreeSharp, controlled observations, no game.");
        if (_assertions != 0 || _unexpected != 0)
            throw new InvalidOperationException("Rogue Cold Blood regression failures");
    }

    private static WoWUnit Target(ulong guid = 2) => new()
    {
        Guid = guid, IsFriendly = false, IsAlive = true, IsValid = true,
        Distance = 3, HealthPercent = 80, InLineOfSpellSight = true
    };

    private static void Prepare(int combo, params string[] known)
    {
        Fixture.Reset();
        StyxWoW.Me.Class = WoWClass.Rogue;
        StyxWoW.Me.Combat = true;
        StyxWoW.Me.PowerType = WoWPowerType.Energy;
        StyxWoW.Me.ComboPoints = combo;
        StyxWoW.Me.CurrentTarget = Target();
        TalentManager.CurrentSpec = TalentSpec.AssasinationRogue;
        Fixture.Known.UnionWith(known);
    }

    private static void Tick(Func<Composite> create, string? expected)
    {
        Fixture.Tick(create());
        string? actual = Fixture.Attempts.Count == 0 ? null : Fixture.Attempts[^1].Spell;
        if (actual != expected || Fixture.Attempts.Count > 1)
            throw new ExpectedFailure($"Expected {expected ?? "no spell"}; got {actual ?? "no spell"}; attempts={Fixture.Attempts.Count}");
        if (expected != null)
        {
            ulong target = expected == "Cold Blood" ? StyxWoW.Me.Guid : StyxWoW.Me.CurrentTarget!.Guid;
            if (Fixture.Attempts[^1].Target != target)
                throw new ExpectedFailure("The selected spell has the wrong recipient");
        }
    }

    private static void Case(string name, System.Action run)
    {
        _cases++;
        try { run(); _passed++; }
        catch (ExpectedFailure error) { _assertions++; Console.WriteLine("FAIL Rogue " + name + ": " + error.Message); }
        catch (Exception error) { _unexpected++; Console.WriteLine("ERROR Rogue " + name + ": " + error); }
    }

    private sealed class ExpectedFailure : Exception
    {
        internal ExpectedFailure(string message) : base(message) { }
    }
}
