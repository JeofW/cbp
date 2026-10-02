using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using CoreRest = Styx.Logic.Common.Rest;

// Actual public Feed, real logging callbacks and host descriptor readers.
// Inventory is known empty in the existing no-executor fixture. No game,
// consumable use, movement command or native target mutation is executed.
internal static class LegacyRestContinuationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Body)>();
        cases.Add(("unrelated missing aura metadata cannot poison legacy rest", f =>
        {
            f.UnknownAura(); f.Feed();
            Check(CoreRest.NoFood && CoreRest.NoDrink, "complete empty inventory control was hidden by unrelated aura metadata");
        }));
        cases.Add(("unreadable inventory is not absent configured food", f =>
        {
            f.UnknownInventory(); f.Feed();
            Check(!CoreRest.NoFood && !CoreRest.NoDrink, "unavailable inventory became an absence claim");
        }));
        foreach (bool legacy in new[] { false, true })
        {
            bool oldEvent = legacy;
            foreach (bool start in new[] { false, true })
            {
                bool atStart = start;
                foreach (string change in new[] { "water", "shoreline", "dead", "mounted", "missing", "wrapper", "memory", "address", "settings", "run" })
                {
                    string state = change;
                    cases.Add(($"{(legacy ? "legacy" : "new")} log/{(start ? "start" : "food")}: {state} revokes continuation", f =>
                    {
                        f.WithHook(oldEvent, atStart, () => f.Change(state), () => f.Feed());
                        Check(!CoreRest.NoDrink, "obsolete Feed continued into the drink inventory branch");
                        Check(!atStart || !CoreRest.NoFood, "obsolete Feed continued into the food inventory branch");
                    }));
                }
                cases.Add(($"{(legacy ? "legacy" : "new")} log/{(start ? "start" : "food")}: unchanged context retains dry empty-bag control", f =>
                {
                    f.WithHook(oldEvent, atStart, () => { }, () => f.Feed());
                    Check(CoreRest.NoFood && CoreRest.NoDrink, "valid dry legacy result was lost");
                }));
                foreach (int errorKind in new[] { 0, 1, 2 })
                {
                    int kind = errorKind;
                    cases.Add(($"{(legacy ? "legacy" : "new")} log/{(start ? "start" : "food")}: exact error {kind} is preserved", f =>
                    {
                        Exception expected = kind == 0 ? new OperationCanceledException("controlled rest cancellation")
                            : kind == 1 ? new ThreadInterruptedException("controlled rest interruption")
                            : new InvalidOperationException("controlled rest logger failure");
                        Exception? observed = null;
                        f.WithHook(oldEvent, atStart, () => throw expected, () =>
                        {
                            try { f.FeedRaw(); } catch (Exception error) { observed = error; }
                        });
                        Check(ReferenceEquals(observed, expected), "logger error was swallowed, substituted or wrapped");
                        Check(!CoreRest.NoDrink, "throwing callback was followed by another inventory branch");
                    }));
                }
            }
            cases.Add(($"{(legacy ? "legacy" : "new")} log: nested Feed does not resume its obsolete parent", f =>
            {
                int drinkMessages = 0;
                Action<LogLevel, string> count = (_, message) => { if (message.Contains("No Rest-test-drink in bags.")) drinkMessages++; };
                Logging.OnMessageLogged += count;
                try { f.WithHook(oldEvent, false, () => CoreRest.Feed(), () => f.Feed()); }
                finally { Logging.OnMessageLogged -= count; }
                Check(drinkMessages == 1, $"nested Feed was followed by {drinkMessages} drink inventory reports instead of one");
            }));
        }
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var f = new Fixture(); item.Body(f); passed++; Console.WriteLine("PASS legacy rest continuation: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL legacy rest continuation assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR legacy rest continuation fixture: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Legacy rest continuation scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual public Feed and logger boundaries; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Legacy rest continuation regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly object aquatic;
        private readonly LocalPlayer player;
        private readonly object? oldSettings;
        private readonly FieldInfo settings = typeof(CharacterSettings).GetField("<Instance>k__BackingField", Hidden)!;
        private readonly Dictionary<ulong, WoWObject> registry;
        private readonly KeyValuePair<ulong, WoWObject>[] oldRegistry;
        private readonly object registryLock;
        private readonly object? oldLegacyOwner;
        private readonly FieldInfo worker = typeof(Styx.Logic.BehaviorTree.TreeRoot).GetField("_workerThread", Hidden)!;
        private readonly object? oldWorker;
        private readonly FieldInfo? legacyOwner = typeof(CoreRest).GetField("_legacyFeedOwner", Hidden);
        internal Fixture()
        {
            aquatic = Activator.CreateInstance(typeof(AquaticObservationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            world = (IDisposable)aquatic;
            player = (LocalPlayer)aquatic.GetType().GetField("Player", Hidden)!.GetValue(aquatic)!;
            oldSettings = settings.GetValue(null);
            settings.SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(CharacterSettings)));
            oldLegacyOwner = legacyOwner?.GetValue(null);
            oldWorker = worker.GetValue(null);
            registry = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", Hidden)!.GetValue(null)!;
            registryLock = typeof(ObjectManager).GetField("_updateLock", Hidden)!.GetValue(null)!;
            lock (registryLock) { oldRegistry = registry.ToArray(); registry[player.Guid] = player; }
            Resource(UnitFields.Health, BitConverter.GetBytes(20u)); Resource(UnitFields.MaxHealth, BitConverter.GetBytes(100u));
            Resource(UnitFields.Mana, BitConverter.GetBytes(10u)); Resource(UnitFields.MaxMana, BitConverter.GetBytes(100u));
            LevelbotSettings.Instance.FoodName = "Rest-test-food";
            LevelbotSettings.Instance.DrinkName = "Rest-test-drink";
            Call("Prime");
            Check(ObjectManager.Executor == null && !player.IsMoving, "offline stationary fixture was not established");
        }
        private void Resource(UnitFields field, byte[] bytes)
        {
            uint descriptor = ObjectManager.Wow!.Read<uint>(player.BaseAddress + 8);
            Call("Cache", descriptor + (uint)field * 4, bytes);
        }
        internal void WithHook(bool legacy, bool start, Action effect, Action run)
        {
            if (start)
            {
                Resource(UnitFields.Target, BitConverter.GetBytes(player.Guid));
                Check(ReferenceEquals(player.CurrentTarget, player), "actual target reader did not observe the registered target");
            }
            int calls = 0;
            void Observe(string text)
            {
                bool match = start ? text.Contains("Resting.") : text.Contains("No Rest-test-food in bags.");
                if (match && calls == 0) { calls++; effect(); }
            }
            Logging.LogMessageDelegate onNew = values => { foreach (var value in values) Observe(value.Message); };
            Action<LogLevel, string> onOld = (_, text) => Observe(text);
            if (legacy) Logging.OnMessageLogged += onOld; else Logging.OnLogMessage += onNew;
            try { run(); }
            finally { if (legacy) Logging.OnMessageLogged -= onOld; else Logging.OnLogMessage -= onNew; }
            Check(calls == 1, "required actual logging boundary was not reached exactly once");
            Check(ObjectManager.Executor == null, "native executor must stay absent");
        }
        internal void Change(string state)
        {
            switch (state)
            {
                case "water": Call("Swim", true); break;
                case "shoreline":
                    player.GetType().GetField("Position", Hidden)!.SetValue(player, player.Location.Add(.125f, 0, 0));
                    Check(CoreRest.GetAdmissionDenial(player, requireStationary:true, allowQueries:false)
                        == "liquid-or-dry-observation-unavailable",
                        "moved dry cache remained prepared rest permission");
                    Call("ObserveWetHere");
                    break;
                case "dead": player.GetType().GetField("Alive", Hidden)!.SetValue(player, false); break;
                case "mounted":
                    player.GetType().GetField("Riding", Hidden)!.SetValue(player, true);
                    Call("SetGroundMounted", true);
                    break;
                case "missing": ObjectManager.Me = null; break;
                case "wrapper": ObjectManager.Me = new LocalPlayer(player.BaseAddress); break;
                case "memory": typeof(ObjectManager).GetProperty("Wow", Hidden)!.SetValue(null, null); break;
                case "address": typeof(WoWObject).GetMethod("UpdateBaseAddress", Hidden)!.Invoke(player, new object[] { player.BaseAddress + 16384 }); break;
                case "settings": LevelbotSettings.Instance.DrinkName = "Replacement-drink"; break;
                case "run": worker.SetValue(null, new Thread(() => { })); break;
            }
        }
        internal void Feed()
        {
            Exception? escaped = null;
            try { FeedRaw(); } catch (Exception error) { escaped = error; }
            Check(escaped == null, "Feed escaped after the controlled state change: " + escaped);
        }
        internal void FeedRaw()
        {
            int observedDrink = 0;
            Action<LogLevel, string> stop = (_, text) =>
            {
                bool terminalObservation = text.Contains("No Rest-test-drink in bags.")
                    || text.Contains("Rest drink deferred: status=UNKNOWN");
                if (!terminalObservation || observedDrink++ != 0) return;
                LevelbotSettings.Instance.DrinkName = "Rest-test-drink-observed";
            };
            Logging.OnMessageLogged += stop;
            try { Call("WithStrictRestObservation", (Action)CoreRest.Feed); }
            finally { Logging.OnMessageLogged -= stop; }
        }
        internal void UnknownInventory() => Call("Cache", player.BaseAddress + 6384U, new byte[17]);
        internal void UnknownAura()
        {
            Call("Cache", player.BaseAddress + 3536U, BitConverter.GetBytes(1));
            var raw = new byte[24]; BitConverter.GetBytes(61988).CopyTo(raw, 8); raw[12] = 1;
            System.Runtime.InteropServices.Marshal.Copy(raw, 0, new IntPtr(unchecked((int)(player.BaseAddress + 3152U))), raw.Length);
        }
        private void Call(string method, params object[] arguments)
        {
            try { aquatic.GetType().GetMethod(method, Hidden)!.Invoke(aquatic, arguments); }
            catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        public void Dispose()
        {
            lock (registryLock) { registry.Clear(); foreach (var entry in oldRegistry) registry.Add(entry.Key, entry.Value); }
            settings.SetValue(null, oldSettings);
            legacyOwner?.SetValue(null, oldLegacyOwner);
            worker.SetValue(null, oldWorker);
            world.Dispose();
        }
    }
    private static void Check(bool valid, string why) { if (!valid) throw new AssertionFailure(why); }
}
