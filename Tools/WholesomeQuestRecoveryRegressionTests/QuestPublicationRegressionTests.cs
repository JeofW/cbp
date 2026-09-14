using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using GreenMagic;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using WholesomeAQ;

// Real Memory/Cache/QuestLog -> ScanAndRefresh/MaterializeSchedule -> XML/file ->
// DoScan/ProfileManager -> running gate. The native executor is absent. All
// readable descriptor/cache storage belongs to this test process, not a game.
// A prior schedule is seeded ONLY to start the old child; the new scan is real.
internal static class QuestPublicationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private const string PriorPath = "controlled-prior-publication.xml";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Publication owner tests require Windows x86.");
        var tests = new List<(string Name, System.Action Test)>
        {
            ("actual raw descriptor and metadata cache materialize the accepted completed quest", () => WithFixture(f =>
            {
                var quests = f.Player.QuestLog.GetAllQuests();
                Check(quests.Count == 1 && quests[0].Id == 867 && quests[0].IsCompleted,
                    "fixture did not reach the real accepted quest/cache owners");
                Check(f.Player.Level == 20 && (int)f.Player.Race == 1 && f.Player.MapId == 1,
                    "fixture player descriptors differ from the supplied observations");
            })),
            ("successful direct scan builds real turn-in XML and the host accepts it", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output);
                Check(scheduler.ScanAndRefresh(f.Player), "normal scan did not build a profile");
                Generated(f, scheduler);
                ProfileManager.LoadNew(f.Output, false);
                Check(ProfileManager.CurrentOuterProfile != null && ProfileManager.CurrentProfile != null,
                    "host did not accept the generated profile");
            })),
            ("normal DoScan publishes and loads the actual generated profile", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); var bot = Bot(scheduler);
                Invoke(bot, "DoScan", scheduler, Lease(bot)); Generated(f, scheduler);
                Check(ProfileManager.XmlLocation == f.Output && ProfileManager.CurrentProfile != null,
                    "normal DoScan did not load its generated output");
            })),
            ("XML serialization failure cannot authorize the old selected child", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); f.Database.Quests[0].Name = "invalid\u0001xml";
                Seed(scheduler); WithRunning(scheduler, (bot, root, child, context) =>
                {
                    Invoke(bot, "DoScan", scheduler, Lease(bot));
                    Check(!File.Exists(f.Output), "invalid XML was written instead of failing serialization");
                    Check(f.Player.ProfileArgumentReads == 1, "actual profile preparation was not reached");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("invalid XML reaches actual builder serialization rather than failing the fixture", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); f.Database.Quests[0].Name = "invalid\u0001xml";
                try { scheduler.ScanAndRefresh(f.Player); throw new AssertionFailure("invalid XML did not fail"); }
                catch (ArgumentException error) { Check(error.StackTrace?.Contains("ProfileBuilder.BuildProfileXml") == true, "error was not from real XML serialization"); }
            })),
            ("unwritable destination reaches actual file writer rather than failing the fixture", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Directory);
                try { scheduler.ScanAndRefresh(f.Player); throw new AssertionFailure("directory destination did not fail"); }
                catch (UnauthorizedAccessException error) { Check(error.StackTrace?.Contains("ProfileBuilder.WriteProfile") == true, "error was not from real profile file writer"); }
            })),
            ("actual filesystem write failure cannot authorize the old selected child", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Directory); Seed(scheduler);
                WithRunning(scheduler, (bot, root, child, context) =>
                {
                    Invoke(bot, "DoScan", scheduler, Lease(bot));
                    Check(f.Player.ProfileArgumentReads == 1, "actual profile preparation was not reached");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("actual filesystem write failure cannot authorize the old grind child", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Directory); Seed(scheduler, true);
                WithRunning(scheduler, (bot, root, child, context) =>
                {
                    Invoke(bot, "DoScan", scheduler, Lease(bot));
                    Check(f.Player.ProfileArgumentReads == 1, "actual profile preparation was not reached");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("null-output builder does not authorize or reload an old child", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(null); Seed(scheduler);
                WithRunning(scheduler, (bot, root, child, context) =>
                {
                    string before = ProfileManager.XmlLocation;
                    Invoke(bot, "DoScan", scheduler, Lease(bot));
                    Check(ProfileManager.XmlLocation == before, "no-output scan loaded a previous path");
                    Check(f.Player.ProfileArgumentReads == 1, "actual profile preparation was not reached");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("merely prepared XML arguments cannot authorize an already-running child", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler);
                WithRunning(scheduler, (bot, root, child, context) =>
                {
                    RunStatus? during = null; int ticks = -1;
                    f.Player.BeforeProfileArguments = () => { during = root.Tick(context); ticks = child.Ticks; };
                    Invoke(bot, "DoScan", scheduler, Lease(bot));
                    Check(f.Player.ProfileArgumentReads == 1, "actual profile-argument boundary was not reached");
                    Check(during == RunStatus.Failure && ticks == 1,
                        "prepared-but-unwritten schedule authorized old running work");
                    Generated(f, scheduler);
                });
            })),
            ("real outer-profile load event occurs before running authorization", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler);
                WithRunning(scheduler, (bot, root, child, context) =>
                {
                    int events = 0, ticks = -1; RunStatus? during = null;
                    BotEvents.Profile.NewProfileLoadedDelegate observe = _ => { events++; during = root.Tick(context); ticks = child.Ticks; };
                    BotEvents.Profile.OnNewOuterProfileLoaded += observe;
                    try { Invoke(bot, "DoScan", scheduler, Lease(bot)); }
                    finally { BotEvents.Profile.OnNewOuterProfileLoaded -= observe; }
                    Check(events == 1, "actual host profile load event did not execute");
                    Check(during == RunStatus.Failure && ticks == 1,
                        "unaccepted host profile authorized an old running child");
                    Generated(f, scheduler);
                });
            })),
            ("ordinary profile-argument failure leaves the gate idle", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler);
                WithRunning(scheduler, (bot, root, child, context) =>
                {
                    f.Player.BeforeProfileArguments = () => throw new InvalidOperationException("controlled profile argument failure");
                    Invoke(bot, "DoScan", scheduler, Lease(bot));
                    Check(f.Player.ProfileArgumentReads == 1, "profile-argument failure was not reached");
                    Check(f.Player.ProfileArgumentReads == 1, "actual profile preparation was not reached");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("profile-argument interruption propagates unchanged without execution authority", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler); var bot = Bot(scheduler);
                var error = new ThreadInterruptedException("controlled publication interruption");
                f.Player.BeforeProfileArguments = () => throw error;
                SameException(() => Invoke(bot, "DoScan", scheduler, Lease(bot)), error);
                Idle(scheduler);
            })),
            ("profile-argument cancellation releases pending refresh and execution authority", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler); var bot = Bot(scheduler); var gate = Gate(bot);
                gate.Start(); Check(gate.TryRequest(), "pending lease setup failed");
                var error = new OperationCanceledException("controlled publication cancellation");
                f.Player.BeforeProfileArguments = () => throw error;
                SameException(() => Invoke(bot, "RunPendingRefresh"), error);
                Check(gate.TryRequest() && gate.Begin().HasValue, "cancelled publication retained refresh lease");
                Idle(scheduler);
            })),
            ("successful stale scan cannot overwrite replacement schedule or path", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler); var bot = Bot(scheduler); var lease = Lease(bot);
                QuestScheduleResult? replacement = null;
                f.BeforeNavigation = () =>
                {
                    f.BeforeNavigation = null; var gate = Gate(bot); gate.Stop(); gate.Start();
                    Check(gate.TryRequest() && gate.Begin().HasValue, "replacement lease setup failed");
                    Seed(scheduler); replacement = scheduler.LastSchedule;
                };
                Invoke(bot, "DoScan", scheduler, lease);
                Check(replacement != null, "actual materialization navigation owner was not reached");
                Check(ReferenceEquals(replacement, scheduler.LastSchedule) && scheduler.CurrentProfilePath == PriorPath,
                    "obsolete successful scan overwrote replacement publication");
            })),
            ("late successful XML preparation cannot overwrite a replacement path", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler); var bot = Bot(scheduler); var lease = Lease(bot);
                QuestScheduleResult? replacement = null;
                f.Player.BeforeProfileArguments = () =>
                {
                    var gate = Gate(bot); gate.Stop(); gate.Start();
                    Check(gate.TryRequest() && gate.Begin().HasValue, "replacement lease setup failed");
                    Seed(scheduler); replacement = scheduler.LastSchedule;
                };
                Invoke(bot, "DoScan", scheduler, lease);
                Check(replacement != null && ReferenceEquals(replacement, scheduler.LastSchedule)
                    && scheduler.CurrentProfilePath == PriorPath,
                    "late obsolete XML write changed replacement schedule/path");
            })),
            ("already-obsolete lease preserves replacement and performs no profile generation", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler); var bot = Bot(scheduler); var lease = Lease(bot);
                Gate(bot).Stop(); Gate(bot).Start(); var replacement = scheduler.LastSchedule;
                Invoke(bot, "DoScan", scheduler, lease);
                Check(ReferenceEquals(replacement, scheduler.LastSchedule) && scheduler.CurrentProfilePath == PriorPath
                    && !File.Exists(f.Output) && f.Player.ProfileArgumentReads == 0,
                    "already-obsolete refresh changed replacement state");
            })),
            ("late failing preparation retains replacement publication", () => WithFixture(f =>
            {
                var scheduler = f.Scheduler(f.Output); Seed(scheduler); var bot = Bot(scheduler); var lease = Lease(bot);
                QuestScheduleResult? replacement = null;
                f.Player.BeforeProfileArguments = () =>
                {
                    var gate = Gate(bot); gate.Stop(); gate.Start();
                    Check(gate.TryRequest() && gate.Begin().HasValue, "replacement lease setup failed");
                    Seed(scheduler); replacement = scheduler.LastSchedule;
                    throw new InvalidOperationException("obsolete arguments failed after replacement");
                };
                Invoke(bot, "DoScan", scheduler, lease);
                Check(replacement != null && ReferenceEquals(replacement, scheduler.LastSchedule)
                    && scheduler.CurrentProfilePath == PriorPath, "late failure revoked replacement work");
            })),
            ("empty raw log and empty database do not reuse prior output", () => WithFixture(f =>
            {
                f.ClearAccepted(); f.Database.Quests.Clear(); var scheduler = f.Scheduler(f.Output); Seed(scheduler);
                WithRunning(scheduler, (bot, root, child, context) =>
                {
                    Invoke(bot, "DoScan", scheduler, Lease(bot)); Stopped(root, child, context);
                    Check(scheduler.LastSchedule.Selected.Count == 0 && scheduler.CurrentProfilePath == null
                        && !File.Exists(f.Output), "empty scan reused a prior selected profile");
                });
            }))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS publication: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL publication assertion: " + test.Name + ": " + error); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR publication fixture/owner: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Publication scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual memory/cache/scan/materialization/XML/file/load/running gate; no game attached.");
        if (assertions != 0 || unexpected != 0)
            throw new InvalidOperationException("Publication regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Directory = Path.Combine(Path.GetTempPath(), "w42-publication-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Directory, "profile.xml");
        internal readonly QuestDatabase Database = new QuestDatabase
        {
            Quests = new List<QuestEntry> { new QuestEntry { Id = 867, Name = "publication", MinLevel = 1, QuestLevel = 20,
                Objectives = new List<QuestObjective> { new QuestObjective { Type = ObjectiveType.TurnInOnly } } } },
            QuestEnders = new List<QuestEnderEntry> { new QuestEnderEntry { QuestId = 867, EnderId = 77, EnderType = QuestObjectType.Creature } },
            CreatureSpawns = new Dictionary<string, List<SpawnPoint>> { ["77"] = new List<SpawnPoint> { new SpawnPoint { Map = 1, X = 10, Y = 10, Z = 10 } } }
        };
        internal readonly ObservedPlayer Player;
        internal System.Action? BeforeNavigation;
        private readonly LocalPlayer? previousPlayer = ObjectManager.Me;
        private readonly Memory? previousMemory = ObjectManager.Wow;
        private readonly ExecutorRand? previousExecutor = ObjectManager.Executor;
        private readonly NavigationProvider previousNavigation = Navigator.NavigationProvider;
        private readonly object? previousCache;
        private bool cacheCaptured;
        private readonly Memory memory = (Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));
        private readonly ThreadLocal<Dictionary<IntPtr, byte[]>> cache = new ThreadLocal<Dictionary<IntPtr, byte[]>>(() => new());
        private readonly ThreadLocal<bool> enabled = new ThreadLocal<bool>(() => true);
        private readonly IntPtr storage = Marshal.AllocHGlobal(65536);
        private readonly uint descriptor;
        private readonly List<(FieldInfo Field, object? Value)> profileState = new();
        private readonly string? previousRememberedPath;

        internal Fixture()
        {
          try
          {
            System.IO.Directory.CreateDirectory(Directory);
            Marshal.Copy(new byte[65536], 0, storage, 65536);
            uint start = unchecked((uint)storage.ToInt32()); descriptor = start + 4096;
            Set(memory, "_cache", cache); Set(memory, "_cacheEnabled", enabled);
            // -1 is this test process only. No process is opened or injected and no
            // assembler/executor exists. Allocated descriptor/cache bytes are owned here.
            Set(memory, "_hProcess", new IntPtr(-1));
            Bytes(0xBD0792, new byte[] { 1 }); Bytes(0xB6A9E0, BitConverter.GetBytes(0u));
            Bytes(0xBD088C, BitConverter.GetBytes(1u));
            Write(start + 8, descriptor); Write(start + 0x14, 4); Write(start + 0xBC, 0);
            Type fields = typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFields");
            Write(descriptor + Convert.ToUInt32(Enum.Parse(fields, "Level")) * 4, 20);
            Write(descriptor + Convert.ToUInt32(Enum.Parse(fields, "Bytes0")) * 4, 0x0101);
            Write(descriptor + 632, 867); Write(descriptor + 636, (uint)WoWDescriptorQuestFlags.Completed);
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, memory);
            ObjectManager.Executor = null; Player = new ObservedPlayer(start); ObjectManager.Me = Player;
            // Reading a StyxWoW static field initializes Landmarks. Install its
            // controlled observations first; the constants below match the actual
            // Landmarks fields, not the differing hexadecimal comments there.
            Bytes(12488416, BitConverter.GetBytes(0));
            Bytes(12488476, BitConverter.GetBytes(0u));
            previousCache = typeof(StyxWoW).GetField("_cache", StaticHidden)!.GetValue(null);
            cacheCaptured = true;
            var questCache = new WoWCache(); typeof(StyxWoW).GetField("_cache", StaticHidden)!.SetValue(null, questCache);
            WoWCache.Cache owner = questCache[CacheDb.Quest]; Set(owner, "_entryOffset", 0u);
            uint table = start + 32768, node = start + 33024;
            byte[] header = new byte[48]; BitConverter.GetBytes(table).CopyTo(header, 36);
            Bytes(owner.Address, header); Write(table + 8, node); Write(node, 867); Write(node + 4, 0); Write(node + 24, 867);
            Navigator.NavigationProvider = new TestNavigationProvider(_ => { BeforeNavigation?.Invoke(); return 1f; });
            // Trigger the actual manager's static constructor before retaining its state.
            _ = ProfileManager.XmlLocation;
            foreach (string name in new[] { "_currentOuterProfile", "_currentProfile", "_profileless", "<XmlLocation>k__BackingField" })
            {
                var field = typeof(ProfileManager).GetField(name, StaticHidden)!;
                profileState.Add((field, field.GetValue(null)));
            }
            previousRememberedPath = Styx.Helpers.LevelbotSettings.Instance.LastUsedPath;
            if (!StyxWoW.IsInWorld || !Player.IsValid || Player.Level != 20 || (int)Player.Race != 1)
                throw new InvalidOperationException("Controlled world/descriptor setup invalid; level=" + Player.Level + "; race=" + Player.Race);
          }
          catch { Dispose(); throw; }
        }
        private void Bytes(uint address, byte[] bytes) => cache.Value![new IntPtr(unchecked((int)address))] = bytes;
        private static void Write(uint address, uint value) => Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value));
        internal void ClearAccepted() { Write(descriptor + 632, 0); Write(descriptor + 636, 0); cache.Value!.Remove(new IntPtr(unchecked((int)(descriptor + 632)))); }
        internal QuestScheduler Scheduler(string? path)
        {
            var loader = new DataLoader(); Set(loader, "_database", Database);
            return new QuestScheduler(loader, new ProfileBuilder(path!), new WholesomeAQSettings());
        }
        public void Dispose()
        {
            foreach (var item in profileState) item.Field.SetValue(null, item.Value);
            if (previousRememberedPath != null) Styx.Helpers.LevelbotSettings.Instance.LastUsedPath = previousRememberedPath;
            Navigator.NavigationProvider = previousNavigation;
            // Do not initialize the host or overwrite uncaptured cache state
            // while unwinding a partially constructed fixture.
            if (cacheCaptured) typeof(StyxWoW).GetField("_cache", StaticHidden)!.SetValue(null, previousCache);
            ObjectManager.Me = previousPlayer; ObjectManager.Executor = previousExecutor;
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, previousMemory);
            Set(memory, "_hProcess", IntPtr.Zero); cache.Dispose(); enabled.Dispose(); Marshal.FreeHGlobal(storage);
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
    private sealed class ObservedPlayer : LocalPlayer
    {
        internal System.Action? BeforeProfileArguments;
        internal int ProfileArgumentReads;
        internal ObservedPlayer(uint address) : base(address) { }
        public override string Name
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                var frames = new StackTrace().GetFrames();
                // The scheduler's XML argument read is distinct from the existing
                // recovery/history identity readers. No production method is replaced.
                if (frames.Any(f => f.GetMethod()?.DeclaringType == typeof(QuestScheduler))
                    && !frames.Any(f => f.GetMethod()?.DeclaringType == typeof(QuestRecoveryRuntime)
                        || f.GetMethod()?.DeclaringType == typeof(QuestLog)))
                {
                    ProfileArgumentReads++; var action = BeforeProfileArguments; BeforeProfileArguments = null; action?.Invoke();
                }
                return "W42-Publication-Fixture";
            }
        }
        public override WoWPoint Location => new WoWPoint(10, 10, 10);
    }
    private static void WithFixture(System.Action<Fixture> action) { using var fixture = new Fixture(); action(fixture); }
    private static void Generated(Fixture f, QuestScheduler scheduler)
    {
        Check(File.Exists(f.Output), "actual generated file is absent; status=" + scheduler.LastStatus);
        var document = XDocument.Load(f.Output);
        Check(document.Descendants("TurnIn").Any(e => (string?)e.Attribute("QuestId") == "867"), "real materialization did not produce quest867 turn-in XML");
        Check(scheduler.LastSchedule.Selected.Any(c => c.QuestId == 867 && c.Stage == QuestWorkStage.TurnIn)
            && scheduler.CurrentProfilePath == f.Output, "normal selected schedule/path was not published");
    }
    private static void Seed(QuestScheduler scheduler, bool grind = false)
    {
        Set(scheduler, "<LastSchedule>k__BackingField", new QuestScheduleResult
        {
            Selected = grind ? Array.Empty<QuestWorkCandidate>() : new[] { new QuestWorkCandidate { QuestId = 900001, Stage = QuestWorkStage.Objective,
                Recovery = new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true } } },
            FallbackMode = grind ? QuestFallbackMode.ValidatedGrind : QuestFallbackMode.None,
            ValidatedGrindProfilePath = grind ? PriorPath : ""
        });
        Set(scheduler, "<CurrentProfilePath>k__BackingField", PriorPath);
        Set(scheduler, "<ActiveQuestIds>k__BackingField", new HashSet<int> { 900001 });
        Set(scheduler, "<LastQuestCount>k__BackingField", 1);
    }
    private static WholesomeAutoQuest Bot(QuestScheduler scheduler)
    {
        var bot = new WholesomeAutoQuest(); Set(bot, "_stopped", false); Set(bot, "_initialized", true);
        Set(bot, "_dataReady", true); Set(bot, "_scheduler", scheduler); return bot;
    }
    private static RefreshGate Gate(WholesomeAutoQuest bot) => (RefreshGate)typeof(WholesomeAutoQuest).GetField("_refreshGate", Hidden)!.GetValue(bot)!;
    private static RefreshLease Lease(WholesomeAutoQuest bot)
    {
        var gate = Gate(bot); gate.Start(); Check(gate.TryRequest(), "refresh request setup failed");
        return gate.Begin() ?? throw new AssertionFailure("refresh lease setup failed");
    }
    private static void WithRunning(QuestScheduler scheduler, System.Action<WholesomeAutoQuest, GroupComposite, RunningChild, object> action)
    {
        var bot = Bot(scheduler); var root = (GroupComposite)bot.Root; root.Children.Clear(); var child = new RunningChild(); root.Children.Add(child);
        object context = new object(); root.Start(context);
        try { Check(root.Tick(context) == RunStatus.Running && child.Ticks == 1, "old child setup failed"); action(bot, root, child, context); }
        finally { root.Stop(context); Gate(bot).Stop(); }
    }
    private sealed class RunningChild : Composite
    {
        internal int Ticks, Stops;
        protected override IEnumerable<RunStatus> Execute(object context) { while (true) { Ticks++; yield return RunStatus.Running; } }
        public override void Stop(object context) { Stops++; base.Stop(context); }
    }
    private static void Stopped(GroupComposite root, RunningChild child, object context) => Check(root.Tick(context) == RunStatus.Failure && child.Ticks == 1 && child.Stops > 0,
        "unpublished refresh authorized the old running child");
    private static void Idle(QuestScheduler scheduler) => Check(scheduler.LastSchedule.Selected.Count == 0
        && scheduler.LastSchedule.FallbackMode != QuestFallbackMode.ValidatedGrind && scheduler.CurrentProfilePath == null,
        "unpublished candidate retained selected/profile execution authority");
    private static void SameException(System.Action action, Exception expected)
    {
        try { action(); throw new AssertionFailure("cancellation did not propagate"); }
        catch (Exception actual) when (ReferenceEquals(actual, expected))
        {
            Check(actual.StackTrace?.Contains("QuestScheduler.ScanAndRefresh") == true, "failure did not traverse the actual scheduler");
        }
    }
    private static object? Invoke(object target, string method, params object[] args)
    {
        try { return target.GetType().GetMethod(method, Hidden)!.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, Hidden)!.SetValue(target, value);
    private static void Check(bool ok, string message) { if (!ok) throw new AssertionFailure(message); }
}
