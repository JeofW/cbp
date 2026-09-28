using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using GreenMagic;
using GreenMagic.Native;
using Styx.WoWInternals;

// The actual shared fixture reaches real Interact -> ResetAfk before the
// missing-executor refusal. Its process handle must permit observations only.
// Every write probe below targets this test's own allocated canary, never a
// client-global numeric address or any other process.
internal static class PublicationFixtureAccessRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Real fixture handle checks require Windows x86.");
        var cases = new List<(string Name, Action<Probe> Run)>
        {
            ("shared fixture has a restricted real handle", probe =>
                Check(probe.Handle != IntPtr.Zero && probe.Handle != new IntPtr(-1), "fixture retained the writable self-process pseudo handle")),
            ("actual typed observation reads allocated canary", probe =>
                Check(probe.Memory.Read<uint>(probe.Address) == Probe.Seed, "restricted observation cannot read fixture bytes")),
            ("native observation reads allocated canary", probe =>
                Check(Imports.ReadProcessMemory(probe.Handle, probe.Address, new byte[4], 4, out int read) && read == 4,
                    "native observation no longer has read permission")),
            ("AFK-shaped uint write is refused", probe =>
                Check(!probe.Memory.Write<uint>(probe.Address, 42U) && probe.Value == Probe.Seed,
                    "actual uint write changed allocated canary through the observation handle")),
            ("typed byte array write is refused", probe =>
                Check(!probe.Memory.Write<byte[]>(probe.Address, BitConverter.GetBytes(42U)) && probe.Value == Probe.Seed,
                    "byte array write bypassed observation-only permission")),
            ("raw native write is refused", probe =>
                Check(!Imports.WriteProcessMemory(probe.Handle, probe.Address, BitConverter.GetBytes(42U), 4U, out int written)
                    && written == 0 && probe.Value == Probe.Seed, "native write permission remains available")),
            ("fixture disposal detaches and closes its observation handle", probe =>
            {
                probe.DisposeFixture();
                Check(probe.Memory.ProcessHandle == IntPtr.Zero
                    && !Imports.ReadProcessMemory(probe.Handle, probe.Address, new byte[4], 4, out _),
                    "disposed fixture left a usable observation handle");
            }),
            ("offline fixture still has no native executor", probe =>
                Check(ObjectManager.Executor == null, "fixture acquired a native executor"))
        };
        int pass = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var probe = new Probe(); item.Run(probe); pass++; Console.WriteLine("PASS publication fixture access: " + item.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL publication fixture access: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR publication fixture access: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Publication fixture access scenarios: {pass}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual shared fixture/Memory/native access; only owned canary writes; no client-global write or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Publication fixture access regression");
    }

    private sealed class Probe : IDisposable
    {
        internal const uint Seed = 0x12345678;
        private readonly IntPtr canary = Marshal.AllocHGlobal(4);
        private IDisposable? fixture;
        internal Memory Memory { get; }
        internal IntPtr Handle { get; }
        internal uint Address => unchecked((uint)canary.ToInt32());
        internal uint Value => unchecked((uint)Marshal.ReadInt32(canary));

        internal Probe()
        {
            try
            {
                Marshal.WriteInt32(canary, unchecked((int)Seed));
                var type = typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!;
                try { fixture = (IDisposable)Activator.CreateInstance(type, true)!; }
                catch (TargetInvocationException error) when (error.InnerException != null)
                { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
                Memory = (Memory)type.GetField("memory", Hidden)!.GetValue(fixture)!;
                Handle = Memory.ProcessHandle;
            }
            catch { Dispose(); throw; }
        }

        internal void DisposeFixture()
        {
            var owned = fixture;
            fixture = null;
            owned?.Dispose();
        }

        public void Dispose()
        {
            try { DisposeFixture(); }
            finally { Marshal.FreeHGlobal(canary); }
        }
    }

    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
