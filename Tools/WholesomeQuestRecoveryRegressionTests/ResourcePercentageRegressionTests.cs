using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual WoWUnit getters, descriptor mapping, and Memory.Read against owned
// process storage. Large non-health samples prove arithmetic boundaries only;
// they do not claim these resources are attainable on a live original-era unit.
internal static class ResourcePercentageRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class Failure(string message) : Exception(message) { }
    private sealed record Power(WoWPowerType Type, UnitFields Current, UnitFields Maximum, int Scale);
    private static readonly Power[] Powers =
    {
        new(WoWPowerType.Health, UnitFields.Health, UnitFields.MaxHealth, 1),
        new(WoWPowerType.Mana, UnitFields.Mana, UnitFields.MaxMana, 1),
        new(WoWPowerType.Rage, UnitFields.Rage, UnitFields.MaxRage, 10),
        new(WoWPowerType.Focus, UnitFields.Focus, UnitFields.MaxFocus, 1),
        new(WoWPowerType.Energy, UnitFields.Energy, UnitFields.MaxEnergy, 1),
        new(WoWPowerType.Happiness, UnitFields.Happiness, UnitFields.MaxHappiness, 1),
        new(WoWPowerType.Runes, UnitFields.Runes, UnitFields.MaxRunes, 1),
        new(WoWPowerType.RunicPower, UnitFields.RunicPower, UnitFields.MaxRunicPower, 10)
    };

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Resource percentage owners require Windows x86.");
        var cases = new List<(string Name, System.Action<Storage> Test)>();
        foreach (var power in Powers)
        {
            var samples = new (string Name, int Current, int Maximum, double Expected)[]
            {
                ("zero maximum", 25, 0, 0),
                ("empty", 0, 100, 0),
                ("quarter", 25, 100, 25),
                ("fractional percentage", 1, 8, 12.5),
                ("full", 100, 100, 100),
                ("upper cap", 150, 100, 100),
                ("last current before old multiplication overflow", 21_474_836, 85_899_344, 25),
                ("first current after old multiplication overflow", 21_474_837, 85_899_348, 25),
                ("large half", 60_000_000, 120_000_000, 50),
                ("large three quarters", 90_000_000, 120_000_000, 75),
                ("large full", 120_000_000, 120_000_000, 100),
                ("large upper cap", 120_000_000, 60_000_000, 100)
            };
            foreach (var sample in samples)
                cases.Add(($"{power.Type}: {sample.Name}", storage =>
                {
                    storage.Set(power, sample.Current, sample.Maximum);
                    Check(storage.Unit.GetCurrentPower(power.Type) == sample.Current
                        && storage.Unit.GetMaxPower(power.Type) == sample.Maximum,
                        "actual descriptor/scaling owners did not observe the sample");
                    Equal(sample.Expected, storage.Unit.GetPowerPercent(power.Type));
                }));
            cases.Add(($"{power.Type}: percentage cannot fall as observed stock increases", storage =>
            {
                double previous = -1;
                foreach (int amount in new[] { 0, 1, 10_000_000, 21_474_836, 21_474_837,
                                               30_000_000, 60_000_000, 90_000_000, 120_000_000 })
                {
                    storage.Set(power, amount, 120_000_000);
                    double current = storage.Unit.GetPowerPercent(power.Type);
                    Check(double.IsFinite(current) && current >= previous && current <= 100,
                        "a larger positive observation lowered the percentage: " + previous + " -> " + current);
                    previous = current;
                }
                Equal(100, previous);
            }));
            cases.Add(($"{power.Type}: full signed-positive raw boundary", storage =>
            {
                storage.Raw(power.Current, int.MaxValue);
                storage.Raw(power.Maximum, int.MaxValue);
                Equal(100, storage.Unit.GetPowerPercent(power.Type));
            }));
        }

        foreach (var entry in new (string Name, WoWPowerType Type, Func<WoWUnit, double> Read)[]
        {
            ("HealthPercent", WoWPowerType.Health, unit => unit.HealthPercent),
            ("ManaPercent", WoWPowerType.Mana, unit => unit.ManaPercent),
            ("RagePercent", WoWPowerType.Rage, unit => unit.RagePercent),
            ("FocusPercent", WoWPowerType.Focus, unit => unit.FocusPercent),
            ("EnergyPercent", WoWPowerType.Energy, unit => unit.EnergyPercent),
            ("RunesPercent", WoWPowerType.Runes, unit => unit.RunesPercent),
            ("RunicPowerPercent", WoWPowerType.RunicPower, unit => unit.RunicPowerPercent)
        })
            cases.Add(("public " + entry.Name + " consumes the actual corrected owner", storage =>
            {
                var power = Powers.Single(item => item.Type == entry.Type);
                storage.Set(power, 60_000_000, 120_000_000);
                Equal(50, entry.Read(storage.Unit));
            }));

        cases.Add(("a full large-health target cannot appear in an execute window", storage =>
        {
            storage.Set(Powers[0], 60_000_000, 60_000_000);
            var target = new WoWUnit(storage.Unit.BaseAddress);
            Check(target.CurrentHealth == target.MaxHealth && target.CurrentHealth == 60_000_000,
                "large-health target descriptor control failed");
            Check(target.HealthPercent > 20 && target.HealthPercent > 35,
                "a full target became eligible for low-health decisions: " + target.HealthPercent);
            Equal(100, target.HealthPercent);
        }));
        cases.Add(("large-health threshold remains distinguishable on each side", storage =>
        {
            storage.Set(Powers[0], 24_000_000, 120_000_000);
            Equal(20, storage.Unit.HealthPercent);
            storage.Set(Powers[0], 30_000_000, 120_000_000);
            Equal(25, storage.Unit.HealthPercent);
        }));
        cases.Add(("near maximum signed health remains a three-quarter observation", storage =>
        {
            storage.Set(Powers[0], 1_500_000_000, 2_000_000_000);
            Equal(75, storage.Unit.HealthPercent);
        }));
        foreach (var power in Powers.Where(item => item.Scale == 10))
            cases.Add((power.Type + " preserves the existing raw-tenths normalization", storage =>
            {
                storage.Raw(power.Current, 255);
                storage.Raw(power.Maximum, 1005);
                Check(storage.Unit.GetCurrentPower(power.Type) == 25 && storage.Unit.GetMaxPower(power.Type) == 100,
                    "raw-tenths normalization changed");
                Equal(25, storage.Unit.GetPowerPercent(power.Type));
            }));
        cases.Add(("unsupported power type still rejects the request", storage =>
        {
            try { storage.Unit.GetPowerPercent(WoWPowerType.Unknown); }
            catch (ArgumentOutOfRangeException) { return; }
            throw new Failure("unsupported enum no longer throws its established exception");
        }));

        int passed = 0, assertions = 0, errors = 0;
        foreach (var test in cases)
        {
            try { using var storage = new Storage(); test.Test(storage); passed++; Console.WriteLine("PASS resource percentage: " + test.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL resource percentage assertion: " + test.Name + ": " + error.Message); }
            catch (Exception error) { errors++; Console.Error.WriteLine("ERROR resource percentage owner/fixture: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Resource percentage scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={errors}; actual WoWUnit descriptors/Memory, read-only self-process, no game.");
        if (assertions + errors != 0)
            throw new InvalidOperationException($"Resource percentage regressions: assertions={assertions}; unexpected={errors}");
    }

    private sealed class Storage : IDisposable
    {
        private readonly object fixture;
        private readonly uint descriptor;
        private readonly Dictionary<IntPtr, byte[]> cache;
        internal readonly LocalPlayer Unit;

        internal Storage()
        {
            Type type = typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!;
            fixture = Activator.CreateInstance(type, true)!;
            Unit = (LocalPlayer)Field(fixture, "Player");
            descriptor = (uint)Field(fixture, "descriptor");
            cache = ((ThreadLocal<Dictionary<IntPtr, byte[]>>)Field(fixture, "cache")).Value!;
        }
        internal void Set(Power power, int current, int maximum)
        {
            Raw(power.Current, checked(current * power.Scale));
            Raw(power.Maximum, checked(maximum * power.Scale));
        }
        internal void Raw(UnitFields field, int value)
        {
            IntPtr address = new(unchecked((int)(descriptor + (uint)field * 4)));
            Marshal.WriteInt32(address, value);
            cache.Remove(address);
        }
        public void Dispose() => ((IDisposable)fixture).Dispose();
    }

    private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    private static void Equal(double expected, double actual) => Check(double.IsFinite(actual) && Math.Abs(expected - actual) < 1e-9,
        "expected " + expected + ", observed " + actual);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Failure(message);
    }
}
