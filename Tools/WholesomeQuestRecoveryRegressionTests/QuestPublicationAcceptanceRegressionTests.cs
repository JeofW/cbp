using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Styx;
using Styx.Logic.Profiles;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using WholesomeAQ;
using Action = System.Action;

// Extends, without replacing, the nineteen saved publication tests. Reuses their
// controlled test-process descriptor/cache fixture, never production method mocks.
// The actual host compiler, profile selector, refresh owner and running gate execute.
internal static class QuestPublicationAcceptanceRegressionTests
{
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Publication acceptance tests require Windows x86.");
        var tests = new List<(string Name, Action Test)>
        {
            ("real compiler refusal does not authorize a generated candidate", () => With(c =>
            {
                Profile? candidate = null;
                BotEvents.Profile.NewProfileLoadedDelegate reject = args =>
                {
                    candidate = args.NewProfile;
                    candidate!.CodeComposition.Batch.Add("this is deliberately invalid C#;", candidate.XmlElement);
                };
                BotEvents.Profile.OnNewOuterProfileLoaded += reject;
                try { c.Scan(); }
                finally { BotEvents.Profile.OnNewOuterProfileLoaded -= reject; }
                Check(candidate != null && candidate.CodeComposition.Batch.Errors.Length > 0,
                    "actual profile compiler did not reject the controlled invalid code");
                c.Idle(); c.Stopped();
            })),
            ("real level-selector refusal does not authorize a generated candidate", () => With(c =>
            {
                Profile? candidate = null;
                BotEvents.Profile.NewProfileLoadedDelegate reject = args =>
                {
                    candidate = args.NewProfile; candidate!.MinLevel = 99; candidate.MaxLevel = 100;
                };
                BotEvents.Profile.OnNewOuterProfileLoaded += reject;
                try { c.Scan(); }
                finally { BotEvents.Profile.OnNewOuterProfileLoaded -= reject; }
                Check(candidate != null && typeof(ProfileManager).GetField("_currentProfile", S)!.GetValue(null) == null,
                    "actual level selection did not refuse the out-of-range profile");
                c.Idle(); c.Stopped();
            })),
            ("file-write failure retains conservative prior quest-item protection", () => With(c =>
            {
                c.Scan();
                Check(c.Scheduler.ActiveQuestIds.Contains(900001), "failed publication released prior quest-item protection");
            }, "directory")),
            ("no-output preparation retains conservative prior quest-item protection", () => With(c =>
            {
                c.Scan();
                Check(c.Scheduler.ActiveQuestIds.Contains(900001), "no-output publication released prior quest-item protection");
                c.Idle(); c.Stopped();
            }, "none")),
            ("obsolete XML preparation cannot write over a replacement output", () => With(c =>
            {
                QuestScheduleResult? replacement = null;
                c.BeforeArguments(() =>
                {
                    c.Replace(); replacement = c.Scheduler.LastSchedule;
                    File.WriteAllText(c.Output, "replacement-owned-output");
                });
                c.Scan();
                Check(File.ReadAllText(c.Output) == "replacement-owned-output",
                    "obsolete scan overwrote a file owned by the replacement generation");
                Check(ReferenceEquals(replacement, c.Scheduler.LastSchedule), "obsolete scan replaced the new schedule");
            })),
            ("replacement during outer-profile notification retains its publication", () => With(c =>
            {
                QuestScheduleResult? replacement = null; string? replacementPath = null;
                BotEvents.Profile.NewProfileLoadedDelegate replace = _ =>
                {
                    c.Replace(); replacement = c.Scheduler.LastSchedule; replacementPath = c.Scheduler.CurrentProfilePath;
                };
                BotEvents.Profile.OnNewOuterProfileLoaded += replace;
                try { c.Scan(); }
                finally { BotEvents.Profile.OnNewOuterProfileLoaded -= replace; }
                Check(replacement != null && ReferenceEquals(replacement, c.Scheduler.LastSchedule)
                    && replacementPath == c.Scheduler.CurrentProfilePath, "old loader continuation overwrote replacement publication");
            })),
            ("replacement during selected-profile notification retains its publication", () => With(c =>
            {
                QuestScheduleResult? replacement = null; string? replacementPath = null;
                BotEvents.Profile.NewProfileLoadedDelegate replace = _ =>
                {
                    c.Replace(); replacement = c.Scheduler.LastSchedule; replacementPath = c.Scheduler.CurrentProfilePath;
                };
                BotEvents.Profile.OnNewProfileLoaded += replace;
                try { c.Scan(); }
                finally { BotEvents.Profile.OnNewProfileLoaded -= replace; }
                Check(replacement != null && ReferenceEquals(replacement, c.Scheduler.LastSchedule)
                    && replacementPath == c.Scheduler.CurrentProfilePath, "old selected-profile continuation overwrote replacement publication");
            })),
            ("selected-profile notification precedes authorization of running work", () => With(c =>
            {
                int events = 0, ticks = -1; RunStatus? during = null;
                BotEvents.Profile.NewProfileLoadedDelegate observe = _ =>
                {
                    events++; during = c.Root.Tick(c.Context); ticks = c.Child.Ticks;
                };
                BotEvents.Profile.OnNewProfileLoaded += observe;
                try { c.Scan(); }
                finally { BotEvents.Profile.OnNewProfileLoaded -= observe; }
                Check(events == 1, "actual selected-profile notification did not execute");
                Check(during == RunStatus.Failure && ticks == 1, "unaccepted selected profile authorized old running work");
                Check(c.Scheduler.CurrentProfilePath == c.Output && c.Scheduler.LastSchedule.Selected.Count == 1,
                    "successful publication was not retained after the host accepted it");
            })),
            ("validated-grind direct-call compatibility retains an accepted real profile", () => With(c =>
            {
                CallInstance(c.Fixture, "ClearAccepted");
                ((QuestDatabase)Get(c.Fixture, "Database")!).Quests.Clear();
                var settings = (WholesomeAQSettings)Get(c.Scheduler, "_settings")!;
                typeof(QuestScheduler).GetField("_scanThreshold", I)!.SetValue(c.Scheduler, settings.ScanMaxDistance);
                string grind = Path.Combine(Path.GetDirectoryName(c.Output)!, "validated-grind.xml");
                File.WriteAllText(grind, "<HBProfile><Name>Validated grind control</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><GrindArea><Hotspots><Hotspot X=\"10\" Y=\"10\" Z=\"10\" /></Hotspots></GrindArea></HBProfile>");
                Check(c.Scheduler.ScanAndRefresh(c.Player, grind), "validated-grind scan no longer produces an executable output");
                Check(c.Scheduler.LastSchedule.FallbackMode == QuestFallbackMode.ValidatedGrind
                    && c.Scheduler.CurrentProfilePath == grind, "validated-grind path/fallback compatibility changed");
                ProfileManager.LoadNew(grind, false);
                Check(ProfileManager.CurrentProfile != null, "actual host did not accept the validated-grind control");
            }))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS acceptance: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL acceptance assertion: " + test.Name + ": " + error); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR acceptance fixture/owner: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Publication acceptance scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual compiler/selector/load/scan/running gate; no game attached.");
        if (assertions != 0 || unexpected != 0)
            throw new InvalidOperationException("Publication acceptance regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private sealed class RunningChild : Composite
    {
        internal int Ticks, Stops;
        protected override IEnumerable<RunStatus> Execute(object context) { while (true) { Ticks++; yield return RunStatus.Running; } }
        public override void Stop(object context) { Stops++; base.Stop(context); }
    }
    private sealed class Case : IDisposable
    {
        internal readonly object Fixture;
        internal readonly QuestScheduler Scheduler;
        internal readonly WholesomeAutoQuest Bot;
        internal readonly RefreshGate Gate;
        internal readonly RefreshLease Lease;
        internal readonly GroupComposite Root;
        internal readonly RunningChild Child = new();
        internal readonly object Context = new();
        internal readonly string Output;
        internal LocalPlayer Player => (LocalPlayer)Get(Fixture, "Player")!;
        internal Case(string mode)
        {
            Fixture = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            Output = (string)Fixture.GetType().GetProperty("Output", I)!.GetValue(Fixture)!;
            string? destination = mode == "none" ? null : mode == "directory" ? (string)Get(Fixture, "Directory")! : Output;
            Scheduler = (QuestScheduler)CallInstance(Fixture, "Scheduler", destination)!;
            CallSaved("Seed", Scheduler, false);
            Bot = (WholesomeAutoQuest)CallSaved("Bot", Scheduler)!;
            Gate = (RefreshGate)CallSaved("Gate", Bot)!;
            Root = (GroupComposite)Bot.Root; Root.Children.Clear(); Root.Children.Add(Child); Root.Start(Context);
            Check(Root.Tick(Context) == RunStatus.Running && Child.Ticks == 1, "old child setup failed");
            Lease = (RefreshLease)CallSaved("Lease", Bot)!;
        }
        internal void Scan() => CallInstance(Bot, "DoScan", Scheduler, Lease);
        internal void BeforeArguments(Action action) => Player.GetType().GetField("BeforeProfileArguments", I)!.SetValue(Player, action);
        internal void Replace()
        {
            Gate.Stop(); Gate.Start(); Check(Gate.TryRequest() && Gate.Begin().HasValue, "replacement lease setup failed");
            CallSaved("Seed", Scheduler, false);
        }
        internal void Idle() => Check(Scheduler.LastSchedule.Selected.Count == 0
            && Scheduler.LastSchedule.FallbackMode != QuestFallbackMode.ValidatedGrind && Scheduler.CurrentProfilePath == null,
            "unaccepted publication retained execution authority");
        internal void Stopped() => Check(Root.Tick(Context) == RunStatus.Failure && Child.Ticks == 1 && Child.Stops > 0,
            "unaccepted publication resumed the old running child");
        public void Dispose() { Root.Stop(Context); Gate.Stop(); ((IDisposable)Fixture).Dispose(); }
    }
    private static void With(Action<Case> action, string mode = "") { using var item = new Case(mode); action(item); }
    private static object? Get(object target, string field) => target.GetType().GetField(field, I)!.GetValue(target);
    private static object? CallSaved(string method, params object?[] args) => Invoke(typeof(QuestPublicationRegressionTests).GetMethod(method, S)!, null, args);
    private static object? CallInstance(object target, string method, params object?[] args) => Invoke(target.GetType().GetMethod(method, I)!, target, args);
    private static object? Invoke(MethodInfo method, object? target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new AssertionFailure(message); }
}
