using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using CoreRest = Styx.Logic.Common.Rest;

// Actual liquid-cache and immediate consumable owners. Only cached geometry,
// player observations and test-process descriptors are controlled. No executor,
// native geometry, inventory use, movement or game is attached.
internal static class AquaticObservationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string why) : base(why) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (bool swim in new[] { false, true })
            foreach (bool trace in new[] { false, true })
            {
                bool s = swim, t = trace;
                cases.Add(($"swim={s}, crossing={t}: retained boolean contract", f => Check(LiquidEnvironment.IsInLiquid(s,t) == (s||t), "boolean liquid contract changed")));
            }
        cases.Add(("same-owner stationary cached dry control", f => { f.Prime(); Check(!LiquidEnvironment.IsPlayerInLiquid(f.Player), "valid cached dry observation was discarded"); }));
        cases.Add(("unavailable native trace is not dry permission", f => Check(LiquidEnvironment.IsPlayerInLiquid(f.Player), "missing native trace permitted dry use")));
        foreach (string change in new[] { "up", "down", "map", "wrapper", "address", "future", "expired", "nan", "swim-roundtrip" })
        {
            string c = change;
            cases.Add((c + " invalidates a previous dry cache", f => f.Changed(c)));
        }
        foreach (bool drink in new[] { false, true })
        {
            bool d = drink;
            foreach (string state in new[] { "swimming", "unknown-liquid", "dead", "mounted", "missing-player" })
            {
                string s = state;
                cases.Add(($"{(d?"drink":"food")} {s}: deny before timer and missing-inventory mutation", f => f.Denied(d,s)));
            }
            cases.Add(($"{(d?"drink":"food")} admitted dry empty inventory retains missing flag", f => f.Dry(d)));
            cases.Add(($"{(d?"drink":"food")} existing retry throttle remains authoritative", f => f.Throttled(d)));
        }
        int passed=0, assertions=0, unexpected=0;
        foreach (var c in cases)
        {
            try { using var f = new Fixture(); c.Test(f); passed++; Console.WriteLine("PASS aquatic observation: " + c.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL aquatic observation assertion: " + c.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR aquatic observation fixture/owner: " + c.Name + ": " + e); }
        }
        Console.WriteLine($"Aquatic observation scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; real cache/rest owners; controlled world; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Aquatic observation regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class PlayerState : LocalPlayer
    {
        internal WoWPoint Position = new(10,20,30);
        internal bool Alive=true, Riding;
        internal PlayerState(uint address):base(address) { }
        public override WoWPoint Location => Position;
        public override bool IsAlive => Alive;
        public override bool IsGhost => false;
        public override bool Mounted => Riding;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly List<(FieldInfo Field, object? Value)> liquid = new();
        private readonly bool oldFood = CoreRest.NoFood, oldDrink = CoreRest.NoDrink;
        private readonly WaitTimer food = (WaitTimer)typeof(CoreRest).GetField("_feedTimer", Static)!.GetValue(null)!;
        private readonly WaitTimer drink = (WaitTimer)typeof(CoreRest).GetField("_drinkTimer", Static)!.GetValue(null)!;
        private readonly DateTime foodStart, drinkStart;
        internal PlayerState Player;
        internal Fixture()
        {
            world=(IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            Player = new PlayerState(ObjectManager.Me.BaseAddress); ObjectManager.Me = Player;
            foodStart=food.StartTime; drinkStart=drink.StartTime;
            foreach(var f in typeof(LiquidEnvironment).GetFields(Static).Where(f=>!f.IsLiteral&&!f.IsInitOnly))
            {
                liquid.Add((f,f.GetValue(null))); f.SetValue(null,f.FieldType.IsValueType?Activator.CreateInstance(f.FieldType):null);
            }
            food.Stop(); drink.Stop(); Flag(false,false); Flag(true,false);
            if (ObjectManager.Executor!=null || !Player.IsValid || Player.IsSwimming || !Player.IsAlive)
                throw new InvalidOperationException("Aquatic fixture is not isolated");
        }
        private void Cache(uint address, byte[] bytes)
        {
            var c=(ThreadLocal<Dictionary<IntPtr,byte[]>>)ObjectManager.Wow!.GetType().GetField("_cache",Hidden)!.GetValue(ObjectManager.Wow)!;
            c.Value![new IntPtr(unchecked((int)address))]=bytes;
        }
        private void Swim(bool value) => Cache(Player.BaseAddress+2608,BitConverter.GetBytes(value?2097152u:0u));
        internal void Prime()
        {
            // Warm the real owner/cache fields through the real read, then seed
            // one prior dry geometry result. The test never substitutes its code.
            Check(LiquidEnvironment.IsPlayerInLiquid(Player),"missing executor must conservatively report a hit");
            typeof(LiquidEnvironment).GetField("_lastProbeResult",Static)!.SetValue(null,false);
            typeof(LiquidEnvironment).GetField("_lastProbeTick",Static)!.SetValue(null,Environment.TickCount64);
        }
        internal void Changed(string kind)
        {
            Prime();
            if(kind=="up") Player.Position=Player.Position.Add(0,0,4);
            else if(kind=="down") Player.Position=Player.Position.Add(0,0,-4);
            else if(kind=="map") Cache(0xBD088C,BitConverter.GetBytes(3u));
            else if(kind=="wrapper") { Player=new PlayerState(Player.BaseAddress); ObjectManager.Me=Player; }
            else if(kind=="address") typeof(WoWObject).GetMethod("UpdateBaseAddress",Hidden)!.Invoke(Player,new object[]{Player.BaseAddress+16384});
            else if(kind=="future") typeof(LiquidEnvironment).GetField("_lastProbeTick",Static)!.SetValue(null,Environment.TickCount64+10000);
            else if(kind=="expired") typeof(LiquidEnvironment).GetField("_lastProbeTick",Static)!.SetValue(null,Environment.TickCount64-1000);
            else if(kind=="nan") Player.Position=new WoWPoint(float.NaN,20,30);
            else if(kind=="swim-roundtrip") { Swim(true); Check(LiquidEnvironment.IsPlayerInLiquid(Player),"swimming did not veto dry cache"); Swim(false); }
            Check(LiquidEnvironment.IsPlayerInLiquid(Player),"obsolete dry cache survived "+kind);
        }
        internal void Denied(bool isDrink,string state)
        {
            if(state=="swimming") Swim(true);
            if(state=="dead") { Prime(); Player.Alive=false; }
            if(state=="mounted") { Prime(); Player.Riding=true; }
            if(state=="missing-player") ObjectManager.Me=null;
            var timer=isDrink?drink:food; DateTime start=timer.StartTime;
            if(isDrink) CoreRest.DrinkImmediate(); else CoreRest.FeedImmediate();
            Check(timer.StartTime==start && !(isDrink?CoreRest.NoDrink:CoreRest.NoFood),"denied use consumed retry budget or asserted missing inventory");
        }
        internal void Dry(bool isDrink)
        {
            Prime(); if(isDrink) CoreRest.DrinkImmediate(); else CoreRest.FeedImmediate();
            Check(isDrink?CoreRest.NoDrink:CoreRest.NoFood,"admitted empty bags lost legacy missing flag");
            Check(!(isDrink?drink:food).IsFinished,"admitted attempt lost retry throttle");
        }
        internal void Throttled(bool isDrink)
        {
            Prime(); var timer=isDrink?drink:food; timer.Reset(); DateTime start=timer.StartTime;
            if(isDrink) CoreRest.DrinkImmediate(); else CoreRest.FeedImmediate();
            Check(timer.StartTime==start && !(isDrink?CoreRest.NoDrink:CoreRest.NoFood),"active throttle admitted another attempt");
        }
        private static void Flag(bool isDrink,bool value)=>typeof(CoreRest).GetField(isDrink?"<NoDrink>k__BackingField":"<NoFood>k__BackingField",Static)!.SetValue(null,value);
        public void Dispose()
        {
            foreach(var x in liquid) x.Field.SetValue(null,x.Value);
            Flag(false,oldFood); Flag(true,oldDrink);
            typeof(WaitTimer).GetField("_startTime",Hidden)!.SetValue(food,foodStart);
            typeof(WaitTimer).GetField("_startTime",Hidden)!.SetValue(drink,drinkStart);
            world.Dispose();
        }
    }
    private static void Check(bool value,string why) { if(!value) throw new AssertionFailure(why); }
}
