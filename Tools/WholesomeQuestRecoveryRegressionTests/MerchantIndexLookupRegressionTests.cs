using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using GreenMagic;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.WoWInternals;

// Actual MerchantFrame lookup and MerchantItem decoding over controlled cached
// native observations. No Lua purchase, sale, new executor or game attachment.
internal static class MerchantIndexLookupRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure(string text) : Exception(text) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new List<(string Name,Action<Fixture> Test)>();
        for(int count=1;count<=4;count++)
        {
            int n=count;
            for(int position=0;position<count;position++)
            {
                int p=position;
                cases.Add(($"catalog {n} returns Lua index {p+1}",f=>{ f.Catalog(n); f.Expect((uint)(1001+p),p+1); }));
            }
            cases.Add(($"catalog {n} cannot borrow adjacent out-of-range row",f=>{ f.Catalog(n); f.Expect(7777,null); }));
        }
        cases.Add(("empty catalog cannot borrow first backing record",f=>{f.Catalog(0);f.Expect(7777,null);}));
        cases.Add(("negative unavailable count remains empty",f=>{f.Catalog(-1);f.Expect(1001,null);}));
        cases.Add(("zero item ID cannot match an unhydrated row",f=>{f.Catalog(3);f.Row(1,0);f.Expect(0,null);}));
        cases.Add(("duplicate item IDs preserve first occurrence",f=>{f.Catalog(3);f.Row(0,5555);f.Row(1,5555);f.Expect(5555,1);}));
        cases.Add(("absent item ID is not an adjacent unrelated item",f=>{f.Catalog(3);f.Expect(9999,null);}));
        cases.Add(("changed ordering uses the current observed catalog",f=>{f.Catalog(3);f.Expect(1002,2);f.Row(0,1002);f.Row(1,1001);f.Expect(1002,1);}));
        cases.Add(("shrunken catalog cannot return its former last row",f=>{f.Catalog(3);f.Expect(1003,3);f.Count(2);f.Expect(1003,null);}));
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases)
        {
            try { using var f=new Fixture(); item.Test(f); passed++;Console.WriteLine("PASS merchant index: "+item.Name); }
            catch(AssertionFailure e) { assertions++;Console.Error.WriteLine("FAIL merchant index assertion: "+item.Name+": "+e.Message); }
            catch(Exception e) { unexpected++;Console.Error.WriteLine("ERROR merchant index fixture/owner: "+item.Name+": "+e); }
        }
        Console.WriteLine($"Merchant index scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual lookup/record reader; controlled observations; no purchase dispatched.");
        if(assertions+unexpected!=0)throw new InvalidOperationException($"Merchant index regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly Dictionary<IntPtr,byte[]> cache;
        private readonly MethodInfo lookup=typeof(MerchantFrame).GetMethod("GetMerchantIndex",Hidden)!;
        internal Fixture()
        {
            world=(IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            cache=((ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Hidden)!.GetValue(ObjectManager.Wow)!).Value!;
            if(ObjectManager.Executor!=null)throw new InvalidOperationException("Native executor must remain absent");
        }
        internal void Count(int count)=>cache[new IntPtr(12559344)]=BitConverter.GetBytes(count);
        internal void Catalog(int count)
        {
            Count(count);
            for(int i=0;i<6;i++)Row(i,(uint)(i==Math.Max(0,count)?7777:1001+i));
        }
        internal void Row(int index,uint id)
        {
            var bytes=new byte[32];BitConverter.GetBytes(id).CopyTo(bytes,4);
            BitConverter.GetBytes(-1).CopyTo(bytes,12);BitConverter.GetBytes(100).CopyTo(bytes,16);BitConverter.GetBytes(1).CopyTo(bytes,24);
            cache[new IntPtr(12554536+32*index)]=bytes;
        }
        internal void Expect(uint id,int? expected)
        {
            int? actual;
            try { actual=(int?)lookup.Invoke(MerchantFrame.Instance,new object[]{id}); }
            catch(TargetInvocationException e) when(e.InnerException!=null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw; }
            if(actual!=expected)throw new AssertionFailure($"item {id}: expected {expected?.ToString()??"absent"}, observed {actual?.ToString()??"absent"}");
            if(ObjectManager.Executor!=null)throw new InvalidOperationException("Unexpected native executor");
        }
        public void Dispose()=>world.Dispose();
    }
}
