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
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using WholesomeAQ;
using Action = System.Action;

// Actual allocated raw memory -> successful DoScan/ProfileManager publication ->
// Wholesome root -> QuestBot selector/service predicate -> ForcedBehaviorExecutor.
// Only external combat/service/world leaves and the forced behavior's terminal body
// are controlled. No production gate, executor, observation reader or scheduler is
// replaced. This is offline owner execution, not combat/game/session acceptance.
internal static class QuestActionFreshnessRegressionTests
{
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string s) : base(s) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Quest-action freshness tests require Windows x86.");
        var tests = new List<(string Name, Action Test)>
        {
            ("unchanged completed publication continues the actual running executor", () => With(c =>
            {
                c.StartQuest(); Check(c.Tick() == RunStatus.Running && c.Behavior.Body.Ticks == 2,
                    "fresh running executor did not continue");
            })),
            ("accepted quest removal after publication vetoes the next quest effect", () => With(c =>
            { c.StartQuest(); c.Slot(0, 0); c.NoMoreEffects(); })),
            ("same-count accepted replacement after publication vetoes the next effect", () => With(c =>
            { c.StartQuest(); c.Slot(0, 999); c.NoMoreEffects(); })),
            ("raw objective progress after publication vetoes the next effect", () => With(c =>
            { c.StartQuest(); c.Write(c.Descriptor + 640, 1); c.NoMoreEffects(); })),
            ("completion flag change after publication vetoes the old turn-in effect", () => With(c =>
            { c.StartQuest(); c.Write(c.Descriptor + 636, 0); c.NoMoreEffects(); })),
            ("different raw player GUID after publication vetoes the next effect", () => With(c =>
            { c.StartQuest(); c.Guid(456, 456); c.NoMoreEffects(); })),
            ("mismatched raw player GUID after publication cannot run the old effect", () => With(c =>
            { c.StartQuest(); c.Guid(123, 456); c.NoMoreEffects(); })),
            ("unreadable descriptor after publication is unknown rather than permission", () => With(c =>
            { c.StartQuest(); c.Write(c.Player.BaseAddress + 8, 1); c.NoMoreEffects(); })),
            ("replacement player object with the same address cannot borrow publication", () => With(c =>
            { c.StartQuest(); ObjectManager.Me = new LocalPlayer(c.Player.BaseAddress); c.NoMoreEffects(); })),
            ("observed missing current player vetoes an already-running quest effect", () => With(c =>
            { c.StartQuest(); ObjectManager.Me = null; c.NoMoreEffects(); })),
            ("observed uncertainty does not regain permission by raw equality alone", () => With(c =>
            {
                c.StartQuest(); c.Slot(0, 999); c.NoMoreEffects();
                c.Slot(0, 867); c.NoMoreEffects();
            })),
            ("stopping the publishing refresh lease invalidates its quest effect", () => With(c =>
            { c.StartQuest(); c.Gate.Stop(); c.NoMoreEffects(); })),
            ("new lifecycle epoch cannot reuse the prior published quest effect", () => With(c =>
            { c.StartQuest(); c.Gate.Stop(); c.Gate.Start(); c.NoMoreEffects(); })),
            ("a newer refresh run cannot borrow the previous publication", () => With(c =>
            {
                c.StartQuest(); Check(c.Gate.TryRequest(), "replacement request refused");
                var replacement = c.Gate.Begin(); Check(replacement.HasValue, "replacement lease absent");
                c.NoMoreEffects(); Check(c.Gate.IsCurrent(replacement!.Value), "old execution revoked the replacement lease");
            })),
            ("actual host profile replacement cannot borrow an old publication", () => With(c =>
            {
                string replacement = c.ReplacementProfile();
                Check(ProfileManager.TryLoadNew(replacement, false) && ProfileManager.XmlLocation == replacement,
                    "actual replacement profile was not accepted");
                c.InstallBehavior(); c.NoInitialEffects();
            })),
            ("cancelling an actual rescan blocks old quest effects but retains combat", () => With(c =>
            {
                c.StartQuest(); var error = new OperationCanceledException("quest-action rescan cancelled");
                c.BeforeNavigation(() => throw error); Check(c.Gate.TryRequest(), "cancelled rescan request refused");
                try { Call(c.Bot, "RunPendingRefresh"); throw new AssertionFailure("cancellation did not propagate"); }
                catch (Exception actual) when (ReferenceEquals(actual, error)) { }
                c.AssertDeferred(); c.NoMoreEffects(); c.SupportRuns(combat: true);
            })),
            ("requesting a refresh alone does not suppress unchanged published work", () => With(c =>
            {
                c.StartQuest(); Check(c.Gate.TryRequest(), "normal refresh request refused");
                Check(c.Tick() == RunStatus.Running && c.Behavior.Body.Ticks == 2,
                    "a pending request alone blocked a still-current publication");
            })),
            ("a new successful real publication can execute after a failed scan", () => With(c =>
            {
                c.Slot(0, 999); c.Rescan(); c.AssertDeferred();
                c.Slot(0, 867); c.Rescan(); c.AssertPublished(); c.InstallBehavior(); c.StartQuest();
            })),
            ("normal combat control is reachable without executing a quest leaf", () => With(c => c.SupportRuns(combat: true))),
            ("normal service control is reachable through the actual service predicate", () => With(c => c.SupportRuns(combat: false))),
            ("a deferred quest schedule must not suppress the combat branch", () => With(c =>
            { c.Slot(0, 999); c.Rescan(); c.AssertDeferred(); c.SupportRuns(combat: true); })),
            ("a deferred quest schedule must not suppress ordinary service work", () => With(c =>
            { c.Slot(0, 999); c.Rescan(); c.AssertDeferred(); c.SupportRuns(combat: false); })),
            ("actual refresh owner retains a pending scan during combat", () => With(c =>
            {
                var publication = c.Scheduler.LastSchedule;
                c.SetCombat(true); Check(c.Player.Combat, "actual combat descriptor not set");
                Check(c.Gate.TryRequest(), "combat refresh request refused");
                Call(c.Bot, "RunPendingRefresh");
                Check(ReferenceEquals(publication, c.Scheduler.LastSchedule), "refresh ran during combat");
                var pending = c.Gate.Begin(); Check(pending.HasValue, "combat discarded the pending refresh");
                c.Gate.Complete(pending!.Value); c.SupportRuns(combat: true);
            })),
            ("stopped bot control blocks both combat and quest execution", () => With(c =>
            {
                Set(c.Bot, "_stopped", true); c.Combat.Status = RunStatus.Success;
                c.Root.Start(c.Context); Check(c.Tick() == RunStatus.Failure && c.Combat.Ticks == 0
                    && c.Behavior.Body.Ticks == 0, "stopped lifecycle admitted work");
            })),
            ("normal completed behavior advances the real order without another effect", () => With(c =>
            {
                c.StartQuest(); c.Behavior.Body.Finish = true; c.Tick();
                c.Behavior.Done = true; c.Root.Stop(c.Context); c.Root.Start(c.Context); c.Tick();
                Check(c.Order.Nodes.Count == 0 && c.Behavior.Body.Ticks == 1,
                    "normal completion did not advance without repeating its effect");
            }))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS quest-action freshness: " + test.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL quest-action freshness assertion: " + test.Name + ": " + e); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR quest-action freshness fixture/owner: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Quest-action freshness scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual publication/root/forced executor; controlled support leaves; no game attached.");
        if (assertions != 0 || unexpected != 0)
            throw new InvalidOperationException("Quest-action freshness regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private sealed class SupportLeaf : Composite
    {
        internal int Ticks;
        internal RunStatus Status = RunStatus.Failure;
        protected override IEnumerable<RunStatus> Execute(object context) { Ticks++; yield return Status; }
    }
    private sealed class EffectLeaf : Composite
    {
        internal int Ticks;
        internal bool Finish;
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            while (!Finish) { Ticks++; yield return RunStatus.Running; }
            yield return RunStatus.Success;
        }
    }
    private sealed class ObservedBehavior : ForcedBehavior
    {
        internal readonly EffectLeaf Body = new();
        internal bool Done;
        public override bool IsDone => Done;
        protected override Composite CreateBehavior() => Body;
    }
    private sealed class Case : IDisposable
    {
        private readonly object fixture;
        private readonly FieldInfo sharedRoot = typeof(QuestBot).GetField("rootBehavior", S)!;
        private readonly object? previousRoot;
        private readonly OrderNodeCollection previousNodes;
        private readonly ForcedBehavior? previousBehavior;
        private bool rootStarted;
        internal readonly QuestScheduler Scheduler;
        internal readonly WholesomeAutoQuest Bot;
        internal readonly RefreshGate Gate;
        internal readonly GroupComposite Root;
        internal readonly Bots.Quest.QuestOrder.QuestOrder Order;
        internal readonly SupportLeaf Combat = new(), Service = new();
        internal readonly object Context = new();
        internal ObservedBehavior Behavior = new();
        internal LocalPlayer Player => (LocalPlayer)Get(fixture, "Player")!;
        internal uint Descriptor => (uint)Get(fixture, "descriptor")!;
        private string Output => (string)fixture.GetType().GetProperty("Output", I)!.GetValue(fixture)!;

        internal Case()
        {
            fixture = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            Order = QuestState.Instance.Order;
            previousNodes = Order.Nodes; previousBehavior = Order.CurrentBehavior;
            previousRoot = sharedRoot.GetValue(null);
            try
            {
                Check(Player.QuestLog.CaptureSnapshot().IsComplete, "raw fixture must start complete");
                Scheduler = (QuestScheduler)Call(fixture, "Scheduler", Output)!;
                Bot = (WholesomeAutoQuest)Saved("Bot", Scheduler)!;
                Gate = (RefreshGate)Saved("Gate", Bot)!; Gate.Start();
                Rescan(); AssertPublished();
                sharedRoot.SetValue(null, null);
                Root = (GroupComposite)Bot.Root;
                var tree = Descendants(Root).OfType<PrioritySelector>().Single(g => g.Children.Count == 7
                    && Descendants(g.Children[5]).OfType<ForcedBehaviorExecutor>().Any());
                Check(Descendants(tree.Children[5]).OfType<ForcedBehaviorExecutor>().Count() == 1,
                    "actual forced-behavior executor missing from the quest branch");
                // External support leaves only. Retain the real service Decorator and
                // the entire quest-order selector/executor, rather than replacing Root.
                tree.Children[0] = new SupportLeaf(); tree.Children[1] = Combat;
                tree.Children[2] = new SupportLeaf(); tree.Children[3] = new SupportLeaf();
                tree.Children[6] = new SupportLeaf();
                var service = (GroupComposite)tree.Children[4];
                Check(service is Decorator && service.Children.Count == 1, "actual service predicate shape changed");
                service.Children[0] = Service;
                InstallBehavior();
            }
            catch { Dispose(); throw; }
        }
        internal void InstallBehavior()
        {
            Check(ProfileManager.CurrentProfile?.QuestOrder?.Count > 0, "actual loaded quest order absent");
            Order.Nodes = new OrderNodeCollection(); Order.Nodes.Add(ProfileManager.CurrentProfile!.QuestOrder[0]);
            Behavior = new ObservedBehavior(); Order.CurrentBehavior = Behavior;
        }
        internal void Rescan()
        {
            Check(Gate.TryRequest(), "next real scan request refused");
            RefreshLease lease = Gate.Begin() ?? throw new AssertionFailure("next real scan lease unavailable");
            try { Call(Bot, "DoScan", Scheduler, lease); }
            finally { Gate.Complete(lease); }
        }
        internal void AssertPublished() => Check(File.Exists(Output)
            && Scheduler.LastSchedule.Selected.Any(q => q.QuestId == 867)
            && Scheduler.CurrentProfilePath == Output && ProfileManager.XmlLocation == Output
            && ProfileManager.CurrentProfile != null, "real scheduler/host publication failed: " + Scheduler.LastStatus);
        internal void AssertDeferred() => Check(Scheduler.LastSchedule.Selected.Count == 0
            && Scheduler.LastSchedule.FallbackMode == QuestFallbackMode.TimedIdle
            && Scheduler.CurrentProfilePath == null, "scan did not actually invalidate quest publication");
        internal RunStatus Tick() => Root.Tick(Context);
        internal void StartQuest()
        {
            Root.Start(Context); rootStarted = true;
            Check(Tick() == RunStatus.Running && Behavior.Body.Ticks == 1,
                "actual published root/executor did not reach the controlled quest effect");
        }
        internal void NoMoreEffects()
        {
            int before = Behavior.Body.Ticks; Tick();
            Check(Behavior.Body.Ticks == before, "stale publication executed another forced-behavior effect");
            Check(Scheduler.ActiveQuestIds.Contains(867), "uncertainty released published quest-item protection");
        }
        internal void NoInitialEffects()
        {
            Root.Start(Context); rootStarted = true; Tick();
            Check(Behavior.Body.Ticks == 0, "an unrelated host profile borrowed old quest publication");
        }
        internal string ReplacementProfile()
        {
            string path = Output + ".replacement.xml"; File.Copy(Output, path); return path;
        }
        internal void BeforeNavigation(Action callback) => Set(fixture, "BeforeNavigation", (Action)(() =>
        { Set(fixture, "BeforeNavigation", null); callback(); }));
        internal void SupportRuns(bool combat)
        {
            int effects = Behavior.Body.Ticks, supportTicks = (combat ? Combat : Service).Ticks;
            (combat ? Combat : Service).Status = RunStatus.Success;
            Root.Start(Context); rootStarted = true;
            Check(Tick() == RunStatus.Success && (combat ? Combat : Service).Ticks == supportTicks + 1
                && Behavior.Body.Ticks == effects, (combat ? "combat" : "service") + " was blocked by quest admission");
        }
        internal void Write(uint address, uint value)
        {
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)Get(fixture, "cache")!;
            var pointer = new IntPtr(unchecked((int)address)); cache.Value!.Remove(pointer);
            Marshal.WriteInt32(pointer, unchecked((int)value));
        }
        internal void Slot(int slot, uint id)
        {
            Write(Descriptor + 632 + (uint)slot * 20, id);
            Write(Descriptor + 636 + (uint)slot * 20, id == 0 ? 0 : (uint)WoWDescriptorQuestFlags.Completed);
        }
        internal void Guid(uint objectGuid, uint descriptorGuid) { Write(Player.BaseAddress + 48, objectGuid); Write(Descriptor, descriptorGuid); }
        internal void SetCombat(bool active)
        {
            Type fields = typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFields");
            Type flags = typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFlags");
            Write(Descriptor + Convert.ToUInt32(Enum.Parse(fields, "Flags")) * 4,
                active ? Convert.ToUInt32(Enum.Parse(flags, "InCombat")) : 0);
        }
        public void Dispose()
        {
            try { if (Root != null && rootStarted) Root.Stop(Context); Behavior?.Branch.Stop(Context); }
            finally
            {
                Gate?.Stop(); Order.Nodes = previousNodes; Order.CurrentBehavior = previousBehavior;
                sharedRoot.SetValue(null, previousRoot); ((IDisposable)fixture).Dispose();
            }
        }
    }
    private static IEnumerable<Composite> Descendants(Composite root)
    {
        yield return root;
        if (root is GroupComposite group)
            foreach (Composite child in group.Children)
                if (child != null) foreach (var item in Descendants(child)) yield return item;
    }
    private static void With(Action<Case> test) { using var c = new Case(); test(c); }
    private static object? Get(object target, string field) => target.GetType().GetField(field, I)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, I)!.SetValue(target, value);
    private static object? Saved(string name, params object?[] args) => Invoke(typeof(QuestPublicationRegressionTests).GetMethod(name, S)!, null, args);
    private static object? Call(object target, string name, params object?[] args) => Invoke(target.GetType().GetMethod(name, I)!, target, args);
    private static object? Invoke(MethodInfo method, object? target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new AssertionFailure(message); }
}
