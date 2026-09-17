using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Bots.Grind;
using CommonBehaviors.Actions;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

// Construct the real LevelBot loot tree and execute its actual movement
// admission predicate. Do not tick movement, native interaction or event waits.
internal static class LootConsumerRangeRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure(string why) : Exception(why) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action<Fixture> Test)>();
        foreach (PoiType type in new[] { PoiType.Loot, PoiType.Skin, PoiType.Harvest })
        {
            var poiType=type;
            foreach (var row in new[] {
                (1.5f,1.5f,4.5f,false),(1.5f,1.5f,5f,true),(1.5f,1.5f,5.25f,true),(1.5f,1.5f,5.75f,true),
                (4f,1.5f,6.5f,false),(4f,1.5f,7.4f,true),(1.5f,5f,7f,false),(1.5f,5f,8f,true) })
            {
                var r=row;
                cases.Add(($"{type}: corpse={r.Item1}, actor={r.Item2}, distance={r.Item3}",f=>{
                    f.Reach(r.Item1,r.Item2);f.Distance(r.Item3);f.Publish(f.Unit,poiType);f.Expect(r.Item4);
                }));
            }
            cases.Add(($"{type}: another floor retains full three-dimensional distance",f=>{
                f.Reach(1.5f,1.5f);f.Vertical();f.Distance(5.25f);f.Publish(f.Unit,poiType);f.Expect(true);
            }));
            cases.Add(($"{type}: repeated distance changes re-evaluate the same target",f=>{
                f.Reach(1.5f,1.5f);f.Distance(5.25f);f.Publish(f.Unit,poiType);f.Expect(true);f.Distance(4.5f);f.Expect(false);f.Distance(5.25f);f.Expect(true);
            }));
        }
        foreach(float distance in new[]{3.5f,4f,4.5f,5.25f})
        {
            var d=distance;
            cases.Add(($"non-unit retains its own interaction range at {d}",f=>{
                f.Distance(d);f.Publish(f.CreateObject(),PoiType.Harvest);f.Expect(d>=4f);
            }));
        }
        cases.Add(("no observed object does not start movement",f=>{BotPoi.Current=new BotPoi(PoiType.None);f.Expect(false);}));
        cases.Add(("replacement target uses the new object category",f=>{
            f.Reach(1.5f,1.5f);f.Distance(4.5f);f.Publish(f.Unit,PoiType.Loot);f.Expect(false);
            f.Publish(f.CreateObject(),PoiType.Harvest);f.Expect(true);
        }));
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases)
        {
            try {using var f=new Fixture();item.Test(f);passed++;Console.WriteLine("PASS loot consumer: "+item.Name);}
            catch(AssertionFailure e){assertions++;Console.Error.WriteLine("FAIL loot consumer assertion: "+item.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR loot consumer fixture: "+item.Name+": "+e);}
        }
        Console.WriteLine($"Loot consumer scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual LevelBot factory/predicate/POI; no movement or game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException($"Loot consumer regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class ObjectState(uint address,Func<WoWPoint> location) : WoWGameObject(address)
    {
        public override WoWPoint Location=>location();
        public override float InteractRange=>4f;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly object source;
        private readonly FieldInfo eventFlag=typeof(LevelBot).GetField("_lootEventsAttached",Hidden)!;
        private readonly bool oldEventFlag;
        private readonly LocalPlayer player;
        private readonly Decorator gate;
        internal readonly WoWUnit Unit;
        internal Fixture()
        {
            source=Activator.CreateInstance(typeof(UnitUpstreamContractRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            world=(IDisposable)source;oldEventFlag=(bool)eventFlag.GetValue(null)!;
            try
            {
                player=ObjectManager.Me!;
                Unit=(WoWUnit)source.GetType().GetField("Unit",Hidden)!.GetValue(source)!;
                Register(Unit);
                // Skip only external hook installation while constructing the tree.
                // The real predicate is neither copied nor replaced.
                eventFlag.SetValue(null,true);
                Composite root=LevelBot.CreateLootBehavior();
                gate=Walk(root).OfType<Decorator>().Single(d=>d.Children.Count==1 && d.DecoratedChild is ActionMoveToPoi);
                Check(ObjectManager.Executor==null,"unexpected native executor");
            }
            catch{Dispose();throw;}
        }
        internal void Reach(float target,float actor)=>Invoke(source.GetType().GetMethod("Reach",Hidden)!,source,target,actor);
        internal void Distance(float value)=>source.GetType().GetField("Distance",Hidden)!.SetValue(source,value);
        internal void Vertical()=>source.GetType().GetField("Vertical",Hidden)!.SetValue(source,true);
        internal WoWObject CreateObject()
        {
            var backing=source.GetType().GetField("source",Hidden)!.GetValue(source)!;
            var raw=(WoWObject)Invoke(backing.GetType().GetMethod("Add",Hidden)!,backing,true,54323U)!;
            var obj=new ObjectState(raw.BaseAddress,()=>Unit.Location);Register(obj);return obj;
        }
        private static void Register(WoWObject obj)
        {
            typeof(WoWObject).GetField("_cachedName",Hidden)!.SetValue(obj,"controlled loot target");
            var registry=(Dictionary<ulong,WoWObject>)typeof(ObjectManager).GetField("_objectList",Hidden)!.GetValue(null)!;
            var mutex=typeof(ObjectManager).GetField("_updateLock",Hidden)!.GetValue(null)!;
            lock(mutex)registry[obj.Guid]=obj;
        }
        internal void Publish(WoWObject obj,PoiType type)
        {
            BotPoi.Current=new BotPoi(obj,type);
            Check(ReferenceEquals(BotPoi.Current.AsObject,obj),"actual POI did not retain the registered target");
        }
        internal void Expect(bool expected)
        {
            bool actual=(bool)Invoke(typeof(Decorator).GetMethod("CanRun",Hidden)!,gate,new object())!;
            Check(actual==expected,$"expected move={expected}, observed {actual}");
            Check(ObjectManager.Executor==null && ReferenceEquals(ObjectManager.Me,player),"predicate changed native/world state");
        }
        public void Dispose(){eventFlag.SetValue(null,oldEventFlag);world.Dispose();}
    }
    private static IEnumerable<Composite> Walk(Composite root)
    {
        yield return root;
        foreach(var child in root is GroupComposite g ? g.Children : root.Children)
            foreach(var item in Walk(child))yield return item;
    }
    private static object? Invoke(MethodInfo method,object target,params object[] args)
    {
        try{return method.Invoke(target,args);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static void Check(bool valid,string why){if(!valid)throw new AssertionFailure(why);}
}
