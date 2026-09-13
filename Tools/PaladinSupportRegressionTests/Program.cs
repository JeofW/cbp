using System.Reflection;
using TestAction = System.Action;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using PaladinCommon = Singular.ClassSpecific.Paladin.Common;

var tests = new List<(string Name, TestAction Run)>();
void Test(string name, TestAction run) => tests.Add((name, run));
void Check(bool good, string message) { if (!good) throw new InvalidOperationException(message); }
void Know(params string[] spells) { foreach (string s in spells) Fixture.Known.Add(s); }
void Bless() => Fixture.Tick(PaladinCommon.CreatePaladinPreCombatBuffs());
void Heal() => Fixture.Tick(Retribution.CreateRetributionPaladinHeal());
void Expect(string spell, WoWPlayer? target = null)
{
    Check(Fixture.Attempts.Count == 1 && Fixture.Attempts[0] == (spell, (target ?? StyxWoW.Me).Guid),
        $"expected {spell} on {(target ?? StyxWoW.Me).Guid}, got {string.Join(',', Fixture.Attempts)}");
}
void None() => Check(Fixture.Attempts.Count == 0, "unexpected cast: " + string.Join(',', Fixture.Attempts));
const string Kings = "Blessing of Kings", Might = "Blessing of Might", Wisdom = "Blessing of Wisdom";
Test("an unbuffed player receives Kings", () => { Know(Kings, Might, Wisdom); Bless(); Expect(Kings); });
Test("Mark of the Wild does not replace WotLK Kings", () => { Know(Kings, Might); Fixture.Aura(StyxWoW.Me, "Mark of the Wild", 99); Bless(); Expect(Kings); });
Test("Greater Kings from another Paladin permits complementary Might", () => { Know(Kings, Might); Fixture.Aura(StyxWoW.Me, "Greater Blessing of Kings", 99); Bless(); Expect(Might); });
Test("own Greater Kings is retained rather than downgraded", () => { Know(Kings, Might); Fixture.Aura(StyxWoW.Me, "Greater Blessing of Kings", 1); Bless(); None(); });
Test("external Kings on a caster selects Wisdom", () => { Know(Kings, Might, Wisdom); Fixture.Aura(StyxWoW.Me, Kings, 1); var p = Fixture.Add(WoWClass.Mage); Fixture.Aura(p, Kings, 99); Bless(); Expect(Wisdom, p); });
Test("a third Paladin fills Wisdom when Kings and Might are supplied", () => { Know(Kings, Might, Wisdom); Fixture.Aura(StyxWoW.Me, Kings, 99); Fixture.Aura(StyxWoW.Me, Might, 98); Bless(); Expect(Wisdom); });
Test("own Might is retained while external Kings remains", () => { Know(Kings, Might, Wisdom); Fixture.Aura(StyxWoW.Me, Kings, 99); Fixture.Aura(StyxWoW.Me, Might, 1); Bless(); None(); });
Test("normal and greater Might share one coverage category", () => { Know(Kings, Might, Wisdom); Fixture.Aura(StyxWoW.Me, Kings, 99); Fixture.Aura(StyxWoW.Me, "Greater Blessing of Might", 98); Bless(); Expect(Wisdom); });
Test("non-mana recipient never receives Wisdom as filler", () => { Know(Kings, Might, Wisdom); StyxWoW.Me.MaxMana = 0; Fixture.Aura(StyxWoW.Me, Kings, 99); Fixture.Aura(StyxWoW.Me, Might, 98); Bless(); None(); });
Test("unlearned Kings falls back to useful learned Might", () => { Know(Might); Bless(); Expect(Might); });
Test("explicit Might honors Greater Might coverage", () => { Know(Might); SingularSettings.Instance.Paladin.Blessings = PaladinBlessings.Might; Fixture.Aura(StyxWoW.Me, "Greater Blessing of Might", 99); Bless(); None(); });
Test("unbuffable first member does not starve a later member", () => { Know(Kings); Fixture.Aura(StyxWoW.Me, Kings, 1); var p = Fixture.Add(); p.IsFriendly = false; var good = Fixture.Add(); Bless(); Expect(Kings, good); });
Test("raid members are considered without a party flag", () => { Know(Kings); Fixture.Aura(StyxWoW.Me, Kings, 1); var p = Fixture.Add(raid: true); Bless(); Expect(Kings, p); });
Test("blessings do not dismount travelling player", () => { Know(Kings); StyxWoW.Me.Mounted = true; Bless(); None(); });
Test("blessings do not interrupt Food or Drink", () => { Know(Kings); Fixture.Aura(StyxWoW.Me, "Drink", 1); Bless(); None(); });
Test("duplicate aura names retain caster ownership evidence", () => { Know(Kings, Might); Fixture.Aura(StyxWoW.Me, Kings, 1); Fixture.Aura(StyxWoW.Me, Kings, 99); Bless(); Expect(Might); });

