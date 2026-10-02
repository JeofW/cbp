using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.WoWInternals;

internal static class RestAuraActivityRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0;
        var failures = new List<string>();
        void Case(string name, Action<object> test)
        {
            using var fixture = (IDisposable)Activator.CreateInstance(typeof(AuraQueryCoverageRegressionTests).GetNestedType("Fixture", Hidden)!, true)!;
            try { test(fixture); passed++; Console.WriteLine("PASS rest aura activity: " + name); }
            catch (Exception e) { failures.Add(name + ": " + e); }
        }
        Case("unrelated missing row does not hide supported rest absence", f => { Auras(f, 61988); Check(Observe(out bool food, out bool drink) && !food && !drink, "complete raw coverage was blocked"); });
        Case("food ID beside unknown marker", f => { Auras(f, 61988, 433); Check(Observe(out bool food, out bool drink) && food && !drink, "food activity lost"); });
        Case("drink ID beside unknown marker", f => { Auras(f, 61988, 27089); Check(Observe(out bool food, out bool drink) && !food && drink, "drink activity lost"); });
        Case("food and drink simultaneous", f => { Auras(f, 433, 27089, 61988); Check(Observe(out bool food, out bool drink) && food && drink, "combined activity lost"); });
        Case("refreshment conservative both resources", f => { Auras(f, 57085, 61988); Check(Observe(out bool food, out bool drink) && food && drink, "refreshment was interrupted"); });
        Case("fresh removal in same frame", f => { Auras(f, 433); Check(Observe(out bool food, out _) && food, "initial food"); Auras(f, 61988); Check(Observe(out food, out _) && !food, "cached activity survived removal"); });
        Case("raw failure remains unknown", f => { f.GetType().GetMethod("BadRawPointer", Hidden)!.Invoke(f, null); Check(!Observe(out _, out _), "raw failure became absence"); });
        Console.WriteLine($"Rest aura activity scenarios: {passed}/{passed+failures.Count}; actual allocated-memory aura reader; supported build12340 ID families.");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
    }
    private static void Auras(object f, params int[] ids) => f.GetType().GetMethod("Auras", Hidden)!.Invoke(f, new object[] { ids });
    private static bool Observe(out bool food, out bool drink)
    {
        var method = typeof(Styx.Logic.Common.Rest).GetMethod("TryObserveActivity", Hidden);
        if (method == null)
        {
            food = ObjectManager.Me!.HasAura("Food");
            drink = ObjectManager.Me!.HasAura("Drink");
            return true;
        }
        object?[] args = { ObjectManager.Me, false, false };
        bool result = (bool)method.Invoke(null, args)!;
        food = (bool)args[1]!; drink = (bool)args[2]!;
        return result;
    }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
