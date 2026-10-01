using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx;
using Styx.CommonBot;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;

// Actual shared pulse and BotEvents owners. Targeting callbacks and event
// observations are controlled; no game/native executor is installed.
internal static class SharedPulseObservationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    private sealed class Failure(string text) : Exception(text) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("healthy shared pulse reaches healing and event observations", f =>
            {
                f.Pulse(); Check(f.Healing == 1 && f.Events == 1, "healthy real pulse did not reach downstream owners");
            }),
            ("unavailable targeting cannot starve independent death/event checks", f =>
            {
                f.Target.Callback = () => throw new ObservationUnavailableException("auras", "active aura 61988 unavailable");
                f.Pulse(); Check(f.Healing == 1 && f.Events == 1, "target aura failure starved an independent observation owner");
            }),
            ("unavailable heal targeting cannot starve independent event checks", f =>
            {
                f.Heal.Callback = () => throw new ObservationUnavailableException("auras", "active aura 56817 unavailable");
                f.Pulse(); Check(f.Events == 1, "heal-target aura failure starved event/death checking");
            }),
            ("wrapped unavailable targeting follows the same controlled boundary", f =>
            {
                f.Target.Callback = () => throw new TargetInvocationException(new ObservationUnavailableException("auras", "active aura 61988 unavailable"));
                f.Pulse(); Check(f.Events == 1, "reflection changed the unavailable observation into global pulse failure");
            }),
            ("unavailable event checker does not suppress later independent checkers", f =>
            {
                f.Checkers.Insert(0, () => throw new ObservationUnavailableException("auras", "event aura unavailable"));
                f.Pulse(); Check(f.Events == 1, "an unavailable event checker starved later checkers");
            }),
            ("repeated unavailable checker diagnostics remain bounded", f =>
            {
                f.Checkers.Insert(0, () => throw new ObservationUnavailableException("auras", "repeat-checker-61988"));
                for (int i = 0; i < 1000; i++) BotEvents.PulseEvents();
                Check(f.Events == 1000, "checker recovery lost liveness");
                int count = f.Messages.Count(text => text.Contains("repeat-checker-61988"));
                Check(count > 0 && count < 20, $"optional checker produced {count} redundant logs");
            })
        };
        foreach (string owner in new[] { "target", "heal", "checker" })
        {
            string stage = owner;
            cases.Add(($"explicit Stop during {stage} prevents later event actions", f =>
            {
                typeof(Styx.Logic.BehaviorTree.TreeRoot).GetField("<State>k__BackingField", Hidden)!
                    .SetValue(null, Styx.Logic.BehaviorTree.TreeRootState.Running);
                Action stop = () => Styx.Logic.BehaviorTree.TreeRoot.Stop("controlled shared-pulse Stop request");
                if (stage == "target") f.Target.Callback = stop;
                else if (stage == "heal") f.Heal.Callback = stop;
                else f.Checkers.Insert(0, stop);
                try { f.Pulse(); } catch (OperationCanceledException) { } catch (ThreadInterruptedException) { }
                Check(f.Events == 0, "explicit Stop allowed a later event side effect");
            }));
        }
        foreach (bool wrapped in new[] { false, true })
        foreach (bool interrupted in new[] { false, true })
        foreach (string owner in new[] { "target", "heal", "checker" })
        {
            bool reflection = wrapped, threadStop = interrupted; string stage = owner;
            cases.Add(($"{stage} preserves {(threadStop ? "interruption" : "cancellation")} wrapped={reflection}", f =>
            {
                Exception signal = threadStop ? new ThreadInterruptedException("controlled shared-pulse Stop")
                    : new OperationCanceledException("controlled shared-pulse cancellation");
                Action fail = () => { if (reflection) throw new TargetInvocationException(signal); throw signal; };
                if (stage == "target") f.Target.Callback = fail;
                else if (stage == "heal") f.Heal.Callback = fail;
                else f.Checkers.Insert(0, fail);
                Exception? observed = null;
                try { f.Pulse(); } catch (Exception error) { observed = error; }
                Check(ReferenceEquals(observed, signal) && f.Events == 0, "Stop/cancellation was swallowed, wrapped or allowed later side effects");
            }));
        }
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { using var fixture = new Fixture(); test.Test(fixture); passed++; }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL shared observation: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR shared observation: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Shared observation pulse: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual shared pulse/event owners; controlled callbacks; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Shared observation pulse regressions");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly Targeting previousTarget;
        private readonly HealTargeting previousHeal;
        private readonly object? previousCheckers;
        private readonly Logging.LogMessageDelegate listener;
        internal readonly List<string> Messages = new();
        internal readonly List<Action> Checkers = new();
        internal readonly ProbeTarget Target = new();
        internal readonly ProbeHeal Heal = new();
        internal int Healing, Events;
        internal Fixture()
        {
            world = (IDisposable)Activator.CreateInstance(typeof(AuraWorkerLivenessRegressionTests)
                .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            previousTarget = Targeting.Instance; previousHeal = HealTargeting.Instance;
            previousCheckers = typeof(BotEvents).GetField("_eventCheckers", Hidden)!.GetValue(null);
            Checkers.Add(() => Events++);
            typeof(BotEvents).GetField("_eventCheckers", Hidden)!.SetValue(null, Checkers);
            Targeting.Instance = Target; HealTargeting.Instance = Heal;
            Heal.Callback = () => Healing++;
            listener = batch => { foreach (var row in batch) Messages.Add(row.Message); };
            Logging.OnLogMessage += listener;
        }
        internal void Pulse() => WoWPulsator.Pulse(PulseFlags.Targeting | PulseFlags.BotEvents);
        public void Dispose()
        {
            Logging.OnLogMessage -= listener;
            Targeting.Instance = previousTarget; HealTargeting.Instance = previousHeal;
            typeof(BotEvents).GetField("_eventCheckers", Hidden)!.SetValue(null, previousCheckers);
            world.Dispose();
        }
    }
    private sealed class ProbeTarget : Targeting { internal Action? Callback; public override void Pulse() => Callback?.Invoke(); }
    private sealed class ProbeHeal : HealTargeting { internal Action? Callback; public override void Pulse() => Callback?.Invoke(); }
    private static void Check(bool condition, string message) { if (!condition) throw new Failure(message); }
}
