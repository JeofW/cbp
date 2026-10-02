using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.WoWInternals;

// Real aura/Spell/table readers, including an IN-RANGE null sparse slot matching
// the live incident. The fixture never attaches to or dispatches into the game.
internal static class AuraQueryCoverageRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("exact missing-metadata ID is present", f => Check(ObjectManager.Me!.HasAura(61988), "raw marker was lost")),
            ("unrelated exact ID absence is known", f => Check(!ObjectManager.Me!.HasAura(61987), "unobserved ID became present")),
            ("known positive name survives earlier unknown metadata", f => Check(ObjectManager.Me!.HasAura("Fixture Drink"), "known drink was hidden")),
            ("negative name retains UNKNOWN", f => Unknown(() => ObjectManager.Me!.HasAura("Unobserved Food"))),
            ("full metadata collection remains UNKNOWN", f => Unknown(() => ObjectManager.Me!.GetAllAuras())),
            ("raw query retains creator and active flags", f => { var aura = ObjectManager.Me!.GetAuraById(61988); Check(aura != null && aura.CreatorGuid == 123 && aura.IsActive, "raw ownership was lost"); }),
            ("name fallback cannot fabricate unknown metadata", f => Unknown(() => ObjectManager.Me!.HasAura("Spell_61988"))),
            ("a fresh raw read observes removal in the same frame", f => { f.Epoch(10); Check(ObjectManager.Me!.HasAura(61988), "initial marker"); f.Auras(41001); Check(!ObjectManager.Me!.HasAura(61988), "raw ID presence was cached across mutation"); }),
            ("known metadata resolution is shared in one owned frame", f => { f.Epoch(10); var first = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; var second = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; Check(ReferenceEquals(first, second), "same-frame metadata was resolved twice"); }),
            ("unavailable resolution is shared in one owned frame", f => { f.Epoch(10); var first = ObjectManager.Me!.GetAuraById(61988)!; var second = ObjectManager.Me!.GetAuraById(61988)!; Check(first.Spell == null && second.Spell == null, "missing row fabricated a spell"); var reason = typeof(WoWAura).GetProperty("MetadataFailure", Hidden)!; Check(ReferenceEquals(reason.GetValue(first), reason.GetValue(second)), "same-frame missing row was resolved twice"); }),
            ("a new frame refreshes metadata", f => { f.Epoch(10); var first = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; f.Epoch(11); var second = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; Check(!ReferenceEquals(first, second), "new frame retained old metadata"); }),
            ("an absent executor never creates an immortal epoch", f => { var first = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; var second = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; Check(!ReferenceEquals(first, second), "executor-free observations shared an unbounded epoch"); }),
            ("zero frame never creates an immortal epoch", f => { f.Epoch(0); var first = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; var second = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; Check(!ReferenceEquals(first, second), "zero frame shared an unbounded epoch"); }),
            ("missing row hydrates in the next owned epoch", f => { f.Epoch(10); var aura = ObjectManager.Me!.GetAuraById(61988)!; Check(aura.Spell == null, "missing row was known"); f.HydrateMarker(); f.Epoch(11); Check(aura.Spell?.Id == 61988, "negative epoch survived metadata hydration"); }),
            ("replacement memory invalidates same-frame metadata", f => { f.Epoch(10); var first = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; f.ReplaceMemory(); var second = ObjectManager.Me!.GetAuraByName("Fixture Drink")!.Spell; Check(!ReferenceEquals(first, second), "replacement memory borrowed old metadata"); }),
            ("raw observation failure is explicit and has no collection", f => { f.BadRawPointer(); Check(!ObjectManager.Me!.TryGetRawAuras(out var auras) && auras == null, "unreadable raw bytes became empty coverage"); }),
            ("raw metadata-free observation has complete ID coverage", f => Check(ObjectManager.Me!.TryGetRawAuras(out var auras) && auras!.Select(a => a.SpellId).SequenceEqual(new[] { 61988, 41001 }), "raw observation lost an active ID")),
            ("table replacement invalidates same-frame metadata", f => { f.Epoch(10); _ = ObjectManager.Me!.GetAuraByName("Fixture Drink"); f.RemoveTable(); Unknown(() => ObjectManager.Me!.HasAura("Fixture Drink")); }),
            ("raw IDs do not require the Spell table", f => { f.RemoveTable(); Check(ObjectManager.Me!.HasAura(61988), "raw ID required a metadata table"); }),
            ("unreadable raw records remain UNKNOWN for exact IDs", f => { f.BadRawPointer(); Unknown(() => ObjectManager.Me!.HasAura(61988)); })
        };
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { using var f = new Fixture(); test.Test(f); Console.WriteLine("PASS aura query coverage: " + test.Name); }
            catch (Exception error) { failures.Add(test.Name + ": " + error); Console.Error.WriteLine("FAIL aura query coverage: " + failures[^1]); }
        }
        Console.WriteLine($"Aura query coverage: {cases.Count - failures.Count}/{cases.Count}; actual original-client readers; no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable rows;
        private readonly IntPtr storage;
        private readonly IntPtr array = Marshal.AllocHGlobal((61988 - 41001 + 1) * 4);
        private readonly Dictionary<IntPtr, byte[]> cache;
        private readonly object? names;
        private readonly object? namesReady;
        private ExecutorRand? executor;
        internal Fixture()
        {
            rows = (IDisposable)Activator.CreateInstance(typeof(SpellRowLookupRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            storage = (IntPtr)rows.GetType().GetField("storage", Hidden)!.GetValue(rows)!;
            cache = ((ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(Memory).GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!).Value!;
            Call(rows, "Publish", 0, (uint)40);
            Marshal.Copy(new byte[(61988 - 41001 + 1) * 4], 0, array, (61988 - 41001 + 1) * 4);
            Marshal.WriteInt32(array, Marshal.ReadInt32(IntPtr.Add(storage, 256)));
            var header = typeof(WoWDb).GetNestedType("DbTableHeader", BindingFlags.NonPublic)!;
            Marshal.WriteInt32(storage, Marshal.OffsetOf(header, "MaxIndex").ToInt32(), 61988);
            Marshal.WriteInt32(storage, Marshal.OffsetOf(header, "RowArrayPtr").ToInt32(), array.ToInt32());
            cache.Remove(storage);
            names = typeof(SpellDb).GetField("_spells", Hidden)!.GetValue(null);
            namesReady = typeof(SpellDb).GetField("_initialized", Hidden)!.GetValue(null);
            typeof(SpellDb).GetField("_spells", Hidden)!.SetValue(null, new Dictionary<int, SpellDb.SpellData> { [41001] = new() { Id = 41001, Name = "Fixture Drink" } });
            typeof(SpellDb).GetField("_initialized", Hidden)!.SetValue(null, true);
            Auras(61988, 41001);
        }
        internal void Auras(params int[] ids)
        {
            uint start = ObjectManager.Me!.BaseAddress;
            var count = new IntPtr(unchecked((int)(start + 3536)));
            Marshal.WriteInt32(count, ids.Length); cache.Remove(count);
            byte[] bytes = new byte[ids.Length * 24];
            for (int i = 0; i < ids.Length; i++) { BitConverter.GetBytes((ulong)123).CopyTo(bytes, i * 24); BitConverter.GetBytes(ids[i]).CopyTo(bytes, i * 24 + 8); bytes[i * 24 + 12] = 1; }
            Marshal.Copy(bytes, 0, new IntPtr(unchecked((int)(start + 3152))), bytes.Length);
        }
        internal void Epoch(uint value)
        {
            if (executor == null)
            {
                executor = (ExecutorRand)RuntimeHelpers.GetUninitializedObject(typeof(ExecutorRand));
                typeof(ExecutorRand).GetProperty("Memory")!.SetValue(executor, ObjectManager.Wow);
                typeof(ExecutorRand).GetField("m_FrameCountPtr", Hidden)!.SetValue(executor, unchecked((uint)storage.ToInt32()) + 15000);
                ObjectManager.Executor = executor;
            }
            Marshal.WriteInt32(storage, 15000, unchecked((int)value)); cache.Remove(IntPtr.Add(storage, 15000));
        }
        internal void RemoveTable() => Call(rows, "RemoveTable");
        internal void ReplaceMemory() => Call(rows, "ReplaceMemory");
        internal void HydrateMarker()
        {
            var row = IntPtr.Add(storage, 12000);
            Marshal.Copy(new byte[680], 0, row, 680); Marshal.WriteInt32(row, 61988);
            var slot = IntPtr.Add(array, (61988 - 41001) * 4);
            Marshal.WriteInt32(slot, row.ToInt32()); cache.Remove(slot);
        }
        internal void BadRawPointer()
        {
            uint start = ObjectManager.Me!.BaseAddress;
            foreach (var value in new[] { (3536, -1), (3156, 1), (3160, 1) }) { var ptr = new IntPtr(unchecked((int)(start + (uint)value.Item1))); Marshal.WriteInt32(ptr, value.Item2); cache.Remove(ptr); }
        }
        public void Dispose()
        {
            ObjectManager.Executor = null;
            typeof(SpellDb).GetField("_spells", Hidden)!.SetValue(null, names);
            typeof(SpellDb).GetField("_initialized", Hidden)!.SetValue(null, namesReady);
            Marshal.FreeHGlobal(array); rows.Dispose();
        }
    }
    private static object? Call(object owner, string name, params object[] values)
    {
        try { return owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, values); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Unknown(Action action) { try { action(); } catch (ObservationUnavailableException) { return; } throw new InvalidOperationException("UNKNOWN was lost"); }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
