using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Styx.WoWInternals;

internal static class BagRawCoverageRegressionTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [ModuleInitializer]
    internal static void Run()
    {
        int pass = 0;
        var failures = new List<string>();
        void Case(string name, Action<Fixture> test)
        {
            using var fixture = new Fixture();
            try { test(fixture); pass++; Console.WriteLine("PASS raw bag coverage: " + name); }
            catch (Exception e) { failures.Add(name + ": " + e); }
        }
        Case("complete empty slots", f => Check(f.Read(2, true).Length == 2, "empty slots lost"));
        Case("physical and empty slots preserved", f => { Marshal.WriteInt64(f.Items, 10); var values = f.Read(2, true); Check(values.Length == 2 && values[0] == 10 && values[1] == 0, "GUID bytes changed"); });
        Case("unreadable slots never become empty", f => Unknown(() => f.Read(2, false)));
        Case("invalid structure never becomes an empty bag", f => Unknown(() => f.Structure(1)));
        Case("excessive slot count rejected", f => Unknown(() => f.Read(151, true)));
        Case("unavailable item base rejected", f => Unknown(() => f.Read(2, false, 0)));
        Console.WriteLine($"Raw bag coverage: {pass}/{pass+failures.Count}; actual memory reader against allocated test-process bytes.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world = (IDisposable)Activator.CreateInstance(typeof(SpellRowLookupRegressionTests).GetNestedType("Fixture", All)!, true)!;
        private readonly IntPtr data = Marshal.AllocHGlobal(256);
        internal IntPtr Items => IntPtr.Add(data, 64);
        internal Fixture() { Marshal.Copy(new byte[256], 0, data, 256); ObjectManager.Wow!.DisableCache(); }
        internal object Structure(uint address) => Call(typeof(WoWBag).GetMethod("ReadStructure", All)!, null, address)!;
        internal ulong[] Read(int count, bool readable, uint bad = 1)
        {
            Marshal.WriteInt32(data, count);
            Marshal.WriteInt32(data, 4, unchecked((int)(readable ? (uint)Items.ToInt32() : bad)));
            var structure = Structure(unchecked((uint)data.ToInt32()));
            var bag = Activator.CreateInstance(typeof(WoWBag), All, null, new[] { structure }, null)!;
            return (ulong[])Call(typeof(WoWBag).GetMethod("ReadItemGuids", All)!, bag)!;
        }
        public void Dispose() { Marshal.FreeHGlobal(data); world.Dispose(); }
    }
    private static object? Call(MethodInfo method, object? owner, params object[] args)
    {
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static void Unknown(Action read)
    {
        try { read(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Incomplete read became a usable inventory observation.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
