#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Questing;

/// <summary>
/// Positive self-spell observations for the current original build12340 player.
/// The client list is filtered by TC335 SendInitialSpells; absent client entries
/// never prove Player::HasSpell is false. Recheck by capturing again before use.
/// A matching owner sample is not a transaction or proof against intervening ABA.
/// </summary>
public sealed class QuestSpellKnowledgeSnapshot
{
    private const int MaximumRequestedSpells = 64;
    private const int MaximumReceiptLength = 2048;
    private QuestSpellKnowledgeSnapshot(string status, ulong playerGuid = 0, IEnumerable<uint>? confirmed = null)
    {
        Status = status; PlayerGuid = playerGuid; ObservedUtc = DateTime.UtcNow;
        ConfirmedSpellIds = confirmed == null ? null : Array.AsReadOnly(confirmed.OrderBy(id => id).ToArray());
    }

    public string Status { get; }
    public ulong PlayerGuid { get; }
    public DateTime ObservedUtc { get; }
    public IReadOnlyCollection<uint>? ConfirmedSpellIds { get; }
    /// <summary>Null denotes unconfirmed knowledge, including a false API result.</summary>
    public bool? Confirms(uint spellId) => ConfirmedSpellIds?.Contains(spellId) == true ? true : null;

    public static QuestSpellKnowledgeSnapshot Capture(LocalPlayer? player, int[] requested)
    {
        Memory? memory = ObjectManager.Wow;
        return memory == null ? new("spell-memory-unavailable") : CaptureCore(player, memory, requested,
            script => Lua.GetReturnValues(script, "CopilotBuddy.PositiveSpellKnowledge.lua"));
    }

    // Tests execute the exact generated request in stock Lua5.1, with controlled
    // API observations. The public entry uses only the established Lua transport.
    private static QuestSpellKnowledgeSnapshot CaptureCore(LocalPlayer? player, Memory memory,
        int[] requested, Func<string, List<string>> execute)
    {
        try
        {
            if (requested == null || requested.Length == 0 || requested.Length > MaximumRequestedSpells ||
                requested.Any(id => id <= 0) || execute == null)
                return new("spell-request-unavailable-or-out-of-bounds");
            int[] ids = requested.Distinct().OrderBy(id => id).ToArray();
            if (!CurrentOwner(player, memory)) return new("spell-player-unavailable");
            uint address = player!.BaseAddress;
            (uint Descriptor, ulong Guid) before;
            using (memory.TemporaryCacheState(false)) before = ReadOwner(memory, address);
            string owner = "0x" + before.Guid.ToString("X16", CultureInfo.InvariantCulture);
            string prefix = "KS1|" + owner + "|";
            string script = "if type(UnitGUID)~='function' or type(IsSpellKnown)~='function' then return end " +
                "local owner='" + owner + "'; if UnitGUID('player')~=owner then return end " +
                "local requested={" + string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))) + "}; local known={} " +
                "for _,id in ipairs(requested) do if IsSpellKnown(id,false)==true then known[#known+1]=tostring(id) end end " +
                "if UnitGUID('player')~=owner then return end return '" + prefix + "'..table.concat(known,',')";
            List<string>? values = execute(script);
            if (!CurrentOwner(player, memory) || player.BaseAddress != address)
                return new("spell-owner-changed");
            (uint Descriptor, ulong Guid) after;
            using (memory.TemporaryCacheState(false)) after = ReadOwner(memory, address);
            if (before != after || !CurrentOwner(player, memory)) return new("spell-raw-owner-changed");
            if (values == null || values.Count != 1 || values[0] == null || values[0].Length > MaximumReceiptLength ||
                !values[0].StartsWith(prefix, StringComparison.Ordinal)) return new("spell-receipt-unavailable");
            string body = values[0].Substring(prefix.Length);
            var confirmed = new HashSet<uint>();
            if (body.Length != 0)
            {
                foreach (string value in body.Split(','))
                {
                    if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) ||
                        id == 0 || id > int.MaxValue || value != id.ToString(CultureInfo.InvariantCulture) ||
                        Array.BinarySearch(ids, (int)id) < 0 || !confirmed.Add(id))
                        return new("spell-receipt-invalid");
                }
            }
            return new(confirmed.Count == 0 ? "no-positive-spell-confirmation" : "positive-owned-self-spell-sample",
                before.Guid, confirmed);
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        {
            return new("spell-observation-unavailable:" + error.GetType().Name);
        }
    }

    private static bool CurrentOwner(LocalPlayer? player, Memory memory) => player != null &&
        ReferenceEquals(player, ObjectManager.Me) && ReferenceEquals(memory, ObjectManager.Wow) &&
        ObjectManager.IsInGame && player.IsValid && player.BaseAddress != 0;

    private static (uint Descriptor, ulong Guid) ReadOwner(Memory memory, uint address)
    {
        byte[] Bytes(uint offset, int count)
        {
            if (offset == 0) throw new InvalidDataException("Spell owner address unavailable");
            _ = checked(offset + (uint)count - 1);
            byte[]? bytes = memory.ReadBytes(offset, count);
            if (bytes == null || bytes.Length != count) throw new IOException("Spell owner read incomplete");
            return bytes;
        }
        uint descriptor = BitConverter.ToUInt32(Bytes(checked(address + 8), 4), 0);
        ulong guid = BitConverter.ToUInt64(Bytes(checked(address + 48), 8), 0);
        if (guid == 0 || BitConverter.ToUInt64(Bytes(descriptor, 8), 0) != guid ||
            BitConverter.ToUInt32(Bytes(checked(address + 20), 4), 0) != 4 ||
            BitConverter.ToUInt32(Bytes(checked(address + 8), 4), 0) != descriptor ||
            BitConverter.ToUInt64(Bytes(checked(address + 48), 8), 0) != guid)
            throw new InvalidDataException("Spell owner identities changed or disagree");
        return (descriptor, guid);
    }
}
