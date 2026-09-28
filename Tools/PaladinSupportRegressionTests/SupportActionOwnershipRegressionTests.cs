using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using PaladinCommon = Singular.ClassSpecific.Paladin.Common;

// Linked production selection/revalidation and support trees. Only the existing
// fixture's external actor/roster/API/dispatch leaves are controlled. A managed
// participant check is not native ABA, frame atomicity or server acceptance.
internal static class SupportActionOwnershipRegressionTests
{
    private sealed class AssertionFailure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Body)>();
        foreach (string family in new[] { "Kings", "Might", "Wisdom", "Sanctuary" })
        foreach (bool greater in new[] { false, true })
        foreach (string change in new[] { "unchanged", "different-caster", "same-guid-new-caster", "caster-guid", "recipient-guid" })
        {
            cases.Add(($"{family}/{(greater ? "greater" : "normal")}/{change}", () =>
            {
                var target = Setup(family, greater);
                string spell = (greater ? "Greater " : "") + "Blessing of " + family;
                object action = CaptureBlessing(spell, target);
                Change(change, target);
                Check(Valid(action, spell) == (change == "unchanged"),
                    "captured support action adopted a different caster/recipient identity");
                Check(Fixture.Attempts.Count == 0, "validation alone dispatched a spell");
            }));
        }

        foreach (bool duringSelection in new[] { true, false })
        foreach (string change in new[] { "different-caster", "caster-guid", "recipient-guid" })
        {
            cases.Add(($"actual-blessing-tree/{(duringSelection ? "selection" : "revalidation")}/{change}", () =>
            {
                var target = Setup("Kings", false);
                EnablePallyPower();
                int targetQueries = 0, mutations = 0;
                Fixture.LuaResult = query =>
                {
                    if (query.Contains("targetName=\"" + target.Name + "\"", StringComparison.Ordinal) &&
                        ++targetQueries == (duringSelection ? 1 : 2))
                    {
                        Change(change, target);
                        mutations++;
                    }
                    return new List<string> { "1", "3", "0", "0" };
                };
                RunStatus status = Fixture.Tick(BlessingTree());
                Check(mutations == 1, "actual assignment observation boundary was not exercised");
                Check(status == RunStatus.Failure && Fixture.Attempts.Count == 0,
                    "identity changed inside the actual policy query but the old action still dispatched");
                Check(Fixture.Errors.Count == 0, "identity rejection escaped through an exception");
            }));
        }

        cases.Add(("actual-blessing-tree/unchanged-verified-assignment", () =>
        {
            var target = Setup("Kings", false);
            EnablePallyPower();
            Fixture.LuaResult = _ => new List<string> { "1", "3", "0", "0" };
            Check(Fixture.Tick(BlessingTree()) == RunStatus.Success, "unchanged verified assignment was denied");
            Expect("Blessing of Kings", target);
        }));

        cases.Add(("fresh-selection-recovers-after-caster-replacement", () =>
        {
            var target = Setup("Kings", false);
            object old = CaptureBlessing("Blessing of Kings", target);
            Change("different-caster", target);
            Check(!Valid(old, "Blessing of Kings"), "old caster's action survived replacement");
            object current = CaptureBlessing("Blessing of Kings", target);
            Check(Valid(current, "Blessing of Kings"), "new caster could not make its own valid selection");
            Check(Fixture.Tick(BlessingTree()) == RunStatus.Success, "new valid support decision became sticky-denied");
            Expect("Blessing of Kings", target);
        }));
        cases.Add(("fresh-selection-recovers-after-recipient-identity-change", () =>
        {
            var target = Setup("Kings", false);
            object old = CaptureBlessing("Blessing of Kings", target);
            target.Guid = 91;
            Check(!Valid(old, "Blessing of Kings"), "old recipient identity survived");
            Check(Valid(CaptureBlessing("Blessing of Kings", target), "Blessing of Kings"),
                "a newly captured current recipient was incorrectly denied");
        }));
        foreach (string missing in new[] { "caster", "caster-guid", "recipient-guid" })
        {
            cases.Add(("capture-requires-known-participants/" + missing, () =>
            {
                var target = Setup("Kings", false);
                if (missing == "caster") StyxWoW.Me = null!;
                else if (missing == "caster-guid") StyxWoW.Me.Guid = 0;
                else target.Guid = 0;
                Check(Invoke("FindBlessingAction") == null, "unknown participant identity acquired a support action");
            }));
        }
        foreach (string change in new[] { "recipient-left", "recipient-dead", "external-coverage", "other-member-joined", "recipient-name", "caster-health" })
        {
            cases.Add(("preserved-policy/" + change, () =>
            {
                var target = Setup("Kings", false);
                object action = CaptureBlessing("Blessing of Kings", target);
                bool expected = change is "other-member-joined" or "recipient-name" or "caster-health";
                if (change == "recipient-left") StyxWoW.Me.PartyMembers.Clear();
                if (change == "recipient-dead") target.IsAlive = false;
                if (change == "external-coverage") Fixture.Aura(target, "Greater Blessing of Kings", 99);
                if (change == "other-member-joined") Fixture.Add(WoWClass.Warrior);
                if (change == "recipient-name") target.Name = "CurrentVisibleName";
                if (change == "caster-health") StyxWoW.Me.HealthPercent = 85;
                Check(Valid(action, "Blessing of Kings") == expected,
                    "existing coverage/roster policy changed or unrelated observations revoked a valid action");
            }));
        }

        cases.Add(("self-aura-caster-guid-is-bound", () =>
        {
            Fixture.Reset();
            Fixture.Known.Add("Devotion Aura");
            SingularSettings.Instance.Paladin.Aura = PaladinAura.Devotion;
            object action = CapturePolicy(false, "SelectAura", "Devotion Aura", StyxWoW.Me);
            StyxWoW.Me.Guid = 91;
            Check(!Valid(action, "Devotion Aura"), "captured aura action adopted a changed caster GUID");
        }));
        cases.Add(("self-aura-unchanged-remains-valid", () =>
        {
            Fixture.Reset();
            Fixture.Known.Add("Devotion Aura");
            SingularSettings.Instance.Paladin.Aura = PaladinAura.Devotion;
            object action = CapturePolicy(false, "SelectAura", "Devotion Aura", StyxWoW.Me);
            Check(Valid(action, "Devotion Aura"), "valid self aura was denied");
        }));
        foreach (string change in new[] { "unchanged", "different-caster", "recipient-guid" })
        {
            cases.Add(("group-dispel/" + change, () =>
            {
                Fixture.Reset();
                Fixture.Known.Add("Purify");
                var target = Fixture.Add(WoWClass.Mage);
                Fixture.Aura(target, "controlled disease", 99, 100, WoWDispelType.Disease);
                object action = CapturePolicy(true, "SelectDispel", "Purify", target);
                Change(change, target);
                Check(Valid(action, "Purify") == (change == "unchanged"),
                    "dispel action adopted a different participant or denied an unchanged decision");
            }));
        }

        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { test.Body(); passed++; Console.WriteLine("PASS support action ownership: " + test.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL support action ownership: " + test.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR support action ownership: " + test.Name + ": " + e); }
            finally { Fixture.Reset(); }
        }
        Console.WriteLine($"Support action ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; linked actual support selection/revalidation and blessing trees; controlled actor/roster/PallyPower response/dispatch; no native/frame/server proof.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Support action ownership regression");
    }

    private static WoWPlayer Setup(string family, bool greater)
    {
        Fixture.Reset();
        string normal = "Blessing of " + family, mass = "Greater " + normal;
        SingularSettings.Instance.Paladin.Blessings = Enum.Parse<PaladinBlessings>(family);
        SingularSettings.Instance.Paladin.UseGreaterBlessings = greater;
        Fixture.Known.UnionWith(new[] { normal, mass });
        var metadata = new WoWSpell();
        metadata.InternalInfo.Reagent![0] = 777;
        metadata.InternalInfo.ReagentCount![0] = 1;
        Fixture.Metadata[mass] = metadata;
        StyxWoW.Me.ItemCounts[777] = 1;
        Fixture.Aura(StyxWoW.Me, normal, StyxWoW.Me.Guid);
        return Fixture.Add(WoWClass.Mage);
    }

    private static void EnablePallyPower()
    {
        SingularSettings.Instance.Paladin.Blessings = PaladinBlessings.Auto;
        SingularSettings.Instance.Paladin.UsePallyPowerAssignments = true;
    }

    private static void Change(string change, WoWPlayer target)
    {
        var old = StyxWoW.Me;
        if (change is "different-caster" or "same-guid-new-caster")
        {
            var replacement = new LocalPlayer
            {
                Guid = change == "same-guid-new-caster" ? old.Guid : 91,
                Class = WoWClass.Paladin, Name = old.Name,
                IsInParty = old.IsInParty, IsInRaid = old.IsInRaid
            };
            replacement.PartyMembers.AddRange(old.PartyMembers);
            replacement.RaidMembers.AddRange(old.RaidMembers);
            replacement.ObservedAuras.AddRange(old.ObservedAuras);
            foreach (var count in old.ItemCounts) replacement.ItemCounts[count.Key] = count.Value;
            StyxWoW.Me = replacement;
        }
        else if (change == "caster-guid") old.Guid = 91;
        else if (change == "recipient-guid") target.Guid = 92;
    }

    private static object CaptureBlessing(string spell, WoWPlayer target) => AssertAction(Invoke("FindBlessingAction"), spell, target);
    private static object CapturePolicy(bool group, string method, string spell, WoWPlayer target)
    {
        var selector = (Func<WoWPlayer, string>)typeof(PaladinCommon)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate(typeof(Func<WoWPlayer, string>));
        return AssertAction(Invoke("FindSupportAction", group, selector), spell, target);
    }
    private static object AssertAction(object? action, string spell, WoWPlayer target)
    {
        Check(action != null, "expected current support action was absent");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Check((string?)action!.GetType().GetField("Spell", fields)!.GetValue(action) == spell &&
              ReferenceEquals(action.GetType().GetField("Target", fields)!.GetValue(action), target),
              "test did not capture the actual expected support decision");
        return action;
    }
    private static bool Valid(object action, string spell) => (bool)Invoke("ValidSupportAction", action, spell)!;
    private static Composite BlessingTree() => (Composite)Invoke("CreatePaladinBlessBehavior")!;
    private static object? Invoke(string name, params object[] arguments)
    {
        try { return typeof(PaladinCommon).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static void Expect(string spell, WoWPlayer target) =>
        Check(Fixture.Attempts.Count == 1 && Fixture.Attempts[0] == (spell, target.Guid), "wrong support submission");
    private static void Check(bool value, string message) { if (!value) throw new AssertionFailure(message); }
}
