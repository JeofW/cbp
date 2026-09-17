using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx.Helpers;
using Styx.Patchables;
using Styx.WoWInternals;

// Actual WoWDb constructor/indexer, Memory field reads and DbTable headers.
// Registration fields are controlled cache observations; row storage belongs to
// the test process. No client registration code, spell or native executor runs.
internal static class DatabaseDiscoveryRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Body)>();
        void Add(string name, Action<Fixture> test) => cases.Add((name, test));
        Add("one complete registration is published", f => { f.Record(0); f.End(1); f.Open(); f.ExpectReady(1); });
        Add("multiple complete registrations are published together", f => { f.Record(0); f.Record(1, ClientDb.Lock); f.End(2); f.Open(); f.ExpectReady(2); });
        Add("identical duplicate registration remains idempotent", f => { f.Record(0); f.Record(1); f.End(2); f.Open(); f.ExpectReady(1); });
        Add("table header may be discovered before its rows are loaded", f => { f.HeaderLoaded(0); f.Record(0); f.End(1); f.Open(); f.ExpectReady(1); Check(!f.Db![ClientDb.Spell]!.IsLoaded,"unloaded header lost its state"); });
        Add("missing memory does not commit readiness", f => { f.DropMemory(); f.Open(); f.ExpectUnavailable(); });
        Add("same object retries when memory arrives", f => { f.DropMemory(); f.Open(); f.RestoreMemory(); f.Record(0); f.End(1); Check(f.Db![ClientDb.Spell]!=null,"same indexer never retried initialization"); f.ExpectReady(1); });
        Add("empty registration list is not completed discovery", f => { f.End(0); f.Open(); f.ExpectUnavailable(); });
        foreach (uint id in new uint[] {0, uint.MaxValue, 1000000})
        {
            uint value=id;
            Add("unrecognized table ID " + id + " cannot be published", f => { f.Record(0,(ClientDb)value); f.End(1); f.Open(); f.ExpectUnavailable(); });
        }
        foreach (uint address in new uint[] {0, 1, 0xFFFFFFF8})
        {
            uint value=address;
            Add("invalid table address " + address + " remains retryable", f => { f.Record(0, pointer:value); f.End(1); f.Open(); f.ExpectUnavailable(); });
        }
        Add("failure after one readable record cannot publish a prefix", f => { f.Record(0); f.Record(1,ClientDb.Lock,1); f.End(2); f.Open(); f.ExpectUnavailable(); });
        Add("readable prefix followed by unknown ID is rejected atomically", f => { f.Record(0); f.Record(1,(ClientDb)0); f.End(2); f.Open(); f.ExpectUnavailable(); });
        Add("failed discovery recovers on the same indexer after repair", f => { f.Record(0,pointer:1); f.End(1); f.Open(); f.Record(0); Check(f.Db![ClientDb.Spell]!=null,"failed discovery poisoned future lookup"); f.ExpectReady(1); });
        Add("empty list can become available on the same indexer", f => { f.End(0); f.Open(); f.Record(0); f.End(1); Check(f.Db![ClientDb.Spell]!=null,"empty list was permanently cached"); f.ExpectReady(1); });
        Add("new instance can recover after incomplete discovery", f => { f.Record(0,pointer:1); f.End(1); f.Open(); f.Record(0); f.Open(); f.ExpectReady(1); });
        Add("conflicting duplicate table pointer is not silently first-wins", f => { f.Record(0); f.CopyHeader(64); f.Record(1,pointer:f.HeaderAddress+64); f.End(2); f.Open(); f.ExpectUnavailable(); });
        Add("walk cannot exceed its bounded registration budget", f => { for(int i=0;i<1025;i++)f.Record(i); f.End(1025); f.Open(); f.ExpectUnavailable(); });
        Add("already completed discovery keeps the published table", f => { f.Record(0); f.End(1); f.Open(); var table=f.Db![ClientDb.Spell]; f.Record(0,pointer:1); f.Open(); Check(ReferenceEquals(table,f.Db![ClientDb.Spell]),"completed table was gratuitously rebuilt"); });
        Add("unrelated lookup does not fabricate a table", f => { f.Record(0); f.End(1); f.Open(); Check(f.Db![ClientDb.Lock]==null,"absent table was invented"); });
        Add("observer sees only the completed registry", f => {
            f.Record(0); f.Record(1,ClientDb.Lock); f.End(2); bool observed=false;
            f.WithLog(()=>{observed=true;Check(f.Ready && f.Count==2,"diagnostic observed partial registry");},()=>f.Open());
            Check(observed,"expected production diagnostic was not reached"); f.ExpectReady(2);
        });
        foreach (int kind in new[]{0,1,2})
        {
            int errorKind=kind;
            Add("diagnostic exception policy " + kind, f => {
                f.Record(0); f.End(1);
                Exception expected=errorKind==0?new OperationCanceledException("discovery cancel"):
                    errorKind==1?new ThreadInterruptedException("discovery interrupt"):new InvalidOperationException("diagnostic failed");
                Exception? actual=null;
                f.WithLog(()=>throw expected,()=>{try{f.Open();}catch(Exception error){actual=error;}});
                Check(errorKind==2?actual==null:ReferenceEquals(actual,expected),"stop signal was swallowed/wrapped or ordinary compatibility changed");
                Check(f.Ready && f.Count==1,"diagnostic failure discarded already complete discovery");
            });
        }
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases)
        {
            try { using var f=new Fixture();item.Body(f);passed++;Console.WriteLine("PASS database discovery: "+item.Name); }
            catch(AssertionFailure e){assertions++;Console.Error.WriteLine("FAIL database discovery assertion: "+item.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR database discovery fixture/owner: "+item.Name+": "+e);}
        }
        Console.WriteLine($"Database discovery scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual constructor/indexer and registration fields; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException($"Database discovery regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable rows;
        private readonly Memory memory;
        private readonly Dictionary<IntPtr,byte[]> cache;
        private readonly FieldInfo tableField=typeof(WoWDb).GetField("_tables",Hidden)!;
        private readonly FieldInfo readyField=typeof(WoWDb).GetField("_initialized",Hidden)!;
        private readonly object originalTables;
        private readonly bool originalReady;
        internal WoWDb? Db;
        internal uint HeaderAddress;
        private readonly uint registry=(uint)GlobalOffsets.ClientDb_RegisterBase;
        internal bool Ready=>(bool)readyField.GetValue(null)!;
        internal int Count=>((Dictionary<ClientDb,WoWDb.DbTable>)tableField.GetValue(null)!).Count;
        internal Fixture()
        {
            rows=(IDisposable)Activator.CreateInstance(typeof(SpellRowLookupRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            memory=ObjectManager.Wow!;
            cache=((ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Hidden)!.GetValue(memory)!).Value!;
            originalTables=tableField.GetValue(null)!; originalReady=Ready;
            HeaderAddress=unchecked((uint)((IntPtr)rows.GetType().GetField("storage",Hidden)!.GetValue(rows)!).ToInt32());
            ((Dictionary<ClientDb,WoWDb.DbTable>)originalTables).Clear(); readyField.SetValue(null,false);
            if(ObjectManager.Executor!=null)throw new InvalidOperationException("No native executor is allowed.");
        }
        internal void Record(int slot,ClientDb table=ClientDb.Spell,uint? pointer=null)
        {
            uint address=registry+(uint)slot*17;
            cache[Ptr(address)]=new byte[]{0x68};
            cache[Ptr(address+1)]=BitConverter.GetBytes(unchecked((uint)table));
            cache[Ptr(address+11)]=BitConverter.GetBytes(pointer??HeaderAddress);
        }
        internal void End(int slot)=>cache[Ptr(registry+(uint)slot*17)]=new byte[]{0xC3};
        internal void Open()
        {
            try { Db=(WoWDb)Activator.CreateInstance(typeof(WoWDb),true)!; }
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        internal void CopyHeader(int delta)
        {
            int size=Marshal.SizeOf(typeof(WoWDb).GetNestedType("DbTableHeader",BindingFlags.NonPublic)!);
            var bytes=new byte[size];Marshal.Copy(Ptr(HeaderAddress),bytes,0,size);Marshal.Copy(bytes,0,Ptr(HeaderAddress+(uint)delta),size);
        }
        internal void HeaderLoaded(int value)
        {
            var type=typeof(WoWDb).GetNestedType("DbTableHeader",BindingFlags.NonPublic)!;
            Marshal.WriteInt32(Ptr(HeaderAddress),Marshal.OffsetOf(type,"IsLoaded").ToInt32(),value);cache.Remove(Ptr(HeaderAddress));
        }
        internal void DropMemory()=>typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,null);
        internal void RestoreMemory()=>typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,memory);
        internal void ExpectReady(int count)=>Check(Ready && Count==count,$"expected complete registry of {count}, ready={Ready}, count={Count}");
        internal void ExpectUnavailable()=>Check(!Ready && Count==0,$"failed discovery published ready={Ready}, count={Count}");
        internal void WithLog(Action change,Action action)
        {
            bool fired=false;
            Action<LogLevel,string> handler=(_,message)=>{if(!fired && message.Contains("[WoWDb] Loaded ",StringComparison.Ordinal)){fired=true;change();}};
            Logging.OnMessageLogged+=handler;try{action();}finally{Logging.OnMessageLogged-=handler;}
            Check(fired,"actual diagnostic hook was not reached");
        }
        public void Dispose()
        {
            RestoreMemory();
            if(!tableField.IsInitOnly)tableField.SetValue(null,originalTables);
            readyField.SetValue(null,originalReady);rows.Dispose();
        }
        private static IntPtr Ptr(uint address)=>new(unchecked((int)address));
    }
    private static void Check(bool condition,string why){if(!condition)throw new AssertionFailure(why);}
}
