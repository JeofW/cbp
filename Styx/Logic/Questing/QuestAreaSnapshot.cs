#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Questing;

/// <summary>
/// A checked observation of the current original-build12340 AreaTable identity.
/// Unknown is null, never the parent zone or an authoritative negative result.
/// Matching samples do not imply a server transaction, phase equivalence or ABA protection.
/// </summary>
public sealed class QuestAreaSnapshot
{
    // Existing LocalPlayer.SubZoneId/MapId layout, independently checked against
    // build12340: GameUI update0x5204C0, area lookup0x5167E0, teardown0x529160.
    private const uint CurrentArea = 0xBD0810;
    private const uint CurrentMap = 0xBD088C;
    private readonly Observation? _observation;

    private QuestAreaSnapshot(string status, Observation? observation = null)
    {
        Status=status; _observation=observation;
        AreaId=observation?.AreaId; MapId=observation?.MapId;
        PlayerGuid=observation?.Guid ?? 0; ObservedUtc=DateTime.UtcNow;
    }

    public string Status { get; }
    public int? AreaId { get; }
    public int? MapId { get; }
    public ulong PlayerGuid { get; }
    public DateTime ObservedUtc { get; }
    public bool IsComplete => AreaId.HasValue && MapId.HasValue;

    public static QuestAreaSnapshot Capture(LocalPlayer? player)
    {
        Memory? memory=ObjectManager.Wow;
        return memory==null ? new("area-memory-unavailable") : CaptureCore(player,memory,memory.ReadBytes);
    }

    // Only the fixed global memory transport is substituted by the allocated
    // regression fixture. The public path always supplies the actual Memory reader.
    private static QuestAreaSnapshot CaptureCore(LocalPlayer? player,Memory memory,Func<uint,int,byte[]> read)
    {
        try
        {
            if(player==null || !ReferenceEquals(player,ObjectManager.Me) || !ReferenceEquals(memory,ObjectManager.Wow) ||
               !ObjectManager.IsInGame || !player.IsValid || player.BaseAddress==0)
                return new("area-player-unavailable");
            var sample=new Observation(player,memory,read);
            using(memory.TemporaryCacheState(false))
            {
                sample.Capture();
                if(!sample.ReadbackMatches())return new("area-changed-during-capture");
            }
            return sample.OwnersCurrent() ? new("complete-owned-area-sample",sample) : new("area-owner-changed");
        }
        catch(Exception error)when(OrdinaryFailure(error)) { return new("area-unavailable:"+error.GetType().Name); }
    }

    public bool IsCurrent()
    {
        if(_observation==null || !IsComplete)return false;
        try
        {
            if(!_observation.OwnersCurrent())return false;
            bool matches;
            using(_observation.Memory.TemporaryCacheState(false))matches=_observation.ReadbackMatches();
            return matches && _observation.OwnersCurrent();
        }
        catch(Exception error)when(OrdinaryFailure(error)){return false;}
    }

    private static bool OrdinaryFailure(Exception error)=>error is not OperationCanceledException && error is not ThreadInterruptedException;

    private sealed class Observation(LocalPlayer player,Memory memory,Func<uint,int,byte[]> read)
    {
        private readonly uint _base=player.BaseAddress;
        private readonly Dictionary<(uint Address,int Count),byte[]> _bytes=new();
        internal Memory Memory=>memory;
        internal ulong Guid { get; private set; }
        internal int AreaId { get; private set; }
        internal int MapId { get; private set; }

        private byte[] Bytes(uint address,int count)
        {
            if(address==0 || count is not (4 or 8))throw new InvalidDataException("Invalid area observation range");
            _=checked(address+(uint)count-1);
            byte[]? result=read(address,count);
            if(result==null || result.Length!=count)throw new IOException("Incomplete area observation");
            var key=(address,count);
            if(_bytes.TryGetValue(key,out byte[]? old) && !old.SequenceEqual(result))throw new IOException("Changed area observation");
            _bytes[key]=(byte[])result.Clone();
            return result;
        }
        private uint U32(uint address)=>BitConverter.ToUInt32(Bytes(address,4),0);
        private ulong U64(uint address)=>BitConverter.ToUInt64(Bytes(address,8),0);

        internal void Capture()
        {
            uint descriptor=U32(checked(_base+8));
            Guid=U64(checked(_base+48));
            if(Guid==0 || U64(descriptor)!=Guid || U32(checked(_base+20))!=4)
                throw new InvalidDataException("Area player identities disagree");
            uint area=U32(CurrentArea),map=U32(CurrentMap);
            if(area==0 || area>int.MaxValue || map>int.MaxValue)
                throw new InvalidDataException("Current area or map is not a usable identity");
            AreaId=(int)area;MapId=(int)map;
        }
        internal bool ReadbackMatches()=>_bytes.All(row=>
        {
            byte[]? current=read(row.Key.Address,row.Key.Count);
            return current!=null && current.Length==row.Key.Count && row.Value.SequenceEqual(current);
        });
        internal bool OwnersCurrent()=>ReferenceEquals(player,ObjectManager.Me) && ReferenceEquals(memory,ObjectManager.Wow) &&
            ObjectManager.IsInGame && player.IsValid && player.BaseAddress==_base && player.Guid==Guid;
    }
}
