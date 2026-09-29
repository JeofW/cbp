using Singular.ClassSpecific.Paladin;
using Singular.Dynamics;
using Singular.Managers;
using Singular.Settings;
using Styx;
using TreeSharp;

// Actual linked Ret selector/entry trees, captured tactics and real TreeSharp.
// The optional thresholds are user policy, not native heal coefficients or a
// DPS simulation. All world/spell observations remain explicit fixture leaves.
internal static class RetributionSealRecoveryRegressionTests
{
    private const string Light = "Seal of Light", Stack = "Seal of Vengeance";
    private const string Right = "Seal of Righteousness", Command = "Seal of Command";
    private sealed class Failure(string reason) : Exception(reason) { }

    internal static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, System.Action body)
        {
            total++;
            try { body(); passed++; Console.WriteLine("PASS Ret seal recovery: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL Ret seal recovery: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR Ret seal recovery: " + name + ": " + e); }
            finally { Singular.SingularRoutine.CurrentWoWContext = WoWContext.Normal; }
        }

        foreach (double health in new[] { 31d, 45d, 50d })
            Case("eligible-solo-entry/" + health, () => { Setup(health); Tick(); Expect(Light); });
        Case("above-entry-keeps-damage-policy", () => { Setup(51); Tick(); Expect(Stack); });
        foreach (double health in new[] { 31d, 50d, 51d, 74d })
            Case("observed-Light-hysteresis/" + health, () => { Setup(health); Aura(Light); Tick(); Expect("movement"); });
        foreach (double health in new[] { 75d, 100d })
            Case("recovered-restores-damage/" + health, () => { Setup(health); Aura(Light); Tick(); Expect(Stack); });
        foreach (double health in new[] { 0d, 15d, 30d })
            Case("emergency-window-does-not-spend-a-seal-cast/" + health, () => { Setup(health); Tick(); Expect("movement"); });
        Case("emergency-existing-Light-not-replaced", () => { Setup(20); Aura(Light); Tick(); Expect("movement"); });
        Case("existing-defensive-precedes-seal", () => { Setup(10); Ready("Divine Shield"); Tick(); Expect("Divine Shield"); });
        Case("custom-emergency-heal-window-is-respected", () => {
            Setup(40); SingularSettings.Instance.Paladin.RetributionHealHealth = 45; Tick(); Expect("movement");
        });
        Case("default-disabled-retains-existing-policy", () => {
            Setup(45); SingularSettings.Instance.Paladin.UseSoloSealOfLight = false; Tick(); Expect(Stack);
        });
        Case("unknown-Light-retains-known-damage", () => { Setup(45); Fixture.Known.Remove(Light); Tick(); Expect(Stack); });
        Case("unavailable-Light-does-not-manufacture-dispatch", () => {
            Setup(45); Fixture.Ready.Remove(Light); Aura(Stack); Tick(); Expect("movement");
        });
        Case("mana-reserve-prevents-entry", () => { Setup(45); StyxWoW.Me.ManaPercent = 29; Tick(); Expect(Stack); });
        Case("mana-reserve-boundary-allows-entry", () => { Setup(45); StyxWoW.Me.ManaPercent = 30; Tick(); Expect(Light); });
        Case("low-mana-does-not-recast-an-existing-Light", () => {
            Setup(60); Aura(Light); StyxWoW.Me.ManaPercent = 5; Tick(); Expect("movement");
        });
        Case("melee-required-to-start-proc-recovery", () => { Setup(45); StyxWoW.Me.CurrentTarget!.Distance = 20; Tick(); Expect(Stack); });
        Case("backing-away-does-not-oscillate-existing-Light", () => {
            Setup(60); Aura(Light); StyxWoW.Me.CurrentTarget!.Distance = 20; Tick(); Expect("movement");
        });

        foreach (string mode in new[] { "party", "raid", "not-in-combat", "player", "boss", "elite", "no-target", "dead-target", "invalid-target", "instance", "battleground" })
            Case("recovery-scope/" + mode, () => {
                Setup(45);
                if (mode == "party") StyxWoW.Me.IsInParty = true;
                if (mode == "raid") StyxWoW.Me.IsInRaid = true;
                if (mode == "not-in-combat") StyxWoW.Me.Combat = false;
                if (mode == "player") StyxWoW.Me.CurrentTarget!.IsPlayer = true;
                if (mode == "boss") StyxWoW.Me.CurrentTarget!.Boss = true;
                if (mode == "elite") StyxWoW.Me.CurrentTarget!.Elite = true;
                if (mode == "no-target") StyxWoW.Me.CurrentTarget = null;
                if (mode == "dead-target") StyxWoW.Me.CurrentTarget!.IsAlive = false;
                if (mode == "invalid-target") StyxWoW.Me.CurrentTarget!.IsValid = false;
                if (mode == "instance") Singular.SingularRoutine.CurrentWoWContext = WoWContext.Instances;
                if (mode == "battleground") Singular.SingularRoutine.CurrentWoWContext = WoWContext.Battlegrounds;
                Tick(); Expect(mode == "player" ? Right : Stack);
            });
        foreach (string mode in new[] { "mounted", "transport", "casting", "channeling", "food", "drink" })
            Case("retained-seal-admission/" + mode, () => {
                Setup(45);
                if (mode == "mounted") StyxWoW.Me.Mounted = true;
                if (mode == "transport") StyxWoW.Me.IsOnTransport = true;
                if (mode == "casting") StyxWoW.Me.IsCasting = true;
                if (mode == "channeling") StyxWoW.Me.IsChanneling = true;
                if (mode == "food") Aura("Food");
                if (mode == "drink") Aura("Drink");
                Tick(); Expect("movement");
            });
        foreach (string mode in new[] { "start-zero", "start-negative", "end-over-100", "equal", "inverted", "mana-negative", "mana-over-100" })
            Case("invalid-option-config-does-not-enable-recovery/" + mode, () => {
                Setup(45); var settings = SingularSettings.Instance.Paladin;
                if (mode == "start-zero") settings.SoloSealOfLightHealth = 0;
                if (mode == "start-negative") settings.SoloSealOfLightHealth = -1;
                if (mode == "end-over-100") settings.SoloSealOfLightRecoveryHealth = 101;
                if (mode == "equal") settings.SoloSealOfLightRecoveryHealth = 50;
                if (mode == "inverted") settings.SoloSealOfLightRecoveryHealth = 40;
                if (mode == "mana-negative") settings.SoloSealOfLightMinimumMana = -1;
                if (mode == "mana-over-100") settings.SoloSealOfLightMinimumMana = 101;
                Tick(); Expect(Stack);
            });
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, -1d, 101d })
            Case("unknown-health-does-not-authorize-recovery/" + value, () => { Setup(value); Tick(); Expect("movement"); });
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, -1d, 101d })
            Case("invalid-mana-does-not-start-recovery/" + value, () => {
                Setup(45); StyxWoW.Me.ManaPercent = value; Tick(); Expect(Stack);
            });

        foreach (PaladinSeal seal in Enum.GetValues<PaladinSeal>().Where(s => s != PaladinSeal.Auto))
            Case("manual-selection-stays-authoritative/" + seal, () => {
                Setup(45); SingularSettings.Instance.Paladin.Seal = seal;
                Ready("Seal of " + seal); Tick(); Expect("Seal of " + seal);
            });
        Case("explicit-Justice-is-not-overridden-by-PvP-default", () => {
            Setup(45); StyxWoW.Me.CurrentTarget!.IsPlayer = true;
            SingularSettings.Instance.Paladin.Seal = PaladinSeal.Justice; Ready("Seal of Justice"); Tick(); Expect("Seal of Justice");
        });
        Case("Justice-judgement-does-not-require-Justice-seal", () => {
            Setup(45); StyxWoW.Me.CurrentTarget!.IsPlayer = true; StyxWoW.Me.CurrentTarget.IsMoving = true;
            Aura(Right); Ready("Judgement of Justice", "Judgement of Light", "Judgement of Wisdom"); Tick(); Expect("Judgement of Justice");
        });
        Case("real-interrupt-precedes-recovery-selection", () => {
            Setup(45); StyxWoW.Me.CurrentTarget!.IsCasting = true; StyxWoW.Me.CurrentTarget.CanInterruptCurrentSpellCast = true;
            Ready("Hammer of Justice"); Tick(); Expect("Hammer of Justice");
        });
        foreach (string change in new[] { "health-recovers", "mana-lost", "party-joined", "option-disabled", "target-left-melee", "player-reference", "player-guid", "target-reference", "target-guid", "spec" })
            Case("captured-recovery-revalidates/" + change, () => {
                Setup(45); Fixture.BeforeDispatch = () => {
                    if (change == "health-recovers") StyxWoW.Me.HealthPercent = 60;
                    if (change == "mana-lost") StyxWoW.Me.ManaPercent = 5;
                    if (change == "party-joined") StyxWoW.Me.IsInParty = true;
                    if (change == "option-disabled") SingularSettings.Instance.Paladin.UseSoloSealOfLight = false;
                    if (change == "target-left-melee") StyxWoW.Me.CurrentTarget!.Distance = 20;
                    if (change == "player-reference") StyxWoW.Me = new Player { Guid = 7, CurrentTarget = StyxWoW.Me.CurrentTarget };
                    if (change == "player-guid") StyxWoW.Me.Guid = 7;
                    if (change == "target-reference") StyxWoW.Me.CurrentTarget = new UnitState { Guid = 8 };
                    if (change == "target-guid") StyxWoW.Me.CurrentTarget!.Guid = 8;
                    if (change == "spec") TalentManager.CurrentSpec = TalentSpec.ProtectionPaladin;
                };
                Tick(); Expect("movement");
            });
        Case("same-tree-stable-recovery-and-damage-return", () => {
            Setup(45); Fixture.ApplyEffects = true;
            var tree = Retribution.CreateRetributionPaladinNormalPullAndCombat();
            Tick(tree); Expect(Light);
            StyxWoW.Me.HealthPercent = 51; Tick(tree); Expect("movement");
            StyxWoW.Me.HealthPercent = 74; Tick(tree); Expect("movement");
            StyxWoW.Me.HealthPercent = 75; Tick(tree); Expect(Stack);
            StyxWoW.Me.HealthPercent = 51; Tick(tree); Expect("movement");
        });
        Case("shared-seal-owner-uses-the-same-recovery-policy", () => {
            Setup(45); Tick(Retribution.CreateRetributionSealBehavior()); Expect(Light);
        });

        Console.WriteLine($"Ret seal recovery scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; linked real Ret/tactics/TreeSharp; controlled world/readiness; opt-in policy, not native healing, DR or DPS proof.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Ret seal recovery regression");
    }

    private static void Setup(double health)
    {
        Fixture.Reset(); Singular.SingularRoutine.CurrentWoWContext = WoWContext.Normal;
        StyxWoW.Me.Combat = true; StyxWoW.Me.HealthPercent = health;
        SingularSettings.Instance.Paladin.UseSoloSealOfLight = true;
        Ready(Light, Stack, Right, Command);
    }
    private static void Ready(params string[] names) { Fixture.Known.UnionWith(names); Fixture.Ready.UnionWith(names); }
    private static void Aura(string name) => StyxWoW.Me.Auras[name] = new Aura { Name = name, CreatorGuid = StyxWoW.Me.Guid };
    private static void Tick(Composite? tree = null)
    {
        Fixture.Selected = null;
        tree ??= Retribution.CreateRetributionPaladinNormalPullAndCombat();
        tree.Start(null!);
        try { Check(tree.Tick(null!) != RunStatus.Running, "unexpected blocking decision"); Check(Fixture.Exceptions.Count == 0, "swallowed boundary exception"); }
        finally { tree.Stop(null!); }
    }
    private static void Expect(string name) => Check(Fixture.Selected == name, $"expected {name}, got {Fixture.Selected}; {string.Join(',', Fixture.Trace)}");
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
