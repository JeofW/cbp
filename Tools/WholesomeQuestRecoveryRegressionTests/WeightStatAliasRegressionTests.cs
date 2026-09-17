using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Styx.Logic.Inventory;

// Actual Stat enum and WeightSetEx scoring. No constructor/Lua event registration,
// item use, equipment change, game attachment or replacement scoring algorithm.
internal static class WeightStatAliasRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        foreach (string culture in new[] { "en-US", "tr-TR" })
        {
            var locale = culture;
            foreach (string name in new[] { "ManaRegeneration", "ManaPer5Sec", "manaper5sec", "MANAPER5SEC", "ManaPer5Sec2" })
            {
                var spelling = name;
                cases.Add((locale + " scores " + spelling + " through the real weight owner", () =>
                {
                    using var fixture = new Fixture(locale);
                    Check(fixture.Weight.GetStatScore(spelling, 12f) == 30f, "supported mana regeneration was discarded or rescaled");
                }));
            }
            cases.Add((locale + " XML enum-name contract accepts the legacy MP5 alias", () =>
            {
                using var fixture = new Fixture(locale);
                Check(Enum.TryParse<Stat>("ManaPer5Sec", true, out var parsed) && parsed == Stat.ManaRegeneration,
                    "the name consumed by the actual XML loader is not the canonical mana stat");
            }));
        }
        cases.Add(("typed canonical scoring is unchanged", () => { using var f = new Fixture("en-US"); Check(f.Weight.GetStatScore(Stat.ManaRegeneration, 12f) == 30f, "canonical score changed"); }));
        cases.Add(("zero-weight MP5 remains intentionally zero", () => { using var f = new Fixture("en-US"); f.Weight.StatScores[Stat.ManaRegeneration] = 0f; Check(f.Weight.GetStatScore("ManaPer5Sec", 12f) == 0f, "zero weight gained a score"); }));
        cases.Add(("missing mana weight stays zero", () => { using var f = new Fixture("en-US"); f.Weight.StatScores.Clear(); Check(f.Weight.GetStatScore("ManaPer5Sec", 12f) == 0f, "missing weight borrowed an unrelated default"); }));
        cases.Add(("zero and NaN points retain zero contribution", () => { using var f = new Fixture("en-US"); Check(f.Weight.GetStatScore("ManaPer5Sec", 0f) == 0f && f.Weight.GetStatScore("ManaPer5Sec", float.NaN) == 0f, "invalid points gained a score"); }));
        cases.Add(("negative point deltas retain their sign", () => { using var f = new Fixture("en-US"); Check(f.Weight.GetStatScore("ManaPer5Sec", -2f) == -5f, "negative delta lost its canonical weight"); }));
        cases.Add(("unrelated weapon defaults remain unchanged", () => { using var f = new Fixture("en-US"); Check(f.Weight.GetStatScore("DPS", 7f) == 7f, "weapon fallback changed"); }));
        cases.Add(("unknown names are still unknown", () => { using var f = new Fixture("en-US"); Check(!Enum.TryParse<Stat>("ManaPer5SecTypo", true, out _) && f.Weight.GetStatScore("ManaPer5SecTypo", 12f) == 0f, "arbitrary spelling silently gained a weight"); }));
        cases.Add(("null stat name retains argument exception", () => { using var f = new Fixture("en-US"); bool caught = false; try { f.Weight.GetStatScore(null!, 12f); } catch (ArgumentNullException) { caught = true; } Check(caught, "null name contract changed"); }));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS weight stat alias: " + item.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL weight stat alias assertion: " + item.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR weight stat alias fixture: " + item.Name + ": " + e); }
        }
        Console.WriteLine($"Weight stat alias scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual WeightSetEx; no equipment or game effects.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Weight stat alias regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Fixture : IDisposable
    {
        private static readonly FieldInfo Cached = typeof(WeightSetEx).GetField("_cachedWeightSet", BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly FieldInfo Loaded = typeof(WeightSetEx).GetField("_loadedWeightSets", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? oldCached = Cached.GetValue(null), oldLoaded = Loaded.GetValue(null);
        private readonly CultureInfo oldCulture = CultureInfo.CurrentCulture, oldUiCulture = CultureInfo.CurrentUICulture;
        internal readonly WeightSetEx Weight;
        internal Fixture(string culture)
        {
            Weight = (WeightSetEx)FormatterServices.GetUninitializedObject(typeof(WeightSetEx));
            typeof(WeightSetEx).GetProperty("StatScores")!.SetValue(Weight, new Dictionary<Stat, float> { { Stat.ManaRegeneration, 2.5f } });
            Loaded.SetValue(null, new[] { Weight }); Cached.SetValue(null, Weight);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        }
        public void Dispose() { Cached.SetValue(null, oldCached); Loaded.SetValue(null, oldLoaded); CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUiCulture; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
