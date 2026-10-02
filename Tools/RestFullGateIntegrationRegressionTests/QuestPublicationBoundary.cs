using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;

// Focused extraction of the controlled process/memory boundary used by the
// publication fixture. It supplies only external observation state; the three
// linked suites execute the actual CopilotBuddy liquid/rest owners.
internal static class QuestPublicationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Fixture : IDisposable
    {
        internal readonly LocalPlayer Player;
        private readonly LocalPlayer? previousPlayer = ObjectManager.Me;
        private readonly Memory? previousMemory = ObjectManager.Wow;
        private readonly ExecutorRand? previousExecutor = ObjectManager.Executor;
        private readonly Memory memory = (Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));
        private readonly ThreadLocal<Dictionary<IntPtr, byte[]>> cache = new(() => new());
        private readonly ThreadLocal<bool> enabled = new(() => true);
        private readonly IntPtr storage = Marshal.AllocHGlobal(65536);
        private IntPtr readHandle;
        private object? previousCache;
        private bool cacheCaptured;

        internal Fixture()
        {
            try
            {
                Marshal.Copy(new byte[65536], 0, storage, 65536);
                uint start = unchecked((uint)storage.ToInt32());
                uint descriptor = start + 4096u;
                Set(memory, "_cache", cache);
                Set(memory, "_cacheEnabled", enabled);
                readHandle = GreenMagic.Native.Imports.OpenProcess(0x0010, false, Environment.ProcessId);
                if (readHandle == IntPtr.Zero)
                    throw new InvalidOperationException("Could not open the focused fixture read handle.");
                Set(memory, "_hProcess", readHandle);

                Bytes(0xBD0792u, new byte[] { 1 });
                Bytes(0xB6A9E0u, BitConverter.GetBytes(0u));
                Bytes(0xB6AA38u, BitConverter.GetBytes(0u));
                Bytes(0xBD088Cu, BitConverter.GetBytes(1u));
                Bytes(12488416u, BitConverter.GetBytes(0));
                Bytes(12488476u, BitConverter.GetBytes(0u));

                Write(start + 8u, descriptor);
                Write(start + 0x14u, 4u);
                Write(start + 0xBCu, 0u);
                Write(start + 48u, 123u);
                Write(descriptor, 123u);
                Type fields = typeof(Styx.Offsets.WoWUnitFields);
                Write(descriptor + Convert.ToUInt32(Enum.Parse(fields, "Level")) * 4u, 20u);
                Write(descriptor + Convert.ToUInt32(Enum.Parse(fields, "Bytes0")) * 4u, 0x0101u);

                typeof(ObjectManager).GetProperty("Wow", Hidden)!.SetValue(null, memory);
                ObjectManager.Executor = null;
                Player = new LocalPlayer(start);
                ObjectManager.Me = Player;

                previousCache = typeof(StyxWoW).GetField("_cache", Hidden)!.GetValue(null);
                cacheCaptured = true;
                typeof(StyxWoW).GetField("_cache", Hidden)!.SetValue(null, new WoWCache());
                if (!Player.IsValid || Player.Guid == 0 || Player.MapId != 1u)
                    throw new InvalidOperationException("Focused world observation setup is incomplete.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Bytes(uint address, byte[] bytes) => cache.Value![new IntPtr(unchecked((int)address))] = bytes;
        private static void Write(uint address, uint value)
            => Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value));
        private static void Set(object target, string name, object? value)
            => target.GetType().GetField(name, Hidden)!.SetValue(target, value);

        public void Dispose()
        {
            if (cacheCaptured) typeof(StyxWoW).GetField("_cache", Hidden)!.SetValue(null, previousCache);
            ObjectManager.Me = previousPlayer;
            ObjectManager.Executor = previousExecutor;
            typeof(ObjectManager).GetProperty("Wow", Hidden)!.SetValue(null, previousMemory);
            Set(memory, "_hProcess", IntPtr.Zero);
            if (readHandle != IntPtr.Zero)
            {
                GreenMagic.Native.Imports.CloseHandle(readHandle);
                readHandle = IntPtr.Zero;
            }
            cache.Dispose();
            enabled.Dispose();
            Marshal.FreeHGlobal(storage);
        }
    }
}