foreach (var kind in new[] { WoWDispelType.Disease, WoWDispelType.Poison })
{
    var type = kind;
    Test($"level 33 uses learned Purify for self {kind}", () => { Know("Purify"); StyxWoW.Me.Level = 33; Fixture.Aura(StyxWoW.Me, "fixture debuff", 9, 100, type); Heal(); Expect("Purify"); });
    Test($"group {kind} is dispelled on that member rather than self", () => { Know("Purify"); var p = Fixture.Add(); Fixture.Aura(p, "fixture debuff", 9, 100, type); Heal(); Expect("Purify", p); });
}
Test("Cleanse removes Magic when actually learned", () => { Know("Cleanse"); Fixture.Aura(StyxWoW.Me, "fixture magic", 9, 100, WoWDispelType.Magic); Heal(); Expect("Cleanse"); });
Test("Purify cannot remove Magic", () => { Know("Purify"); Fixture.Aura(StyxWoW.Me, "fixture magic", 9, 100, WoWDispelType.Magic); Heal(); None(); });
Test("neither spell can remove a Curse", () => { Know("Purify", "Cleanse"); Fixture.Aura(StyxWoW.Me, "fixture curse", 9, 100, WoWDispelType.Curse); Heal(); None(); });
Test("no learned cleansing spell does not claim a cure", () => { Fixture.Aura(StyxWoW.Me, "fixture disease", 9, 100, WoWDispelType.Disease); Heal(); None(); });
Test("economical Purify is preferred for Disease without Magic", () => { Know("Purify", "Cleanse"); Fixture.Aura(StyxWoW.Me, "fixture disease", 9, 100, WoWDispelType.Disease); Heal(); Expect("Purify"); });
Test("unavailable Purify permits valid Cleanse fallback", () => { Know("Purify", "Cleanse"); Fixture.Unavailable.Add("Purify"); Fixture.Aura(StyxWoW.Me, "fixture disease", 9, 100, WoWDispelType.Disease); Heal(); Expect("Cleanse"); });
Test("a dead diseased member does not starve a live member", () => { Know("Purify"); var dead = Fixture.Add(); dead.IsAlive = false; Fixture.Aura(dead, "fixture", 9, 100, WoWDispelType.Disease); var p = Fixture.Add(); Fixture.Aura(p, "fixture", 9, 100, WoWDispelType.Disease); Heal(); Expect("Purify", p); });
Test("an out-of-sight member does not starve a visible member", () => { Know("Purify"); var blind = Fixture.Add(); blind.InLineOfSpellSight = false; Fixture.Aura(blind, "fixture", 9, 100, WoWDispelType.Disease); var p = Fixture.Add(); Fixture.Aura(p, "fixture", 9, 100, WoWDispelType.Poison); Heal(); Expect("Purify", p); });
Test("raid-only roster is eligible for cleansing", () => { Know("Cleanse"); var p = Fixture.Add(raid: true); Fixture.Aura(p, "fixture", 9, 100, WoWDispelType.Magic); Heal(); Expect("Cleanse", p); });
Test("dispel setting can disable automatic cleansing", () => { Know("Purify"); SingularSettings.Instance.Paladin.DispelDebuffs = false; Fixture.Aura(StyxWoW.Me, "fixture", 9, 100, WoWDispelType.Disease); Heal(); None(); });
Test("party dispel setting does not disable self dispel", () => { Know("Purify"); SingularSettings.Instance.Paladin.DispelParty = false; var p = Fixture.Add(); Fixture.Aura(p, "fixture", 9, 100, WoWDispelType.Disease); Heal(); None(); Fixture.Aura(StyxWoW.Me, "fixture", 9, 100, WoWDispelType.Disease); Heal(); Expect("Purify"); });
Test("cleansing does not interrupt a mounted journey", () => { Know("Purify"); StyxWoW.Me.Mounted = true; Fixture.Aura(StyxWoW.Me, "fixture", 9, 100, WoWDispelType.Disease); Heal(); None(); });
Test("emergency Lay on Hands stays ahead of routine cleansing", () => { Know("Purify", "Lay on Hands"); StyxWoW.Me.HealthPercent = 10; Fixture.Aura(StyxWoW.Me, "fixture", 9, 100, WoWDispelType.Disease); Heal(); Expect("Lay on Hands"); });
Test("empty debuffs perform no dispel", () => { Know("Purify", "Cleanse"); Heal(); None(); });
Test("unknown aura metadata is skipped without throwing", () => { Know("Purify"); StyxWoW.Me.ObservedAuras.Add(new WoWAura { IsHarmful = true, Spell = null }); Heal(); None(); });
Test("Mutating Injection requires encounter coordination", () => { Know("Purify", "Cleanse"); Fixture.Aura(StyxWoW.Me, "localized injection", 9, 28169, WoWDispelType.Disease); Heal(); None(); });
Test("unsafe Magic may coexist with safely Purifiable Poison", () => { Know("Purify", "Cleanse"); Fixture.Aura(StyxWoW.Me, "localized UA", 9, 47843, WoWDispelType.Magic); Fixture.Aura(StyxWoW.Me, "fixture poison", 9, 100, WoWDispelType.Poison); Heal(); Expect("Purify"); });
Test("Cleanse must not incidentally remove unsafe Magic", () => { Know("Cleanse"); Fixture.Aura(StyxWoW.Me, "localized UA", 9, 47843, WoWDispelType.Magic); Fixture.Aura(StyxWoW.Me, "fixture disease", 9, 100, WoWDispelType.Disease); Heal(); None(); });
Test("out-of-combat cleansing is wired before ordinary buffs", () => { Know("Purify", Kings); Fixture.Aura(StyxWoW.Me, "fixture", 9, 100, WoWDispelType.Disease); Bless(); Expect("Purify"); });

