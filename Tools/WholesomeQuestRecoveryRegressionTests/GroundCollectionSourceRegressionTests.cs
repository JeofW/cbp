using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Bots.Quest.Objectives;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// This executes the real CollectItemObjective source predicate and original
// descriptor readers. No fake selector, fabricated cache, or game is involved.
internal static class GroundCollectionSourceRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string, Action<Fixture>)>
        {
            ("declared gameobject source survives missing optional query cache", f =>
            { Check(!f.Object.GetCachedInfo(out _), "fixture supplied a query cache"); Check(f.Match(), "declared live source was rejected by optional cache absence"); }),
            ("same source number in creature namespace is not a gameobject", f =>
            { f.OwnerFor("Mob"); Check(!f.Match(), "creature hint certified gameobject source"); }),
            ("unlisted uncached gameobject remains unknown", f =>
            { f.Entry(900502); Check(!f.Match(), "unlisted source acquired authority"); }),
            ("null gameobject is rejected without throwing", f => Check(!f.Match(null), "null source admitted")),
            ("zero wrapper is rejected without throwing", f => Check(!f.Match(new WoWGameObject(0)), "empty wrapper admitted")),
            ("missing loaded GUID is rejected", f => { f.Identity(0,0,5); Check(!f.Match(), "zero GUID borrowed entry"); }),
            ("descriptor and object GUID must agree", f => { f.Identity(900501,900502,5); Check(!f.Match(), "identity mismatch admitted"); }),
            ("creature object cannot be relabelled gameobject", f => { f.Identity(900501,900501,3); Check(!f.Match(), "unit storage accepted as gameobject"); }),
            ("disabled object is not a live source", f => { f.Disable(); Check(!f.Match(), "disabled source admitted"); }),
            ("memoized source cannot outlive invalidation", f => { Check(f.Match(), "source control failed"); f.Disable(); Check(!f.Match(), "memoized source bypassed invalidation"); }),
            ("memoized entry does not bypass current dynamic availability", f =>
            { Check(f.Match(), "source control failed"); f.Dynamic(0); Check(!f.Match(), "unavailable object retained loot authority"); f.Dynamic(9); Check(f.Match(), "respawn/availability recovery was permanently excluded"); }),
            ("activation without loot sparkle remains unconfirmed", f => { f.Dynamic(1); Check(!f.Match(), "partial dynamic eligibility admitted"); }),
            ("sparkle without activation remains unconfirmed", f => { f.Dynamic(8); Check(!f.Match(), "partial dynamic eligibility admitted"); }),
            ("zero progress is not completed by source selection", f =>
            { Check(f.Match(), "source control failed"); f.Live.SetInventory(new()); Check(!f.Owner.IsCompleted, "entry discovery fabricated item stock"); }),
            ("observed partial/full stock remains distinct", f =>
            { f.Live.SetInventory(new(){[28116]=29}); Check(!f.Owner.IsCompleted, "partial debris count completed"); f.Live.SetInventory(new(){[28116]=30}); Check(f.Owner.IsCompleted, "complete observed item count ignored"); f.Live.SetAccepted(true,failed:true); Check(!f.Owner.IsCompleted,"failed quest completed from held items"); })
        };
        int passed=0, assertions=0, unexpected=0;
        foreach(var (name,test) in tests)
        {
            try { using var fixture=new Fixture(); test(fixture); passed++; }
            catch(Failure error) { assertions++; Console.Error.WriteLine("FAIL ground source: "+name+": "+error.Message); }
            catch(Exception error) { unexpected++; Console.Error.WriteLine("ERROR ground source: "+name+": "+error); }
        }
        Console.WriteLine($"Ground collection source scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual source predicate and allocated descriptors; no live execution proof.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Ground collection source regressions");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly QuestDatasetObservationFixture Live = new();
        private readonly IntPtr storage=Marshal.AllocHGlobal(4096);
        private readonly GreenMagic.Memory memory=ObjectManager.Wow!;
        private uint Address=>unchecked((uint)storage.ToInt32());
        internal readonly WoWGameObject Object;
        internal CollectItemObjective Owner=null!;
        internal Fixture()
        {
            Marshal.Copy(new byte[4096],0,storage,4096);
            Marshal.WriteInt32(storage,8,unchecked((int)(Address+512)));
            Identity(900501,900501,5); Entry(183394); Dynamic(9);
            // Original descriptor state=READY, type=CHEST. No client code runs.
            Marshal.WriteInt32(storage,512+68,1|(3<<8));
            Object=new WoWGameObject(Address);
            Live.SetQuest(10161,"In Case of Emergency...",58,new int[4],new int[4],new[]{28116,0,0,0,0,0},new[]{30,0,0,0,0,0});
            OwnerFor("GameObject");
        }
        internal void OwnerFor(string source)
        {
            Owner?.Dispose();
            Live.LoadProfile("<HBProfile><Name>Ground fixture</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><Quest Id=\"10161\" Name=\"In Case of Emergency...\"><Objective Type=\"CollectItem\" ItemId=\"28116\" CollectCount=\"30\"><CollectFrom><"+source+" Id=\"183394\" /></CollectFrom></Objective></Quest><QuestOrder /></HBProfile>");
            Owner=new CollectItemObjective(Live.Quest,new(),Live.Quest.GetObjectives().Single(o=>o.ID==28116),new());
        }
        internal bool Match()=>Match(Object);
        internal bool Match(WoWGameObject? value)
        {
            using(memory.TemporaryCacheState(false))
            {
                try{return (bool)typeof(CollectItemObjective).GetMethod("IsValidGameObjectTarget",Hidden)!.Invoke(Owner,new object?[]{value})!;}
                catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
            }
        }
        internal void Identity(ulong guid,ulong descriptor,int type)
        { Marshal.WriteInt64(storage,48,unchecked((long)guid)); Marshal.WriteInt64(storage,512,unchecked((long)descriptor)); Marshal.WriteInt32(storage,20,type); }
        internal void Entry(uint entry)=>Marshal.WriteInt32(storage,512+12,unchecked((int)entry));
        internal void Dynamic(uint value)=>Marshal.WriteInt32(storage,512+56,unchecked((int)value));
        internal void Disable()=>Marshal.WriteInt32(storage,188,0x10000);
        public void Dispose(){Owner?.Dispose();Live.Dispose();Marshal.FreeHGlobal(storage);}
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
