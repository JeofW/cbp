using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using WholesomeAQ;

// Actual allocated quest log/history -> actual completion fence -> actual scan,
// publication and generated XML. The UI is exercised by the retained stock-Lua
// driver; its checked reward dispatch is explicitly delivered to this world.
internal static class QuestTurnInCompletionRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    [ModuleInitializer]
    internal static void Run()
    {
        int count = 0;var failures=new List<string>();
        void Case(string name, Action<Probe> body)
        {
            using var probe = new Probe(); count++;
            try{body(probe);Console.WriteLine("PASS turn-in confirmation: " + name);}
            catch(Exception error){failures.Add(name+": "+error.Message);Console.Error.WriteLine("FAIL turn-in confirmation: "+failures.Last());if(error is NullReferenceException)Console.Error.WriteLine(error);}
        }
        Case("real UI departure cannot reopen Pickup before delayed history", p =>
        {
            Check(p.World.Player.QuestLog.GetQuestById(10161)?.IsCompleted == true, "controlled quest was not actually accepted complete before scanning");
            Check(p.Scan(), "initial TurnIn scan did not publish");
            var document = XDocument.Load(p.Output);
            Check(document.Descendants("TurnIn").Count() == 1, "initial scan omitted controlled TurnIn: " + document);
            var turnin = document.Descendants("TurnIn").Single();
            p.ObserveReady();
            using var ui = new QuestTurnInExecutionRegressionTests.TurnInExecutionFixture();
            QuestTurnInCompletion? record = null;
            ui.ExecuteBound(turnin, true, 10161, 19367,
                () =>
                {
                    p.FreshHistory(10161);
                    Check(record!.Observe() == QuestTurnInCompletionState.Confirmed, "fresh positive history did not confirm the owned reward");
                },
                () =>
                {
                    record = p.Submit();
                    Check(p.World.Player.QuestLog.ContainsQuest(10161), "UI submission fabricated accepted-log departure");
                }, expectedMap: 1, delayHistory: true,
                afterDeparture: () =>
                {
                    p.World.SetAccepted(false);
                    Check(p.ObserveReady(), "actual Wholesome departure observer did not request rebuilding");
                    Check(record!.Observe() != QuestTurnInCompletionState.Confirmed, "stale history confirmed departure");
                    p.FreshHistory(); // Authoritatively fresh, but still lacks 10161.
                    p.Scan();
                    Check(record.BlocksPickup && !p.Scheduler.LastSchedule.Plan.Any(x => x.Quest.Id == 10161), "scheduler reopened the pending quest");
                    Check(p.Scheduler.LastSchedule.Plan.Any(x => x.Quest.Id == 9349 && x.Stage == QuestWorkStage.Pickup), "per-quest fence starved unrelated work from the same NPC");
                });
            p.Scan();
            Check(!p.Scheduler.LastSchedule.Plan.Any(x => x.Quest.Id == 10161), "confirmed quest was generated again");
        });
        Case("submission and success return do not acknowledge removal", p =>
        {
            var record = p.Submit();
            Check(record.Observe() == QuestTurnInCompletionState.Submitted, "accepted quest falsely completed");
            Check(!record.Submit(p.Owner), "same submission was admitted twice");
        });
        Case("failed history after departure remains pending and blocks only its quest", p =>
        {
            var record = p.Submit(); p.World.SetAccepted(false); record.Observe();
            p.World.SetHistory(Array.Empty<uint>(), authoritative: false);
            Check(record.Observe() != QuestTurnInCompletionState.Confirmed && record.BlocksPickup, "UNKNOWN released confirmation");
        });
        Case("bounded unresolved departure cannot become Pickup", p =>
        {
            var record = p.Submit(); p.World.SetAccepted(false); record.Observe(); p.FreshHistory();
            Set(record, "_submittedTick", Environment.TickCount64 - 120000);
            Check(record.Observe() == QuestTurnInCompletionState.Unresolved && record.BlocksPickup, "deadline invented completion or reopened Pickup");
            p.Scan(); Check(!p.Scheduler.LastSchedule.Plan.Any(x => x.Quest.Id == 10161), "unresolved quest escaped its scoped fence");
        });
        Case("fresh negative while still accepted permits controlled retry", p =>
        {
            var record = p.Submit(); Set(record, "_submittedTick", Environment.TickCount64 - 120000);
            record.Observe(); p.FreshHistory();
            Check(record.Observe() == QuestTurnInCompletionState.Rejected, "known retained acceptance and fresh negative history could not recover");
            var replacement = QuestTurnInCompletion.Prepare(10161, p.Owner);
            Check(replacement != null && replacement.Generation != record.Generation && !record.Submit(p.Owner), "old-generation callback acquired replacement authority");
        });
        Case("explicit cancellation revokes old callbacks", p =>
        {
            var record = p.Submit(); record.Cancel(p.Owner);
            var replacement = QuestTurnInCompletion.Prepare(10161, new object());
            Check(record.Observe() == QuestTurnInCompletionState.Cancelled && !record.Submit(p.Owner)
                && replacement != null && replacement.Generation != record.Generation, "cancelled generation survived");
        });
        Case("Stop and restart cannot inherit submitted authority", p =>
        {
            var record = p.Submit(); typeof(TreeRoot).GetProperty("State", Hidden)!.SetValue(null, TreeRootState.Stopped);
            Check(QuestTurnInCompletion.Find(10161) == null, "Stop retained active fence");
            typeof(TreeRoot).GetProperty("State", Hidden)!.SetValue(null, TreeRootState.Running);
            Check(record.Observe() == QuestTurnInCompletionState.Cancelled, "restart reacquired old reward owner");
        });
        foreach(bool processReplacement in new[]{false,true})
        Case("same character session revocation invalidates young history/"+processReplacement, p =>
        {
            var record=p.Submit();p.World.SetAccepted(false);
            var memory=Styx.WoWInternals.ObjectManager.Wow;
            var processField=typeof(GreenMagic.Memory).GetField("_processId",Hidden)!;
            var prior=processField.GetValue(memory);
            try
            {
                if(processReplacement)processField.SetValue(memory,(int)prior!+1);
                else typeof(TreeRoot).GetProperty("State",Hidden)!.SetValue(null,TreeRootState.Stopped);
                Check(QuestTurnInCompletion.Find(10161)==null,"old session retained fence");
                Check(p.World.Player.QuestLog.CompletedQuestCacheStatus!=CompletedQuestCacheStatus.Valid,"revocation retained young negative history as current-session authority");
                typeof(TreeRoot).GetProperty("State",Hidden)!.SetValue(null,TreeRootState.Running);
                p.World.SetHistory(Array.Empty<uint>(),authoritative:false);
                p.Scan();Check(!p.Scheduler.LastSchedule.Plan.Any(x=>x.Quest.Id==10161&&x.Stage==QuestWorkStage.Pickup),"restart reopened quest before a fresh history result");
                p.FreshHistory(10161);p.Scan();Check(!p.Scheduler.LastSchedule.Plan.Any(x=>x.Quest.Id==10161),"fresh rewarded history reopened quest");
            }
            finally{processField.SetValue(memory,prior);typeof(TreeRoot).GetProperty("State",Hidden)!.SetValue(null,TreeRootState.Running);}
        });
        Case("generated profile replacement preserves submitted confirmation", p =>
        {
            var record = p.Submit(); p.World.SetAccepted(false); record.Observe(); p.FreshHistory(); p.Scan();
            Check(ReferenceEquals(record, QuestTurnInCompletion.Find(10161)) && record.BlocksPickup, "normal scheduler replacement erased an in-flight server action");
        });
        Case("repeatable assignment is scoped and does not disable another quest fence", p =>
        {
            QuestTurnInCompletion.SetRepeatableQuestIds(new uint[] { 9349 });
            Check(!QuestTurnInCompletion.RequiresCompletedHistory(9349) && QuestTurnInCompletion.RequiresCompletedHistory(10161), "repeatable policy became global");
            var repeatable = QuestTurnInCompletion.Prepare(9349, p.Owner);
            Check(repeatable != null && repeatable.Submit(p.Owner) && repeatable.BlocksPickup && p.Submit().BlocksPickup,
                "repeatable did not acquire a transient one-shot submission owner");
            p.FreshHistory(9349);
            Check(repeatable.Observe() != QuestTurnInCompletionState.Confirmed && repeatable.BlocksPickup,
                "permanent history falsely acknowledged repeatable reward");
            Set(repeatable, "_submittedTick", Environment.TickCount64 - 120000);
            Check(repeatable.Observe() == QuestTurnInCompletionState.Unresolved && repeatable.BlocksPickup,
                "repeatable timer acknowledged or reopened an unknown reward outcome");
            repeatable.Cancel(p.Owner);
        });
        Case("stale accepted-log callback cannot reject or confirm a newer observation", p =>
        {
            var record = p.Submit(); p.World.SetAccepted(false); record.Observe();
            var departed = p.World.Player.QuestLog.CaptureSnapshot();
            p.FreshHistory(10161); p.World.SetAccepted(true, complete: true);
            typeof(QuestTurnInCompletion).GetMethod("Observe", Hidden, null, new[] { typeof(QuestLogSnapshot) }, null)!.Invoke(record, new object[] { departed });
            Check(record.State != QuestTurnInCompletionState.Confirmed && record.BlocksPickup, "stale departure confirmed a changed accepted log");
        });
        Case("replacement actor and old owner cannot inherit submitted authority", p =>
        {
            var record = p.Submit();
            record.Cancel(new object());
            Check(record.BlocksPickup, "foreign cancellation revoked the owner");
            var field = typeof(Styx.WoWInternals.ObjectManager).GetField("<Me>k__BackingField", Hidden)!;
            try
            {
                field.SetValue(null, new ReplacementPlayer(p.World.Player.BaseAddress));
                Check(QuestTurnInCompletion.Find(10161) == null && record.Observe() == QuestTurnInCompletionState.Cancelled,
                    "replacement character inherited a submitted reward");
            }
            finally { field.SetValue(null, p.World.Player); }
            var replacement = p.Submit();
            Check(replacement.Generation != record.Generation && !record.Submit(p.Owner), "old actor callback acquired replacement owner");
        });
        Case("same actor wrapper cannot retain authority after GUID mutation", p =>
        {
            var record = p.Submit();
            var field = typeof(Styx.WoWInternals.WoWObjects.WoWObject).GetField("_cachedGuid", Hidden)!;
            var prior = field.GetValue(p.World.Player);
            try
            {
                field.SetValue(p.World.Player, (ulong)998877);
                Check(QuestTurnInCompletion.Find(10161) == null && record.Observe() == QuestTurnInCompletionState.Cancelled,
                    "changed GUID on the same wrapper retained reward authority");
            }
            finally { field.SetValue(p.World.Player, prior); }
        });
        Case("replaced quest log cannot retain a stale Pickup fence", p =>
        {
            var record = p.Submit(); var prior = p.World.Player.QuestLog;
            var field = typeof(Styx.WoWInternals.WoWObjects.LocalPlayer).GetField("QuestLog", Hidden)!;
            try
            {
                field.SetValue(p.World.Player, new QuestLog());
                Check(QuestTurnInCompletion.Find(10161) == null && record.Observe() == QuestTurnInCompletionState.Cancelled,
                    "replaced quest-log owner retained pending confirmation");
                Check(QuestTurnInCompletion.ObservePending(p.World.Player.QuestLog.CaptureSnapshot()).Count == 0,
                    "stale quest-log record kept blocking new scheduler ownership");
            }
            finally { field.SetValue(p.World.Player, prior); }
        });
        Case("new exact daily descriptor membership acknowledges the submitted daily", p =>
        {
            p.SetDailyQuest(); p.DailySlots();
            var record = p.Submit();
            p.World.SetAccepted(false);
            Check(record.Observe() != QuestTurnInCompletionState.Confirmed, "daily log departure became a reward acknowledgement");
            p.DailySlots(10161);
            Check(record.Observe() == QuestTurnInCompletionState.Confirmed && !record.BlocksPickup,
                "fresh exact daily descriptor membership did not acknowledge the submitted reward");
            Check(!p.World.Player.QuestLog.CaptureCompletedQuestHistory().QuestIds.Contains(10161), "daily receipt was copied into permanent history");
        });
        foreach (string condition in new[] { "already-present", "unrelated-id", "duplicate", "invalid", "accepted", "ordinary-repeatable" })
        Case("daily receipt rejects " + condition, p =>
        {
            if (condition != "ordinary-repeatable") p.SetDailyQuest();
            else QuestTurnInCompletion.SetRepeatableQuestIds(new uint[] { 10161 });
            p.DailySlots(condition == "already-present" ? new uint[] { 10161 } : Array.Empty<uint>());
            var record = QuestTurnInCompletion.Prepare(10161, p.Owner);
            bool submitted = record?.Submit(p.Owner) == true;
            if (condition != "accepted") p.World.SetAccepted(false);
            p.DailySlots(condition switch
            {
                "unrelated-id" => new uint[] { 9349 }, "duplicate" => new uint[] { 10161, 10161 },
                "invalid" => new uint[] { 10161, uint.MaxValue }, _ => new uint[] { 10161 }
            });
            if (record != null)
                Check(record.Observe() != QuestTurnInCompletionState.Confirmed, "invalid or unrelated daily state acknowledged a reward");
            if (condition == "already-present") Check(!submitted, "already-completed daily was admitted as a new reward operation");
            else Check(submitted && record!.BlocksPickup, "unknown daily result lost its submission fence");
        });
        foreach (bool changed in new[] { false, true })
        Case("daily reward is attributed once to its exact " + (changed ? "previous" : "current") + " behavior", p =>
        {
            p.SetDailyQuest(); p.DailySlots();
            var behavior = new ForcedQuestTurnIn(10161, "Controlled daily", 19367, "Ender", new WoWPoint(10, 10, 10));
            p.Owner = behavior; var record = p.Submit();
            var (key, generation) = p.Claim(behavior);
            var before = QuestRecoveryManager.Instance.GetEntries().Single(row => row.Key.Equals(key));
            p.World.SetAccepted(false); p.DailySlots(10161);
            p.Complete(behavior, changed);
            var recovery = QuestRecoveryManager.Instance.GetEntries().Single(row => row.Key.Equals(key));
            // Successful attempt release advances its recovery cycle. Completed
            // is a permanent quest sentinel; using it for a daily would prevent
            // subsequent reset periods. Eligible alone also admits abandonment,
            // so require the exact success generation, cycle and evidence.
            Check(!p.Ownership.TryGet(behavior, out _) && !QuestRecoveryManager.Instance.OwnsAttempt(key, generation)
                && recovery.State == QuestRecoveryState.Eligible && recovery.AttemptGeneration == generation
                && recovery.RecoveryCycleId == before.RecoveryCycleId + 1
                && recovery.Evidence.Count == before.Evidence.Count + 1
                && recovery.Evidence.Last().Text == (changed ? "TurnIn stage completed." : "TurnIn behavior completed."),
                "confirmed daily did not report success for its exact owned generation; receipt="
                    + record.State + "; behaviorOwner=" + p.Ownership.TryGet(behavior, out _)
                    + "; attemptOwner=" + QuestRecoveryManager.Instance.OwnsAttempt(key, generation)
                    + "; recovery=" + recovery.State);
            p.Complete(behavior, changed);
            var repeated = QuestRecoveryManager.Instance.GetEntries().Single(row => row.Key.Equals(key));
            Check(record.Observe() == QuestTurnInCompletionState.Confirmed && !p.Ownership.TryGet(behavior, out _),
                "repeat observation recreated a completed attempt");
            Check(repeated.RecoveryCycleId == recovery.RecoveryCycleId && repeated.Evidence.Count == recovery.Evidence.Count,
                "repeat observation recorded a second success for one reward");

            // Controlled later reset/acceptance observations must admit a new
            // reward owner, while the old confirmed receipt cannot complete it.
            p.DailySlots(); p.World.SetAccepted(true, complete: true);
            var next = new ForcedQuestTurnIn(10161, "Next daily period", 19367, "Ender", new WoWPoint(10, 10, 10));
            p.Owner = next; var nextRecord = p.Submit(); var (_, nextGeneration) = p.Claim(next);
            Check(nextGeneration > generation && nextRecord.Generation != record.Generation,
                "daily reset could not acquire a new attempt/submission generation");
            p.Complete(behavior, changed);
            Check(p.Ownership.TryGet(next, out _) && QuestRecoveryManager.Instance.OwnsAttempt(key, nextGeneration)
                && !nextRecord.IsConfirmedFor(next), "prior daily receipt completed the new period's owner");
            p.World.SetAccepted(false); p.DailySlots(10161); p.Complete(next, changed);
            var nextResult = QuestRecoveryManager.Instance.GetEntries().Single(row => row.Key.Equals(key));
            Check(nextRecord.IsConfirmedFor(next) && !p.Ownership.TryGet(next, out _)
                && nextResult.RecoveryCycleId == recovery.RecoveryCycleId + 1
                && nextResult.AttemptGeneration == nextGeneration && nextResult.State == QuestRecoveryState.Eligible,
                "fresh daily reward did not independently complete its new owned attempt");
        });
        foreach (bool changed in new[] { false, true })
        Case("another behavior cannot consume an existing reward receipt/" + changed, p =>
        {
            var original = new ForcedQuestTurnIn(10161, "Original", 19367, "Ender", new WoWPoint(10, 10, 10));
            p.Owner = original; var record = p.Submit(); p.World.SetAccepted(false); record.Observe(); p.FreshHistory(10161);
            Check(record.Observe() == QuestTurnInCompletionState.Confirmed, "control permanent receipt did not confirm");
            var replacement = new ForcedQuestTurnIn(10161, "Replacement", 19367, "Ender", new WoWPoint(10, 10, 10));
            var (key, generation) = p.Claim(replacement);
            p.Complete(replacement, changed);
            Check(p.Ownership.TryGet(replacement, out _) && QuestRecoveryManager.Instance.OwnsAttempt(key, generation),
                "another behavior's confirmation was attributed to the replacement attempt");
        });
        Case("completion observation preserves cancellation from behavior readiness", p =>
        {
            var signal = new OperationCanceledException("controlled completion cancellation");
            var behavior = new ThrowingTurnIn(signal); p.Claim(behavior);
            Exception? seen = null; try { p.Complete(behavior, false); } catch (Exception error) { seen = error; }
            Check(ReferenceEquals(seen, signal), "completion polling swallowed cancellation as an unfinished behavior");
        });
        Console.WriteLine($"Turn-in confirmation scenarios: {count-failures.Count}/{count}; actual UI/log/cache/fence/scheduler owners; controlled server observations.");
        if(failures.Count!=0)throw new InvalidOperationException(string.Join("; ",failures));
    }

    private sealed class ReplacementPlayer(uint address) : Styx.WoWInternals.WoWObjects.LocalPlayer(address)
    {
        public override string Name => "Replacement character";
        public override string RealmName => "Replacement realm";
    }
    private sealed class ThrowingTurnIn(Exception signal) : ForcedQuestTurnIn(10161, "Cancelled", 19367, "Ender", new WoWPoint(10, 10, 10))
    {
        public override bool IsDone => throw signal;
    }

    private sealed class Probe : IDisposable
    {
        internal readonly QuestDatasetObservationFixture World = new("TurnIn Confirmation Fixture " + Guid.NewGuid().ToString("N"));
        internal object Owner = new();
        internal readonly QuestScheduler Scheduler;
        private readonly WholesomeAutoQuest bot = new();
        internal readonly string Output;
        private readonly object? oldThread = typeof(TreeRoot).GetField("_workerThread", Hidden)!.GetValue(null);
        private readonly TreeRootState oldState = TreeRoot.State;
        private readonly string recoveryRoot = Path.Combine(Path.GetTempPath(), "daily-confirmation-" + Guid.NewGuid().ToString("N"));
        internal WholesomeAttemptOwnership Ownership => (WholesomeAttemptOwnership)typeof(WholesomeAutoQuest).GetField("_attemptOwnership", Hidden)!.GetValue(bot)!;
        internal Probe()
        {
            Set(bot, "_stopped", false);
            World.LoadProfile("<HBProfile><Name>Completion fixture</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><QuestOrder /></HBProfile>");
            typeof(TreeRoot).GetField("_workerThread", Hidden)!.SetValue(null, Thread.CurrentThread);
            typeof(TreeRoot).GetProperty("State", Hidden)!.SetValue(null, TreeRootState.Running);
            World.SetQuest(10161, "In Case of Emergency...", 58, new int[4], new int[4], new int[6], new int[6]);
            World.SetAccepted(true, complete: true); FreshHistory();
            object baseline = typeof(QuestDatasetObservationFixture).GetField("baseline", Hidden)!.GetValue(World)!;
            var db = (QuestDatabase)baseline.GetType().GetField("Database", Hidden)!.GetValue(baseline)!;
            db.Quests = new() { Quest(10161), Quest(9349) };
            db.QuestGivers = new() { new() { QuestId = 10161, GiverId = 19367, GiverType = WholesomeAQ.QuestObjectType.Creature }, new() { QuestId = 9349, GiverId = 19367, GiverType = WholesomeAQ.QuestObjectType.Creature } };
            db.QuestEnders = new() { new() { QuestId = 10161, EnderId = 19367, EnderType = WholesomeAQ.QuestObjectType.Creature } };
            db.CreatureSpawns = new() { ["19367"] = new() { new() { Map = 1, X = 10, Y = 10, Z = 10 } } };
            Output = (string)baseline.GetType().GetProperty("Output", Hidden)!.GetValue(baseline)!;
            var loader = new DataLoader(); Set(loader, "_database", db);
            Scheduler = new QuestScheduler(loader, new ProfileBuilder(Output), new WholesomeAQSettings());
        }
        private static QuestEntry Quest(int id) => new() { Id = id, Name = "Controlled " + id, MinLevel = 1, QuestLevel = 58,
            Objectives = new() { new() { Type = ObjectiveType.TurnInOnly } } };
        internal void SetDailyQuest()
        {
            World.SetQuest(10161, "Controlled daily", 58, new int[4], new int[4], new int[6], new int[6], 0x1000);
            World.SetAccepted(true, complete: true); FreshHistory();
            Check(Styx.Logic.Questing.Quest.FromId(10161)?.IsDaily == true, "original quest cache daily flag was not observed");
            QuestTurnInCompletion.SetRepeatableQuestIds(new uint[] { 10161 });
        }
        internal void DailySlots(params uint[] ids)
        {
            if (ids.Length > 25) throw new ArgumentOutOfRangeException(nameof(ids));
            uint descriptor = ObjectManager.Wow!.Read<uint>(World.Player.BaseAddress + 8);
            for (int index = 0; index < 25; index++)
                Marshal.WriteInt32(new IntPtr(unchecked((int)(descriptor + 0x1400 + index * 4))),
                    unchecked((int)(index < ids.Length ? ids[index] : 0)));
        }
        internal (QuestRecoveryKey Key, long Generation) Claim(ForcedBehavior behavior)
        {
            var manager = QuestRecoveryManager.Instance;
            manager.Configure(new QuestRecoveryEnvironment(recoveryRoot, World.Player.Name, World.Player.RealmName, "daily-fixture", "core", "nav"));
            var key = QuestScheduler.ActivationKey(behavior);
            var decision = manager.TryBeginAttempt(key, QuestRecoveryRuntime.Capture());
            Check(decision.MayAttempt && decision.AttemptGeneration > 0, "actual recovery generation was not acquired");
            Ownership.Begin(behavior, key, decision);
            return (key, decision.AttemptGeneration);
        }
        internal void Complete(ForcedBehavior behavior, bool changed)
        {
            // Loading a profile does not create the QuestBot's order. Install
            // the real order only for this callback and restore its singleton;
            // never start the live bot loop to obtain a test session.
            var prior = QuestOrder.Instance;
            _ = new QuestOrder { CurrentBehavior = behavior };
            try
            {
                var method = typeof(WholesomeAutoQuest).GetMethod(changed ? "CompleteChangedStageWhenProven" : "CompleteCurrentStageBeforeTick", Hidden)!;
                method.Invoke(bot, changed ? new object[] { new ForcedQuestTurnIn(9349, "Next", 19367, "Ender", new WoWPoint(10, 10, 10)) } : null);
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            finally { typeof(QuestOrder).GetProperty("Instance", Hidden)!.SetValue(null, prior); }
        }
        internal QuestTurnInCompletion Submit()
        {
            var record = QuestTurnInCompletion.Prepare(10161, Owner);
            Check(record != null && record.Submit(Owner), "owned reward preparation/submission failed"); return record!;
        }
        internal void FreshHistory(params uint[] ids)
        {
            World.SetHistory(ids);
            var type = typeof(QuestLog);
            long requested = (long)type.GetField("_completedQuestRequestedGeneration", Hidden)!.GetValue(null)!;
            long revision = (long)type.GetField("_completedQuestObservationRevision", Hidden)!.GetValue(null)!;
            type.GetField("_completedQuestObservedGeneration", Hidden)!.SetValue(null, requested);
            type.GetField("_completedQuestObservationRevision", Hidden)!.SetValue(null, revision + 1);
            type.GetField("_completedQuestObservedUtc", Hidden)!.SetValue(null, DateTime.UtcNow);
        }
        internal bool ObserveReady() => (bool)typeof(WholesomeAutoQuest).GetMethod("ObserveReadyQuestLog", Hidden)!.Invoke(bot, null)!;
        internal bool Scan() => Scheduler.ScanAndRefreshOwned(World.Player, null!, action => { action(); return true; }, path => ProfileManager.TryLoadNew(path, false), () => true);
        public void Dispose()
        {
            QuestTurnInCompletion.Find(10161)?.Cancel(Owner);
            if (Directory.Exists(recoveryRoot)) QuestRecoveryManager.Instance.Flush();
            typeof(TreeRoot).GetField("_workerThread", Hidden)!.SetValue(null, oldThread);
            typeof(TreeRoot).GetProperty("State", Hidden)!.SetValue(null, oldState);
            World.Dispose();
            if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true);
        }
    }
    private static void Set(object value, string name, object? field) => value.GetType().GetField(name, Hidden)!.SetValue(value, field);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
