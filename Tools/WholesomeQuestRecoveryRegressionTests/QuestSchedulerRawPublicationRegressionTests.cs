using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using Styx;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using WholesomeAQ;
using Action = System.Action;

// Actual allocated Memory/QuestLog -> scheduler -> XML/file/host -> running gate.
// Reuses only the retained external-world fixture; no production method is replaced.
internal static class QuestSchedulerRawPublicationRegressionTests
{
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string s) : base(s) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Scheduler raw-publication tests require Windows x86.");
        var tests = new List<(string Name, Action Test)>
        {
            ("complete raw observation publishes a real accepted turn-in", () => With(c => { c.Scan(); c.Published(); })),
            ("occupied missing metadata cannot become empty-log permission", () => With(c => { c.Slot(0, 999); c.Scan(); c.Deferred(); })),
            ("occupied missing metadata vetoes validated grind", () => With(c =>
            {
                c.Slot(0, 999); c.Database.Quests.Clear(); c.ScanWithGrind(c.GrindFile()); c.Deferred();
            })),
            ("duplicate occupied identities cannot publish a turn-in", () => With(c => { c.Slot(1, 867); c.Scan(); c.Deferred(); })),
            ("invalid occupied identity cannot be silently omitted", () => With(c => { c.Slot(1, uint.MaxValue); c.Scan(); c.Deferred(); })),
            ("unreadable descriptor is unknown rather than empty", () => With(c => { c.Write(c.Player.BaseAddress + 8, 1); c.Scan(); c.Deferred(); })),
            ("zero raw owner GUID cannot authorize selected work", () => With(c => { c.Guid(0, 0); c.Scan(); c.Deferred(); })),
            ("mismatched raw owner GUIDs cannot authorize selected work", () => With(c => { c.Guid(123, 456); c.Scan(); c.Deferred(); })),
            ("same-count swap during actual navigation prevents output", () => With(c =>
            {
                c.BeforeNavigation(() => c.Slot(0, 999)); c.Scan(); c.ReachedNavigation(); c.Deferred();
            })),
            ("progress changes during navigation prevent output", () => With(c =>
            {
                c.BeforeNavigation(() => c.Write(c.Descriptor + 640, 1)); c.Scan(); c.ReachedNavigation(); c.Deferred();
            })),
            ("raw owner replacement during navigation prevents output", () => With(c =>
            {
                c.BeforeNavigation(() => c.Guid(456, 456)); c.Scan(); c.ReachedNavigation(); c.Deferred();
            })),
            ("same-count swap during XML arguments prevents file write", () => With(c =>
            {
                c.BeforeArguments(() => c.Slot(0, 999)); c.Scan(); c.ReachedArguments(); c.Deferred();
            })),
            ("progress changes during XML arguments prevent file write", () => With(c =>
            {
                c.BeforeArguments(() => c.Write(c.Descriptor + 640, 1)); c.Scan(); c.ReachedArguments(); c.Deferred();
            })),
            ("outer profile notification cannot grant changed-log authority", () => With(c =>
            {
                int events = 0;
                BotEvents.Profile.NewProfileLoadedDelegate change = _ => { events++; c.Slot(0, 999); };
                BotEvents.Profile.OnNewOuterProfileLoaded += change;
                try { c.Scan(); } finally { BotEvents.Profile.OnNewOuterProfileLoaded -= change; }
                Check(events == 1, "actual outer-profile event was not reached"); c.Deferred(expectNoFile: false);
            })),
            ("selected profile notification cannot grant changed-log authority", () => With(c =>
            {
                int events = 0;
                BotEvents.Profile.NewProfileLoadedDelegate change = _ => { events++; c.Write(c.Descriptor + 640, 1); };
                BotEvents.Profile.OnNewProfileLoaded += change;
                try { c.Scan(); } finally { BotEvents.Profile.OnNewProfileLoaded -= change; }
                Check(events == 1, "actual selected-profile event was not reached"); c.Deferred(expectNoFile: false);
            })),
            ("uncertainty stops an already-running validated-grind child", () => With(c =>
            {
                c.Slot(1, 867); c.Scan(); c.Deferred();
            }, oldGrind: true)),
            ("raw change from an obsolete scan preserves replacement output", () => With(c =>
            {
                QuestScheduleResult? replacement = null;
                c.BeforeArguments(() => { c.Replace(); replacement = c.Scheduler.LastSchedule; c.Slot(0, 999); File.WriteAllText(c.Output, "replacement-owned-output"); });
                c.Scan(); c.ReachedArguments();
                Check(replacement != null && ReferenceEquals(replacement, c.Scheduler.LastSchedule)
                    && c.Scheduler.CurrentProfilePath == "controlled-prior-publication.xml"
                    && File.ReadAllText(c.Output) == "replacement-owned-output", "obsolete raw change overwrote/revoked replacement");
            })),
            ("replacement from a loader event retains its own authority", () => With(c =>
            {
                QuestScheduleResult? replacement = null;
                BotEvents.Profile.NewProfileLoadedDelegate replace = _ => { c.Replace(); replacement = c.Scheduler.LastSchedule; c.Slot(0, 999); };
                BotEvents.Profile.OnNewOuterProfileLoaded += replace;
                try { c.Scan(); } finally { BotEvents.Profile.OnNewOuterProfileLoaded -= replace; }
                Check(replacement != null && ReferenceEquals(replacement, c.Scheduler.LastSchedule)
                    && c.Scheduler.CurrentProfilePath == "controlled-prior-publication.xml", "late raw check revoked a replacement lease");
            })),
            ("interruption propagates without old-child authorization", () => With(c =>
            {
                var error = new ThreadInterruptedException("scheduler publication interrupted"); c.BeforeArguments(() => throw error);
                SameException(c.Scan, error); c.Deferred();
            })),
            ("cancellation propagates and releases the pending refresh", () => With(c =>
            {
                c.Gate.Stop(); c.Gate.Start(); Check(c.Gate.TryRequest(), "pending request failed");
                var error = new OperationCanceledException("scheduler publication cancelled"); c.BeforeArguments(() => throw error);
                SameException(() => Call(c.Bot, "RunPendingRefresh"), error);
                Check(c.Gate.TryRequest() && c.Gate.Begin().HasValue, "cancelled scan retained refresh lease"); c.Deferred();
            })),
            ("a fresh complete scan can recover after uncertainty", () => With(c =>
            {
                c.Slot(0, 999); c.Scan(); c.Deferred(); c.Slot(0, 867); c.Renew(); c.Scan(); c.Published();
            })),
            ("genuinely empty raw log does not reuse the old selected profile", () => With(c =>
            {
                c.Slot(0, 0); c.Database.Quests.Clear(); c.Scan();
                Check(c.Scheduler.LastSchedule.Selected.Count == 0 && c.Scheduler.CurrentProfilePath == null
                    && !File.Exists(c.Output), "empty complete scan reused old output"); c.Stopped();
            })),
            ("complete empty raw log preserves validated-grind compatibility", () => With(c =>
            {
                c.Slot(0, 0); c.Database.Quests.Clear(); string grind = c.GrindFile();
                Check(c.ScanWithGrind(grind), "complete validated-grind scan failed");
                Check(c.Scheduler.LastSchedule.FallbackMode == QuestFallbackMode.ValidatedGrind
                    && c.Scheduler.CurrentProfilePath == grind && ProfileManager.CurrentProfile != null,
                    "complete validated-grind output was not accepted");
            }))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS scheduler raw publication: " + test.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL scheduler raw publication assertion: " + test.Name + ": " + e); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR scheduler raw publication fixture/owner: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Scheduler raw-publication scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual raw memory/scan/XML/load/running gate; no game attached.");
        if (assertions != 0 || unexpected != 0)
            throw new InvalidOperationException("Scheduler raw-publication regressions: assertions=" + assertions + "; unexpected=" + unexpected);
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
        internal RefreshLease Lease;
        internal readonly GroupComposite Root;
        internal readonly RunningChild Child = new();
        internal readonly object Context = new();
        internal readonly string Output;
        internal uint Descriptor => (uint)Get(Fixture, "descriptor")!;
        internal LocalPlayer Player => (LocalPlayer)Get(Fixture, "Player")!;
        internal QuestDatabase Database => (QuestDatabase)Get(Fixture, "Database")!;
        private int navigationCalls, argumentCalls;
        internal Case(bool oldGrind)
        {
            Fixture = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            try
            {
                Check(Player.QuestLog.CaptureSnapshot().IsComplete, "raw allocated fixture must start complete");
                Output = (string)Fixture.GetType().GetProperty("Output", I)!.GetValue(Fixture)!;
                Scheduler = (QuestScheduler)Call(Fixture, "Scheduler", Output)!;
                Saved("Seed", Scheduler, oldGrind); Bot = (WholesomeAutoQuest)Saved("Bot", Scheduler)!;
                Gate = (RefreshGate)Saved("Gate", Bot)!;
                Root = (GroupComposite)Bot.Root; Root.Children.Clear(); Root.Children.Add(Child); Root.Start(Context);
                Check(Root.Tick(Context) == RunStatus.Running && Child.Ticks == 1, "old running child setup failed");
                Lease = (RefreshLease)Saved("Lease", Bot)!;
            }
            catch { ((IDisposable)Fixture).Dispose(); throw; }
        }
        internal void Write(uint address, uint value)
        {
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)Get(Fixture, "cache")!;
            cache.Value!.Remove(new IntPtr(unchecked((int)address)));
            Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value));
        }
        internal void Slot(int slot, uint id) { Write(Descriptor + 632 + (uint)slot * 20, id); Write(Descriptor + 636 + (uint)slot * 20, id == 0 ? 0 : (uint)WoWDescriptorQuestFlags.Completed); }
        internal void Guid(uint objectGuid, uint descriptorGuid) { Write(Player.BaseAddress + 48, objectGuid); Write(Descriptor, descriptorGuid); }
        internal void BeforeNavigation(Action action) => Set(Fixture, "BeforeNavigation", (Action)(() => { Set(Fixture, "BeforeNavigation", null); navigationCalls++; action(); }));
        internal void BeforeArguments(Action action) => Set(Player, "BeforeProfileArguments", (Action)(() => { argumentCalls++; action(); }));
        internal void ReachedNavigation() => Check(navigationCalls == 1, "actual navigation observation not reached");
        internal void ReachedArguments() => Check(argumentCalls == 1, "actual XML argument observation not reached");
        internal void Scan() => Call(Bot, "DoScan", Scheduler, Lease);
        internal void Renew() { Gate.Stop(); Gate.Start(); Check(Gate.TryRequest(), "renewal request failed"); Lease = Gate.Begin() ?? throw new AssertionFailure("renewal failed"); }
        internal void Replace() { Renew(); Saved("Seed", Scheduler, false); }
        internal string GrindFile()
        {
            var settings = (WholesomeAQSettings)Get(Scheduler, "_settings")!; Set(Scheduler, "_scanThreshold", settings.ScanMaxDistance);
            string path = Path.Combine(Path.GetDirectoryName(Output)!, "validated-grind.xml");
            File.WriteAllText(path, "<HBProfile><Name>Validated grind control</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><GrindArea><Hotspots><Hotspot X=\"10\" Y=\"10\" Z=\"10\" /></Hotspots></GrindArea></HBProfile>");
            return path;
        }
        internal bool ScanWithGrind(string path)
        {
            var lease = Lease;
            return (bool)Call(Scheduler, "ScanAndRefresh", Player, path,
                (Func<Action, bool>)(apply => Gate.TryApply(lease, apply)),
                (Func<string, bool>)(file => ProfileManager.TryLoadNew(file, false)))!;
        }
        internal void Published()
        {
            Check(File.Exists(Output) && XDocument.Load(Output).Descendants("TurnIn").Any(e => (string?)e.Attribute("QuestId") == "867"), "actual turn-in output absent");
            Check(Scheduler.LastSchedule.Selected.Any(q => q.QuestId == 867 && q.Stage == QuestWorkStage.TurnIn)
                && Scheduler.CurrentProfilePath == Output && ProfileManager.XmlLocation == Output && ProfileManager.CurrentProfile != null,
                "complete real profile was not published/loaded");
        }
        internal void Deferred(bool expectNoFile = true)
        {
            Check(Scheduler.LastSchedule.Selected.Count == 0 && Scheduler.LastSchedule.FallbackMode == QuestFallbackMode.TimedIdle
                && Scheduler.CurrentProfilePath == null, "unknown/changed observation retained executable work instead of timed idle");
            Check(Scheduler.ActiveQuestIds.Contains(900001), "uncertainty released prior item protection");
            Check(Scheduler.EarliestRetryUtc.HasValue && Scheduler.EarliestRetryUtc > DateTime.UtcNow.AddSeconds(-1)
                && Scheduler.EarliestRetryUtc < DateTime.UtcNow.AddSeconds(20), "no bounded fresh-observation retry");
            if (expectNoFile) Check(!File.Exists(Output), "untrusted observation wrote profile output");
            Stopped();
        }
        internal void Stopped() => Check(Root.Tick(Context) == RunStatus.Failure && Child.Ticks == 1 && Child.Stops > 0, "old running child regained authorization");
        public void Dispose() { Root.Stop(Context); Gate.Stop(); ((IDisposable)Fixture).Dispose(); }
    }
    private static void With(Action<Case> action, bool oldGrind = false) { using var c = new Case(oldGrind); action(c); }
    private static object? Get(object target, string field) => target.GetType().GetField(field, I)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, I)!.SetValue(target, value);
    private static object? Saved(string name, params object?[] args) => Invoke(typeof(QuestPublicationRegressionTests).GetMethod(name, S)!, null, args);
    private static object? Call(object target, string name, params object?[] args) => Invoke(target.GetType().GetMethod(name, I)!, target, args);
    private static object? Invoke(MethodInfo method, object? target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static void SameException(Action action, Exception expected)
    {
        try { action(); throw new AssertionFailure("cancellation did not propagate"); }
        catch (Exception actual) when (ReferenceEquals(actual, expected)) { }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new AssertionFailure(message); }
}
