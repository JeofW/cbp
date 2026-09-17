using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual descriptor-backed WoWUnit properties and object registry. Missing
// public APIs are assertion failures, not fixture compilation failures.
internal static class UnitUpstreamContractRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure(string why) : Exception(why) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (uint value in new uint[] { 0, 1, 128, 4096, 8192, 0x80000000, uint.MaxValue })
        {
            uint flags=value;
            cases.Add(($"raw NPC flags preserve unsigned mask {flags:X8}", f => { f.Set(f.Unit,UnitFields.NpcFlags,BitConverter.GetBytes(flags)); Check(f.Read<uint>("NpcFlags")==flags,"raw mask changed"); }));
            cases.Add(($"existing NPC helpers retain semantics for {flags:X8}", f => { f.Set(f.Unit,UnitFields.NpcFlags,BitConverter.GetBytes(flags)); Check(f.Unit.CanGossip==((flags&1)==1) && f.Unit.IsVendor==((flags&128)==128) && f.Unit.IsRepairMerchant==((flags&4096)==4096) && f.Unit.IsFlightMaster==((flags&8192)==8192),"existing flag helpers changed"); }));
        }
        foreach (ulong value in new ulong[] { 0, 1, 0x12345678ABCDEF01, ulong.MaxValue })
        {
            ulong guid=value;
            cases.Add(($"charm GUID alias retains all64 bits {guid:X16}", f => { f.Set(f.Unit,UnitFields.CharmedBy,BitConverter.GetBytes(guid)); Check(f.Read<ulong>("CharmedByUnitGuid")==guid && f.Unit.CharmedByGuid==guid,"charm alias changed identity"); }));
            cases.Add(($"creator GUID alias retains all64 bits {guid:X16}", f => { f.Set(f.Unit,UnitFields.CreatedBy,BitConverter.GetBytes(guid)); Check(f.Read<ulong>("CreatedByUnitGuid")==guid && f.Unit.CreatedByGuid==guid,"creator alias changed identity"); }));
        }
        cases.Add(("charm alias resolves the actual registered unit", f => { f.Set(f.Unit,UnitFields.CharmedBy,BitConverter.GetBytes(f.Other.Guid)); Check(ReferenceEquals(f.Read<WoWUnit?>("CharmedByUnit"),f.Other) && ReferenceEquals(f.Unit.CharmedBy,f.Other),"alias selected a different wrapper"); }));
        cases.Add(("zero charm alias remains null", f => Check(f.Read<WoWUnit?>("CharmedByUnit")==null,"zero identity resolved")));
        cases.Add(("unobserved charm alias remains null", f => { f.Set(f.Unit,UnitFields.CharmedBy,BitConverter.GetBytes(555555UL)); Check(f.Read<WoWUnit?>("CharmedByUnit")==null,"missing identity borrowed another unit"); }));
        cases.Add(("charm alias follows descriptor changes instead of caching", f => { f.Set(f.Unit,UnitFields.CharmedBy,BitConverter.GetBytes(f.Other.Guid)); Check(ReferenceEquals(f.Read<WoWUnit?>("CharmedByUnit"),f.Other),"initial owner"); f.Set(f.Unit,UnitFields.CharmedBy,BitConverter.GetBytes(0UL)); Check(f.Read<WoWUnit?>("CharmedByUnit")==null,"cleared owner remained cached"); }));
        foreach (var values in new[] { (0f,0f,5f),(0.5f,1.5f,5f),(1.5f,1.5f,5f),(4f,1.5f,6.8333335f),(10f,2f,13.333333f) })
        {
            var v=values;
            cases.Add(($"loot reach {v.Item1}/{v.Item2} is independent of normal interaction", f => { f.Reach(v.Item1,v.Item2); Near(f.Read<float>("LootRange"),v.Item3); Near(f.Unit.InteractRange,v.Item1+4f); }));
            foreach (float gap in new[] { -0.01f, 0.01f })
            {
                float delta=gap;
                cases.Add(($"loot range boundary {v.Item1}/{v.Item2}/{delta}", f => { f.Reach(v.Item1,v.Item2); f.Distance=v.Item3+delta; Check(f.Read<bool>("WithinLootRange")== (delta<0),"wrong loot admission at boundary"); }));
            }
        }
        cases.Add(("exact five-yard boundary remains outside", f => { f.Reach(1.5f,1.5f); f.Distance=5f; Check(!f.Read<bool>("WithinLootRange"),"strict boundary became inclusive"); }));
        cases.Add(("vertical distance is not discarded", f => { f.Reach(1.5f,1.5f); f.Vertical=true; f.Distance=6f; Check(!f.Read<bool>("WithinLootRange"),"other floor borrowed planar proximity"); }));
        cases.Add(("missing player cannot authorize looting", f => { ObjectManager.Me=null; Check(!f.Read<bool>("WithinLootRange"),"missing actor authorized proximity"); }));
        foreach (float bad in new[] { float.NaN,float.PositiveInfinity,-1f,float.MaxValue })
        {
            float value=bad;
            cases.Add(($"invalid corpse reach {value} cannot authorize looting", f => { f.Reach(value,1.5f); f.Distance=1f; Check(!f.Read<bool>("WithinLootRange"),"unknown/overflowing reach authorized action"); }));
            cases.Add(($"invalid player reach {value} cannot authorize looting", f => { f.Reach(1.5f,value); f.Distance=1f; Check(!f.Read<bool>("WithinLootRange"),"unknown/overflowing actor reach authorized action"); }));
        }
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases)
        {
            try { using var f=new Fixture(); item.Test(f); passed++; Console.WriteLine("PASS upstream unit: "+item.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL upstream unit assertion: "+item.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR upstream unit fixture: "+item.Name+": "+e); }
        }
        Console.WriteLine($"Upstream unit scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual properties/descriptor/registry; controlled geometry; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Upstream unit regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class UnitState(uint p, Func<WoWPoint> position) : WoWUnit(p)
    {
        public override WoWPoint Location => position();
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly object source;
        private readonly Memory memory;
        private readonly LocalPlayer player;
        internal readonly WoWUnit Unit,Other;
        internal float Distance=3f;
        internal bool Vertical;
        internal Fixture()
        {
            source=Activator.CreateInstance(typeof(QuestPoiTargetIdentityRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            world=(IDisposable)source;
            try
            {
                memory=ObjectManager.Wow!; player=ObjectManager.Me!;
                var raw=(WoWUnit)Invoke(source.GetType().GetMethod("Add",Hidden)!,source,false,54321U)!;
                Other=(WoWUnit)Invoke(source.GetType().GetMethod("Add",Hidden)!,source,false,54322U)!;
                Unit=new UnitState(raw.BaseAddress,()=>player.Location.Add(Vertical ? 0 : Distance,0,Vertical ? Distance : 0));
                Reach(1.5f,1.5f);
                if(!Unit.IsValid || !Other.IsValid || ObjectManager.Executor!=null) throw new InvalidOperationException("descriptor-only fixture not established");
            }
            catch { world.Dispose(); throw; }
        }
        internal T Read<T>(string name)
        {
            var prop=typeof(WoWUnit).GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
            Check(prop!=null && prop.GetMethod?.IsPublic==true && prop.SetMethod==null && prop.PropertyType==typeof(T),"missing public read-only "+name+" contract");
            return (T)Invoke(prop!.GetMethod!,Unit)!;
        }
        internal void Set(WoWUnit unit,UnitFields field,byte[] bytes)
        {
            uint descriptor=memory.Read<uint>(unit.BaseAddress+8), address=descriptor+(uint)field*4;
            var ptr=new IntPtr(unchecked((int)address)); Marshal.Copy(bytes,0,ptr,bytes.Length);
            var cache=(ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Hidden)!.GetValue(memory)!;
            cache.Value!.Remove(ptr);
        }
        internal void Reach(float corpse,float actor) { Set(Unit,UnitFields.CombatReach,BitConverter.GetBytes(corpse)); Set(player,UnitFields.CombatReach,BitConverter.GetBytes(actor)); }
        public void Dispose() => world.Dispose();
    }
    private static object? Invoke(MethodInfo method,object target,params object[] args)
    {
        try { return method.Invoke(target,args); }
        catch(TargetInvocationException e) when(e.InnerException!=null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static void Near(float actual,float expected) => Check(Math.Abs(actual-expected)<0.00001f,$"expected {expected}, got {actual}");
    private static void Check(bool ok,string why) { if(!ok) throw new AssertionFailure(why); }
}
