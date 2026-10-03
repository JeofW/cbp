using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using GreenMagic;
using Styx.Logic.Questing;
using Styx.WoWInternals;

internal static class QuestFixtureIsolationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static IntPtr Pointer(uint value) => new(unchecked((int)value));
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    [ModuleInitializer]
    internal static void Run()
    {
        var failures = new List<string>(); int count = 0;
        void Case(string name, Action action)
        {
            count++;
            try { action(); Console.WriteLine("PASS fixture isolation: " + name); }
            catch (Exception error) { failures.Add(name + ": " + (error.InnerException ?? error).Message); Console.Error.WriteLine("FAIL fixture isolation: " + failures.Last()); }
        }
        foreach (var sample in new[] {
            (Field: "start", Address: 0xBD0000u, Bytes: 65536),
            (Field: "start", Address: 0xBCF000u, Bytes: 65536),
            (Field: "node", Address: 0xBD0792u, Bytes: 4096),
            (Field: "node", Address: 0xBD0791u, Bytes: 4096),
            (Field: "itemStorage", Address: 0xBD0000u, Bytes: 8192),
            (Field: "itemStorage", Address: 0xBD0792u, Bytes: 8192) })
        {
            var selected = sample;
            Case("fixed world address cannot alias " + selected.Field + "/" + selected.Address.ToString("X"), () =>
            {
                using var fixture = new QuestInventorySnapshotRegressionTests.Fixture();
                var native = fixture.NativeFixture;
                var field = native.GetType().GetField(selected.Field, Hidden)!;
                object original = field.GetValue(native)!;
                IntPtr actual = original is IntPtr pointer ? pointer : Pointer((uint)original);
                var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(Memory).GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!;
                var offered = new Queue<IntPtr>(new[] { Pointer(selected.Address), actual });
                var released = new List<IntPtr>();
                // Replay an allocation result only through the real invalidator's
                // address arithmetic. Never read/write/free the synthetic address.
                IntPtr admitted = QuestFixtureBuffer.AllocateCore(selected.Bytes, _ => offered.Dequeue(), released.Add);
                try
                {
                    field.SetValue(native, original is IntPtr ? (object)admitted : unchecked((uint)admitted.ToInt32()));
                    native.GetType().GetMethod("Invalidate", Hidden)!.Invoke(native, null);
                }
                finally { field.SetValue(native, original); }
                bool worldPresent = cache.Value!.TryGetValue(Pointer(0xBD0792), out byte[]? world) && world.SequenceEqual(new byte[] { 1 });
                // Once its seeded flag is lost there is no owned native value at
                // this synthetic address. Supply an explicit absent observation
                // instead of reading an unrelated byte of the CLR process.
                if (!worldPresent) cache.Value![Pointer(0xBD0792)] = new byte[] { 0 };
                var inventory = (QuestInventorySnapshot)fixture.Capture();
                Check(worldPresent && admitted == actual && released.SequenceEqual(new[] { Pointer(selected.Address) })
                    && inventory.IsComplete && inventory.IsCurrent(), "world seed preserved=" + worldPresent
                    + "; actual inventory status=" + inventory.Status + "; allocator must keep the address spaces independent");
            });
        }
        foreach (var sample in new[] { (Address: 0x01000000u, Bytes: 1), (Address: 0x80000000u, Bytes: 4096), (Address: 0xFFFFF000u, Bytes: 4096) })
        {
            var selected = sample;
            Case("valid x86 allocation " + selected.Address.ToString("X"), () => {
                var freed = new List<IntPtr>(); int calls = 0;
                var value = QuestFixtureBuffer.AllocateCore(selected.Bytes, _ => { calls++; return Pointer(selected.Address); }, freed.Add);
                Check(value == Pointer(selected.Address) && calls == 1 && freed.Count == 0, "valid address changed or was released");
            });
        }
        foreach (uint rejected in new[] { 1u, 0x400000u, 0xFFFFFFu, 0xFFFFFF00u })
        {
            uint selected = rejected;
            Case("unsafe or wrapping allocation " + selected.ToString("X"), () => {
                var offered = new Queue<IntPtr>(new[] { Pointer(selected), Pointer(0x30000000) }); var freed = new List<IntPtr>();
                var value = QuestFixtureBuffer.AllocateCore(4096, _ => offered.Dequeue(), freed.Add);
                Check(value == Pointer(0x30000000) && freed.SequenceEqual(new[] { Pointer(selected) }), "unsafe address was accepted or not released");
            });
        }
        foreach (int size in new[] { 0, -1, int.MaxValue })
        {
            int selected = size;
            Case("invalid buffer size " + selected, () => {
                int calls = 0; Exception? error = null; try { QuestFixtureBuffer.AllocateCore(selected, _ => { calls++; return Pointer(0x30000000); }, _ => { }); } catch (Exception observed) { error = observed; }
                Check(error is ArgumentOutOfRangeException && calls == 0, "invalid size reached allocator");
            });
        }
        Case("allocation failure releases rejected reservations", () => {
            int calls = 0; var freed = new List<IntPtr>(); var expected = new OutOfMemoryException("controlled allocation failure"); Exception? error = null;
            try { QuestFixtureBuffer.AllocateCore(4096, _ => ++calls == 1 ? Pointer(0xBD0000) : throw expected, freed.Add); } catch (Exception observed) { error = observed; }
            Check(ReferenceEquals(error, expected) && freed.SequenceEqual(new[] { Pointer(0xBD0000) }), "failure did not preserve cause and release reservations");
        });
        Case("bounded allocation failure", () => {
            int calls = 0, released = 0; Exception? error = null;
            try { QuestFixtureBuffer.AllocateCore(4096, _ => { calls++; return Pointer(0xBD0000); }, _ => released++); } catch (Exception observed) { error = observed; }
            Check(error is OutOfMemoryException && calls == 32 && released == calls, "allocation retry is not bounded and fully released");
        });
        Case("missing world stays unavailable", () => {
            using var fixture = new QuestInventorySnapshotRegressionTests.Fixture();
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(Memory).GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!;
            cache.Value![Pointer(0xBD0792)] = new byte[] { 0 };
            Check(!((QuestInventorySnapshot)fixture.Capture()).IsComplete, "fixture allocation manufactured world presence");
        });
        Console.WriteLine($"Quest fixture allocation isolation: {count - failures.Count}/{count}; synthetic address arithmetic with actual invalidation/inventory readers; no writes to fixed client addresses.");
        if (failures.Count != 0) throw new InvalidOperationException("fixture allocation isolation: " + string.Join("; ", failures));
    }
}
