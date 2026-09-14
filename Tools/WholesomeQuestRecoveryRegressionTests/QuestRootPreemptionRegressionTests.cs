using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Bots.Quest;
using Bots.Quest.Actions;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using WholesomeAQ;
using Action = System.Action;

// Actual raw observation, scheduler publication, host load, root and executor.
// Support/terminal leaves are controlled; eligibility changes also set the real
// combat descriptor or service POI. Step follows TreeRoot's Running lifecycle.
// Cleanup is asserted before teardown and before the next support effect.
internal static class QuestRootPreemptionRegressionTests
{
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Quest-root preemption tests require Windows x86.");
        var cases = new List<(string Name, Action Test)>
        {
            ("unchanged running root retains one nested lifetime", () => With(c =>
            {
                c.StartQuest(); c.Step(); c.Step();
                Check(c.Behavior.Body.Effects == 3 && c.Behavior.Body.Starts == 1 && c.Behavior.Body.Cleanups == 0,
                    "normal continuation restarted or cleaned the active quest");
            })),
            ("initial combat remains reachable without a quest effect", () => With(c =>
            { c.EnableCombat(); Check(c.Step() == RunStatus.Success && c.Combat.Effects == 1 && c.Behavior.Body.Effects == 0, "initial combat was denied"); })),
            ("real combat arrival preempts a running quest before the support effect", () => With(c =>
            { c.StartQuest(); c.EnableCombat(); c.ExpectSupport(c.Combat); })),
            ("combat arrival and stale raw progress still clean quest before combat", () => With(c =>
            { c.StartQuest(); c.RawProgress(1); c.EnableCombat(); c.ExpectSupport(c.Combat); c.Protected(); })),
            ("service POI arrival preempts nonexclusive running quest before service", () => With(c =>
            { c.StartQuest(); c.EnableService(); c.ExpectSupport(c.Service); })),
            ("real deferred rescan does not strand running root ahead of service", () => With(c =>
            { c.StartQuest(); c.Slot(999); c.Rescan(); c.AssertDeferred(); c.EnableService(); c.ExpectSupport(c.Service); })),
            ("denied quest remains behind the idle shield and never falls into roam", () => With(c =>
            { c.StartQuest(); c.RawProgress(1); c.ExpectIdle(); c.Step(); Check(c.Roam.Effects == 0, "denial later escaped to roam"); c.Protected(); })),
            ("valid exclusive owner retains service priority while quest continues", () => With(c =>
            {
                c.Behavior.Exclusive = true; c.StartQuest(); c.EnableService(); c.Step();
                Check(c.Service.Effects == 0 && c.Behavior.Body.Effects == 2 && c.Behavior.Body.Cleanups == 0,
                    "valid service exclusivity was erased or the running owner restarted");
            })),
            ("stale exclusive owner cannot suppress service", () => With(c =>
            { c.Behavior.Exclusive = true; c.StartQuest(); c.EnableService(); c.RawProgress(1); c.ExpectSupport(c.Service); c.Protected(); })),
            ("completed exclusive owner cannot suppress service", () => With(c =>
            { c.Behavior.Exclusive = true; c.StartQuest(); c.Behavior.Done = true; c.EnableService(); c.ExpectSupport(c.Service); })),
            ("temporarily deferred exclusive owner cannot suppress service", () => With(c =>
            { c.Behavior.Exclusive = true; c.StartQuest(); c.Behavior.Deferred = true; c.EnableService(); c.ExpectSupport(c.Service); })),
            ("Wholesome composition never borrows the shared ordinary QuestBot root", () => With(c =>
            {
                var ordinary = new QuestBot().Root;
                Check(!All(c.Root).Any(x => ReferenceEquals(x, ordinary)), "Wholesome borrowed ordinary QuestBot root state");
                Check(ReferenceEquals(ordinary, new QuestBot().Root), "ordinary QuestBot root contract changed");
            })),
            ("two Wholesome instances have disjoint executable root nodes", () => With(c =>
            {
                var other = c.NewBot(c.Scheduler); var nodes = All(c.Root).ToArray();
                Check(!All(other.Root).Any(x => nodes.Any(y => ReferenceEquals(x, y))), "instances share executable root nodes");
            })),
            ("stopping an unticked instance cannot drain another instance's running executor", () => With(c =>
            {
                var other = c.NewBot(c.Scheduler); c.Rescan(other); var root = (GroupComposite)other.Root;
                c.Configure(root, separateSupport: true); var context = new object(); root.Start(context);
                try
                {
                    Check(root.Tick(context) == RunStatus.Running && c.Behavior.Body.Effects == 1, "second instance setup failed");
                    c.Root.Start(c.Context); c.Root.Stop(c.Context);
                    Check(c.Behavior.Body.Cleanups == 0, "inactive instance stopped the other nested branch");
                    Check(root.Tick(context) == RunStatus.Running && c.Behavior.Body.Effects == 2, "other instance lost its iterator");
                }
                finally { root.Stop(context); ((RefreshGate)Get(other, "_refreshGate")!).Stop(); }
            })),
            ("stopped lifecycle still blocks support and quest work", () => With(c =>
            {
                Set(c.Bot, "_stopped", true); c.EnableCombat();
                Check(c.Step() == RunStatus.Failure && c.Combat.Effects == 0 && c.Behavior.Body.Effects == 0, "stopped lifecycle admitted work");
            })),
            ("expired refresh epoch cannot execute or fall through to roaming", () => With(c =>
            { c.StartQuest(); c.Gate.Stop(); c.Gate.Start(); c.ExpectIdle(); c.Protected(); })),
            ("pending refresh alone preserves the completed publication and running lifetime", () => With(c =>
            {
                c.StartQuest(); Check(c.Gate.TryRequest(), "pending request setup failed"); c.Step();
                Check(c.Behavior.Body.Effects == 2 && c.Behavior.Body.Starts == 1 && c.Behavior.Body.Cleanups == 0, "pending request revoked valid completed publication");
            })),
            ("quest resumes a fresh nested lifetime after completed combat", () => With(c =>
            {
                c.StartQuest(); c.EnableCombat(); c.ExpectSupport(c.Combat); c.SetCombat(false); c.Combat.Status = RunStatus.Failure;
                Check(c.Step() == RunStatus.Running && c.Behavior.Body.Effects == 2 && c.Behavior.Body.Starts == 2 && c.Behavior.Body.Cleanups == 1,
                    "retained quest failed to resume after protection");
            })),
            ("replacement publication during cleanup is retained but not ticked by old cycle", () => With(c =>
            {
                c.StartQuest(); var original = c.Behavior; ObservedBehavior? replacement = null; int callbacks = 0;
                original.Body.OnCleanup = () => { callbacks++; c.RawProgress(0); c.Rescan(); replacement = c.InstallBehavior(); };
                c.RawProgress(1); try { c.Step(); } finally { original.Body.OnCleanup = null; }
                Check(callbacks == 1 && replacement != null, "old running owner was not cleaned at admission loss");
                Check(original.Body.Effects == 1 && original.Body.Cleanups == 1 && replacement!.Body.Effects == 0 && replacement.Body.Cleanups == 0,
                    "obsolete cleanup ticked/stopped replacement work");
                c.AssertPublished(); Check(ReferenceEquals(c.Order.CurrentBehavior, replacement), "replacement behavior was discarded");
                Check(c.Step() == RunStatus.Running && replacement.Body.Effects == 1, "replacement was not admitted on the next driver cycle");
            })),
            ("cancellation from preemption cleanup propagates without combat or fallback", () => CleanupSignal(new OperationCanceledException("root cleanup cancellation"))),
            ("interruption from preemption cleanup propagates without combat or fallback", () => CleanupSignal(new ThreadInterruptedException("root cleanup interruption"))),
            ("ordinary cleanup failure cannot authorize a new support effect", () => With(c =>
            {
                c.StartQuest(); c.Behavior.Body.OnCleanup = () => throw new InvalidOperationException("ordinary root cleanup failure"); c.EnableCombat();
                try { c.Step(); } catch (InvalidOperationException) { } finally { c.Behavior.Body.OnCleanup = null; }
                Check(c.Behavior.Body.Cleanups == 1 && c.Combat.Effects == 0 && c.Roam.Effects == 0, "cleanup failure authorized fallback or skipped cleanup");
            })),
            ("observed uncertainty cannot restore authority merely by restoring raw bytes", () => With(c =>
            { c.StartQuest(); c.RawProgress(1); c.ExpectIdle(); c.RawProgress(0); c.Step(); Check(c.Behavior.Body.Effects == 1 && c.Roam.Effects == 0, "raw equality resurrected revoked publication"); })),
            ("same-path host reload is a different publication owner", () => With(c =>
            {
                c.StartQuest(); Check(ProfileManager.TryLoadNew(c.Output, false), "same-path reload was not accepted"); c.InstallBehavior(); c.Step();
                Check(c.Behavior.Body.Effects == 0 && c.Roam.Effects == 0, "same-path replacement borrowed old authorization");
            })),
            ("replacement during exclusivity observation does not authorize its quest effect", () => With(c =>
            {
                c.Behavior.Exclusive = true; c.StartQuest(); var original = c.Behavior; ObservedBehavior? replacement = null; int observations = 0;
                original.OnExclusive = () => { observations++; replacement = c.InstallBehavior(); };
                c.EnableService(); c.Step();
                Check(observations == 1 && replacement != null, "continuing root did not revisit the exclusivity owner");
                Check(original.Body.Effects == 1 && original.Body.Cleanups == 1 && replacement!.Body.Effects == 0 && replacement.Body.Cleanups == 0,
                    "old exclusivity continuation touched replacement effects or abandoned its own cleanup");
            })),
            ("cancellation at actual exclusivity observation cleans the running owner", () => ObservationSignal(new OperationCanceledException("exclusivity cancel"))),
            ("interruption at actual exclusivity observation cleans the running owner", () => ObservationSignal(new ThreadInterruptedException("exclusivity interrupt")))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { test.Test(); Console.WriteLine("PASS root preemption: " + test.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL root preemption assertion: " + test.Name + ": " + e); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR root preemption fixture/owner: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Root preemption scenarios: {cases.Count - assertions - unexpected}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual Running-root/publication/executor; counted support and cleanup; no game attached.");
        if (assertions != 0 || unexpected != 0) throw new InvalidOperationException("Root preemption regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private static void CleanupSignal(Exception signal) => With(c =>
    {
        c.StartQuest(); c.Behavior.Body.OnCleanup = () => throw signal; c.EnableCombat(); Exception? actual;
        try { actual = Catch(c.Step); } finally { c.Behavior.Body.OnCleanup = null; }
        Check(ReferenceEquals(actual, signal), "exact cleanup stop signal was not propagated");
        Check(c.Behavior.Body.Cleanups == 1 && c.Combat.Effects == 0 && c.Roam.Effects == 0, "cancellation cleanup or no-fallback contract failed");
    });
    private static void ObservationSignal(Exception signal) => With(c =>
    {
        c.Behavior.Exclusive = true; c.StartQuest(); int observations = 0;
        c.Behavior.OnExclusive = () => { observations++; throw signal; }; c.EnableService(); Exception? actual = Catch(c.Step);
        Check(observations == 1, "actual exclusivity observation was not invoked exactly once");
        Check(ReferenceEquals(actual, signal) && c.Behavior.Body.Cleanups == 1 && c.Combat.Effects == 0 && c.Service.Effects == 0 && c.Roam.Effects == 0,
            "observation cancellation was swallowed or authorized a fallback");
    });
    private static Exception? Catch(Func<RunStatus> run) { try { run(); return null; } catch (Exception e) { return e; } }

    private sealed class EffectLeaf : Composite
    {
        internal int Effects, Starts, Cleanups;
        internal readonly List<string> Events;
        internal Action? OnCleanup;
        internal EffectLeaf(List<string> events) { Events = events; }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            Starts++;
            try { while (true) { Effects++; Events.Add("quest"); yield return RunStatus.Running; } }
            finally { Cleanups++; Events.Add("cleanup"); var callback = OnCleanup; OnCleanup = null; callback?.Invoke(); }
        }
    }
    private sealed class ProbeLeaf : Composite
    {
        internal int Effects;
        internal RunStatus Status = RunStatus.Failure;
        private readonly string name;
        private readonly List<string> events;
        internal ProbeLeaf(string name, List<string> events) { this.name = name; this.events = events; }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            if (Status != RunStatus.Failure) { Effects++; events.Add(name); }
            yield return Status;
        }
    }
    private sealed class ObservedBehavior : ForcedBehavior
    {
        internal readonly EffectLeaf Body;
        internal bool Exclusive, Done, Deferred;
        internal Action? OnExclusive;
        internal ObservedBehavior(List<string> events) { Body = new EffectLeaf(events); }
        public override bool IsDone => Done;
        public override bool IsExecutionDeferred => Deferred;
        public override bool SuppressServiceBehavior { get { var callback = OnExclusive; OnExclusive = null; callback?.Invoke(); return Exclusive; } }
        protected override Composite CreateBehavior() => Body;
    }
    private sealed class Case : IDisposable
    {
        private readonly object fixture;
        private readonly FieldInfo sharedRoot = typeof(QuestBot).GetField("rootBehavior", S)!;
        private readonly object? previousRoot;
        private readonly OrderNodeCollection previousNodes;
        private readonly ForcedBehavior? previousBehavior;
        private readonly BotPoi previousPoi;
        internal readonly List<string> Events = new();
        internal readonly QuestScheduler Scheduler = null!;
        internal readonly WholesomeAutoQuest Bot = null!;
        internal readonly RefreshGate Gate = null!;
        internal readonly GroupComposite Root = null!;
        internal readonly Bots.Quest.QuestOrder.QuestOrder Order;
        internal readonly ProbeLeaf Combat, Service, Roam;
        internal readonly object Context = new();
        internal ObservedBehavior Behavior = null!;
        internal readonly string Output;
        internal LocalPlayer Player => (LocalPlayer)Get(fixture, "Player")!;
        private uint Descriptor => (uint)Get(fixture, "descriptor")!;
        internal Case()
        {
            fixture = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            Order = QuestState.Instance.Order; previousNodes = Order.Nodes; previousBehavior = Order.CurrentBehavior;
            previousRoot = sharedRoot.GetValue(null); previousPoi = BotPoi.Current;
            Output = (string)fixture.GetType().GetProperty("Output", I)!.GetValue(fixture)!;
            Combat = new ProbeLeaf("combat", Events); Service = new ProbeLeaf("service", Events); Roam = new ProbeLeaf("roam", Events) { Status = RunStatus.Success };
            try
            {
                BotPoi.Current = new BotPoi(PoiType.None);
                Scheduler = (QuestScheduler)Call(fixture, "Scheduler", Output)!;
                Bot = NewBot(Scheduler); Gate = (RefreshGate)Get(Bot, "_refreshGate")!; Gate.Start(); Rescan(); AssertPublished();
                sharedRoot.SetValue(null, null); Root = (GroupComposite)Bot.Root; Configure(Root); InstallBehavior();
            }
            catch { Dispose(); throw; }
        }
        internal WholesomeAutoQuest NewBot(QuestScheduler scheduler) => (WholesomeAutoQuest)Invoke(typeof(QuestPublicationRegressionTests).GetMethod("Bot", S)!, null, scheduler)!;
        internal void Configure(GroupComposite root, bool separateSupport = false)
        {
            var tree = All(root).OfType<PrioritySelector>().Single(g => g.Children.Count == 7 && All(g.Children[5]).OfType<ForcedBehaviorExecutor>().Any());
            tree.Children[0] = new ProbeLeaf("death", Events); tree.Children[1] = separateSupport ? new ProbeLeaf("combat", Events) : Combat;
            tree.Children[2] = new ProbeLeaf("loot", Events); tree.Children[3] = new ProbeLeaf("targeting", Events); tree.Children[6] = separateSupport ? new ProbeLeaf("roam", Events) { Status = RunStatus.Success } : Roam;
            var service = (GroupComposite)tree.Children[4]; Check(service is Decorator && service.Children.Count == 1, "actual service predicate topology changed"); service.Children[0] = separateSupport ? new ProbeLeaf("service", Events) : Service;
        }
        internal ObservedBehavior InstallBehavior()
        {
            Check(ProfileManager.CurrentProfile?.QuestOrder?.Count > 0, "host profile has no actual order");
            Order.Nodes = new OrderNodeCollection(); Order.Nodes.Add(ProfileManager.CurrentProfile!.QuestOrder[0]);
            Behavior = new ObservedBehavior(Events); Order.CurrentBehavior = Behavior; return Behavior;
        }
        internal void Rescan(WholesomeAutoQuest? bot = null)
        {
            bot ??= Bot; var gate = (RefreshGate)Get(bot, "_refreshGate")!;
            Check(gate.TryRequest(), "real refresh request failed"); var lease = gate.Begin() ?? throw new AssertionFailure("real lease absent");
            try { Call(bot, "DoScan", Scheduler, lease); } finally { gate.Complete(lease); }
        }
        internal RunStatus Step() { if (Root.LastStatus != RunStatus.Running) Root.Start(Context); return Root.Tick(Context); }
        internal void StartQuest() => Check(Step() == RunStatus.Running && Behavior.Body.Effects == 1 && Behavior.Body.Cleanups == 0, "real root did not start quest control");
        internal void EnableCombat() { SetCombat(true); Check(Player.Combat, "actual combat descriptor did not change"); Combat.Status = RunStatus.Success; }
        internal void EnableService() { BotPoi.Current = new BotPoi(new WoWPoint(10, 10, 10), PoiType.Sell); Service.Status = RunStatus.Success; }
        internal void ExpectSupport(ProbeLeaf support)
        {
            int before = Behavior.Body.Effects; int supportBefore = support.Effects; int from = Events.Count; Step();
            Check(support.Effects == supportBefore + 1, "higher-priority support was not selected by a continuing root");
            Check(Behavior.Body.Effects == before && Behavior.Body.Cleanups == 1 && Roam.Effects == 0, "preemption repeated quest, missed cleanup, or roamed");
            var order = Events.Skip(from).ToArray(); Check(order.Length >= 2 && order[0] == "cleanup" && order[1] == (ReferenceEquals(support, Combat) ? "combat" : "service"), "support effect preceded old-owner cleanup");
        }
        internal void ExpectIdle()
        {
            int effects = Behavior.Body.Effects;
            Check(Step() == RunStatus.Success && Behavior.Body.Effects == effects && Behavior.Body.Cleanups == 1 && Roam.Effects == 0, "denied quest did not cleanly yield to the idle shield");
        }
        internal void AssertPublished() => Check(Scheduler.LastSchedule.Selected.Any(q => q.QuestId == 867) && Scheduler.CurrentProfilePath == Output && ProfileManager.XmlLocation == Output, "real publication failed: " + Scheduler.LastStatus);
        internal void AssertDeferred() => Check(Scheduler.LastSchedule.Selected.Count == 0 && Scheduler.CurrentProfilePath == null, "failed scan did not revoke publication");
        internal void Protected() => Check(Scheduler.ActiveQuestIds.Contains(867), "uncertain publication released item protection");
        internal void RawProgress(uint value) => Write(Descriptor + 640, value);
        internal void Slot(uint value) { Write(Descriptor + 632, value); Write(Descriptor + 636, value == 0 ? 0 : (uint)WoWDescriptorQuestFlags.Completed); }
        internal void SetCombat(bool active)
        {
            Type fields = typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFields");
            Type flags = typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFlags");
            Write(Descriptor + Convert.ToUInt32(Enum.Parse(fields, "Flags")) * 4, active ? Convert.ToUInt32(Enum.Parse(flags, "InCombat")) : 0);
        }
        private void Write(uint address, uint value)
        {
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)Get(fixture, "cache")!; var pointer = new IntPtr(unchecked((int)address));
            cache.Value!.Remove(pointer); Marshal.WriteInt32(pointer, unchecked((int)value));
        }
        public void Dispose()
        {
            try { Root?.Stop(Context); Behavior?.Branch.Stop(Context); }
            finally { Gate?.Stop(); Order.Nodes = previousNodes; Order.CurrentBehavior = previousBehavior; sharedRoot.SetValue(null, previousRoot); BotPoi.Current = previousPoi; ((IDisposable)fixture).Dispose(); }
        }
    }
    private static IEnumerable<Composite> All(Composite root)
    {
        yield return root;
        if (root is GroupComposite group) foreach (var child in group.Children) if (child != null) foreach (var item in All(child)) yield return item;
    }
    private static void With(Action<Case> run) { using var c = new Case(); run(c); }
    private static object? Get(object owner, string name) => owner.GetType().GetField(name, I)!.GetValue(owner);
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, I)!.SetValue(owner, value);
    private static object? Call(object owner, string name, params object?[] args) => Invoke(owner.GetType().GetMethod(name, I)!, owner, args);
    private static object? Invoke(MethodInfo method, object? owner, params object?[] args)
    {
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
