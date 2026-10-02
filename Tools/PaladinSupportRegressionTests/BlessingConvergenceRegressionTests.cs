using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.WoWInternals.WoWObjects;

// Each round observes the same prior effects for every participant, then applies
// all submissions. Only observations/effects are simulated; every choice executes
// the linked production selector. This is not a server stacking/rank proof.
internal static class BlessingConvergenceRegressionTests
{
    private const string Kings = "Blessing of Kings", Might = "Blessing of Might", Wisdom = "Blessing of Wisdom";
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Run)>();
        foreach (bool raid in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        foreach (bool greater in new[] { false, true })
        foreach (int count in new[] { 1, 2, 3, 5 })
        foreach (WoWClass recipient in new[] { WoWClass.Paladin, WoWClass.Mage, WoWClass.Warrior })
        {
            var r = raid; var rev = reverse; var g = greater; var n = count; var c = recipient;
            cases.Add(($"{n} Paladins/{c}/raid={r}/reverse={rev}/greater={g}", () => Converge(n, c, r, rev, g)));
        }
        cases.Add(("Battle Shout permits stable Kings and Wisdom", () => Converge(3, WoWClass.Paladin, false, false, true, true)));
        cases.Add(("unknown creator cannot force owned contribution replacement", () =>
        {
            var target = Setup(9, WoWClass.Paladin, false);
            Fixture.Aura(target, Kings, 9); Fixture.Aura(target, Kings, 0);
            Check(Choose(target) == null, "unknown duplicate owner forced reassignment");
        }));
        foreach (string family in new[] { Kings, Might, Wisdom })
        {
            var name = family;
            cases.Add((name + " known foreign coverage is never overwritten", () =>
            {
                var target = Setup(9, WoWClass.Paladin, false);
                Fixture.Known.Clear(); Fixture.Known.Add(name);
                Fixture.Aura(target, "Greater " + name, 20, name == Kings ? 25898 : name == Might ? 48934 : 48938);
                Check(Choose(target) == null, "same family external coverage was replaced");
            }));
            cases.Add((name + " expiry permits fresh contribution", () =>
            {
                var target = Setup(9, WoWClass.Paladin, false);
                Fixture.Known.Clear(); Fixture.Known.Add(name);
                Fixture.Aura(target, name, 20); target.ObservedAuras[0].TimeLeft = TimeSpan.Zero;
                Check(Choose(target) == name, "expired foreign coverage froze rebuffing");
            }));
        }
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS blessing convergence: " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL blessing convergence: " + test.Name + ": " + error.Message); }
        }
        Fixture.Reset();
        Console.WriteLine($"Blessing convergence scenarios: {cases.Count - failures}/{cases.Count}; actual linked selection; simultaneous controlled effects; no live proof.");
        if (failures != 0) Environment.ExitCode = 1;
    }

    private static void Converge(int count, WoWClass recipientClass, bool raid, bool reverse, bool greater, bool shout = false)
    {
        var owned = Enumerable.Range(1, count).ToDictionary(i => (ulong)i, _ => Kings);
        int lateChanges = 0;
        for (int round = 0; round < 10; round++)
        {
            var next = new Dictionary<ulong, string>(owned);
            foreach (var caster in reverse ? owned.Reverse() : owned)
            {
                var target = Setup(caster.Key, recipientClass, raid);
                foreach (var observed in owned)
                    Fixture.Aura(target, (greater && observed.Key % 2 == 0 ? "Greater " : "") + observed.Value, observed.Key);
                if (shout) Fixture.Aura(target, "Battle Shout", 500);
                var choice = Choose(target);
                if (choice != null)
                {
                    next[caster.Key] = choice;
                    if (round >= 5) lateChanges++;
                }
            }
            owned = next;
        }
        int usefulFamilies = recipientClass == WoWClass.Paladin && !shout ? 3 : 2;
        Check(owned.Values.Distinct().Count() == Math.Min(count, usefulFamilies), "did not fill useful distinct contributions: " + string.Join(',', owned.Values));
        Check(lateChanges == 0, "continued rebuffing after convergence window: " + lateChanges);
        Check(recipientClass != WoWClass.Warrior || !owned.Values.Contains(Wisdom), "mana-less recipient received Wisdom");
        Check(recipientClass != WoWClass.Mage || !owned.Values.Contains(Might), "caster recipient received Might");
        Check(!shout || !owned.Values.Contains(Might), "Battle Shout coverage was fought");
    }

    private static WoWPlayer Setup(ulong caster, WoWClass recipient, bool raid)
    {
        Fixture.Reset(); StyxWoW.Me.Guid = caster;
        Fixture.Known.UnionWith(new[] { Kings, Might, Wisdom });
        return Fixture.Add(recipient, raid);
    }
    private static string? Choose(WoWPlayer target)
    {
        try { return (string?)typeof(Common).GetMethod("SelectNormalBlessing", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { target }); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
}
