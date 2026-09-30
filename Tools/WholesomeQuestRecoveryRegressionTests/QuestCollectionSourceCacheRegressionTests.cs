using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Bots.Quest.Objectives;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

// Test the existing source predicate with allocated unit memory. A declared
// source is not permission to attack; combat/loot admission owners stay separate.
internal static class QuestCollectionSourceCacheRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action<Fixture> Body)>
        {
            ("declared creature source does not require optional template metadata", f =>
            {
                Check(!f.Unit.GetCachedInfo(out _), "fixture accidentally supplied creature metadata");
                Check(f.Matches(f.Unit), "declared profile source was withheld by the missing optional cache");
            }),
            ("unlisted uncached creature remains unknown", f => Check(!f.Matches(f.Other), "unlisted uncached target became a known source")),
            ("same number in a gameobject hint cannot certify a creature", f =>
            { f.ReplaceOwner(gameObjectHint:true); Check(!f.Matches(f.Unit), "source hint crossed object namespaces"); }),
            ("zero unit wrapper cannot use a declared source", f => Check(!f.Matches(new WoWUnit(0)), "zero wrapper was admitted")),
            ("null input is not an eligible source", f => Check(!f.Matches(null), "null source was admitted")),
            ("declared source still requires a nonzero loaded GUID", f =>
            { f.ChangeIdentity(0,0,3); Check(!f.Matches(f.Unit), "zero identity borrowed a declared source"); }),
            ("object and descriptor identity must agree", f =>
            { f.ChangeIdentity(901001,901002,3); Check(!f.Matches(f.Unit), "mismatched identity borrowed a declared source"); }),
            ("player or non-unit type cannot borrow a creature hint", f =>
            { f.ChangeIdentity(901001,901001,4); Check(!f.Matches(f.Unit), "non-creature object borrowed a creature source"); }),
            ("disabled source remains rejected before and after memoization", f =>
            {
                Check(f.Matches(f.Unit), "valid source control failed");
                f.Disable(); Check(!f.Matches(f.Unit), "memoized source bypassed object invalidation");
            }),
            ("source hint does not manufacture item progress", f =>
            {
                Check(f.Matches(f.Unit), "source control failed");
                f.Live.SetInventory(new()); Check(!f.Owner.IsCompleted, "source hint became inventory");
                f.Live.SetInventory(new(){[901]=1}); Check(!f.Owner.IsCompleted, "partial item count completed");
                f.Live.SetInventory(new(){[901]=2}); Check(f.Owner.IsCompleted, "observed item count failed acknowledgement");
                f.Live.SetAccepted(true,failed:true); Check(!f.Owner.IsCompleted, "failed acceptance retained item completion");
            })
        };
        int passed=0,assertions=0,unexpected=0;
        foreach(var test in tests)
        {
            try { using var fixture=new Fixture(); test.Body(fixture); passed++; Console.WriteLine("PASS collection source cache: "+test.Name); }
            catch(Failure error) { assertions++; Console.Error.WriteLine("FAIL collection source cache: "+test.Name+": "+error.Message); }
            catch(Exception error) { unexpected++; Console.Error.WriteLine("ERROR collection source cache: "+test.Name+": "+error); }
        }
        Console.WriteLine($"Collection source cache scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; allocated unit memory and actual source/item owners; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Collection source cache regressions");
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly QuestDatasetObservationFixture Live=new();
        private readonly IntPtr storage=Marshal.AllocHGlobal(8192);
        private readonly GreenMagic.Memory memory=ObjectManager.Wow!;
        internal readonly WoWUnit Unit,Other;
        internal CollectItemObjective Owner=null!;
        private uint Start => unchecked((uint)storage.ToInt32());
        internal Fixture()
        {
            Marshal.Copy(new byte[8192],0,storage,8192);
            Setup(Start,501,901001); Setup(Start+4096,502,901002);
            Unit=new WoWUnit(Start); Other=new WoWUnit(Start+4096);
            Live.SetQuest(991001,"Fixture",60,new int[4],new int[4],new[]{901,0,0,0,0,0},new[]{2,0,0,0,0,0});
            ReplaceOwner(false);
        }
        private static void Setup(uint address,uint entry,ulong guid)
        {
            var pointer=new IntPtr(unchecked((int)address)); uint fields=address+512;
            Marshal.WriteInt32(pointer,8,unchecked((int)fields)); Marshal.WriteInt32(pointer,20,3);
            Marshal.WriteInt64(pointer,48,unchecked((long)guid));
            var descriptor=new IntPtr(unchecked((int)fields));
            Marshal.WriteInt64(descriptor,unchecked((long)guid)); Marshal.WriteInt32(descriptor,12,unchecked((int)entry));
        }
        internal void ReplaceOwner(bool gameObjectHint)
        {
            Owner?.Dispose();
            string kind=gameObjectHint?"GameObject":"Mob";
            Live.LoadProfile("<HBProfile><Name>Fixture</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><Quest Id=\"991001\" Name=\"Fixture\"><Objective Type=\"CollectItem\" ItemId=\"901\" CollectCount=\"2\"><CollectFrom><"+kind+" Id=\"501\" /></CollectFrom></Objective></Quest><QuestOrder /></HBProfile>");
            Owner=new CollectItemObjective(Live.Quest,new(),Live.Quest.GetObjectives().Single(o=>o.ID==901),new());
        }
        internal bool Matches(WoWUnit? unit)
        {
            using(memory.TemporaryCacheState(false))
            {
                try { return (bool)typeof(CollectItemObjective).GetMethod("IsValidMobTarget",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Owner,new object?[]{unit})!; }
                catch(TargetInvocationException error) when(error.InnerException!=null)
                { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            }
        }
        internal void Disable()=>Marshal.WriteInt32(storage,188,0x10000);
        internal void ChangeIdentity(ulong guid,ulong descriptorGuid,int type)
        {
            Marshal.WriteInt64(storage,48,unchecked((long)guid));
            Marshal.WriteInt64(new IntPtr(unchecked((int)(Start+512))),unchecked((long)descriptorGuid));
            Marshal.WriteInt32(storage,20,type);
        }
        public void Dispose() { Owner?.Dispose(); Live.Dispose(); Marshal.FreeHGlobal(storage); }
    }
    private static void Check(bool value,string message) { if(!value)throw new Failure(message); }
}
