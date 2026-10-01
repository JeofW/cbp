using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommonBehaviors.Actions;
using GreenMagic;
using Styx;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.POI;
using Styx.WoWInternals;
using TreeSharp;

// Actual allocated-memory aura/Spell readers -> BotBase.Pulse -> RunTickBody ->
// WorkerThread. Only world observations and the bot's independent root are
// controlled. This exercises the owner on the calling thread, without a game,
// native executor, synthetic success in the aura reader, or background thread.
internal static class AuraWorkerLivenessRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Failure(string text) : Exception(text) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action<Fixture> Test)>
        {
            ("complete empty coverage reaches two independent root ticks", f =>
            {
                f.Aura(0); f.Run();
                Check(f.Bot.Pulses == 2 && f.Bot.RootTicks == 2, "healthy pulse did not execute actual worker/root");
            }),
            ("active 61988 cannot kill worker or starve independent recovery", f =>
            {
                f.Aura(61988); f.Run();
                Check(f.Bot.Pulses == 2 && f.Bot.RootTicks == 2,
                    $"active metadata UNKNOWN killed/starved worker: pulses={f.Bot.Pulses}; root={f.Bot.RootTicks}");
                Check(f.Bot.AfterObservation == 0, "unknown aura permitted code requiring a complete observation");
            }),
            ("active 56817 cannot kill worker or starve independent recovery", f =>
            {
                f.Aura(56817); f.Run();
                Check(f.Bot.Pulses == 2 && f.Bot.RootTicks == 2, "second live aura ID terminated or starved worker");
                Check(f.Bot.AfterObservation == 0, "second unknown aura was interpreted as absent");
            }),
            ("unknown aura cannot authorize absent-buff actions", f =>
            {
                f.Aura(61988); bool authorized = false, rejected = false;
                try { authorized = !ObjectManager.Me!.HasAura("Food"); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !authorized, "UNKNOWN was changed into FALSE");
            }),
            ("later complete aura coverage recovers without another Start", f =>
            {
                f.Aura(61988);
                f.Bot.BeforePulse = () => { if (f.Bot.Pulses == 2) f.Aura(0); };
                f.Run();
                Check(f.Bot.RootTicks == 2 && f.Bot.AfterObservation == 1, "metadata recovery required a new worker");
            }),
            ("pulse cancellation never reaches independent root", f =>
            {
                f.Bot.BeforePulse = () => throw new OperationCanceledException("controlled pulse cancellation");
                f.Run();
                Check(f.Bot.Pulses == 1 && f.Bot.RootTicks == 0, "cancellation became recoverable observation failure");
            }),
            ("explicit Stop inside pulse cannot run another root", f =>
            {
                f.Aura(0); f.Bot.BeforePulse = () => TreeRoot.Stop("controlled explicit Stop");
                f.Run();
                Check(f.Bot.Pulses == 1 && f.Bot.RootTicks == 0, "Stop allowed root side effects later in the tick");
            }),
            ("SafeAction preserves operation cancellation", f =>
            {
                var signal = new OperationCanceledException("controlled safe action cancellation");
                bool propagated = false;
                var safe = (Func<System.Action, string, bool, bool>)typeof(TreeRoot).GetMethod("SafeAction", Hidden)!
                    .CreateDelegate(typeof(Func<System.Action, string, bool, bool>));
                try { safe(() => throw signal, "cancellation probe", false); }
                catch (OperationCanceledException observed) { propagated = ReferenceEquals(signal, observed); }
                Check(propagated, "SafeAction swallowed operation cancellation");
            }),
            ("metadata failure remains visible without an exception storm", f =>
            {
                f.Aura(61988);
                for (int i = 0; i < 1000; i++)
                {
                    try { ObjectManager.Me!.GetAllAuras(); }
                    catch (InvalidOperationException error) { Logging.WriteException(error); }
                }
                int emitted = f.Messages.Count(text => text.Contains("61988"));
                Check(emitted > 0 && emitted < 20, $"unavailable aura produced {emitted} redundant diagnostics");
            })
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS aura worker: " + item.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL aura worker assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR aura worker fixture: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Aura worker scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual aura/Spell/worker/tick owners; controlled world; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Aura worker regressions remain");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly List<(FieldInfo Field, object? Before)> saved = new();
        private readonly bool frameLock = StyxSettings.Instance.UseFrameLock;
        private readonly bool fileLogging = Logging.FileLogging;
        private readonly byte treeTicks = TreeRoot.TicksPerSecond;
        private readonly CultureInfo culture = Thread.CurrentThread.CurrentCulture;
        private readonly CultureInfo uiCulture = Thread.CurrentThread.CurrentUICulture;
        private readonly BotPoi poi = BotPoi.Current;
        private readonly Logging.LogMessageDelegate listener;
        private readonly ActionRunCoroutine precheck = new((Func<object?, Task<bool>>)(_ => Task.FromResult(false)));
        internal readonly List<string> Messages = new();
        internal readonly ProbeBot Bot = new();

        internal Fixture()
        {
            if (ObjectManager.Executor != null || TreeRoot.IsRunning)
                throw new InvalidOperationException("A game executor or worker is already active; fixture refuses to attach");
            world = (IDisposable)Activator.CreateInstance(typeof(SpellRowLookupRegressionTests)
                .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            Save(typeof(CharacterSettings), "<Instance>k__BackingField",
                RuntimeHelpers.GetUninitializedObject(typeof(CharacterSettings)));
            Save(typeof(TreeRoot), "_workerThread", Thread.CurrentThread);
            Save(typeof(TreeRoot), "<State>k__BackingField", TreeRootState.Starting);
            Save(typeof(TreeRoot), "_composite0", precheck);
            Save(typeof(TreeRoot), "_paused", false);
            Save(typeof(TreeRoot), "_wasFalling", false);
            Save(typeof(TreeRoot), "_onTaxi", false);
            Save(typeof(BotManager), "_current", Bot);
            Save(typeof(BotEvents), "_onBotStarted", null);
            Save(typeof(BotEvents), "_onBotStopped", null);
            Save(typeof(RoutineManager), "_current", Activator.CreateInstance(
                typeof(RoutineManager).GetNestedType("DefaultCombatRoutine", BindingFlags.NonPublic)!, true));
            ((Stopwatch)typeof(TreeRoot).GetField("_afkCheckTimer", Hidden)!.GetValue(null)!).Restart();
            StyxSettings.Instance.UseFrameLock = false;
            CharacterSettings.Instance.TicksPerSecond = 100;
            Logging.FileLogging = false;
            listener = batch => { foreach (var message in batch) Messages.Add(message.Message); };
            Logging.OnLogMessage += listener;
            Bot.Read = () => { _ = ObjectManager.Me!.GetAllAuras(); };
            Check(ObjectManager.Me!.IsValid && StyxWoW.IsInGame && ObjectManager.Executor == null,
                "fixture must publish a valid actor and original aura reader without dispatch authority");
        }
        private void Save(Type owner, string name, object? value)
        {
            var field = owner.GetField(name, Hidden) ?? throw new MissingFieldException(owner.FullName, name);
            saved.Add((field, field.GetValue(null))); field.SetValue(null, value);
        }
        internal void Aura(uint id)
        {
            uint start = ObjectManager.Me!.BaseAddress;
            Marshal.WriteInt32(new IntPtr(unchecked((int)(start + 3536))), id == 0 ? 0 : 1);
            if (id != 0)
            {
                var bytes = new byte[24];
                BitConverter.GetBytes(id).CopyTo(bytes, 8);
                bytes[12] = 1; // active effect, no fabricated spell metadata
                BitConverter.GetBytes(60000u).CopyTo(bytes, 16);
                Marshal.Copy(bytes, 0, new IntPtr(unchecked((int)(start + 3152))), bytes.Length);
            }
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(Memory).GetField("_cache", Instance)!
                .GetValue(ObjectManager.Wow)!;
            cache.Value!.Remove(new IntPtr(unchecked((int)(start + 3536))));
        }
        internal void Run()
        {
            var run = (System.Action)typeof(TreeRoot).GetMethod("WorkerThread", Hidden)!.CreateDelegate(typeof(System.Action));
            run();
            Check(TreeRoot.State == TreeRootState.Stopped && Bot.Stops == 1, "worker cleanup did not execute exactly once");
            // Stop interrupts this actual owner. Consume any pending signal before
            // restoring unrelated harness state, even when no timed wait occurred.
            try { Thread.Sleep(0); } catch (ThreadInterruptedException) { }
        }
        public void Dispose()
        {
            try { Thread.Sleep(0); } catch (ThreadInterruptedException) { }
            precheck.Stop(null);
            Logging.OnLogMessage -= listener;
            Logging.FileLogging = fileLogging;
            StyxSettings.Instance.UseFrameLock = frameLock;
            TreeRoot.TicksPerSecond = treeTicks;
            foreach (var item in saved.AsEnumerable().Reverse()) item.Field.SetValue(null, item.Before);
            BotPoi.Current = poi;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = uiCulture;
            world.Dispose();
        }
    }
    private sealed class ProbeBot : BotBase
    {
        internal int Pulses, RootTicks, AfterObservation, Stops;
        internal System.Action? BeforePulse, Read;
        public override string Name => "Aura liveness replay";
        public override PulseFlags PulseFlags => (PulseFlags)0;
        public override Composite Root { get; }
        internal ProbeBot() => Root = new TreeSharp.Action(_ =>
        {
            RootTicks++;
            if (RootTicks >= 2) typeof(TreeRoot).GetField("<State>k__BackingField", Hidden)!.SetValue(null, TreeRootState.Stopping);
            return RunStatus.Success;
        });
        public override void Pulse()
        {
            if (++Pulses > 5) throw new OperationCanceledException("bounded fixture watchdog");
            BeforePulse?.Invoke(); Read?.Invoke(); AfterObservation++;
        }
        public override void Stop() => Stops++;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Failure(message); }
}
