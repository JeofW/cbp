using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
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
        cases.Add(("prepared action current dry cache is sufficient", f => { f.Prime(); Check(!LiquidEnvironment.IsPlayerInLiquid(f.Player, allowQuery:false), "valid dry cache denied prepared action"); }));
        cases.Add(("prepared action cannot warm missing geometry", f =>
        {
            Check(LiquidEnvironment.IsPlayerInLiquid(f.Player, allowQuery:false), "missing geometry authorized prepared action");
            Check(!(bool)typeof(LiquidEnvironment).GetField("_hasProbe",Static)!.GetValue(null)!, "prepared action attempted a native geometry observation");
        }));
        cases.Add(("unavailable native trace is not dry permission", f => f.UnavailableTrace()));
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
            var current = ObjectManager.Me ?? throw new InvalidOperationException("Aquatic fixture player is unavailable");
            Player = new PlayerState(current.BaseAddress); ObjectManager.Me = Player;
            SeedStrictRestState();
            // A known empty inventory must contain a complete backpack header,
            // sixteen observed empty slots and four observed absent bag GUIDs.
            // Missing executor/default reads alone are not evidence of absence.
            uint inventoryItems = Player.BaseAddress + 45000;
            var bag = new byte[17];
            BitConverter.GetBytes(150u).CopyTo(bag, 0);
            BitConverter.GetBytes(inventoryItems).CopyTo(bag, 4);
            bag[16] = 1;
            Cache(Player.BaseAddress + 6384, bag);
            Cache(inventoryItems + 23 * 8, new byte[16 * 8]);
            for (uint slot = 0; slot < 4; slot++) Cache(12727616U + 8U * slot, new byte[8]);
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
            ObserveLiquid(false);
            Check(!LiquidEnvironment.IsPlayerInLiquid(Player, allowQuery:false),
                "explicit current dry observation was not accepted");
        }
        internal void ObserveWetHere()
        {
            ObserveLiquid(true);
            Check(LiquidEnvironment.IsPlayerInLiquid(Player, allowQuery:false),
                "explicit current wet observation was not retained");
        }
        internal void UnavailableTrace()
        {
            Exception? observed = null;
            try { _ = LiquidEnvironment.IsPlayerInLiquid(Player); }
            catch (ObservationUnavailableException error) { observed = error; }
            Check(observed is ObservationUnavailableException,
                "missing native trace did not remain UNKNOWN");
            Check(!(bool)typeof(LiquidEnvironment).GetField("_hasProbe",Static)!.GetValue(null)!,
                "failed native trace fabricated a reusable geometry observation");
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
            RejectDry("obsolete dry cache survived "+kind);
        }
        internal void Denied(bool isDrink,string state)
        {
            if(state=="swimming") Swim(true);
            if(state=="dead") { Prime(); Player.Alive=false; }
            if(state=="mounted") { Prime(); Player.Riding=true; SetGroundMounted(true); }
            if(state=="missing-player") ObjectManager.Me=null;
            var timer=isDrink?drink:food; DateTime start=timer.StartTime;
            Action attempt = () => { if(isDrink) CoreRest.DrinkImmediate(); else CoreRest.FeedImmediate(); };
            if(state=="unknown-liquid")
            {
                WithStrictRestObservation(() => Check(
                    CoreRest.GetAdmissionDenial(Player, requireStationary:true, allowQueries:false)
                        == "liquid-or-dry-observation-unavailable",
                    "missing liquid observation was not the prepared rest denial"));
                attempt();
            }
            else if(state=="swimming" || state=="mounted")
                WithStrictRestObservation(attempt);
            else
                attempt();
            Check(timer.StartTime==start && !(isDrink?CoreRest.NoDrink:CoreRest.NoFood),"denied use consumed retry budget or asserted missing inventory");
        }
        internal void Dry(bool isDrink)
        {
            Prime(); WithStrictRestObservation(() => { if(isDrink) CoreRest.DrinkImmediate(); else CoreRest.FeedImmediate(); });
            Check(isDrink?CoreRest.NoDrink:CoreRest.NoFood,"admitted empty bags lost legacy missing flag");
            Check(!(isDrink?drink:food).IsFinished,"admitted attempt lost retry throttle");
        }
        internal void Throttled(bool isDrink)
        {
            Prime(); var timer=isDrink?drink:food; timer.Reset(); DateTime start=timer.StartTime;
            if(isDrink) CoreRest.DrinkImmediate(); else CoreRest.FeedImmediate();
            Check(timer.StartTime==start && !(isDrink?CoreRest.NoDrink:CoreRest.NoFood),"active throttle admitted another attempt");
        }
        internal void WithStrictRestObservation(Action action)
        {
            var previous = ObjectManager.Executor;
            Check(previous == null, "strict rest fixture inherited an executor");
            // Public rest admission now observes the same recovery run as final
            // item submission. Represent that owner explicitly while keeping the
            // executor incapable of native dispatch and the inventory empty.
            var root = typeof(Styx.Logic.BehaviorTree.TreeRoot);
            var workerField = root.GetField("_workerThread", Static)!;
            var stateField = root.GetField("<State>k__BackingField", Static)!;
            var previousWorker = workerField.GetValue(null);
            var previousState = stateField.GetValue(null);
            var memory = ObjectManager.Wow ?? throw new InvalidOperationException("strict rest fixture lost memory");
            var executor = (ExecutorRand)RuntimeHelpers.GetUninitializedObject(typeof(ExecutorRand));
            typeof(ExecutorRand).GetField("<Memory>k__BackingField",Hidden)!.SetValue(executor,memory);
            typeof(ExecutorRand).GetField("<IsInitialized>k__BackingField",Hidden)!.SetValue(executor,true);
            typeof(ExecutorRand).GetField("AssemblyLock",Hidden)!.SetValue(executor,new object());
            var threadField = typeof(GreenMagic.Memory).GetField("_hThread",Hidden)!;
            var previousThread = threadField.GetValue(memory);
            threadField.SetValue(memory,new IntPtr(1));
            ObjectManager.Executor = executor;
            workerField.SetValue(null, Thread.CurrentThread);
            stateField.SetValue(null, Styx.Logic.BehaviorTree.TreeRootState.Running);
            try { action(); }
            finally
            {
                workerField.SetValue(null, previousWorker);
                stateField.SetValue(null, previousState);
                ObjectManager.Executor = previous;
                threadField.SetValue(memory,previousThread);
            }
        }
        internal void SetGroundMounted(bool value)
        {
            uint descriptor = ObjectManager.Wow!.Read<uint>(Player.BaseAddress + 8);
            uint field = StrictField("MountDisplayId");
            Raw(descriptor + field * 4, BitConverter.GetBytes(value ? 1u : 0u));
        }
        private void SeedStrictRestState()
        {
            uint descriptor = ObjectManager.Wow!.Read<uint>(Player.BaseAddress + 8);
            uint movement = Player.BaseAddress + 8192u;
            Raw(Player.BaseAddress + 216u, BitConverter.GetBytes(movement));
            Raw(movement + 8u, BitConverter.GetBytes(0UL));
            Raw(movement + 68u, BitConverter.GetBytes(0u));
            Raw(descriptor + StrictField("MountDisplayId") * 4u, BitConverter.GetBytes(0u));
            Raw(descriptor + StrictField("Bytes2") * 4u, BitConverter.GetBytes(0u));
            Raw(descriptor + StrictField("Flags") * 4u, BitConverter.GetBytes(0u));
        }
        private static uint StrictField(string name)
        {
            return Convert.ToUInt32(Enum.Parse(typeof(Styx.Offsets.WoWUnitFields), name));
        }
        private static void Raw(uint address, byte[] bytes)
            => Marshal.Copy(bytes, 0, new IntPtr(unchecked((int)address)), bytes.Length);
        private void ObserveLiquid(bool wet)
        {
            typeof(LiquidEnvironment).GetField("_lastProbeTick",Static)!.SetValue(null,Environment.TickCount64);
            typeof(LiquidEnvironment).GetField("_lastProbeMapId",Static)!.SetValue(null,Player.MapId);
            typeof(LiquidEnvironment).GetField("_lastProbeLocation",Static)!.SetValue(null,Player.Location);
            typeof(LiquidEnvironment).GetField("_lastProbeResult",Static)!.SetValue(null,wet);
            typeof(LiquidEnvironment).GetField("_hasProbe",Static)!.SetValue(null,true);
            typeof(LiquidEnvironment).GetField("_lastProbePlayer",Static)!.SetValue(null,Player);
            typeof(LiquidEnvironment).GetField("_lastProbeMemory",Static)!.SetValue(null,ObjectManager.Wow);
            typeof(LiquidEnvironment).GetField("_lastProbeAddress",Static)!.SetValue(null,Player.BaseAddress);
        }
        private void RejectDry(string why)
        {
            try { Check(LiquidEnvironment.IsPlayerInLiquid(Player), why); }
            catch (ObservationUnavailableException) { }
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
