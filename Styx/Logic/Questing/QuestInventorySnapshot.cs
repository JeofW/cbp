#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Questing;

/// <summary>
/// A complete observed carried inventory for one original build12340 player.
/// Unknown is represented by null counts, never an empty inventory. Matching
/// reads detect observed changes; they are not an atomic client transaction,
/// a server acceptance receipt, a bank observation or protection against ABA.
/// </summary>
public sealed class QuestInventorySnapshot
{
    private const uint InventoryOffset = 6384;
    private const uint ContainerOffset = 1888;
    // Build12340 CloseTrade/0x5870A0 closes the dialog without clearing its
    // seven item GUID slots. The server-status handler 0x7044A0 separately
    // clears the session partner after cancellation/completion processing.
    // Require both states to be closed; stale item slots are not active trade.
    private const uint TradeDialogPartner = 0xBFA658;
    private const uint TradeSessionPartner = 0xCA0FE8;
    private const int InventorySlots = 150;
    private const int MaximumContainerSlots = 36;
    private readonly Observation? _observation;

    private QuestInventorySnapshot(string status, Observation? observation = null, Dictionary<int, long>? counts = null)
    {
        Status = status;
        _observation = observation;
        ItemCounts = counts == null ? null : new ReadOnlyDictionary<int, long>(counts);
        PlayerGuid = observation?.PlayerGuid ?? 0;
        ObservedUtc = DateTime.UtcNow;
    }

    public bool IsComplete => ItemCounts != null;
    public string Status { get; }
    public ulong PlayerGuid { get; }
    public DateTime ObservedUtc { get; }
    public IReadOnlyDictionary<int, long>? ItemCounts { get; }

    public static QuestInventorySnapshot Capture(LocalPlayer? player)
    {
        Memory? memory = ObjectManager.Wow;
        return memory == null ? new("memory-unavailable") : CaptureCore(player, memory, memory.ReadBytes);
    }

    // The public entry always uses the actual memory reader. Tests map only
    // fixed client globals into their allocated process through this boundary.
    private static QuestInventorySnapshot CaptureCore(LocalPlayer? player, Memory memory, Func<uint, int, byte[]> read)
    {
        try
        {
            if (player == null || memory == null || !ReferenceEquals(memory, ObjectManager.Wow) || !ReferenceEquals(player, ObjectManager.Me) ||
                !ObjectManager.IsInGame || !player.IsValid || player.BaseAddress == 0)
                return new("player-unavailable");
            var observation = new Observation(player, memory, read);
            Dictionary<int, long> counts;
            using (memory.TemporaryCacheState(false))
            {
                observation.ReadPlayerOwner();
                if (observation.Bytes(TradeDialogPartner, sizeof(ulong)).Any(value => value != 0) ||
                    observation.Bytes(TradeSessionPartner, sizeof(ulong)).Any(value => value != 0))
                    return new("trade-session-or-dialog-active");
                counts = observation.ReadInventory();
                if (!observation.ReadbackMatches()) return new("inventory-changed-during-capture");
            }
            return observation.OwnersCurrent() ? new("complete-owned-carried-sample", observation, counts)
                : new("inventory-owner-changed");
        }
        catch (Exception error) when (OrdinaryFailure(error))
        {
            return new("inventory-unavailable:" + error.GetType().Name);
        }
    }

    /// <summary>Re-observes the exact source bytes and loaded object owners.</summary>
    public bool IsCurrent()
    {
        if (!IsComplete || _observation == null) return false;
        try
        {
            if (!_observation.OwnersCurrent()) return false;
            bool matches;
            using (_observation.Memory.TemporaryCacheState(false)) matches = _observation.ReadbackMatches();
            return matches && _observation.OwnersCurrent();
        }
        catch (Exception error) when (OrdinaryFailure(error)) { return false; }
    }

    private static bool OrdinaryFailure(Exception error) => error is not OperationCanceledException && error is not ThreadInterruptedException;

    private sealed class Observation(LocalPlayer player, Memory memory, Func<uint, int, byte[]> read)
    {
        private readonly uint _base = player.BaseAddress;
        private readonly Dictionary<(uint Address, int Count), byte[]> _samples = new();
        private readonly Dictionary<ulong, WoWItem> _items = new();
        private readonly HashSet<ulong> _seen = new();
        internal Memory Memory => memory;
        internal ulong PlayerGuid { get; private set; }

        internal byte[] Bytes(uint address, int count)
        {
            if (address == 0 || count <= 0 || count > 4096) throw new InvalidDataException("Inventory observation range is invalid");
            _ = checked(address + (uint)count - 1);
            byte[]? bytes = read(address, count);
            if (bytes == null || bytes.Length != count) throw new IOException("Inventory observation was not completely read");
            var key = (address, count);
            if (_samples.TryGetValue(key, out byte[]? previous) && !previous.SequenceEqual(bytes))
                throw new InvalidDataException("Inventory source changed during observation");
            _samples[key] = (byte[])bytes.Clone();
            return bytes;
        }

