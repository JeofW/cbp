using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;
using CoreRest = Styx.Logic.Common.Rest;

// Actual WholesomeAutoQuest.Pulse, real rest-pause fields and test-process player.
// No bot worker, native movement, inventory use, terrain or shoreline simulation.
internal static class WholesomeAquaticPauseRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string why) : base(why) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (string environment in new[] { "swimming", "unknown", "vertical-change" })
        {
            string e = environment;
            cases.Add((e + ": an existing pause is revoked before its 30-second wait", f => f.Wet(e, true, false)));
            cases.Add((e + ": low health cannot start an ineligible pause", f => f.Wet(e, false, false)));
            cases.Add((e + ": low mana cannot start an ineligible pause", f => f.Wet(e, false, true)));
        }
        cases.Add(("stopped lifecycle cannot restart a water-rest request", f => f.Stopped()));
        cases.Add(("dry low-health control still starts rest", f => f.Dry(false, false)));
        cases.Add(("dry low-mana control still starts rest", f => f.Dry(false, true)));
        cases.Add(("dry pending rest retains its original pause and deadline", f => f.Dry(true, false)));
        cases.Add(("dry recovery releases the pause normally", f => f.Recovered()));
        cases.Add(("dry timeout preserves its ordinary cooldown", f => f.Timeout()));
        cases.Add(("returning to an observed dry location can rest again", f => f.ReturnDry()));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var c in cases)
        {
            try { using var f = new Fixture(); c.Test(f); passed++; Console.WriteLine("PASS Wholesome aquatic pause: " + c.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL Wholesome aquatic pause assertion: " + c.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR Wholesome aquatic pause fixture/owner: " + c.Name + ": " + e); }
        }
        Console.WriteLine($"Wholesome aquatic-pause scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual Pulse; controlled world/mover; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Wholesome aquatic-pause regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Mover : IPlayerMover
    {
        internal int Stops;
        public void MoveStop() => Stops++;
        public void Move(WoWMovement.MovementDirection direction) => throw new InvalidOperationException("Native movement forbidden");
        public void MoveTowards(WoWPoint point) => throw new InvalidOperationException("Native movement forbidden");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly object world;
        private readonly LocalPlayer player;
        private readonly WholesomeAutoQuest bot = new();
        private readonly Mover mover = new();
        private readonly FieldInfo moverField = typeof(Navigator).GetField("_playerMover", Static)!;
        private readonly FieldInfo poiField = typeof(BotPoi).GetField("_current", Static)!;
        private readonly object? previousMover, previousPoi;
        private readonly WaitTimer food = (WaitTimer)typeof(CoreRest).GetField("_feedTimer", Static)!.GetValue(null)!;
        private readonly WaitTimer drink = (WaitTimer)typeof(CoreRest).GetField("_drinkTimer", Static)!.GetValue(null)!;
        internal Fixture()
        {
            world = Activator.CreateInstance(typeof(AquaticObservationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            player = ObjectManager.Me;
            previousMover = moverField.GetValue(null); previousPoi = poiField.GetValue(null);
            moverField.SetValue(null, mover); poiField.SetValue(null, new BotPoi(PoiType.None));
            Set("_stopped", false);
            Resource("Health", 20); Resource("MaxHealth", 100); Resource("Mana", 100); Resource("MaxMana", 100);
            if (TreeRoot.IsRunning || player.Combat || player.Dead || ObjectManager.Executor != null)
                throw new InvalidOperationException("Fixture does not have an idle managed host");
        }
        private void Set(string name, object value) => typeof(WholesomeAutoQuest).GetField(name, Hidden)!.SetValue(bot, value);
        private T Get<T>(string name) => (T)typeof(WholesomeAutoQuest).GetField(name, Hidden)!.GetValue(bot)!;
        private bool Paused => Get<bool>("_restingPaused");
        private void WorldCall(string name, params object[] args)
        {
            try { world.GetType().GetMethod(name, Hidden)!.Invoke(world, args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
        }
        private void Resource(string name, uint value)
        {
            Type fields = typeof(WoWUnit).Assembly.GetType("Styx.Offsets.UnitFields")!;
            uint descriptor = ObjectManager.Wow!.Read<uint>(player.BaseAddress + 8);
            uint address = descriptor + Convert.ToUInt32(Enum.Parse(fields, name)) * 4;
            WorldCall("Cache", address, BitConverter.GetBytes(value));
        }
        private void Prime() => WorldCall("Prime");
        private void LowMana() { Resource("Health", 100); Resource("Mana", 10); }
        private void SeedPause() { Set("_restingPaused", true); Set("_restStartTime", DateTime.Now); }
        internal void Wet(string environment, bool existing, bool lowMana)
        {
            if (lowMana) LowMana();
            if (existing) SeedPause();
            if (environment == "swimming") WorldCall("Swim", true);
            else if (environment == "vertical-change")
            {
                Prime(); player.GetType().GetField("Position", Hidden)!.SetValue(player, player.Location.Add(0, 0, -4));
            }
            DateTime start = Get<DateTime>("_restStartTime"), timeout = Get<DateTime>("_restTimeoutEnd");
            DateTime feedStart = food.StartTime, drinkStart = drink.StartTime;
            bot.Pulse();
            Check(!Paused, "wet/unknown world kept the quest root behind a rest pause");
            Check(mover.Stops == 0, "ineligible rest issued a movement stop");
            Check(Get<DateTime>("_restStartTime") == start && Get<DateTime>("_restTimeoutEnd") == timeout,
                "ineligible rest mutated the pause/retry deadline");
            Check(food.StartTime == feedStart && drink.StartTime == drinkStart && !CoreRest.NoFood && !CoreRest.NoDrink,
                "wet pause consumed an item retry or fabricated missing inventory");
        }
        internal void Stopped()
        {
            Set("_stopped", true); WorldCall("Swim", true); bot.Pulse();
            Check(!Paused && mover.Stops == 0, "stopped host began resting");
        }
        internal void Dry(bool existing, bool lowMana)
        {
            if (lowMana) LowMana(); if (existing) SeedPause();
            DateTime start = Get<DateTime>("_restStartTime");
            Prime(); bot.Pulse();
            Check(Paused && mover.Stops == (existing ? 0 : 1), "ordinary dry rest changed");
            Check(!existing || Get<DateTime>("_restStartTime") == start, "ongoing dry pause changed its deadline");
        }
        internal void Recovered()
        {
            SeedPause(); Resource("Health", 100); Resource("Mana", 100); Prime(); bot.Pulse();
            Check(!Paused && mover.Stops == 0, "recovered dry host remains paused");
        }
        internal void Timeout()
        {
            SeedPause(); Set("_restStartTime", DateTime.Now.AddSeconds(-35)); Prime(); bot.Pulse();
            Check(!Paused && mover.Stops == 0 && Get<DateTime>("_restTimeoutEnd") > DateTime.Now,
                "ordinary dry timeout lost its cooldown");
        }
        internal void ReturnDry()
        {
            WorldCall("Swim", true); bot.Pulse(); Check(!Paused, "water control was not released");
            WorldCall("Swim", false); Prime(); bot.Pulse();
            Check(Paused && mover.Stops == 1, "dry reentry failed to restore ordinary rest");
        }
        public void Dispose()
        {
            moverField.SetValue(null, previousMover); poiField.SetValue(null, previousPoi);
            ((IDisposable)world).Dispose();
        }
    }
    private static void Check(bool value, string why) { if (!value) throw new AssertionFailure(why); }
}
