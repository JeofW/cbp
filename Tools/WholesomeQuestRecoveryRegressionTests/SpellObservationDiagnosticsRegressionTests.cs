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

// Observe the production exception through the actual active-aura and Spell row
// readers. Failures originate in controlled test-process metadata, not fake
// exceptions injected at the assertion boundary.
internal static class SpellObservationDiagnosticsRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("known row remains a complete active aura", f => Check(ObjectManager.Me!.GetAllAuras().Count == 1, "known metadata lost its aura")),
            ("missing sparse row explains the unavailable aura", f => { f.RowCall("ClearRow", 0); f.Reason("row-missing"); }),
            ("missing Spell table is distinct from a missing row", f => { f.RowCall("RemoveTable"); f.Reason("table-unavailable"); }),
            ("unloaded header is reported without reading a row", f => { f.PackedCall("Header", "IsLoaded", 0); f.Reason("header-invalid"); }),
            ("invalid row count is reported", f => { f.PackedCall("Header", "NumRows", 0); f.Reason("header-invalid"); }),
            ("live aura 61988 reports the actual range rejection", f => { f.Aura(61988); f.Reason("id-out-of-range", "61988", "41001", "41002"); }),
            ("live aura 56817 reports the actual range rejection", f => { f.Aura(56817); f.Reason("id-out-of-range", "56817", "41001", "41002"); }),
            ("unknown compression mode 2 remains conservative", f => { f.PackedCall("Flag", (byte)2); f.Reason("compression-mode-unknown", "2"); }),
            ("unknown compression mode 255 remains conservative", f => { f.PackedCall("Flag", (byte)255); f.Reason("compression-mode-unknown", "255"); }),
            ("wrong decoded row identity remains unavailable", f => { f.PackedCall("RawId", 41002); f.Reason("row-id-mismatch", "41001", "41002"); }),
            ("a malformed packed stream is diagnosed", f =>
            {
                f.PackedCall("Flag", (byte)1);
                f.PackedCall("Payload", new byte[] { 7, 7, 255, 7, 7, 255, 7, 7, 255 });
                f.Reason("packed-row-unavailable");
            }),
            ("readable slot with unreadable row is distinct from a null slot", f =>
            {
                Marshal.WriteInt32(IntPtr.Add(f.Storage, 256), 1); f.Cache.Remove(IntPtr.Add(f.Storage, 256));
                f.Reason("raw-row-unavailable");
            }),
            ("zero sparse array cannot borrow row storage", f =>
            {
                f.PackedCall("Header", "RowArrayPtr", 0); f.Reason("row-array-invalid");
            }),
            ("a later row hydration clears the prior failure", f =>
            {
                f.RowCall("ClearRow", 0); f.Reason("row-missing"); f.PackedCall("Publish", (uint)44);
                Check(ObjectManager.Me!.GetAllAuras().Single().Spell!.BaseLevel == 44, "a negative lookup poisoned hydration");
            }),
            ("an existing aura retries metadata after hydration", f =>
            {
                f.RowCall("ClearRow", 0);
                var aura = new WoWAura(41001, 1, WoWAura.AuraFlags.FirstEffect, 1, 1, 60000, 60000);
                Check(aura.Spell == null, "missing row fabricated metadata");
                f.PackedCall("Publish", (uint)45);
                Check(aura.Spell?.BaseLevel == 45, "the same aura retained a negative metadata cache");
            }),
            ("changing failure family updates the diagnostic", f =>
            {
                f.RowCall("ClearRow", 0); f.Reason("row-missing");
                f.PackedCall("Publish", (uint)46); f.PackedCall("Flag", (byte)2);
                f.Reason("compression-mode-unknown");
            })
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { using var fixture = new Fixture(); test.Test(fixture); passed++; }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL spell observation: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR spell observation: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Spell observation diagnostics: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual active-aura/Spell/row/memory owners; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Spell observation diagnostics regressions");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object packed;
        private readonly object rows;
        internal readonly IntPtr Storage;
        internal readonly Dictionary<IntPtr, byte[]> Cache;
        internal Fixture()
        {
            packed = Activator.CreateInstance(typeof(PackedSpellRowRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            rows = packed.GetType().GetField("rows", Hidden)!.GetValue(packed)!;
            Storage = (IntPtr)packed.GetType().GetField("storage", Hidden)!.GetValue(packed)!;
            Cache = ((ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(Memory).GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!).Value!;
            Aura(41001);
        }
        internal void Aura(int id)
        {
            uint start = ObjectManager.Me!.BaseAddress;
            var count = new IntPtr(unchecked((int)(start + 3536)));
            Marshal.WriteInt32(count, 1); Cache.Remove(count);
            byte[] bytes = new byte[24]; BitConverter.GetBytes(id).CopyTo(bytes, 8); bytes[12] = 1;
            BitConverter.GetBytes(60000).CopyTo(bytes, 16);
            Marshal.Copy(bytes, 0, new IntPtr(unchecked((int)(start + 3152))), bytes.Length);
        }
        internal void RowCall(string method, params object[] values) => Call(rows, method, values);
        internal void PackedCall(string method, params object[] values) => Call(packed, method, values);
        internal void Reason(params string[] fragments)
        {
            Exception? observed = null;
            try { ObjectManager.Me!.GetAllAuras(); } catch (ObservationUnavailableException error) { observed = error; }
            Check(observed != null, "missing metadata was published as a complete/empty collection");
            foreach (string fragment in fragments)
                Check(observed!.Message.Contains(fragment, StringComparison.Ordinal), "missing diagnostic '" + fragment + "': " + observed.Message);
        }
        public void Dispose() => ((IDisposable)packed).Dispose();
    }
    private static void Call(object owner, string name, object[] values)
    {
        try { owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, values); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool condition, string why) { if (!condition) throw new Failure(why); }
}