        private uint UInt32(uint address) => BitConverter.ToUInt32(Bytes(address, sizeof(uint)), 0);
        private ulong UInt64(uint address) => BitConverter.ToUInt64(Bytes(address, sizeof(ulong)), 0);

        internal void ReadPlayerOwner()
        {
            uint descriptor = UInt32(checked(_base + 8));
            PlayerGuid = UInt64(checked(_base + 48));
            if (PlayerGuid == 0 || UInt64(descriptor) != PlayerGuid || UInt32(checked(_base + 20)) != 4)
                throw new InvalidDataException("Inventory player identities disagree");
        }

        internal Dictionary<int, long> ReadInventory()
        {
            var counts = new Dictionary<int, long>();
            uint slots = BagRecord(checked(_base + InventoryOffset), PlayerGuid, inventory: true, out int count);
            byte[] first = Bytes(slots, 39 * sizeof(ulong));
            byte[] carried = Bytes(checked(slots + 86U * sizeof(ulong)), 64 * sizeof(ulong));
            for (int index = 0; index < 39; index++)
            {
                ulong guid = BitConverter.ToUInt64(first, index * sizeof(ulong));
                if (guid == 0) continue;
                WoWItem item = Item(guid, PlayerGuid, counts);
                if (index is >= 19 and <= 22)
                {
                    if (item is not WoWContainer) throw new InvalidDataException("Equipped bag has no loaded container owner");
                    uint bagSlots = BagRecord(checked(item.BaseAddress + ContainerOffset), guid, inventory: false, out int bagCount);
                    byte[] contents = Bytes(bagSlots, checked(bagCount * sizeof(ulong)));
                    for (int position = 0; position < bagCount; position++)
                    {
                        ulong nested = BitConverter.ToUInt64(contents, position * sizeof(ulong));
                        if (nested != 0) Item(nested, guid, counts);
                    }
                }
            }
            for (int index = 0; index < 64; index++)
            {
                ulong guid = BitConverter.ToUInt64(carried, index * sizeof(ulong));
                if (guid != 0) Item(guid, PlayerGuid, counts);
            }
            return counts;
        }

        private uint BagRecord(uint address, ulong owner, bool inventory, out int count)
        {
            byte[] header = Bytes(address, 17);
            uint size = BitConverter.ToUInt32(header, 0), pointer = BitConverter.ToUInt32(header, 4);
            ulong guid = BitConverter.ToUInt64(header, 8);
            if (guid != owner || header[16] != (inventory ? 1 : 0) || pointer == 0 ||
                (inventory ? size != InventorySlots : size == 0 || size > MaximumContainerSlots))
                throw new InvalidDataException("Inventory or bag header is incomplete or belongs to another owner");
            count = checked((int)size);
            _ = checked(pointer + size * sizeof(ulong) - 1);
            return pointer;
        }

        private WoWItem Item(ulong guid, ulong container, Dictionary<int, long> counts)
        {
            if (!_seen.Add(guid) || _seen.Count > 39 + 64 + 4 * MaximumContainerSlots)
                throw new InvalidDataException("Inventory GUID is duplicated or oversized");
            WoWItem? item = ObjectManager.GetObjectByGuid<WoWItem>(guid);
            if (item == null || item.BaseAddress == 0) throw new InvalidDataException("Occupied inventory slot lacks a loaded item");
            uint address = item.BaseAddress;
            uint kind = UInt32(checked(address + 20)), fields = UInt32(checked(address + 8));
            if (kind is not (1 or 2) || (kind == 2) != (item is WoWContainer) || UInt64(checked(address + 48)) != guid)
                throw new InvalidDataException("Loaded inventory object identity disagrees");
            byte[] descriptor = Bytes(fields, 64);
            uint entry = BitConverter.ToUInt32(descriptor, 12), stack = BitConverter.ToUInt32(descriptor, 56);
            if (BitConverter.ToUInt64(descriptor, 0) != guid || BitConverter.ToUInt64(descriptor, 24) != PlayerGuid ||
                BitConverter.ToUInt64(descriptor, 32) != container || entry == 0 || entry > int.MaxValue || stack == 0 ||
                (UInt32(checked(address + 188)) & 0x10000) != 0)
                throw new InvalidDataException("Inventory item owner, container or quantity is unconfirmed");
            int id = (int)entry;
            counts[id] = checked((counts.TryGetValue(id, out long previous) ? previous : 0) + stack);
            _items.Add(guid, item);
            return item;
        }

        internal bool ReadbackMatches() => _samples.All(sample =>
        {
            byte[]? current = read(sample.Key.Address, sample.Key.Count);
            return current != null && sample.Value.SequenceEqual(current);
        }) && _items.All(item => ReferenceEquals(ObjectManager.GetObjectByGuid<WoWItem>(item.Key), item.Value));

        internal bool OwnersCurrent() => ReferenceEquals(ObjectManager.Wow, memory) && ReferenceEquals(ObjectManager.Me, player)
            && ObjectManager.IsInGame && player.IsValid && player.BaseAddress == _base && player.Guid == PlayerGuid;
    }
}
