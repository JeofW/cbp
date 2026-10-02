using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Bots.Quest.QuestOrder;
using GreenMagic;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

internal static class AuraConsumerDeferralRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Failure(string text) : Exception(text) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, Action<Fixture> Test)[]
        {
            ("complete non-resting coverage admits an actual active work sample", f =>
                Check(f.Sample().IsActiveWork, "controlled live owner was not active")),
            ("unrelated missing 61988 metadata preserves actual work sampling", f =>
            {
                f.Aura(61988);
                Check(f.Sample().IsActiveWork, "unrelated metadata prevented supported rest absence observation");
            }),
            ("unrelated missing 56817 metadata preserves actual work sampling", f =>
            {
                f.Aura(56817);
                Check(f.Sample().IsActiveWork, "second unrelated metadata row suspended active work");
            }),
            ("unknown coverage cannot produce failure or death attribution", f =>
            {
                var clock = new TestRecoveryClock(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
                var progress = new WholesomeProgressMonitor(clock);
                var death = new WholesomeDeathMonitor(clock);
                var before = f.Sample();
                death.Capture(f.Behavior, f.Key, 17, new QuestWorkSample {
                    Key = f.Key, AttemptGeneration = 17, IsActiveWork = true, CombatOwnedByQuest = true,
                    ObjectiveCounts = before.ObjectiveCounts });
                progress.Sample(before);
                f.Unavailable();
                for (int step = 0; step < 600; step++)
                {
                    clock.Advance(TimeSpan.FromSeconds(2));
                    var unknown = f.Sample();
                    Check(progress.Sample(unknown).Outcomes.Count == 0, "unknown coverage poisoned recovery/quarantine");
                    death.Capture(f.Behavior, f.Key, 17, unknown);
                }
                Check(!death.TryRecordDeath(f.Behavior, f.Key, 17, out _), "unknown sample retained stale attributable combat");
            }),
            ("coverage hydration resumes sampling without stale inactive time", f =>
            {
                f.Unavailable(); Check(!f.Sample().IsActiveWork, "unknown sample was active");
                f.Aura(0); Check(f.Sample().IsActiveWork, "later known coverage remained poisoned");
            }),
            ("nonthrowing API distinguishes unknown from a known empty collection", f =>
            {
                var method = typeof(WoWUnit).GetMethod("TryGetAllAuras");
                Check(method != null, "shared aura reader has no explicit unavailable result");
                object?[] args = { null, "AuraConsumerDeferralRegressionTests" };
                Check((bool)Call(method!, ObjectManager.Me!, args)! && args[0] is System.Collections.ICollection { Count: 0 },
                    "known empty coverage was not returned as complete");
                f.Aura(61988); args[0] = null;
                Check(!(bool)Call(method!, ObjectManager.Me!, args)! && args[0] == null,
                    "unavailable metadata returned an empty or partial usable collection");
            }),
            ("nonthrowing API refuses a disappeared actor", f =>
            {
                var method = typeof(WoWUnit).GetMethod("TryGetAllAuras");
                Check(method != null, "explicit aura observation API is missing");
                var actor = new WoWUnit(0);
                object?[] args = { null, "disappeared-actor" };
                Check(!(bool)Call(method!, actor, args)! && args[0] == null, "disappeared actor became authoritative no-auras");
            })
        };
        int passed = 0, failed = 0;
        foreach (var test in cases)
        {
            try { using var fixture = new Fixture(); test.Test(fixture); passed++; Console.WriteLine("PASS aura deferral: " + test.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL aura deferral: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Aura consumer deferral: {passed}/{cases.Length}; failed={failed}; actual aura/work sample/progress/death owners; controlled test-process memory; no game attached.");
        if (failed != 0) throw new InvalidOperationException("Aura consumer deferral regressions remain");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly object driver;
        private readonly WholesomeAutoQuest bot = new();
        internal readonly ForcedQuestObjective Behavior = new(TestQuestObjective.Create(867, new WoWPoint(80, 0, 0)));
        internal readonly QuestRecoveryKey Key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
        internal Fixture()
        {
            driver = Activator.CreateInstance(typeof(AuraWorkerLivenessRegressionTests)
                .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            world = (IDisposable)driver;
            var actor = ObjectManager.Me!;
            IntPtr descriptors = new(Marshal.ReadInt32(new IntPtr(unchecked((int)(actor.BaseAddress + 8)))));
            var fields = typeof(WoWUnit).Assembly.GetTypes().Single(type => type.Name == "UnitFields");
            foreach (string name in new[] { "Health", "MaxHealth" })
            {
                int offset = Convert.ToInt32(Enum.Parse(fields, name)) * 4;
                IntPtr address = IntPtr.Add(descriptors, offset);
                Marshal.WriteInt32(address, 100);
                Cache().Remove(address);
            }
            BotPoi.Current = new BotPoi(new WoWPoint(80, 0, 0), PoiType.Quest) { Entry = 867 };
            Aura(0);
            Check(actor.IsAlive && !actor.IsGhost && StyxWoW.IsInWorld, "active-world fixture was not published");
        }
        private Dictionary<IntPtr, byte[]> Cache() => ((ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(Memory)
            .GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!).Value!;
        internal void Aura(uint id) => Call(driver.GetType().GetMethod("Aura", Hidden)!, driver, new object?[] { id });
        internal void Unavailable()
        {
            uint start = ObjectManager.Me!.BaseAddress;
            foreach (var (offset, value) in new[] { (3536, -1), (3156, 1), (3160, 1) })
            {
                var ptr = new IntPtr(unchecked((int)(start + (uint)offset)));
                Marshal.WriteInt32(ptr, value); Cache().Remove(ptr);
            }
        }
        internal QuestWorkSample Sample()
        {
            try { return (QuestWorkSample)Call(typeof(WholesomeAutoQuest).GetMethod("CreateLiveWorkSample", Hidden)!,
                bot, new object?[] { Behavior, Key, 17L })!; }
            catch (InvalidOperationException error) when (error.Message.Contains("active aura"))
            { throw new Failure("optional activity observation escaped its owner: " + error.Message); }
        }
        public void Dispose() { Behavior.Dispose(); world.Dispose(); }
    }
    private static object? Call(MethodInfo method, object owner, object?[] args)
    {
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Failure(message); }
}
