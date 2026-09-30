using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual allocated inventory/item memory. Only the fixed client trade-global
// address is mapped into an allocated fixture buffer by the read transport.
internal static class QuestInventorySnapshotRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const uint TradeItems = 0xBFA620;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4) throw new PlatformNotSupportedException("Windows x86 inventory observations required");
        var cases = new List<(string Name, Action Body)>();
        void Case(string name, Action<Fixture> action) => cases.Add((name, () => { using var f = new Fixture(); action(f); }));
        Case("complete empty inventory has known zero counts", f => { var result = f.Capture(); Known(result); Check(Counts(result)!.Count == 0, "known empty was not empty"); });
        Case("backpack stack belongs to the current player", f => { f.Items((301, 2)); Check(Count(f.Capture(), 301) == 2, "stack missing"); });
        Case("multiple physical stacks add without truncation", f => { f.Item(23, 301, 4); f.Item(24, 301, 7); Check(Count(f.Capture(), 301) == 11, "stack totals differ"); });
        foreach (int slot in new[] { 0, 18, 19, 22, 23, 38, 86, 117, 118, 149 })
        {
            int captured = slot;
            Case("carried slot " + captured + " remains represented", f =>
            {
                if (captured is >= 19 and <= 22) f.Bag(captured, 701, 2);
                else f.Item(captured, 701, 2);
                Check(Count(f.Capture(), 701) == (captured is >= 19 and <= 22 ? 1 : 2), "carried range omitted");
            });
        }
        foreach (int slot in new[] { 39, 66, 67, 73, 74, 85 })
        {
            int captured = slot;
            Case("bank or buyback slot " + captured + " cannot donate stock", f => { f.Item(captured, 301, 9); Check(!Counts(f.Capture())!.ContainsKey(301), "noncarried item counted"); });
        }
        Case("all four equipped bags are traversed", f =>
        {
            for (int slot = 19; slot <= 22; slot++) { var bag = f.Bag(slot, 700 + slot, 2); f.InBag(bag, 0, 301, 2); }
            Check(Count(f.Capture(), 301) == 8, "bag sources omitted");
        });
        Case("maximum supported carried layout stays bounded and complete", f =>
        {
            foreach (int slot in Enumerable.Range(0, 39).Concat(Enumerable.Range(86, 64)).Where(slot => slot < 19 || slot > 22)) f.Item(slot, 301, 1);
            for (int slot = 19; slot <= 22; slot++)
            {
                var bag = f.Bag(slot, 700 + slot, 36);
                for (int index = 0; index < 36; index++) f.InBag(bag, index, 301, 1);
            }
            Check(Count(f.Capture(), 301) == 243, "bounded maximum layout lost or duplicated items");
        });
        Case("nonzero unresolved GUID is unknown instead of absent", f =>
        {
            f.MainSlot(23, 0xAABB); Check(f.Player.CarriedItems.Count == 0, "legacy comparison fixture changed"); Unknown(f.Capture());
        });
        Case("duplicate GUID does not become duplicated stock", f => { var item = f.Item(23, 301, 2); f.MainSlot(24, item.Guid); Unknown(f.Capture()); });
        Case("duplicate across bag and backpack remains unknown", f =>
        {
            var item = f.Item(23, 301, 2); var bag = f.Bag(19, 700, 2); f.Write64(bag.Slots, item.Guid); Unknown(f.Capture());
        });
        Case("wrong item owner cannot donate a stack", f => { var item = f.Item(23, 301, 2); f.Write64(item.Fields + 24, 999); Unknown(f.Capture()); });
        Case("wrong containing bag cannot donate a stack", f => { var item = f.Item(23, 301, 2); f.Write64(item.Fields + 32, 999); Unknown(f.Capture()); });
        Case("mismatched object and descriptor GUID cannot donate stock", f => { var item = f.Item(23, 301, 2); f.Write64(item.Fields, 999); Unknown(f.Capture()); });
        Case("zero item entry is incomplete metadata", f => { var item = f.Item(23, 301, 2); f.Write32(item.Fields + 12, 0); Unknown(f.Capture()); });
        Case("zero physical stack remains unknown", f => { var item = f.Item(23, 301, 2); f.Write32(item.Fields + 56, 0); Unknown(f.Capture()); });
        Case("unreadable item descriptor cannot be silently skipped", f => { var item = f.Item(23, 301, 2); f.Write32(item.Address + 8, 1); Unknown(f.Capture()); });
        Case("wrong player inventory GUID is not current inventory", f => { f.Write64(f.Player.BaseAddress + 6392, 999); Unknown(f.Capture()); });
        Case("wrong inventory layout is not an empty bag", f => { f.Write32(f.Player.BaseAddress + 6384, 149); Unknown(f.Capture()); });
        Case("unreadable inventory array remains unknown", f => { f.Write32(f.Player.BaseAddress + 6388, 1); Unknown(f.Capture()); });
        Case("equipped container with unknown contents remains unknown", f => { var bag = f.Bag(19, 700, 2); f.Write32(bag.Address + 1892, 1); Unknown(f.Capture()); });
        Case("unhydrated zero-slot equipped container remains unknown", f => { var bag = f.Bag(19, 700, 2); f.Write32(bag.Address + 1888, 0); Unknown(f.Capture()); });
        Case("oversized bag cannot trigger unbounded reads", f => { var bag = f.Bag(19, 700, 2); f.Write32(bag.Address + 1888, uint.MaxValue); Unknown(f.Capture()); });
        Case("bag record identity must match its item GUID", f => { var bag = f.Bag(19, 700, 2); f.Write64(bag.Address + 1896, 999); Unknown(f.Capture()); });
        Case("active trade slot presence cannot be interpreted as absent stock", f => { f.Items((301, 2)); f.TradeOwners(777, 777); f.Write64(f.Trade, 99000001); Unknown(f.Capture()); });
        Case("active seventh trade slot also withholds quantity authority", f => { f.TradeOwners(777, 777); f.Write64(f.Trade + 48, 777); Unknown(f.Capture()); });
        Case("old trade slot GUIDs do not block a confirmed closed trade", f => { f.Items((301, 2)); f.Write64(f.Trade, 99000001); Check(Count(f.Capture(), 301) == 2, "closed trade's stale slot blocked current stock"); });
        Case("empty active trade also withholds quantity authority", f => { f.TradeOwners(777, 777); Unknown(f.Capture()); });
        Case("closed dialog still awaits the server cancellation state", f => { f.TradeOwners(0, 777); Unknown(f.Capture()); });
        Case("server cancellation does not borrow a still-open dialog", f => { f.TradeOwners(777, 0); Unknown(f.Capture()); });
        Case("unreadable trade slots do not imply no trade", f => { f.TradeReadable = false; Unknown(f.Capture()); });
        Case("short byte read is rejected", f => { f.ShortRead = true; Unknown(f.Capture()); });
        Case("ordinary memory exception becomes unknown", f => { f.ReadError = new IOException("controlled incomplete read"); Unknown(f.Capture()); });
        Case("cancellation propagates", f => { var expected = new OperationCanceledException("stop"); f.ReadError = expected; ThrowsSame(() => f.Capture(), expected); });
        Case("thread interruption propagates", f => { var expected = new ThreadInterruptedException("stop"); f.ReadError = expected; ThrowsSame(() => f.Capture(), expected); });
        Case("changed stack before final readback is not published", f =>
        {
            var item = f.Item(23, 301, 2); f.AfterRead = address => { if (address == item.Fields) { f.AfterRead = null; f.Write32(item.Fields + 56, 3); } }; Unknown(f.Capture());
        });
        Case("replacement player wrapper cannot borrow the old sample", f => { f.AfterRead = _ => { f.AfterRead = null; ObjectManager.Me = new LocalPlayer(f.Player.BaseAddress); }; Unknown(f.Capture()); });
        Case("a reader from another memory owner cannot be relabelled", f => { f.ReplaceMemoryWrapper(); Unknown(f.Capture()); });
        Case("capture restores the caller's cache setting", f => { bool before = f.CacheEnabled.Value; Known(f.Capture()); Check(f.CacheEnabled.Value == before, "caller cache state leaked"); });
        Case("unknown capture also restores the caller's cache setting", f => { bool before = f.CacheEnabled.Value; f.ShortRead = true; Unknown(f.Capture()); Check(f.CacheEnabled.Value == before, "failure leaked cache setting"); });
        Case("unchanged snapshot can be revalidated", f => { f.Items((301, 2)); var snapshot = f.Capture(); Known(snapshot); Check(Current(snapshot), "current sample rejected"); });
        Case("stack change revokes the recorded sample", f => { var item = f.Item(23, 301, 2); var snapshot = f.Capture(); Known(snapshot); f.Write32(item.Fields + 56, 3); Check(!Current(snapshot), "changed stack stayed current"); });
        Case("lost loaded object revokes the recorded sample", f => { var item = f.Item(23, 301, 2); var snapshot = f.Capture(); Known(snapshot); f.Objects.Remove(item.Guid); Check(!Current(snapshot), "lost item stayed current"); });
        Case("trade change revokes a previously complete sample", f => { var snapshot = f.Capture(); Known(snapshot); f.TradeOwners(777, 777); f.Write64(f.Trade, 777); Check(!Current(snapshot), "new trade stayed current"); });
        Case("stale closed-trade slots are not current inventory state", f => { var snapshot = f.Capture(); Known(snapshot); f.Write64(f.Trade, 777); Check(Current(snapshot), "stale dialog slot revoked an unchanged closed-trade observation"); });
        Case("later complete read recovers without reusing unknown counts", f => { f.TradeReadable = false; Unknown(f.Capture()); f.TradeReadable = true; Known(f.Capture()); });
        int passed = 0, failures = 0, errors = 0;
        foreach (var item in cases)
        {
            try { item.Body(); passed++; Console.WriteLine("PASS inventory snapshot: " + item.Name); }
            catch (Failure error) { failures++; Console.Error.WriteLine("FAIL inventory snapshot: " + item.Name + ": " + error.Message); }
            catch (Exception error) { errors++; Console.Error.WriteLine("ERROR inventory snapshot: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Inventory snapshot scenarios: {passed}/{cases.Count}; assertions={failures}; unexpected={errors}; allocated item/bag memory, fixed-global transport fixture, no game.");
        if (failures + errors != 0) throw new InvalidOperationException("Inventory snapshot regression");
    }

    private static object Invoke(MethodInfo method, object? target, params object?[] args)
    {
        try { return method.Invoke(target, args)!; }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static IReadOnlyDictionary<int, long>? Counts(object snapshot) => (IReadOnlyDictionary<int, long>?)snapshot.GetType().GetProperty("ItemCounts")!.GetValue(snapshot);
    private static void Known(object snapshot) => Check((bool)snapshot.GetType().GetProperty("IsComplete")!.GetValue(snapshot)! && Counts(snapshot) != null, "complete inventory not established: " + snapshot.GetType().GetProperty("Status")!.GetValue(snapshot));
    private static void Unknown(object snapshot) => Check(!(bool)snapshot.GetType().GetProperty("IsComplete")!.GetValue(snapshot)! && Counts(snapshot) == null, "unknown observation published usable counts");
    private static long Count(object snapshot, int item) { Known(snapshot); return Counts(snapshot)!.TryGetValue(item, out long value) ? value : 0; }
    private static bool Current(object snapshot) => (bool)Invoke(snapshot.GetType().GetMethod("IsCurrent")!, snapshot);
    private static void ThrowsSame(Action action, Exception expected) { try { action(); throw new Failure("cancellation swallowed"); } catch (Exception actual) when (ReferenceEquals(actual, expected)) { } }
    private static void Check(bool ok, string message) { if (!ok) throw new Failure(message); }

    internal sealed class Fixture : IDisposable
    {
        private readonly QuestDatasetObservationFixture original = new();
        private readonly List<IntPtr> allocations = new();
        private readonly Memory memory = ObjectManager.Wow!;
        internal LocalPlayer Player => original.Player;
        internal QuestDatasetObservationFixture NativeFixture => original;
        internal readonly Dictionary<ulong, WoWObject> Objects;
        internal readonly ThreadLocal<bool> CacheEnabled;
        internal readonly uint Main, Trade;
        internal bool TradeReadable = true, ShortRead;
        internal Exception? ReadError;
        internal Action<uint>? AfterRead;
        private ulong nextGuid = 99110000;
        internal Fixture()
        {
            Objects = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", Hidden)!.GetValue(null)!;
            CacheEnabled = (ThreadLocal<bool>)typeof(Memory).GetField("_cacheEnabled", Hidden)!.GetValue(memory)!;
            Main = (uint)typeof(QuestDatasetObservationFixture).GetField("inventory", Hidden)!.GetValue(original)!;
            Trade = Allocate(128);
        }
        internal object Capture()
        {
            var type = typeof(LocalPlayer).Assembly.GetType("Styx.Logic.Questing.QuestInventorySnapshot");
            var method = type?.GetMethod("CaptureCore", Hidden);
            Check(method != null, "complete owned inventory observation API missing");
            Func<uint, int, byte[]> read = (address, count) =>
            {
                if (ReadError != null) throw ReadError;
                bool trade = address == TradeItems || address == 0xBFA658 || address == 0xCA0FE8;
                if (trade && !TradeReadable) return null!;
                uint mapped = address == TradeItems ? Trade : address == 0xBFA658 ? Trade + 56 : address == 0xCA0FE8 ? Trade + 64 : address;
                byte[] bytes = memory.ReadBytes(mapped, count);
                AfterRead?.Invoke(address);
                return ShortRead && bytes?.Length > 0 ? bytes.Take(bytes.Length - 1).ToArray() : bytes!;
            };
            return method!.GetParameters().Length == 3 ? Invoke(method, null, Player, memory, read) : Invoke(method, null, Player, read);
        }
        internal void ReplaceMemoryWrapper()
        {
            var clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(memory, null);
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, clone);
        }
        internal void Items(params (int Id, uint Count)[] entries)
        {
            for (int i = 0; i < entries.Length; i++) Item(23 + i, entries[i].Id, entries[i].Count);
        }
        internal (uint Address, uint Fields, ulong Guid) Item(int slot, int entry, uint count)
        {
            var item = AllocateItem(entry, count, Player.Guid, false); MainSlot(slot, item.Guid); return item;
        }
        internal (uint Address, uint Slots, ulong Guid) Bag(int slot, int entry, uint count)
        {
            var bag = AllocateItem(entry, 1, Player.Guid, true);
            uint contents = bag.Address + 2304;
            Write32(bag.Address + 1888, count); Write32(bag.Address + 1892, contents);
            Write64(bag.Address + 1896, bag.Guid); MainSlot(slot, bag.Guid);
            return (bag.Address, contents, bag.Guid);
        }
        internal void InBag((uint Address, uint Slots, ulong Guid) bag, int slot, int entry, uint count)
        {
            var item = AllocateItem(entry, count, bag.Guid, false); Write64(bag.Slots + (uint)slot * 8, item.Guid);
        }
        private (uint Address, uint Fields, ulong Guid) AllocateItem(int entry, uint count, ulong container, bool bag)
        {
            uint address = Allocate(bag ? 4096 : 512), fields = address + 256; ulong guid = nextGuid++;
            Write32(address + 8, fields); Write32(address + 20, bag ? 2U : 1U); Write64(address + 48, guid);
            Write64(fields, guid); Write32(fields + 8, bag ? 7U : 3U); Write32(fields + 12, (uint)entry);
            Write64(fields + 24, Player.Guid); Write64(fields + 32, container); Write32(fields + 56, count);
            Objects[guid] = bag ? new WoWContainer(address) : new WoWItem(address);
            return (address, fields, guid);
        }
        private uint Allocate(int bytes)
        {
            IntPtr address = Marshal.AllocHGlobal(bytes); allocations.Add(address); Marshal.Copy(new byte[bytes], 0, address, bytes);
            return unchecked((uint)address.ToInt32());
        }
        internal void MainSlot(int slot, ulong guid) => Write64(Main + (uint)slot * 8, guid);
        internal void TradeOwners(ulong dialog, ulong session) { Write64(Trade + 56, dialog); Write64(Trade + 64, session); }
        internal void Write32(uint address, uint value) { Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value)); Invalidate(address); }
        internal void Write64(uint address, ulong value) { Marshal.WriteInt64(new IntPtr(unchecked((int)address)), unchecked((long)value)); Invalidate(address); }
        private void Invalidate(uint address)
        {
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(Memory).GetField("_cache", Hidden)!.GetValue(memory)!;
            foreach (var pair in cache.Value!.Where(pair => address >= unchecked((uint)pair.Key.ToInt32()) && address < unchecked((uint)pair.Key.ToInt32()) + pair.Value.Length).ToArray()) cache.Value.Remove(pair.Key);
        }
        public void Dispose() { original.Dispose(); foreach (IntPtr address in allocations) Marshal.FreeHGlobal(address); }
    }
}
