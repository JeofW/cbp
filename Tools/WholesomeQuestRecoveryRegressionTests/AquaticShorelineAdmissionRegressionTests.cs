using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using CoreRest = Styx.Logic.Common.Rest;

// Execute the real liquid-cache and all three public rest entry points.
// Reuse the existing controlled descriptor fixture; no native executor or item use.
internal static class AquaticShorelineAdmissionRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string why) : base(why) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (var offset in new[] { new WoWPoint(.125f,0,0), new WoWPoint(-.125f,0,0),
            new WoWPoint(0,.125f,0), new WoWPoint(0,-.125f,0), new WoWPoint(1,1,0), new WoWPoint(1.999f,0,0) })
        {
            var delta = offset;
            cases.Add(($"dry cache cannot cross an unobserved shoreline at {delta}", f => {
                f.Prime(); f.Move(delta); Check(LiquidEnvironment.IsPlayerInLiquid(f.Player), "another position borrowed the old dry probe"); }));
            foreach (bool drink in new[] { false, true })
            {
                bool d = drink;
                cases.Add(($"{(d ? "drink" : "food")} denies moved dry cache at {delta}", f => {
                    f.Prime(); f.Move(delta); f.ImmediateDenied(d); }));
            }
        }
        cases.Add(("stationary dry cache remains reusable", f => { f.Prime(); Check(!LiquidEnvironment.IsPlayerInLiquid(f.Player), "unchanged position lost its dry control"); }));
        cases.Add(("nearby wet cache remains conservative and reusable", f => {
            f.Prime(); SetLiquid("_lastProbeResult", true); var original = GetLiquid<WoWPoint>("_lastProbeLocation");
            f.Move(new WoWPoint(.125f,0,0)); Check(LiquidEnvironment.IsPlayerInLiquid(f.Player)
                && GetLiquid<WoWPoint>("_lastProbeLocation") == original, "safe wet-cache reuse was unnecessarily removed"); }));
        cases.Add(("far movement already invalidates dry permission", f => {
            f.Prime(); f.Move(new WoWPoint(3,0,0)); Check(LiquidEnvironment.IsPlayerInLiquid(f.Player), "far dry cache remained authoritative"); }));
        foreach (bool drink in new[] { false, true })
        {
            bool d = drink;
            foreach (string state in new[] { "swimming", "unknown", "vertical", "dead", "mounted", "missing" })
            {
                string s = state;
                cases.Add(($"legacy Feed {(d ? "drink" : "food")} denies {s} before missing-inventory mutation", f => f.Legacy(d,s)));
            }
            cases.Add(($"legacy Feed dry {(d ? "drink" : "food")} retains its empty-inventory behavior", f => f.Legacy(d,"dry")));
        }
        int passed=0, assertions=0, unexpected=0;
        foreach (var item in cases)
        {
            try { using var f = new Fixture(); item.Test(f); passed++; Console.WriteLine("PASS aquatic shoreline: " + item.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL aquatic shoreline assertion: " + item.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR aquatic shoreline fixture/owner: " + item.Name + ": " + e); }
        }
        Console.WriteLine($"Aquatic shoreline scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual cache and public rest owners; no game attached.");
        if (assertions+unexpected != 0) throw new InvalidOperationException($"Aquatic shoreline regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly string oldFood = LevelbotSettings.Instance.FoodName, oldDrink = LevelbotSettings.Instance.DrinkName;
        internal readonly LocalPlayer Player;
        internal Fixture()
        {
            world=(IDisposable)Activator.CreateInstance(typeof(AquaticObservationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            Player=(LocalPlayer)world.GetType().GetField("Player",Hidden)!.GetValue(world)!;
            Check(ObjectManager.Executor == null, "native executor must remain absent");
        }
        private void Call(string name, params object[] values)
        {
            try { world.GetType().GetMethod(name,Hidden)!.Invoke(world,values); }
            catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        internal void Prime() => Call("Prime");
        internal void Move(WoWPoint delta)
        {
            var p=Player.Location;
            Player.GetType().GetField("Position",Hidden)!.SetValue(Player,new WoWPoint(p.X+delta.X,p.Y+delta.Y,p.Z+delta.Z));
        }
        internal void ImmediateDenied(bool drink)
        {
            var timer=(WaitTimer)typeof(CoreRest).GetField(drink ? "_drinkTimer" : "_feedTimer",Static)!.GetValue(null)!;
            var before=timer.StartTime;
            if (drink) CoreRest.DrinkImmediate(); else CoreRest.FeedImmediate();
            Check(timer.StartTime==before && !(drink ? CoreRest.NoDrink : CoreRest.NoFood), "unobserved position consumed retry or asserted missing inventory");
        }
        private void Resource(string name,uint value)
        {
            uint descriptor=ObjectManager.Wow!.Read<uint>(Player.BaseAddress+8);
            uint field=Convert.ToUInt32(Enum.Parse(typeof(UnitFields),name));
            Call("Cache",descriptor+field*4,BitConverter.GetBytes(value));
        }
        internal void Legacy(bool drink,string state)
        {
            Resource("UNIT_FIELD_HEALTH",20); Resource("UNIT_FIELD_MAXHEALTH",100);
            Resource("UNIT_FIELD_POWER1",10); Resource("UNIT_FIELD_MAXPOWER1",100);
            LevelbotSettings.Instance.FoodName=drink ? "" : "Aquatic-test-food";
            LevelbotSettings.Instance.DrinkName=drink ? "Aquatic-test-drink" : "";
            if (state != "unknown") Prime();
            if (state=="swimming") Call("Swim",true);
            if (state=="vertical") Move(new WoWPoint(0,0,-4));
            if (state=="dead") Player.GetType().GetField("Alive",Hidden)!.SetValue(Player,false);
            if (state=="mounted") Player.GetType().GetField("Riding",Hidden)!.SetValue(Player,true);
            if (state=="missing") ObjectManager.Me=null;
            CoreRest.Feed();
            Check((drink ? CoreRest.NoDrink : CoreRest.NoFood)==(state=="dry"), "legacy Feed did not honor environment/lifetime admission");
        }
        public void Dispose()
        {
            LevelbotSettings.Instance.FoodName=oldFood; LevelbotSettings.Instance.DrinkName=oldDrink;
            world.Dispose();
        }
    }
    private static T GetLiquid<T>(string name) => (T)typeof(LiquidEnvironment).GetField(name,Static)!.GetValue(null)!;
    private static void SetLiquid(string name,object value) => typeof(LiquidEnvironment).GetField(name,Static)!.SetValue(null,value);
    private static void Check(bool valid,string why) { if (!valid) throw new AssertionFailure(why); }
}
