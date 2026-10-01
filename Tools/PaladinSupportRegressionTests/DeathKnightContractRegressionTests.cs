using System.Runtime.CompilerServices;
using Singular.ClassSpecific.DeathKnight;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using DKCommon = Singular.ClassSpecific.DeathKnight.Common;

// All four tracked DK classes execute with real TreeSharp. Only external unit,
// glyph, spell availability/dispatch, settings, movement and time are controlled.
// These prove the named contracts, not complete rotation/role/PvP performance.
internal static class DeathKnightContractRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private sealed record Owner(string Name, TalentSpec Spec, Func<Composite> Build, bool Spread = true);
    private static readonly Owner[] Owners =
    {
        new("Blood/Normal", TalentSpec.BloodDeathKnight, Blood.CreateBloodDeathKnightNormalCombat),
        new("Blood/Instances", TalentSpec.BloodDeathKnight, Blood.CreateBloodDeathKnightInstanceCombat),
        new("Blood/PvP", TalentSpec.BloodDeathKnight, Blood.CreateBloodDeathKnightPvPCombat, false),
        new("Frost/Normal", TalentSpec.FrostDeathKnight, Frost.CreateFrostDeathKnightNormalCombat),
        new("Frost/Instances", TalentSpec.FrostDeathKnight, Frost.CreateFrostDeathKnightInstanceCombat),
        new("Frost/PvP", TalentSpec.FrostDeathKnight, Frost.CreateFrostDeathKnightPvPCombat, false),
        new("Unholy/Normal", TalentSpec.UnholyDeathKnight, Unholy.CreateUnholyDeathKnightNormalCombat),
        new("Unholy/Instances", TalentSpec.UnholyDeathKnight, Unholy.CreateUnholyDeathKnightInstanceCombat),
        new("Unholy/PvP", TalentSpec.UnholyDeathKnight, Unholy.CreateUnholyDeathKnightPvPCombat, false)
    };

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Test)>();
        void Add(string name, System.Action test) => cases.Add((name, () => { Fixture.Reset(); Setup(); test(); }));

        foreach (var choice in new[] { (TalentSpec.Lowbie, "Blood Presence"),
                                      (TalentSpec.BloodDeathKnight, "Frost Presence"),
                                      (TalentSpec.FrostDeathKnight, "Unholy Presence"),
                                      (TalentSpec.UnholyDeathKnight, "Unholy Presence") })
        {
            Add("preserve declared precombat presence: " + choice.Item1, () =>
            {
                TalentManager.CurrentSpec = choice.Item1;
                Know("Blood Presence", "Frost Presence", "Unholy Presence");
                Fixture.Tick(DKCommon.CreateDeathKnightPreCombatBuffs());
                Expect(choice.Item2, StyxWoW.Me);
            });
        }
        foreach (var owner in Owners)
        {
            Add(owner.Name + " retains its existing combat presence policy", () =>
            {
                TalentManager.CurrentSpec = owner.Spec;
                Know("Blood Presence", "Frost Presence", "Unholy Presence");
                Fixture.Tick(owner.Build());
                Expect(owner.Spec == TalentSpec.BloodDeathKnight ? "Frost Presence" : "Blood Presence", StyxWoW.Me);
            });
        }

        foreach (var sample in new[] { ("no equivalent buff", "", true),
                                       ("Battle Shout is not the stat buff", "Battle Shout", true),
                                       ("Strength of Earth preserves observed external stat buff", "Strength of Earth", false),
                                       ("own Horn remains active", "Horn of Winter", false) })
            Add("Horn: " + sample.Item1, () =>
            {
                Know("Horn of Winter");
                if (sample.Item2.Length != 0) Fixture.Aura(StyxWoW.Me, sample.Item2, 77);
                Fixture.Tick(DKCommon.CreateDeathKnightPreCombatBuffs());
                if (sample.Item3) Expect("Horn of Winter", StyxWoW.Me); else None();
            });
        Add("Horn ignores an inactive Strength of Earth observation", () =>
        {
            Fixture.Aura(StyxWoW.Me, "Strength of Earth", 77);
            StyxWoW.Me.ObservedAuras.Last().IsActive = false;
            Know("Horn of Winter"); Fixture.Tick(DKCommon.CreateDeathKnightPreCombatBuffs());
            Expect("Horn of Winter", StyxWoW.Me);
        });
        foreach (bool unavailable in new[] { false, true })
            Add("Horn respects " + (unavailable ? "cooldown/resource unavailability" : "unlearned ability"), () =>
            {
                if (unavailable) { Know("Horn of Winter"); Fixture.Unavailable.Add("Horn of Winter"); }
                Fixture.Tick(DKCommon.CreateDeathKnightPreCombatBuffs()); None();
            });

        foreach (var owner in Owners.Where(row => row.Spec == TalentSpec.BloodDeathKnight))
        {
            foreach (var sample in new[] { ("no unrelated passive", 89d, false, true),
                                           ("passive does not prohibit a learned ability", 89d, true, true),
                                           ("exact established threshold", 90d, true, false),
                                           ("full health", 100d, true, false) })
                Add(owner.Name + "/Rune Tap: " + sample.Item1, () =>
                {
                    TalentManager.CurrentSpec = owner.Spec;
                    StyxWoW.Me.HealthPercent = sample.Item2;
                    if (sample.Item3) Fixture.Aura(StyxWoW.Me, "Will of the Necropolis", StyxWoW.Me.Guid);
                    Know("Rune Tap"); Fixture.Tick(owner.Build());
                    if (sample.Item4) Expect("Rune Tap", StyxWoW.Me); else None();
                });
            foreach (bool unavailable in new[] { false, true })
                Add(owner.Name + "/Rune Tap: " + (unavailable ? "unavailable" : "unlearned"), () =>
                {
                    TalentManager.CurrentSpec = owner.Spec; StyxWoW.Me.HealthPercent = 20;
                    if (unavailable) { Know("Rune Tap"); Fixture.Unavailable.Add("Rune Tap"); }
                    Fixture.Tick(owner.Build()); None();
                });
        }

        foreach (var owner in Owners.Where(row => row.Spec == TalentSpec.FrostDeathKnight && row.Spread))
        {
            foreach (var sample in new[] {
                ("unglyphed fever expiring", false, 1d, 30d, false),
                ("glyphed fever expiring", true, 1d, 30d, true),
                ("glyphed blood plague expiring", true, 30d, 1d, true),
                ("unglyphed blood plague expiring", false, 30d, 1d, false),
                ("both diseases healthy", true, 30d, 30d, false),
                ("exact existing refresh threshold", true, 3d, 3d, false),
                ("both diseases expiring", true, 2d, 2d, true) })
                Add(owner.Name + "/primary refresh: " + sample.Item1, () =>
                {
                    TalentManager.CurrentSpec = owner.Spec;
                    if (sample.Item2) TalentManager.Glyphs.Add("Disease");
                    OwnDiseases(StyxWoW.Me.CurrentTarget!, sample.Item3, sample.Item4);
                    Know("Pestilence"); Fixture.Tick(owner.Build());
                    if (sample.Item5) Expect("Pestilence", StyxWoW.Me.CurrentTarget!); else None();
                });
            foreach (bool ownShort in new[] { false, true })
                Add(owner.Name + "/primary refresh: foreign timer cannot " + (ownShort ? "hide own expiry" : "invent own expiry"), () =>
                {
                    TalentManager.CurrentSpec = owner.Spec; TalentManager.Glyphs.Add("Disease");
                    var target = StyxWoW.Me.CurrentTarget!;
                    OwnDiseases(target, ownShort ? 1 : 30, 30);
                    Aura(target, "Frost Fever", 77, ownShort ? 30 : 1);
                    Know("Pestilence"); Fixture.Tick(owner.Build());
                    if (ownShort) Expect("Pestilence", target); else None();
                });
            foreach (string missing in new[] { "Frost Fever", "Blood Plague", "both-own", "inactive-own" })
                Add(owner.Name + "/primary refresh requires observed own disease: " + missing, () =>
                {
                    TalentManager.CurrentSpec = owner.Spec; TalentManager.Glyphs.Add("Disease");
                    var target = StyxWoW.Me.CurrentTarget!;
                    OwnDiseases(target, 1, 1);
                    if (missing == "both-own")
                        foreach (var aura in target.ObservedAuras) aura.CreatorGuid = 77;
                    else if (missing == "inactive-own")
                        foreach (var aura in target.ObservedAuras) aura.IsActive = false;
                    else target.ObservedAuras.RemoveAll(aura => aura.Name == missing);
                    Know("Pestilence"); Fixture.Tick(owner.Build()); None();
                });
            Add(owner.Name + "/primary refresh preserves spell availability", () =>
            {
                TalentManager.CurrentSpec = owner.Spec; TalentManager.Glyphs.Add("Disease");
                OwnDiseases(StyxWoW.Me.CurrentTarget!, 1, 1);
                Know("Pestilence"); Fixture.Unavailable.Add("Pestilence"); Fixture.Tick(owner.Build()); None();
            });
        }

        foreach (var owner in Owners.Where(row => row.Spread))
        {
            foreach (string existing in new[] { "Frost Fever", "Blood Plague", "neither", "both", "foreign-only" })
                Add(owner.Name + "/spread recipient: " + existing, () =>
                {
                    TalentManager.CurrentSpec = owner.Spec;
                    var target = StyxWoW.Me.CurrentTarget!;
                    OwnDiseases(target, 30, 30);
                    var recipient = Pack();
                    if (existing is "Frost Fever" or "both") Aura(recipient, "Frost Fever", StyxWoW.Me.Guid, 30);
                    if (existing is "Blood Plague" or "both") Aura(recipient, "Blood Plague", StyxWoW.Me.Guid, 30);
                    if (existing == "foreign-only") { Aura(recipient, "Frost Fever", 77, 30); Aura(recipient, "Blood Plague", 77, 30); }
                    Know("Pestilence"); Fixture.Tick(owner.Build());
                    if (existing == "both") None(); else Expect("Pestilence", target);
                });
            foreach (string source in new[] { "only-fever", "only-plague", "foreign-diseases" })
                Add(owner.Name + "/spread source admission: " + source, () =>
                {
                    TalentManager.CurrentSpec = owner.Spec;
                    var target = StyxWoW.Me.CurrentTarget!;
                    if (source == "only-fever") Aura(target, "Frost Fever", StyxWoW.Me.Guid, 30);
                    else if (source == "only-plague") Aura(target, "Blood Plague", StyxWoW.Me.Guid, 30);
                    else { Aura(target, "Frost Fever", 77, 30); Aura(target, "Blood Plague", 77, 30); }
                    Pack(); Know("Pestilence"); Fixture.Tick(owner.Build()); None();
                });
            Add(owner.Name + "/spread keeps the existing pack threshold", () =>
            {
                TalentManager.CurrentSpec = owner.Spec;
                OwnDiseases(StyxWoW.Me.CurrentTarget!, 30, 30);
                Pack(); SingularSettings.Instance.DeathKnight.DeathAndDecayCount = 3;
                Know("Pestilence"); Fixture.Tick(owner.Build()); None();
            });
        }

        foreach (var owner in Owners.Where(row => row.Spec == TalentSpec.UnholyDeathKnight))
        foreach (bool pack in owner.Spread ? new[] { false, true } : new[] { false })
        {
            string label = owner.Name + "/Ghoul Frenzy/" + (pack ? "pack" : "single");
            WoWUnit Prepare()
            {
                TalentManager.CurrentSpec = owner.Spec;
                OwnDiseases(StyxWoW.Me.CurrentTarget!, 30, 30);
                if (pack) OwnDiseases(Pack(), 30, 30);
                var pet = new WoWUnit { Guid = 33, IsFriendly = true, Distance = 4 };
                StyxWoW.Me.Pet = pet; Know("Ghoul Frenzy"); return pet;
            }
            Add(label + ": explicitly selects the owned pet", () =>
            { var pet = Prepare(); Fixture.Tick(owner.Build()); Expect("Ghoul Frenzy", pet); });
            foreach (ulong auraOwner in new ulong[] { 1, 77 })
                Add(label + ": no recast while active pet aura exists, caster=" + auraOwner, () =>
                {
                    var pet = Prepare(); Aura(pet, "Ghoul Frenzy", auraOwner, 25);
                    Fixture.Tick(owner.Build()); None();
                });
            Add(label + ": expired pet buff is admitted again", () =>
            {
                var pet = Prepare(); Aura(pet, "Ghoul Frenzy", StyxWoW.Me.Guid, 0);
                pet.ObservedAuras.Last().IsActive = false;
                Fixture.Tick(owner.Build()); Expect("Ghoul Frenzy", pet);
            });
            foreach (string veto in new[] { "missing", "dead", "invalid", "range", "sight", "unlearned", "unavailable" })
                Add(label + ": preserves pet/spell admission " + veto, () =>
                {
                    var pet = Prepare();
                    switch (veto)
                    {
                        case "missing": StyxWoW.Me.Pet = null; break;
                        case "dead": pet.IsAlive = false; break;
                        case "invalid": pet.IsValid = false; break;
                        case "range": pet.Distance = 50; break;
                        case "sight": pet.InLineOfSpellSight = false; break;
                        case "unlearned": Fixture.Known.Remove("Ghoul Frenzy"); break;
                        case "unavailable": Fixture.Unavailable.Add("Ghoul Frenzy"); break;
                    }
                    Fixture.Tick(owner.Build()); None();
                });
            Add(label + ": a buff on the hostile target cannot hide the pet's missing buff", () =>
            {
                var pet = Prepare(); Aura(StyxWoW.Me.CurrentTarget!, "Ghoul Frenzy", 77, 25);
                Fixture.Tick(owner.Build()); Expect("Ghoul Frenzy", pet);
            });
        }

        int passed = 0, assertions = 0, errors = 0;
        foreach (var test in cases)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS DK contract: " + test.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL DK contract assertion: " + test.Name + ": " + error.Message); }
            catch (Exception error) { errors++; Console.Error.WriteLine("ERROR DK contract fixture/owner: " + test.Name + ": " + error); }
        }
        Fixture.Reset();
        Console.WriteLine($"Death Knight contract scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={errors}; complete four classes, actual TreeSharp; controlled external boundaries; no game.");
        if (assertions + errors != 0)
            throw new InvalidOperationException($"Death Knight contract regressions: assertions={assertions}; unexpected={errors}");
    }

    private static void Setup()
    {
        StyxWoW.Me.Class = WoWClass.DeathKnight;
        StyxWoW.Me.Combat = true;
        StyxWoW.Me.CurrentTarget = new WoWUnit { Guid = 22, IsFriendly = false, Distance = 5, Name = "ObservedEnemy" };
    }
    private static WoWUnit Pack()
    {
        var other = new WoWUnit { Guid = 23, IsFriendly = false, Distance = 5, Name = "NearbyEnemy" };
        Unit.NearbyUnfriendlyUnits.Add(StyxWoW.Me.CurrentTarget!);
        Unit.NearbyUnfriendlyUnits.Add(other);
        return other;
    }
    private static void OwnDiseases(WoWUnit target, double fever, double plague)
    { Aura(target, "Frost Fever", StyxWoW.Me.Guid, fever); Aura(target, "Blood Plague", StyxWoW.Me.Guid, plague); }
    private static void Aura(WoWUnit target, string name, ulong owner, double seconds)
    {
        Fixture.Aura(target, name, owner);
        target.ObservedAuras.Last().TimeLeft = TimeSpan.FromSeconds(seconds);
    }
    private static void Know(params string[] names) => Fixture.Known.UnionWith(names);
    private static void Expect(string name, WoWUnit target) => Check(Fixture.Attempts.Count == 1 && Fixture.Attempts[0] == (name, target.Guid),
        "expected " + name + " on " + target.Guid + ", got " + string.Join(',', Fixture.Attempts));
    private static void None() => Check(Fixture.Attempts.Count == 0, "unexpected dispatch " + string.Join(',', Fixture.Attempts));
    private static void Check(bool condition, string message)
    { if (!condition) throw new Failure(message); }
}
