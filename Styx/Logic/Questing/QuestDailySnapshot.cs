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
/// Checked current daily quest IDs from the original build12340 player descriptor.
/// Null means unavailable, including an incomplete read. Permanent reward history,
/// aggregate daily counts and wall-clock reset estimates are never substituted.
/// Matching samples are not a transaction or proof against an intervening ABA.
/// </summary>
public sealed class QuestDailySnapshot
{
    // GetDailyQuestsCompleted58DB30 reads25 IDs at player-descriptor+4528.
    // Constructor6D1CF0 binds that pointer to base-descriptor+592: total0x1400.
    private const uint DailyOffset=0x1400;
    private const int DailySlots=25;
    private readonly Observation? _observation;
    private QuestDailySnapshot(string status,Observation? observation=null)
    {
        Status=status;_observation=observation;
        QuestIds=observation==null ? null : Array.AsReadOnly(observation.Ids.ToArray());
        PlayerGuid=observation?.Guid ?? 0;ObservedUtc=DateTime.UtcNow;
    }
    public string Status { get; }
    public IReadOnlyCollection<uint>? QuestIds { get; }
    public ulong PlayerGuid { get; }
    public DateTime ObservedUtc { get; }
    public bool IsComplete=>QuestIds!=null;

    public static QuestDailySnapshot Capture(LocalPlayer? player)
    {
        Memory? memory=ObjectManager.Wow;
        return memory==null ? new("daily-memory-unavailable") : CaptureCore(player,memory,memory.ReadBytes);
    }
    private static QuestDailySnapshot CaptureCore(LocalPlayer? player,Memory memory,Func<uint,int,byte[]> read)
    {
        try
        {
            if(player==null || !ReferenceEquals(player,ObjectManager.Me) || !ReferenceEquals(memory,ObjectManager.Wow) ||
               !ObjectManager.IsInGame || !player.IsValid || player.BaseAddress==0)
                return new("daily-player-unavailable");
            var sample=new Observation(player,memory,read);
            using(memory.TemporaryCacheState(false))
            {
                sample.Capture();
                if(!sample.ReadbackMatches())return new("daily-changed-during-capture");
            }
            return sample.OwnersCurrent() ? new("complete-owned-daily-sample",sample) : new("daily-owner-changed");
        }
        catch(Exception error)when(OrdinaryFailure(error)){return new("daily-unavailable:"+error.GetType().Name);}
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
        internal List<uint> Ids { get; }=new();
        private byte[] Bytes(uint address,int count)
        {
            if(address==0 || count is not (4 or 8 or DailySlots*4))throw new InvalidDataException("Invalid daily observation range");
            _=checked(address+(uint)count-1);
            byte[]? result=read(address,count);
            if(result==null || result.Length!=count)throw new IOException("Incomplete daily observation");
            var key=(address,count);
            if(_bytes.TryGetValue(key,out byte[]? old) && !old.SequenceEqual(result))throw new IOException("Changed daily observation");
            _bytes[key]=(byte[])result.Clone();return result;
        }
        private uint U32(uint address)=>BitConverter.ToUInt32(Bytes(address,4),0);
        private ulong U64(uint address)=>BitConverter.ToUInt64(Bytes(address,8),0);
        internal void Capture()
        {
            uint descriptor=U32(checked(_base+8));
            Guid=U64(checked(_base+48));
            if(Guid==0 || U64(descriptor)!=Guid || U32(checked(_base+20))!=4)
                throw new InvalidDataException("Daily player identities disagree");
            byte[] slots=Bytes(checked(descriptor+DailyOffset),DailySlots*4);
            var seen=new HashSet<uint>();
            for(int i=0;i<DailySlots;i++)
            {
                uint id=BitConverter.ToUInt32(slots,i*4);
                if(id==0)continue;
                if(id>int.MaxValue || !seen.Add(id))throw new InvalidDataException("Invalid or duplicate daily quest ID");
                Ids.Add(id);
            }
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
