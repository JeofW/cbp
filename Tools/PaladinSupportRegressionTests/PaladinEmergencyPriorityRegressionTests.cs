using System.Runtime.CompilerServices;
using Singular.ClassSpecific.Paladin;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

// Complete tracked Holy/Protection factories and real TreeSharp. Only observed
// units, spell availability/dispatch, movement and rest are controlled boundaries.
// These cases prove factory priorities, not throughput or live survival.
internal static class PaladinEmergencyPriorityRegressionTests
{
    private sealed class AssertionFailure : Exception
    {
        internal AssertionFailure(string message) : base(message) { }
    }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Test)>();
        void Add(string name, System.Action test) => cases.Add((name, () => { Fixture.Reset(); test(); }));

        foreach (bool raidValue in new[] { false, true })
        {
            bool raid = raidValue;
            Add($"Holy emergency on {(raid ? "raid" : "party")} tank precedes Beacon", () =>
            {
                var target = HolyTarget(10, raid);
                Know("Beacon of Light", "Lay on Hands", "Holy Shock");
                Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
                Expect("Lay on Hands", target);
            });
            foreach (string fallbackValue in new[] { "Holy Shock", "Flash of Light", "Holy Light" })
            {
                string fallback = fallbackValue;
                Add($"Holy unavailable emergency cooldown preserves {fallback}, raid={raid}", () =>
                {
                    var target = HolyTarget(10, raid);
                    Know("Beacon of Light", "Lay on Hands", fallback);
                    Fixture.Unavailable.Add("Lay on Hands");
                    Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
                    Expect(fallback, target);
                });
            }
        }

        Add("Holy unlearned Lay on Hands still heals the critical tank before Beacon", () =>
        {
            var target = HolyTarget(10);
            Know("Beacon of Light", "Holy Shock");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            Expect("Holy Shock", target);
        });
        Add("Holy another player's Forbearance does not reject Lay on Hands", () =>
        {
            var target = HolyTarget(10);
            Fixture.Aura(target, "Forbearance", 99, 25771);
            Know("Beacon of Light", "Lay on Hands", "Holy Shock");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            Expect("Lay on Hands", target);
        });
        Add("Holy self Forbearance still requires an ordinary emergency heal", () =>
        {
            HolyTarget(10);
            StyxWoW.Me.HealthPercent = 10;
            Fixture.Aura(StyxWoW.Me, "Forbearance", 99, 25771);
            Know("Lay on Hands", "Holy Shock");
            Fixture.Tick(Holy.CreatePaladinHealBehavior(true));
            Expect("Holy Shock", StyxWoW.Me);
        });
        foreach (int markerValue in new[] { 61987, 61988 })
        {
            int marker = markerValue;
            Add("Holy self restriction marker prevents Lay on Hands: " + marker, () =>
            {
                HolyTarget(10); StyxWoW.Me.HealthPercent = 10;
                Fixture.Aura(StyxWoW.Me, "localized restriction", 99, marker);
                Know("Lay on Hands", "Holy Shock");
                Fixture.Tick(Holy.CreatePaladinHealBehavior(true));
                Expect("Holy Shock", StyxWoW.Me);
            });
            Add("Holy another player's restriction marker does not reject Lay on Hands: " + marker, () =>
            {
                var target = HolyTarget(10);
                Fixture.Aura(target, "localized restriction", 99, marker);
                Know("Lay on Hands", "Holy Shock");
                Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
                Expect("Lay on Hands", target);
            });
        }
        Add("Holy exact emergency threshold precedes maintenance", () =>
        {
            var target = HolyTarget(SingularSettings.Instance.Paladin.LayOnHandsHealth);
            Know("Beacon of Light", "Lay on Hands");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            Expect("Lay on Hands", target);
        });
        Add("Holy above emergency threshold retains Beacon maintenance", () =>
        {
            var target = HolyTarget(SingularSettings.Instance.Paladin.LayOnHandsHealth + 1);
            Know("Beacon of Light", "Lay on Hands", "Holy Shock");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            Expect("Beacon of Light", target);
        });
        Add("Holy existing own Beacon prevents another maintenance cast", () =>
        {
            var target = HolyTarget(50);
            Fixture.Aura(target, "Beacon of Light", StyxWoW.Me.Guid);
            Know("Beacon of Light", "Holy Shock");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            Expect("Holy Shock", target);
        });
        Add("Holy another caster's Beacon does not replace our assignment", () =>
        {
            var target = HolyTarget(50);
            Fixture.Aura(target, "Beacon of Light", 99);
            Know("Beacon of Light", "Holy Shock");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            Expect("Beacon of Light", target);
        });
        Add("Holy critical non-tank is the emergency recipient", () =>
        {
            var target = HolyTarget(10);
            Group.Tanks.Clear();
            Know("Beacon of Light", "Lay on Hands");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            Expect("Lay on Hands", target);
        });
        Add("Holy self-only recovery keeps its emergency recipient", () =>
        {
            HolyTarget(10);
            StyxWoW.Me.HealthPercent = 10;
            Know("Lay on Hands");
            Fixture.Tick(Holy.CreatePaladinHealBehavior(true));
            Expect("Lay on Hands", StyxWoW.Me);
        });
        Add("Holy self emergency does not require unrelated aura metadata", () =>
        {
            HolyTarget(10); StyxWoW.Me.HealthPercent = 10;
            Fixture.Aura(StyxWoW.Me, "", 99, 65000); StyxWoW.Me.MetadataUnknown = true;
            Know("Lay on Hands");
            TickWithCompleteRestrictionIds(Holy.CreatePaladinHealBehavior(true));
            Expect("Lay on Hands", StyxWoW.Me);
        });
        Add("Holy no selected recipient dispatches nothing", () =>
        {
            TalentManager.CurrentSpec = TalentSpec.HolyPaladin;
            Know("Beacon of Light", "Lay on Hands", "Holy Shock");
            Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
            None();
        });
        foreach (string vetoValue in new[] { "dead", "invalid", "out-of-sight", "out-of-range" })
        {
            string veto = vetoValue;
            Add("Holy emergency still respects recipient " + veto, () =>
            {
                var target = HolyTarget(10);
                switch (veto)
                {
                    case "dead": target.IsAlive = false; break;
                    case "invalid": target.IsValid = false; break;
                    case "out-of-sight": target.InLineOfSpellSight = false; break;
                    case "out-of-range": target.Distance = 45; break;
                }
                Know("Beacon of Light", "Lay on Hands", "Holy Shock");
                Fixture.Tick(Holy.CreateHolyPaladinHealBehavior());
                None();
            });
        }
        Add("Holy critical selected ally blocks starting Plea", () =>
        {
            HolyTarget(10);
            StyxWoW.Me.ManaPercent = 10;
            Know("Divine Plea");
            Fixture.Tick(Holy.CreateHolyPaladinCombatBuffsBehavior());
            None();
        });
        Add("Holy critical self blocks Plea even with a healthy selected ally", () =>
        {
            HolyTarget(95);
            StyxWoW.Me.HealthPercent = 10;
            StyxWoW.Me.ManaPercent = 10;
            Know("Divine Plea");
            Fixture.Tick(Holy.CreateHolyPaladinCombatBuffsBehavior());
            None();
        });
        Add("Holy known healthy recovery remains available", () =>
        {
            HolyTarget(95);
            StyxWoW.Me.ManaPercent = 10;
            Know("Divine Plea");
            Fixture.Tick(Holy.CreateHolyPaladinCombatBuffsBehavior());
            Expect("Divine Plea", StyxWoW.Me);
        });
        Add("Holy full mana does not activate recovery", () =>
        {
            HolyTarget(95);
            Know("Divine Plea");
            Fixture.Tick(Holy.CreateHolyPaladinCombatBuffsBehavior());
            None();
        });
        foreach (string vetoValue in new[] { "dead", "ghost", "invalid", "mounted", "transport", "casting", "channeling",
                                             "unknown-health", "infinite-health", "negative-health", "excess-health",
                                             "unknown-mana", "infinite-mana", "negative-mana", "excess-mana" })
        {
            string veto = vetoValue;
            Add("Holy Plea preserves " + veto + " admission", () =>
            {
                HolyTarget(95);
                StyxWoW.Me.ManaPercent = 10;
                RejectState(StyxWoW.Me, veto);
                Know("Divine Plea");
                Fixture.Tick(Holy.CreateHolyPaladinCombatBuffsBehavior());
                None();
            });
        }
        foreach (double healthValue in new[] { double.NaN, double.PositiveInfinity, -1d, 101d, 0d })
        {
            double health = healthValue;
            Add("Holy unknown or critical recipient health refuses Plea: " + health, () =>
            {
                HolyTarget(health);
                StyxWoW.Me.ManaPercent = 10;
                Know("Divine Plea");
                Fixture.Tick(Holy.CreateHolyPaladinCombatBuffsBehavior());
                None();
            });
        }

        foreach (string entryValue in new[] { "Heal", "CombatBuffs" })
        {
            string entry = entryValue;
            Composite Build() => entry == "Heal" ? Protection.CreateProtectionPaladinHeal()
                : Protection.CreateProtectionPaladinCombatBuffs();
            void Setup()
            {
                TalentManager.CurrentSpec = TalentSpec.ProtectionPaladin;
                StyxWoW.Me.Combat = true;
                StyxWoW.Me.HealthPercent = 10;
            }
            Add($"Protection/{entry}: emergency heal precedes damage cooldown", () =>
            {
                Setup(); Know("Lay on Hands", "Divine Protection", "Avenging Wrath");
                Fixture.Tick(Build()); Expect("Lay on Hands", StyxWoW.Me);
            });
            Add($"Protection/{entry}: Lay on Hands requires a heal receipt instead of a buff receipt", () =>
            {
                Setup(); Know("Lay on Hands"); Fixture.Tick(Build());
                Check(Fixture.RecoveryRoutes.Single()==("Lay on Hands",false),
                    "direct emergency healing requested an aura acknowledgement");
            });
            foreach (string readySpell in new[] { "Lay on Hands", "Divine Protection" })
            {
                string ready = readySpell;
                Add($"Protection/{entry}: unrelated aura metadata does not block {ready}", () =>
                {
                    Setup(); Know(ready); Fixture.Aura(StyxWoW.Me, "", 99, 65000);
                    StyxWoW.Me.MetadataUnknown = true;
                    TickWithCompleteRestrictionIds(Build());
                    Expect(ready, StyxWoW.Me);
                });
            }
            Add($"Protection/{entry}: emergency heal precedes a pending taunt", () =>
            {
                Setup(); SingularSettings.Instance.EnableTaunting = true;
                TankManager.Instance.NeedToTaunt.Add(new WoWUnit { Guid = 99, IsFriendly = false });
                Know("Lay on Hands", "Divine Protection", "Hand of Reckoning");
                Fixture.Tick(Build()); Expect("Lay on Hands", StyxWoW.Me);
            });
            Add($"Protection/{entry}: unavailable heal permits defensive fallback", () =>
            {
                Setup(); Know("Lay on Hands", "Divine Protection", "Avenging Wrath");
                Fixture.Unavailable.Add("Lay on Hands");
                Fixture.Tick(Build()); Expect("Divine Protection", StyxWoW.Me);
            });
            Add($"Protection/{entry}: damage reduction applies above emergency-heal threshold", () =>
            {
                Setup(); StyxWoW.Me.HealthPercent = SingularSettings.Instance.Paladin.LayOnHandsHealth + 1;
                Know("Lay on Hands", "Divine Protection", "Avenging Wrath");
                Fixture.Tick(Build()); Expect("Divine Protection", StyxWoW.Me);
            });
            Add($"Protection/{entry}: Forbearance prohibits both protected defenses", () =>
            {
                Setup(); Fixture.Aura(StyxWoW.Me, "Forbearance", 99, 25771);
                Know("Lay on Hands", "Divine Protection");
                Fixture.Tick(Build()); None();
            });
            Add($"Protection/{entry}: emergency uses self rather than hostile target", () =>
            {
                Setup(); StyxWoW.Me.CurrentTarget = new WoWUnit { Guid = 99, IsFriendly = false };
                Know("Lay on Hands"); Fixture.Tick(Build()); Expect("Lay on Hands", StyxWoW.Me);
            });
            Add($"Protection/{entry}: no emergency means no defensive cooldown", () =>
            {
                Setup(); StyxWoW.Me.HealthPercent = 100;
                Know("Lay on Hands", "Divine Protection"); Fixture.Tick(Build()); None();
            });
            Add($"Protection/{entry}: moving does not prohibit an instant emergency heal", () =>
            {
                Setup(); StyxWoW.Me.IsMoving = true;
                Know("Lay on Hands"); Fixture.Tick(Build()); Expect("Lay on Hands", StyxWoW.Me);
            });
            Add($"Protection/{entry}: Avenging Wrath marker blocks both protected defenses", () =>
            {
                Setup(); Fixture.Aura(StyxWoW.Me, "localized restriction", 99, 61987);
                Know("Lay on Hands", "Divine Protection"); Fixture.Tick(Build()); None();
            });
            Add($"Protection/{entry}: immune-shield marker blocks self Lay on Hands", () =>
            {
                Setup(); Fixture.Aura(StyxWoW.Me, "localized restriction", 99, 61988);
                Know("Lay on Hands"); Fixture.Tick(Build()); None();
            });
            Add($"Protection/{entry}: inactive restriction marker does not block a ready defense", () =>
            {
                Setup(); Fixture.Aura(StyxWoW.Me, "localized restriction", 99, 61987);
                StyxWoW.Me.ObservedAuras.Last().IsActive = false;
                Know("Lay on Hands"); Fixture.Tick(Build()); Expect("Lay on Hands", StyxWoW.Me);
            });
            foreach (string vetoValue in new[] { "dead", "ghost", "invalid", "mounted", "transport", "casting", "channeling",
                                                 "unknown-health", "infinite-health", "negative-health", "excess-health" })
            {
                string veto = vetoValue;
                Add($"Protection/{entry}: preserves {veto} admission", () =>
                {
                    Setup(); RejectState(StyxWoW.Me, veto);
                    Know("Lay on Hands", "Divine Protection"); Fixture.Tick(Build()); None();
                });
            }
        }

        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS Paladin emergency: " + item.Name); }
            catch (AssertionFailure error)
            { assertions++; Console.Error.WriteLine("FAIL Paladin emergency assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error)
            { unexpected++; Console.Error.WriteLine("ERROR Paladin emergency fixture: " + item.Name + ": " + error); }
        }
        Fixture.Reset();
        Console.WriteLine($"Paladin emergency scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; linked complete Holy/Protection; controlled observations/dispatch; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Paladin emergency regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static WoWPlayer HolyTarget(double health, bool raid = false)
    {
        TalentManager.CurrentSpec = TalentSpec.HolyPaladin;
        StyxWoW.Me.Combat = true;
        var target = Fixture.Add(raid: raid);
        target.HealthPercent = health;
        HealerManager.Instance.FirstUnit = target;
        Group.Tanks.Add(target);
        return target;
    }
    private static void Know(params string[] spells) => Fixture.Known.UnionWith(spells);
    private static void TickWithCompleteRestrictionIds(Composite tree)
    {
        try { Fixture.Tick(tree); }
        catch (InvalidOperationException error) when (error.Message.StartsWith(
            "Swallowed exception: Styx.Helpers.ObservationUnavailableException", StringComparison.Ordinal))
        { throw new AssertionFailure("complete raw restriction IDs were rejected because unrelated aura metadata was unavailable"); }
    }
    private static void RejectState(WoWUnit unit, string state)
    {
        switch (state)
        {
            case "dead": unit.IsAlive = false; break;
            case "ghost": unit.IsGhost = true; break;
            case "invalid": unit.IsValid = false; break;
            case "mounted": unit.Mounted = true; break;
            case "transport": unit.IsOnTransport = true; break;
            case "casting": unit.IsCasting = true; break;
            case "channeling": unit.IsChanneling = true; break;
            case "unknown-health": unit.HealthPercent = double.NaN; break;
            case "infinite-health": unit.HealthPercent = double.PositiveInfinity; break;
            case "negative-health": unit.HealthPercent = -1; break;
            case "excess-health": unit.HealthPercent = 101; break;
            case "unknown-mana": unit.ManaPercent = double.NaN; break;
            case "infinite-mana": unit.ManaPercent = double.PositiveInfinity; break;
            case "negative-mana": unit.ManaPercent = -1; break;
            case "excess-mana": unit.ManaPercent = 101; break;
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }
    }
    private static void Expect(string spell, WoWUnit target) => Check(Fixture.Attempts.Count == 1
        && Fixture.Attempts[0] == (spell, target.Guid),
        "expected " + spell + " on " + target.Guid + ", got " + string.Join(',', Fixture.Attempts));
    private static void None() => Check(Fixture.Attempts.Count == 0, "unexpected dispatch " + string.Join(',', Fixture.Attempts));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new AssertionFailure(message);
    }
}
