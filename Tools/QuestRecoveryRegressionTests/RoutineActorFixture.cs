using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// The same process-owned descriptor/cache technique used by the retained
// QuestPublication fixture. No game process is opened and no executor exists.
// A zero-address LocalPlayer is no longer a valid input for actor-owned casts.
internal sealed class RoutineActorFixture : IDisposable
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly LocalPlayer? previousPlayer = ObjectManager.Me;
    private readonly Memory? previousMemory = ObjectManager.Wow;
    private readonly Memory memory = (Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));
    private readonly ThreadLocal<Dictionary<IntPtr, byte[]>> cache = new(() => new());
    private readonly ThreadLocal<bool> enabled = new(() => true);
    private readonly IntPtr storage;
    private bool disposed;
    internal LocalPlayer Player { get; }

    internal RoutineActorFixture()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4 || ObjectManager.Executor != null
            || previousPlayer != null || previousMemory != null)
            throw new InvalidOperationException("Routine actor fixture requires an unattached Windows/x86 process.");
        storage = Marshal.AllocHGlobal(65536);
        try
        {
            Marshal.Copy(new byte[65536], 0, storage, 65536);
            uint start = unchecked((uint)storage.ToInt32()), descriptor = start + 4096;
            Set("_cache", cache); Set("_cacheEnabled", enabled); Set("_hProcess", new IntPtr(-1));
            void Write(uint address, uint value) => Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value));
            void Bytes(uint address, byte[] bytes) => cache.Value![new IntPtr(unchecked((int)address))] = bytes;
            Write(start + 8, descriptor); Write(start + 0x14, 4); Write(start + 0xBC, 0);
            Write(start + 48, 123); Write(descriptor, 123);
            Type fields = typeof(WoWUnit).Assembly.GetTypes().Single(type => type.IsEnum && type.Name == "UnitFields");
            void Field(string name, uint value) => Write(descriptor + Convert.ToUInt32(Enum.Parse(fields, name)) * 4, value);
            Field("Health", 100); Field("MaxHealth", 100); Field("Level", 20); Field("Bytes0", 0x0201);
            Bytes(0xBD0792, new byte[] { 1 }); Bytes(0xB6AA38, BitConverter.GetBytes(0u));
            Bytes(0xBD088C, BitConverter.GetBytes(1u));
            Bytes(12488416, BitConverter.GetBytes(0)); Bytes(12488476, BitConverter.GetBytes(0u));
            // Original build12340 NetClient getter 6B0970 reads C79CF4.
            // The full reader sees only this allocated NetStats and timer pointer;
            // equal zero indices avoid consuming external samples.
            Bytes(0x00C79CF4, BitConverter.GetBytes(start + 32768));
            Bytes(0xD4159C, BitConverter.GetBytes(0u));
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, memory);
            Player = new ObservedPlayer(start);
            ObjectManager.Me = Player;
            if (!Player.IsValid || !Player.IsAlive || Player.Guid != 123 || ObjectManager.Executor != null)
                throw new InvalidOperationException("Process-owned routine actor did not reach the actual validity/health readers.");
        }
        catch { Dispose(); throw; }
    }

    private void Set(string field, object value) => typeof(Memory).GetField(field, Hidden)!.SetValue(memory, value);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ObjectManager.Me = previousPlayer;
        typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, previousMemory);
        Set("_hProcess", IntPtr.Zero);
        cache.Dispose(); enabled.Dispose();
        if (storage != IntPtr.Zero) Marshal.FreeHGlobal(storage);
    }

    private sealed class ObservedPlayer(uint address) : LocalPlayer(address)
    {
        public override string Name => "Routine actor fixture";
        public override WoWPoint Location => new(10, 10, 10);
    }
}