Test("Auto aura fills Devotion when another Paladin covers Retribution", () => { Know("Retribution Aura", "Devotion Aura"); Fixture.Aura(StyxWoW.Me, "Retribution Aura", 99); Bless(); Expect("Devotion Aura"); });
Test("an owned useful aura does not oscillate while other coverage stays", () => { Know("Retribution Aura", "Devotion Aura"); Fixture.Aura(StyxWoW.Me, "Retribution Aura", 99); Fixture.Aura(StyxWoW.Me, "Devotion Aura", 1); Bless(); None(); });
Test("Auto aura covers Concentration when two others cover the defaults", () => { Know("Retribution Aura", "Devotion Aura", "Concentration Aura"); Fixture.Aura(StyxWoW.Me, "Retribution Aura", 99); Fixture.Aura(StyxWoW.Me, "Devotion Aura", 98); Bless(); Expect("Concentration Aura"); });
Test("manual aura selection remains explicit", () => { Know("Retribution Aura", "Devotion Aura"); SingularSettings.Instance.Paladin.Aura = PaladinAura.Devotion; Bless(); Expect("Devotion Aura"); });
Test("unknown default aura falls back to a learned aura", () => { Know("Devotion Aura"); Bless(); Expect("Devotion Aura"); });

var failures = new List<string>();
foreach (var t in tests)
{
    Fixture.Reset();
    try { t.Run(); Console.WriteLine("PASS support: " + t.Name); }
    catch (Exception e) { failures.Add(t.Name + ": " + e.Message); Console.Error.WriteLine("FAIL support: " + failures[^1]); }
}
Console.WriteLine($"Paladin support scenarios: {tests.Count - failures.Count}/{tests.Count}; linked production decisions; controlled external observations/dispatch; no client attached.");
if (failures.Count != 0) Environment.ExitCode = 1;
