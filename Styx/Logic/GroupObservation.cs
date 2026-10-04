using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
    /// <summary>Complete original-client group membership, including the local player.</summary>
    public static class GroupObservation
    {
        // Original build-12340 APIs. A count and every corresponding token are
        // returned by one Lua execution; a missing token never becomes "solo".
        private const string Query =
            "local r=GetNumRaidMembers();local p=GetNumPartyMembers();" +
            "if type(r)~='number' or type(p)~='number' or r<0 or r>40 or p<0 or p>4 " +
            "or r%1~=0 or p%1~=0 then return end;" +
            "local v={'group-v1',tostring(r),tostring(p),UnitGUID('player') or ''};" +
            "for i=1,(r>0 and r or p) do v[#v+1]=UnitGUID((r>0 and 'raid' or 'party')..i) or '' end;" +
            "return unpack(v)";

        private sealed class Snapshot
        {
            internal object Memory, Executor;
            internal LocalPlayer Player;
            internal ulong Owner;
            internal bool Raid, Party;
            internal uint Frame;
            internal IReadOnlyList<WoWPlayer> Members;
            internal ulong[] Guids;
            internal uint[] Bases;
            internal bool CurrentMembers()
            {
                if (Members.Count != Guids.Length || Bases == null || Bases.Length != Guids.Length)
                    return false;
                for (int index = 0; index < Members.Count; index++)
                {
                    var member = Members[index];
                    ulong guid = Guids[index];
                    uint address = Bases[index];
                    if (member == null || guid == 0 || address == 0 || !member.IsValid
                        || member.Guid != guid || member.BaseAddress != address)
                        return false;
                    var current = guid == Owner ? Player : ObjectManager.GetObjectByGuid<WoWPlayer>(guid);
                    if (!ReferenceEquals(current, member) || current == null || !current.IsValid
                        || current.Guid != guid || current.BaseAddress != address)
                        return false;
                }
                return true;
            }
        }
        private static Snapshot _snapshot;

        public static bool TryGetMembers(LocalPlayer player, out IReadOnlyList<WoWPlayer> members, out string reason, bool allowQuery = true)
        {
            members = Array.Empty<WoWPlayer>();
            reason = "unavailable-owner";
            if (player == null || !ReferenceEquals(player, StyxWoW.Me) || !player.IsValid || player.Guid == 0)
                return false;
            var memory = ObjectManager.Wow;
            var executor = ObjectManager.Executor;
            ulong owner = player.Guid;
            uint ownerBase = player.BaseAddress;
            if (ownerBase == 0) { reason = "player-base-unavailable"; return false; }
            bool raid = player.IsInRaid, party = player.IsInParty;
            bool CurrentOwner() => ReferenceEquals(player, StyxWoW.Me) && player.IsValid && player.Guid == owner
                && player.BaseAddress == ownerBase
                && ReferenceEquals(memory, ObjectManager.Wow) && ReferenceEquals(executor, ObjectManager.Executor)
                && player.IsInRaid == raid && player.IsInParty == party;
            try
            {
                if (memory == null || executor == null || !ReferenceEquals(executor.Memory, memory)) return false;
                uint currentFrame = executor.FrameCount;
                var cached = _snapshot;
                if (currentFrame != 0 && cached != null && cached.Frame == currentFrame
                    && ReferenceEquals(cached.Memory, memory) && ReferenceEquals(cached.Executor, executor)
                    && ReferenceEquals(cached.Player, player) && cached.Owner == owner && cached.Raid == raid && cached.Party == party)
                {
                    if (!cached.CurrentMembers() || !CurrentOwner() || executor.FrameCount != currentFrame)
                    { reason = "cached-owner-changed"; return false; }
                    members = cached.Members; reason = "complete"; return true;
                }
                if (!allowQuery) { reason = "cached-roster-unavailable"; return false; }
                var values = Lua.GetObservedReturnValues(Query);
                if (!CurrentOwner()) { reason = "owner-replaced"; return false; }
                if (values == null || values.Count < 4 || values[0] != "group-v1"
                    || !int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out int raidCount)
                    || !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out int partyCount)
                    || raidCount < 0 || raidCount > 40 || partyCount < 0 || partyCount > 4)
                { reason = "invalid-counts"; return false; }
                bool isRaid = raidCount > 0;
                int count = isRaid ? raidCount : partyCount;
                if (values.Count != count + 4 || raid != isRaid || (!isRaid && party != (partyCount > 0)))
                { reason = "incomplete-or-changing-roster"; return false; }
                bool GuidValue(string value, out ulong guid) => value != null && value.StartsWith("0x", StringComparison.Ordinal)
                    ? ulong.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out guid) && guid != 0
                    : SetUnknown(out guid);
                if (!GuidValue(values[3], out ulong observedOwner) || observedOwner != owner)
                { reason = "player-guid-mismatch"; return false; }
                // Lua dispatch can advance a client frame. The token snapshot owns
                // the resulting frame, not the frame before its execution.
                uint frame = executor.FrameCount;
                var result = new List<WoWPlayer>();
                var guids = new HashSet<ulong>();
                var expectedGuids = new List<ulong>();
                var expectedBases = new List<uint>();
                if (!isRaid) { result.Add(player); guids.Add(owner); expectedGuids.Add(owner); expectedBases.Add(ownerBase); }
                foreach (string token in values.Skip(4))
                {
                    if (!GuidValue(token, out ulong guid) || !guids.Add(guid))
                    { reason = "missing-or-duplicate-guid"; return false; }
                    var member = guid == owner ? player : ObjectManager.GetObjectByGuid<WoWPlayer>(guid);
                    if (member == null || !member.IsValid || member.Guid != guid)
                    { reason = "unresolved-member"; return false; }
                    uint memberBase = member.BaseAddress;
                    if (memberBase == 0)
                    { reason = "member-base-unavailable"; return false; }
                    result.Add(member); expectedGuids.Add(guid); expectedBases.Add(memberBase);
                }
                bool currentMembers = true;
                for (int index = 0; index < result.Count; index++)
                {
                    var member = result[index];
                    ulong expectedGuid = expectedGuids[index];
                    uint expectedBase = expectedBases[index];
                    var current = expectedGuid == owner ? player : ObjectManager.GetObjectByGuid<WoWPlayer>(expectedGuid);
                    if (!member.IsValid || member.Guid != expectedGuid || member.BaseAddress != expectedBase
                        || !ReferenceEquals(current, member))
                    {
                        currentMembers = false;
                        break;
                    }
                }
                if (!guids.Contains(owner) || !currentMembers || !CurrentOwner() || executor.FrameCount != frame)
                { reason = "roster-owner-or-frame-changed"; return false; }
                members = result.AsReadOnly();
                if (frame != 0) _snapshot = new Snapshot { Memory=memory, Executor=executor, Player=player, Owner=owner,
                    Raid=raid, Party=party, Frame=frame, Members=members, Guids=expectedGuids.ToArray(), Bases=expectedBases.ToArray() };
                reason = "complete";
                return true;
            }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                reason = "roster-observation-unavailable";
                return false;
            }
        }

        /// <summary>
        /// Complete raw build12340 membership for prepared-action admission. This
        /// reads no Lua, resolves no names, and includes unloaded group members.
        /// Missing bytes are UNKNOWN, never proof that a player is outside the group.
        /// </summary>
        public static bool TryReadMemberGuids(LocalPlayer player, out ulong[] members)
        {
            members = Array.Empty<ulong>();
            var memory = ObjectManager.Wow;
            if (player == null || memory == null || !ReferenceEquals(player, StyxWoW.Me)) return false;
            try
            {
                using var uncached = memory.TemporaryCacheState(false);
                ulong owner = player.Guid;
                uint address = player.BaseAddress, map = player.MapId;
                bool Current() => owner != 0 && address != 0 && ReferenceEquals(memory, ObjectManager.Wow)
                    && ReferenceEquals(player, StyxWoW.Me) && player.IsValid && player.Guid == owner
                    && player.DescriptorGuid == owner && player.BaseAddress == address && player.MapId == map;
                if (!Current()) return false;
                // Same original addresses used by LocalPlayer.GetPartyMemberGuid,
                // GetRaidMemberGuid and NumRaidMembers; bulk reads require every byte.
                byte[] counts = memory.ReadBytes(12498440, 4);
                if (counts == null || counts.Length != 4) return false;
                int raidCount = BitConverter.ToInt32(counts, 0);
                if (raidCount < 0 || raidCount > 40) return false;
                uint vectorAddress = raidCount == 0 ? 12392776u : 12498280u;
                int vectorSize = raidCount == 0 ? 32 : 160;
                byte[] vector = memory.ReadBytes(vectorAddress, vectorSize);
                if (vector == null || vector.Length != vectorSize) return false;
                var guids = new HashSet<ulong>();
                if (raidCount == 0) guids.Add(owner);
                for (int index = 0; index < (raidCount == 0 ? 4 : 40); index++)
                {
                    ulong guid;
                    if (raidCount == 0) guid = BitConverter.ToUInt64(vector, index * 8);
                    else
                    {
                        uint pointer = BitConverter.ToUInt32(vector, index * 4);
                        if (pointer == 0) continue;
                        if (pointer > uint.MaxValue - 7) return false;
                        byte[] value = memory.ReadBytes(pointer, 8);
                        if (value == null || value.Length != 8) return false;
                        guid = BitConverter.ToUInt64(value, 0);
                        if (guid == 0) return false;
                    }
                    if (guid != 0 && !guids.Add(guid)) return false;
                }
                if (!guids.Contains(owner) || raidCount > 0 && guids.Count != raidCount
                    || !Current() || !counts.SequenceEqual(memory.ReadBytes(12498440, 4))
                    || !vector.SequenceEqual(memory.ReadBytes(vectorAddress, vectorSize))) return false;
                members = guids.OrderBy(guid => guid).ToArray();
                return Current();
            }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                return false;
            }
        }

        private static bool SetUnknown(out ulong value) { value = 0; return false; }
    }
}
