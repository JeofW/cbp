using Bots.Quest.QuestOrder;
using Styx;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Styx.Bot.Quest_Behaviors
{
    internal sealed class SafeTurnInEnderCandidate
    {
        public SafeTurnInEnderCandidate(uint entry, string name, WoWPoint location, string source)
        {
            Entry = entry;
            Name = name;
            Location = location;
            Source = source;
        }

        public uint Entry { get; private set; }
        public string Name { get; private set; }
        public WoWPoint Location { get; private set; }
        public string Source { get; private set; }
    }

    internal sealed class SafeTurnInQuestSnapshot
    {
        public SafeTurnInQuestSnapshot(
            QuestCompletionSnapshot completion,
            IReadOnlyList<int> objectiveAndItemCounts,
            bool progressStateIsCertain,
            int freeQuestLogSlots)
        {
            Completion = completion;
            ObjectiveAndItemCounts = Array.AsReadOnly(
                (objectiveAndItemCounts ?? Array.Empty<int>()).ToArray());
            ProgressStateIsCertain = progressStateIsCertain;
            FreeQuestLogSlots = freeQuestLogSlots;
        }

        public QuestCompletionSnapshot Completion { get; private set; }
        public IReadOnlyList<int> ObjectiveAndItemCounts { get; private set; }
        public bool ProgressStateIsCertain { get; private set; }
        public int FreeQuestLogSlots { get; private set; }
        public bool HasObjectiveProgress
        {
            get { return ObjectiveAndItemCounts.Any(count => count > 0); }
        }
    }

    internal sealed class SafeTurnInDialogOutcome
    {
        private IReadOnlyList<uint> _offeredQuestIds = Array.Empty<uint>();

        public long InteractionCycleId { get; set; }
        public uint ShownQuestId { get; set; }
        public IReadOnlyList<uint> OfferedQuestIds
        {
            get { return _offeredQuestIds; }
            set { _offeredQuestIds = Array.AsReadOnly((value ?? Array.Empty<uint>()).ToArray()); }
        }
    }

    internal interface ISafeTurnInChild : IDisposable
    {
        bool IsDone { get; }
        bool IsExecutionDeferred { get; }
        long InteractionCycleId { get; }
        bool HasVisibleDialog { get; }
        void OnStart();
        RunStatus Tick(object context);
        bool TryConsumeOutcome(out SafeTurnInDialogOutcome outcome);
    }

    internal abstract class SafeTurnInRuntime
    {
        public abstract DateTime UtcNow { get; }
        public abstract WoWPoint CurrentPlayerLocation { get; }
        public abstract int CurrentMapId { get; }
        public abstract BotPoi CurrentBotPoi { get; }
        public abstract void EnsureConfigured();
        public abstract QuestRecoveryContext CaptureContext(IReadOnlyList<int> objectiveAndItemCounts);
        public abstract QuestRecoveryDecision TryBeginAttempt(QuestRecoveryKey key, QuestRecoveryContext context);
        public abstract SafeTurnInQuestSnapshot GetQuestSnapshot(uint questId);
        public abstract QuestPrerequisiteStatus GetPrerequisiteStatus(uint questId);
        public abstract IReadOnlyList<SafeTurnInEnderCandidate> FindLiveEnders(uint enderId);
        public abstract SafeTurnInEnderCandidate FindDatabaseEnder(uint enderId, string enderName);
        public abstract ISafeTurnInChild CreateChild(
            uint questId,
            string questName,
            SafeTurnInEnderCandidate candidate);
        public abstract QuestRecoveryDecision Report(QuestAttemptOutcome outcome, QuestRecoveryContext context);
        public abstract QuestRecoveryReportResult TryReportOwnedOutcome(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context);
        public abstract QuestRecoveryReportResult TryReportOwnedRedirect(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context);
        public abstract bool TryReportGeneratedFailures(
            IReadOnlyList<QuestAttemptOutcome> outcomes,
            QuestRecoveryContext context,
            out IReadOnlyList<QuestRecoveryDecision> decisions);
        public abstract bool OwnsAttempt(QuestRecoveryKey key, long generation);
        public abstract void AbandonAttempt(QuestRecoveryKey key, long generation);
        public abstract bool TryFlush();
        public abstract QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
            QuestRecoveryKey key,
            Func<QuestAbandonmentLiveSnapshot> recapture);
        public abstract void ClearBotPoi(string reason);
    }

    internal sealed class ProductionSafeTurnInRuntime : SafeTurnInRuntime
    {
        public override DateTime UtcNow { get { return DateTime.UtcNow; } }
        public override WoWPoint CurrentPlayerLocation
        {
            get { return StyxWoW.Me == null ? WoWPoint.Zero : StyxWoW.Me.Location; }
        }
        public override int CurrentMapId
        {
            get { return StyxWoW.Me == null ? 0 : unchecked((int)StyxWoW.Me.MapId); }
        }
        public override BotPoi CurrentBotPoi { get { return BotPoi.Current; } }

        public override void EnsureConfigured()
        {
            QuestRecoveryRuntime.EnsureConfigured();
        }

        public override QuestRecoveryContext CaptureContext(IReadOnlyList<int> objectiveAndItemCounts)
        {
            return QuestRecoveryRuntime.Capture(objectiveAndItemCounts);
        }

        public override QuestRecoveryDecision TryBeginAttempt(QuestRecoveryKey key, QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.TryBeginAttempt(key, context);
        }

        public override SafeTurnInQuestSnapshot GetQuestSnapshot(uint questId)
        {
            if (StyxWoW.Me == null)
            {
                return new SafeTurnInQuestSnapshot(
                    new QuestCompletionSnapshot(false, QuestCompletionState.Unknown),
                    Array.Empty<int>(), false, -1);
            }

            QuestCompletionSnapshot completion = StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(questId);
            int freeSlots;
            try
            {
                freeSlots = 25 - StyxWoW.Me.QuestLog.GetAllQuests().Count;
            }
            catch (Exception)
            {
                freeSlots = -1;
            }

            if (!completion.IsAccepted)
            {
                return new SafeTurnInQuestSnapshot(
                    completion,
                    Array.Empty<int>(),
                    completion.State != QuestCompletionState.Unknown && freeSlots >= 0,
                    freeSlots);
            }

            PlayerQuest quest = StyxWoW.Me.QuestLog.GetQuestById(questId);
            if (quest == null || !quest.GetData(out WoWDescriptorQuest data) || data.ObjectivesDone == null)
                return new SafeTurnInQuestSnapshot(completion, Array.Empty<int>(), false, freeSlots);

            var counts = data.ObjectivesDone.Select(count => (int)count).ToList();
            try
            {
                IEnumerable<int> requiredItemIds = quest.CollectItemIds
                    .Concat(quest.CollectIntermediateItemIds)
                    .Where(itemId => itemId > 0);
                foreach (int itemId in requiredItemIds)
                {
                    counts.Add(checked((int)StyxWoW.Me.CarriedItems
                        .Where(item => item.Entry == unchecked((uint)itemId))
                        .Sum(item => (long)item.StackCount)));
                }
            }
            catch (Exception)
            {
                return new SafeTurnInQuestSnapshot(completion, counts, false, freeSlots);
            }

            return new SafeTurnInQuestSnapshot(completion, counts, freeSlots >= 0, freeSlots);
        }

        public override QuestPrerequisiteStatus GetPrerequisiteStatus(uint questId)
        {
            if (StyxWoW.Me == null)
                return QuestPrerequisiteStatus.Unknown;
            try
            {
                return QuestPrerequisiteAuthority.Capture(
                    StyxWoW.Me.QuestLog.GetQuestById(questId));
            }
            catch (Exception ex)
            {
                Logging.WriteDiagnostic(
                    "[SafeTurnIn] Prerequisite authority failed for quest {0}: {1}",
                    questId,
                    ex);
                return QuestPrerequisiteStatus.Unknown;
            }
        }

        public override IReadOnlyList<SafeTurnInEnderCandidate> FindLiveEnders(uint enderId)
        {
            if (StyxWoW.Me == null)
                return Array.Empty<SafeTurnInEnderCandidate>();
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Cast<WoWObject>()
                .Concat(ObjectManager.GetObjectsOfType<WoWGameObject>())
                .Where(candidate => candidate.IsValid && candidate.Entry == enderId)
                .OrderBy(candidate => candidate.Location.DistanceSqr(StyxWoW.Me.Location))
                .Select(candidate => new SafeTurnInEnderCandidate(
                    candidate.Entry, candidate.Name, candidate.Location, "live world object"))
                .ToArray();
        }

        public override SafeTurnInEnderCandidate FindDatabaseEnder(uint enderId, string enderName)
        {
            NpcResult result = NpcQueries.GetNpcById(enderId);
            if (result == null || result.Location == WoWPoint.Zero)
                return null;
            return new SafeTurnInEnderCandidate(
                enderId, enderName, result.Location, "primary NPC database");
        }

        public override ISafeTurnInChild CreateChild(
            uint questId,
            string questName,
            SafeTurnInEnderCandidate candidate)
        {
            return new ForcedQuestTurnInChild(new ForcedQuestTurnIn(
                questId, questName, candidate.Entry, candidate.Name, candidate.Location));
        }

        public override QuestRecoveryDecision Report(QuestAttemptOutcome outcome, QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.Report(outcome, context);
        }

        public override QuestRecoveryReportResult TryReportOwnedOutcome(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.TryReportOwnedOutcome(outcome, context);
        }

        public override QuestRecoveryReportResult TryReportOwnedRedirect(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.TryReportOwnedRedirect(outcome, context);
        }

        public override bool TryReportGeneratedFailures(
            IReadOnlyList<QuestAttemptOutcome> outcomes,
            QuestRecoveryContext context,
            out IReadOnlyList<QuestRecoveryDecision> decisions)
        {
            return QuestRecoveryManager.Instance.TryReportGeneratedFailures(outcomes, context, out decisions);
        }

        public override bool OwnsAttempt(QuestRecoveryKey key, long generation)
        {
            return QuestRecoveryManager.Instance.OwnsAttempt(key, generation);
        }

        public override void AbandonAttempt(QuestRecoveryKey key, long generation)
        {
            QuestRecoveryManager.Instance.AbandonAttempt(key, generation);
        }

        public override bool TryFlush()
        {
            return QuestRecoveryManager.Instance.TryFlush();
        }

        public override QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
            QuestRecoveryKey key,
            Func<QuestAbandonmentLiveSnapshot> recapture)
        {
            return QuestRecoveryManager.Instance.TryExecuteAutomaticAbandonment(
                key,
                recapture,
                () => StyxWoW.Me.QuestLog.AbandonQuestById(key.QuestId));
        }

        public override void ClearBotPoi(string reason)
        {
            BotPoi.Clear(reason);
        }
    }

    internal sealed class ForcedQuestTurnInChild : ISafeTurnInChild
    {
        private readonly ForcedQuestTurnIn _inner;
        private Composite _activeBranch;
        private long _consumedCycleId;

        public ForcedQuestTurnInChild(ForcedQuestTurnIn inner)
        {
            _inner = inner;
        }

        public bool IsDone { get { return _inner.IsDone; } }
        public bool IsExecutionDeferred { get { return _inner.IsExecutionDeferred; } }
        public long InteractionCycleId { get { return _inner.InteractionCycleId; } }
        public bool HasVisibleDialog
        {
            get { return GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible; }
        }

        public void OnStart() { _inner.OnStart(); }

        public RunStatus Tick(object context)
        {
            Composite current = _inner.Branch;
            if (current == null)
                return RunStatus.Success;
            if (!ReferenceEquals(_activeBranch, current))
            {
                StopActive(context);
                _activeBranch = current;
                _activeBranch.Start(context);
            }
            RunStatus status = _activeBranch.Tick(context);
            if (status != RunStatus.Running)
                StopActive(context);
            return status;
        }

        public bool TryConsumeOutcome(out SafeTurnInDialogOutcome outcome)
        {
            outcome = null;
            long cycleId = _inner.InteractionCycleId;
            if (cycleId <= 0 || cycleId == _consumedCycleId || !HasVisibleDialog)
                return false;

            uint shownQuestId = QuestFrame.Instance.IsVisible
                ? QuestFrame.Instance.CurrentShownQuestId
                : 0;
            IReadOnlyList<uint> offeredQuestIds = ReadActiveQuestIds();
            if (shownQuestId == _inner.QuestId || offeredQuestIds.Contains(_inner.QuestId))
                return false;

            _consumedCycleId = cycleId;
            outcome = new SafeTurnInDialogOutcome
            {
                InteractionCycleId = cycleId,
                ShownQuestId = shownQuestId,
                OfferedQuestIds = offeredQuestIds
            };
            return true;
        }

        public void Dispose() { StopActive(null); }

        private static IReadOnlyList<uint> ReadActiveQuestIds()
        {
            var result = new List<uint>();
            if (GossipFrame.Instance.IsVisible)
            {
                IEnumerable<uint> gossipIds = (GossipFrame.Instance.ActiveQuests
                    ?? new List<GossipQuestEntry>())
                    .Where(entry => entry != null && entry.IsValid && entry.Id > 0)
                    .Select(entry => unchecked((uint)entry.Id));
                result.AddRange(gossipIds);
            }
            if (QuestFrame.Instance.IsVisible)
                result.AddRange(QuestFrame.Instance.ActiveQuests);
            return Array.AsReadOnly(result.Distinct().ToArray());
        }

        private void StopActive(object context)
        {
            if (_activeBranch != null && _activeBranch.IsRunning)
                _activeBranch.Stop(context);
            _activeBranch = null;
        }
    }

    /// <summary>Runs a quest turn-in under one bounded, stage-owned recovery episode.</summary>
    public class SafeTurnIn : CustomForcedBehavior
    {
        private const double EndpointCellSize = 80.0;
        private const int MaximumCandidates = 5;
        private const int MaximumInteractionCycles = 3;

        private readonly SafeTurnInRuntime _runtime;
        private readonly List<SafeTurnInEnderCandidate> _enderCandidates = new();
        private readonly List<QuestAttemptOutcome> _pendingNarrowFailures = new();
        private readonly HashSet<string> _consumedInteractionCycles = new(StringComparer.Ordinal);
        private readonly HashSet<string> _consumedDialogCycles = new(StringComparer.Ordinal);
        private ISafeTurnInChild _turnIn;
        private SafeTurnInEnderCandidate _currentCandidate;
        private DateTime _candidateStartedAt;
        private DateTime? _dialogSilenceSince;
        private DateTime? _pausedAt;
        private Composite _root;
        private QuestRecoveryKey _attemptKey;
        private QuestRecoveryContext _recoveryContext;
        private BotPoi _installedPoi;
        private long _attemptGeneration;
        private int _episodeMapId;
        private int _candidateIndex = -1;
        private int _childSerial;
        private int _confirmedInteractionCycles;
        private bool _candidatesInitialized;
        private bool _attemptOwned;
        private bool _terminalOutcomeReported;
        private bool _questStateDiagnosticLogged;
        private bool _isDone;

        public SafeTurnIn(Dictionary<string, string> args)
            : this(args, new ProductionSafeTurnInRuntime()) { }

        internal SafeTurnIn(Dictionary<string, string> args, SafeTurnInRuntime runtime)
            : base(args)
        {
            _runtime = runtime ?? throw new ArgumentNullException("runtime");
            try
            {
                QuestId = (uint)(GetAttributeAsNullable<int>(
                    "QuestId", true, ConstrainAs.QuestId(this), null) ?? 0);
                QuestName = GetAttributeAs<string>(
                    "QuestName", true, ConstrainAs.StringNonEmpty, null) ?? string.Empty;
                TurnInId = (uint)(GetAttributeAsNullable<int>(
                    "TurnInId", true, ConstrainAs.MobId, null) ?? 0);
                TurnInName = GetAttributeAs<string>(
                    "TurnInName", true, ConstrainAs.StringNonEmpty, null) ?? string.Empty;
                TimeoutSeconds = GetAttributeAsNullable<int>(
                    "TimeoutSeconds", false, new ConstrainTo.Domain<int>(5, 300), null) ?? 30;
                ArrivalDistance = GetAttributeAsNullable<double>(
                    "ArrivalDistance", false, new ConstrainTo.Domain<double>(5.0, 50.0), null) ?? 15.0;
                NavigationTimeoutSeconds = GetAttributeAsNullable<int>(
                    "NavigationTimeoutSeconds", false,
                    new ConstrainTo.Domain<int>(30, 900), null) ?? 180;
                AlternateEnders = GetAttributeAs<string>(
                    "AlternateEnders", false, ConstrainAs.StringNonEmpty, null) ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogMessage("error", "SafeTurnIn attribute error: " + ex.Message);
                IsAttributeProblem = true;
            }
        }

        public uint QuestId { get; private set; }
        public string QuestName { get; private set; }
        public uint TurnInId { get; private set; }
        public string TurnInName { get; private set; }
        public int TimeoutSeconds { get; private set; }
        public double ArrivalDistance { get; private set; }
        public int NavigationTimeoutSeconds { get; private set; }
        public string AlternateEnders { get; private set; }
        public override bool IsDone { get { return _isDone; } }

        internal static QuestRecoveryKey CreateEndpointKey(uint questId, int mapId, WoWPoint location)
        {
            int cellX = (int)Math.Floor(location.X / EndpointCellSize);
            int cellY = (int)Math.Floor(location.Y / EndpointCellSize);
            return QuestRecoveryKey.ForEndpoint(
                questId,
                QuestRecoveryStage.Navigation,
                mapId,
                string.Format(CultureInfo.InvariantCulture, "cell:{0}:{1}", cellX, cellY));
        }

        public static QuestAttemptOutcome CreateIncompleteRedirect(uint questId)
        {
            return CreateIncompleteRedirect(questId, null, 0);
        }

        internal static QuestAttemptOutcome CreateIncompleteRedirect(
            uint questId,
            QuestRecoveryKey attemptKey,
            long attemptGeneration)
        {
            if (attemptKey != null)
                ValidateTurnInOwner(questId, attemptKey, attemptGeneration);
            return new QuestAttemptOutcome
            {
                Key = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.TurnIn),
                AttemptKey = attemptKey,
                AttemptGeneration = attemptGeneration,
                Kind = QuestAttemptOutcomeKind.Redirect,
                Reason = QuestFailureReason.TurnInQuestIncomplete,
                IsFailureEpisode = false,
                Evidence = "turn-in reached before objectives completed"
            };
        }

        internal static QuestAttemptOutcome CreateUnavailableOutcome(
            uint questId,
            uint shownQuestId,
            uint enderId,
            IReadOnlyList<uint> offeredQuestIds,
            string evidence,
            bool isFailureEpisode,
            long interactionCycleId,
            QuestRecoveryKey attemptKey,
            long attemptGeneration)
        {
            ValidateTurnInOwner(questId, attemptKey, attemptGeneration);
            if (interactionCycleId <= 0)
                throw new ArgumentOutOfRangeException("interactionCycleId");
            return new QuestAttemptOutcome
            {
                Key = QuestRecoveryKey.ForNpc(questId, QuestRecoveryStage.TurnIn, enderId),
                AttemptKey = attemptKey,
                AttemptGeneration = attemptGeneration,
                Kind = isFailureEpisode
                    ? QuestAttemptOutcomeKind.Failure
                    : QuestAttemptOutcomeKind.Observation,
                Reason = QuestFailureReason.TurnInTargetNotOffered,
                IsFailureEpisode = isFailureEpisode,
                Evidence = evidence ?? string.Empty,
                InteractionCycleId = interactionCycleId,
                ObservedQuestId = shownQuestId,
                OfferedQuestIds = offeredQuestIds ?? Array.Empty<uint>()
            };
        }

        private static void ValidateTurnInOwner(uint questId, QuestRecoveryKey attemptKey, long generation)
        {
            if (attemptKey == null)
                throw new ArgumentNullException("attemptKey");
            if (attemptKey.QuestId != questId ||
                attemptKey.Stage != QuestRecoveryStage.TurnIn ||
                attemptKey.Scope != QuestRecoveryScope.QuestStage)
                throw new ArgumentException(
                    "Turn-in outcomes require their exact turn-in quest-stage owner.", "attemptKey");
            if (generation <= 0)
                throw new ArgumentOutOfRangeException("generation");
        }

        public override void OnStart()
        {
            OnStart_HandleAttributeProblem();
            if (IsAttributeProblem)
            {
                _isDone = true;
                return;
            }

            try
            {
                _runtime.EnsureConfigured();
                SafeTurnInQuestSnapshot snapshot = GetQuestSnapshot();
                _recoveryContext = _runtime.CaptureContext(snapshot.ObjectiveAndItemCounts);
                _attemptKey = QuestRecoveryKey.ForQuestStage(QuestId, QuestRecoveryStage.TurnIn);
                QuestRecoveryDecision claim = _runtime.TryBeginAttempt(_attemptKey, _recoveryContext);
                if (!claim.MayAttempt)
                {
                    Logging.Write(
                        Color.Orange,
                        "[SafeTurnIn] Quest {0} ({1}) turn-in deferred by recovery state {2}: {3}",
                        QuestName, QuestId, claim.State, claim.Status);
                    ApplyAbandonmentPolicy();
                    _isDone = true;
                    return;
                }

                _attemptGeneration = claim.AttemptGeneration;
                _attemptOwned = true;
                _episodeMapId = _runtime.CurrentMapId;
                ContinueFromSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] Could not initialize quest {0} ({1}): {2}",
                    QuestName, QuestId, ex);
                ReportStageFailure(
                    QuestFailureReason.InternalBehaviorError,
                    "SafeTurnIn initialization failed: " + ex.Message);
            }
        }

        protected override Composite CreateBehavior()
        {
            return _root ?? (_root = new Action(context => TickCore(context)));
        }

        internal RunStatus TickForTesting() { return TickCore(null); }

        private RunStatus TickCore(object context)
        {
            if (_isDone || !_attemptOwned)
                return RunStatus.Success;

            SafeTurnInQuestSnapshot snapshot = GetQuestSnapshot();
            if (snapshot.Completion.State == QuestCompletionState.Unknown)
            {
                PauseTimers();
                return RunStatus.Running;
            }

            ResumeTimers();
            _recoveryContext = _runtime.CaptureContext(snapshot.ObjectiveAndItemCounts);
            if (ContinueFromSnapshot(snapshot) || _isDone)
                return RunStatus.Success;

            bool childDone = _turnIn.IsDone;
            if (_turnIn.IsExecutionDeferred)
            {
                PauseTimers();
                return RunStatus.Running;
            }
            ResumeTimers();

            OutcomeProcessing result = ProcessDialogOutcome();
            if (result != OutcomeProcessing.None || _isDone)
                return RunStatus.Success;
            if (childDone)
                return HandleChildDone();
            if (_confirmedInteractionCycles >= MaximumInteractionCycles && !_turnIn.HasVisibleDialog)
            {
                HandleInteractionBudgetExhausted();
                return RunStatus.Success;
            }
            if (HasInteractionTimedOut())
            {
                HandleInteractionTimeout();
                return RunStatus.Success;
            }
            if (HasNavigationTimedOut())
            {
                HandleNavigationTimeout();
                return RunStatus.Success;
            }

            BotPoi before = _runtime.CurrentBotPoi;
            RunStatus status = _turnIn.Tick(context);
            TrackInstalledPoi(before, _runtime.CurrentBotPoi);
            ObserveChildInteractionCycles();
            return status;
        }

        private bool ContinueFromSnapshot(SafeTurnInQuestSnapshot snapshot)
        {
            if (snapshot.Completion.State == QuestCompletionState.Unknown)
            {
                PauseTimers();
                return true;
            }
            if (!snapshot.Completion.IsAccepted)
            {
                if (snapshot.Completion.State == QuestCompletionState.KnownComplete)
                    ReportSuccess("quest turn-in completed");
                else
                    ReleaseOwnedAttempt();
                ClearInstalledPoi();
                _isDone = true;
                return true;
            }
            if (snapshot.Completion.State == QuestCompletionState.KnownIncomplete)
            {
                ReportRedirect();
                _isDone = true;
                return true;
            }

            if (!_candidatesInitialized)
            {
                InitializeCandidates();
                return _isDone || _turnIn == null;
            }
            return false;
        }

        private RunStatus HandleChildDone()
        {
            SafeTurnInQuestSnapshot refreshed = GetQuestSnapshot();
            if (refreshed.Completion.State == QuestCompletionState.Unknown)
            {
                PauseTimers();
                return RunStatus.Running;
            }
            _recoveryContext = _runtime.CaptureContext(refreshed.ObjectiveAndItemCounts);
            if (!refreshed.Completion.IsAccepted &&
                refreshed.Completion.State == QuestCompletionState.KnownComplete)
                ReportSuccess("quest turn-in completed");
            else if (refreshed.Completion.IsAccepted &&
                     refreshed.Completion.State == QuestCompletionState.KnownIncomplete)
                ReportRedirect();
            else
                ReleaseOwnedAttempt();
            ClearInstalledPoi();
            _isDone = true;
            return RunStatus.Success;
        }

        private SafeTurnInQuestSnapshot GetQuestSnapshot()
        {
            try
            {
                return _runtime.GetQuestSnapshot(QuestId);
            }
            catch (Exception ex)
            {
                if (!_questStateDiagnosticLogged)
                {
                    Logging.Write(
                        Color.Orange,
                        "[SafeTurnIn] Quest state authority is temporarily unavailable for {0} ({1}): {2}",
                        QuestName, QuestId, ex.Message);
                    _questStateDiagnosticLogged = true;
                }
                return new SafeTurnInQuestSnapshot(
                    new QuestCompletionSnapshot(false, QuestCompletionState.Unknown),
                    Array.Empty<int>(), false, -1);
            }
        }

        private void InitializeCandidates()
        {
            if (_candidatesInitialized)
                return;
            _candidatesInitialized = true;
            BuildEnderCandidates();
            if (_enderCandidates.Count == 0)
            {
                ReportMissingEnder();
                return;
            }
            StartNextCandidate();
        }

        private void BuildEnderCandidates()
        {
            QuestRelationParseResult parsed = QuestRelationParser.Parse(AlternateEnders);
            foreach (string diagnostic in parsed.Diagnostics)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] AlternateEnders for quest {0} ({1}): {2}",
                    QuestName, QuestId, diagnostic);
            }

            try
            {
                foreach (SafeTurnInEnderCandidate live in
                    _runtime.FindLiveEnders(TurnInId) ?? Array.Empty<SafeTurnInEnderCandidate>())
                    AddCandidate(live);
            }
            catch (Exception ex)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] Live ender lookup failed for {0} ({1}): {2}",
                    TurnInName, TurnInId, ex.Message);
            }

            try
            {
                AddCandidate(_runtime.FindDatabaseEnder(TurnInId, TurnInName));
            }
            catch (Exception ex)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] NPC database lookup failed for ender {0} ({1}): {2}",
                    TurnInName, TurnInId, ex.Message);
            }

            foreach (QuestRelation alternate in parsed.Relations)
            {
                AddCandidate(new SafeTurnInEnderCandidate(
                    alternate.Entry,
                    alternate.Entry == TurnInId ? TurnInName : "alternate ender " + alternate.Entry,
                    new WoWPoint((float)alternate.X, (float)alternate.Y, (float)alternate.Z),
                    "AlternateEnders"));
            }
        }

        private void AddCandidate(SafeTurnInEnderCandidate candidate)
        {
            if (candidate == null || candidate.Location == WoWPoint.Zero ||
                _enderCandidates.Count >= MaximumCandidates)
                return;
            QuestRecoveryKey endpoint = CreateEndpointKey(QuestId, _episodeMapId, candidate.Location);
            if (_enderCandidates.Any(existing =>
                    CreateEndpointKey(QuestId, _episodeMapId, existing.Location).Equals(endpoint)))
                return;
            _enderCandidates.Add(candidate);
        }

        private bool StartNextCandidate()
        {
            while (++_candidateIndex < _enderCandidates.Count)
            {
                SafeTurnInEnderCandidate candidate = _enderCandidates[_candidateIndex];
                try
                {
                    ClearInstalledPoi();
                    if (_turnIn != null)
                        _turnIn.Dispose();
                    _turnIn = null;
                    _currentCandidate = candidate;
                    _childSerial++;
                    _candidateStartedAt = _runtime.UtcNow;
                    _dialogSilenceSince = null;
                    _pausedAt = null;
                    BotPoi before = _runtime.CurrentBotPoi;
                    _turnIn = _runtime.CreateChild(QuestId, QuestName, candidate);
                    _turnIn.OnStart();
                    TrackInstalledPoi(before, _runtime.CurrentBotPoi);
                    Logging.Write(
                        "[SafeTurnIn] Trying ender {0} ({1}) at {2} from {3} for quest {4} ({5}).",
                        candidate.Name, candidate.Entry, candidate.Location, candidate.Source, QuestName, QuestId);
                    return true;
                }
                catch (Exception ex)
                {
                    AddPendingFailure(CreateOwnedFailure(
                        QuestRecoveryKey.ForNpc(QuestId, QuestRecoveryStage.TurnIn, candidate.Entry),
                        QuestFailureReason.InternalBehaviorError,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "ender={0}; source={1}; initialization={2}",
                            candidate.Entry, candidate.Source, ex.Message)));
                }
            }

            QuestAttemptOutcome finalFailure = _pendingNarrowFailures.LastOrDefault()
                ?? CreateOwnedFailure(
                    QuestRecoveryKey.ForNpc(QuestId, QuestRecoveryStage.TurnIn, TurnInId),
                    QuestFailureReason.InternalBehaviorError,
                    "all turn-in ender candidates failed to initialize");
            ReportExhausted(
                finalFailure,
                QuestFailureReason.InternalBehaviorError,
                "all turn-in ender candidates failed to initialize");
            return false;
        }

        private OutcomeProcessing ProcessDialogOutcome()
        {
            if (_turnIn == null ||
                !_turnIn.TryConsumeOutcome(out SafeTurnInDialogOutcome source) ||
                source == null || source.InteractionCycleId <= 0)
                return OutcomeProcessing.None;

            string cycleKey = string.Format(
                CultureInfo.InvariantCulture, "{0}:{1}", _childSerial, source.InteractionCycleId);
            if (!_consumedDialogCycles.Add(cycleKey))
                return OutcomeProcessing.Observed;
            if (_consumedInteractionCycles.Add(cycleKey))
                _confirmedInteractionCycles++;
            _dialogSilenceSince = _runtime.UtcNow;

            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; shown={1}; ender={2}; offered=[{3}]; cycle={4}",
                QuestId,
                source.ShownQuestId,
                _currentCandidate.Entry,
                string.Join(",", source.OfferedQuestIds),
                source.InteractionCycleId);
            QuestAttemptOutcome sample = CreateUnavailableOutcome(
                QuestId,
                source.ShownQuestId,
                _currentCandidate.Entry,
                source.OfferedQuestIds,
                evidence,
                false,
                source.InteractionCycleId,
                _attemptKey,
                _attemptGeneration);
            _runtime.Report(sample, _recoveryContext);

            QuestAttemptOutcome failure = CreateUnavailableOutcome(
                QuestId,
                source.ShownQuestId,
                _currentCandidate.Entry,
                source.OfferedQuestIds,
                evidence,
                true,
                source.InteractionCycleId,
                _attemptKey,
                _attemptGeneration);
            AddPendingFailure(failure);
            if (_confirmedInteractionCycles < MaximumInteractionCycles &&
                _candidateIndex + 1 < _enderCandidates.Count)
            {
                StartNextCandidate();
                return _isDone ? OutcomeProcessing.Terminal : OutcomeProcessing.Observed;
            }
            if (_confirmedInteractionCycles >= MaximumInteractionCycles)
            {
                ReportExhausted(
                    failure,
                    QuestFailureReason.TurnInTargetNotOffered,
                    "three confirmed turn-in interaction cycles exhausted; " + evidence);
                return OutcomeProcessing.Terminal;
            }
            return OutcomeProcessing.Observed;
        }

        private void ObserveChildInteractionCycles()
        {
            if (_turnIn == null || _turnIn.InteractionCycleId <= 0)
                return;
            string cycleKey = string.Format(
                CultureInfo.InvariantCulture, "{0}:{1}", _childSerial, _turnIn.InteractionCycleId);
            if (_consumedInteractionCycles.Add(cycleKey))
                _confirmedInteractionCycles++;
        }

        private bool HasNavigationTimedOut()
        {
            return _turnIn != null &&
                _runtime.UtcNow - _candidateStartedAt >= TimeSpan.FromSeconds(NavigationTimeoutSeconds);
        }

        private bool HasInteractionTimedOut()
        {
            if (_turnIn == null)
                return false;
            double radiusSquared = ArrivalDistance * ArrivalDistance;
            if (_runtime.CurrentPlayerLocation.DistanceSqr(_currentCandidate.Location) > radiusSquared)
            {
                _dialogSilenceSince = null;
                return false;
            }
            if (!_dialogSilenceSince.HasValue)
            {
                _dialogSilenceSince = _runtime.UtcNow;
                return false;
            }
            return _runtime.UtcNow - _dialogSilenceSince.Value >= TimeSpan.FromSeconds(TimeoutSeconds);
        }

        private void HandleInteractionBudgetExhausted()
        {
            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; ender={1}; interactionCycles={2}",
                QuestId, _currentCandidate.Entry, _confirmedInteractionCycles);
            QuestAttemptOutcome failure = CreateOwnedFailure(
                QuestRecoveryKey.ForNpc(QuestId, QuestRecoveryStage.TurnIn, _currentCandidate.Entry),
                QuestFailureReason.InteractionTimedOut,
                evidence);
            ReportExhausted(
                failure,
                QuestFailureReason.InteractionTimedOut,
                "turn-in interaction cycle budget exhausted; " + evidence);
        }

        private void HandleInteractionTimeout()
        {
            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; ender={1}; interactionTimeoutSeconds={2}",
                QuestId, _currentCandidate.Entry, TimeoutSeconds);
            QuestAttemptOutcome failure = CreateOwnedFailure(
                QuestRecoveryKey.ForNpc(QuestId, QuestRecoveryStage.TurnIn, _currentCandidate.Entry),
                QuestFailureReason.InteractionTimedOut,
                evidence);
            AddPendingFailure(failure);
            if (_candidateIndex + 1 < _enderCandidates.Count &&
                _confirmedInteractionCycles < MaximumInteractionCycles)
                StartNextCandidate();
            else
                ReportExhausted(
                    failure,
                    QuestFailureReason.InteractionTimedOut,
                    "all turn-in ender interactions timed out; " + evidence);
        }

        private void HandleNavigationTimeout()
        {
            QuestRecoveryKey endpointKey = CreateEndpointKey(
                QuestId, _episodeMapId, _currentCandidate.Location);
            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; ender={1}; map={2}; endpoint={3}; timeoutSeconds={4}",
                QuestId,
                _currentCandidate.Entry,
                _episodeMapId,
                endpointKey.Endpoint,
                NavigationTimeoutSeconds);
            QuestAttemptOutcome failure = CreateOwnedFailure(
                endpointKey, QuestFailureReason.PathGenerationFailed, evidence);
            AddPendingFailure(failure);
            if (_candidateIndex + 1 < _enderCandidates.Count)
                StartNextCandidate();
            else
                ReportExhausted(
                    failure,
                    QuestFailureReason.EndpointUnreachable,
                    "all turn-in ender endpoints exhausted; " + evidence);
        }

        private void ReportMissingEnder()
        {
            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; ender={1}; live=false; database=false; alternateCount=0",
                QuestId, TurnInId);
            QuestAttemptOutcome relation = CreateOwnedFailure(
                QuestRecoveryKey.ForNpc(QuestId, QuestRecoveryStage.TurnIn, TurnInId),
                QuestFailureReason.NpcMissingFromDatabase,
                evidence);
            ReportExhausted(relation, QuestFailureReason.NpcMissingFromDatabase, evidence);
        }

        private QuestAttemptOutcome CreateOwnedFailure(
            QuestRecoveryKey key,
            QuestFailureReason reason,
            string evidence)
        {
            return QuestAttemptOutcome.Failure(
                key, _attemptKey, _attemptGeneration, reason, evidence);
        }

        private void AddPendingFailure(QuestAttemptOutcome outcome)
        {
            int existing = _pendingNarrowFailures.FindIndex(item => item.Key.Equals(outcome.Key));
            if (existing >= 0)
                _pendingNarrowFailures[existing] = outcome;
            else
                _pendingNarrowFailures.Add(outcome);
        }

        private void ReportExhausted(
            QuestAttemptOutcome narrowFailure,
            QuestFailureReason stageReason,
            string stageEvidence)
        {
            AddPendingFailure(narrowFailure);
            var failures = new List<QuestAttemptOutcome>(_pendingNarrowFailures)
            {
                QuestAttemptOutcome.Failure(
                    _attemptKey,
                    _attemptKey,
                    _attemptGeneration,
                    stageReason,
                    stageEvidence)
            };
            bool reported = false;
            try
            {
                reported = _runtime.TryReportGeneratedFailures(
                    failures,
                    _recoveryContext,
                    out IReadOnlyList<QuestRecoveryDecision> decisions);
                if (!reported)
                {
                    Logging.Write(
                        Color.Orange,
                        "[SafeTurnIn] Recovery rejected stale or contended terminal outcome for quest {0} ({1}).",
                        QuestName, QuestId);
                }
                else
                {
                    TryFlushRecovery("accepted terminal turn-in failure");
                    ApplyAbandonmentPolicy();
                }
            }
            catch (Exception ex)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] Terminal recovery action failed for quest {0} ({1}): {2}",
                    QuestName, QuestId, ex);
            }
            finally
            {
                if (reported)
                {
                    _terminalOutcomeReported = true;
                    _attemptOwned = false;
                    ClearInstalledPoi();
                }
                else
                {
                    _installedPoi = null;
                }
                _isDone = true;
            }
        }

        private void ReportStageFailure(QuestFailureReason reason, string evidence)
        {
            if (!_attemptOwned)
            {
                _isDone = true;
                return;
            }
            QuestRecoveryReportResult result = _runtime.TryReportOwnedOutcome(
                QuestAttemptOutcome.Failure(
                    _attemptKey, _attemptKey, _attemptGeneration, reason, evidence),
                _recoveryContext);
            if (!result.Accepted)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] Recovery rejected stale stage failure for quest {0} ({1}).",
                    QuestName, QuestId);
                _installedPoi = null;
                _isDone = true;
                return;
            }
            _terminalOutcomeReported = true;
            _attemptOwned = false;
            TryFlushRecovery("accepted turn-in stage failure");
            ClearInstalledPoi();
            _isDone = true;
        }

        private void ReportRedirect()
        {
            if (!_attemptOwned)
                return;
            QuestRecoveryReportResult result = _runtime.TryReportOwnedRedirect(
                CreateIncompleteRedirect(QuestId, _attemptKey, _attemptGeneration),
                _recoveryContext);
            if (!result.Accepted)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] Recovery rejected stale or malformed incomplete redirect for quest {0} ({1}).",
                    QuestName, QuestId);
                _installedPoi = null;
                return;
            }
            _terminalOutcomeReported = true;
            _attemptOwned = false;
            TryFlushRecovery("accepted turn-in redirect");
            ClearInstalledPoi();
        }

        private void ReportSuccess(string evidence)
        {
            if (!_attemptOwned)
                return;
            QuestRecoveryReportResult result = _runtime.TryReportOwnedOutcome(
                QuestAttemptOutcome.Success(_attemptKey, _attemptGeneration, evidence),
                _recoveryContext);
            if (!result.Accepted)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] Recovery rejected stale success for quest {0} ({1}).",
                    QuestName, QuestId);
                _installedPoi = null;
                _isDone = true;
                return;
            }
            _terminalOutcomeReported = true;
            _attemptOwned = false;
            TryFlushRecovery("accepted turn-in success");
            ClearInstalledPoi();
        }

        private void ApplyAbandonmentPolicy()
        {
            QuestAbandonmentDecision decision = _runtime.TryExecuteAutomaticAbandonment(
                _attemptKey,
                () =>
                {
                    SafeTurnInQuestSnapshot snapshot = GetQuestSnapshot();
                    return new QuestAbandonmentLiveSnapshot
                    {
                        IsAccepted = snapshot.Completion.IsAccepted,
                        IsCompleted = snapshot.Completion.State == QuestCompletionState.KnownComplete,
                        StateIsCertain = snapshot.Completion.State != QuestCompletionState.Unknown &&
                            snapshot.ProgressStateIsCertain,
                        HasObjectiveProgress = snapshot.HasObjectiveProgress,
                        PrerequisiteStatus = _runtime.GetPrerequisiteStatus(QuestId),
                        FreeQuestLogSlots = snapshot.FreeQuestLogSlots,
                        RecoveryContext = _runtime.CaptureContext(snapshot.ObjectiveAndItemCounts)
                    };
                });
            Logging.Write(Color.Orange, "[SafeTurnIn] {0}", decision.Reason);
            if (!decision.MayAbandon)
                return;
            Logging.Write(
                Color.Orange,
                "[SafeTurnIn] Automatically abandoned quest {0} ({1}): {2}",
                QuestName, QuestId, decision.Reason);
        }

        private void PauseTimers()
        {
            if (!_pausedAt.HasValue)
                _pausedAt = _runtime.UtcNow;
        }

        private void ResumeTimers()
        {
            if (!_pausedAt.HasValue)
                return;
            TimeSpan paused = _runtime.UtcNow - _pausedAt.Value;
            if (_candidateStartedAt != default(DateTime))
                _candidateStartedAt = _candidateStartedAt.Add(paused);
            if (_dialogSilenceSince.HasValue)
                _dialogSilenceSince = _dialogSilenceSince.Value.Add(paused);
            _pausedAt = null;
        }

        private void TrackInstalledPoi(BotPoi before, BotPoi after)
        {
            if (!ReferenceEquals(before, after) && IsExactCurrentCandidatePoi(after))
                _installedPoi = after;
        }

        private bool IsExactCurrentCandidatePoi(BotPoi poi)
        {
            return poi != null &&
                poi.Type == PoiType.QuestTurnIn &&
                _currentCandidate != null &&
                poi.Entry == _currentCandidate.Entry &&
                poi.Location.DistanceSqr(_currentCandidate.Location) < 0.01;
        }

        private void ClearInstalledPoi()
        {
            if (_installedPoi != null &&
                ReferenceEquals(_runtime.CurrentBotPoi, _installedPoi) &&
                IsExactCurrentCandidatePoi(_installedPoi))
                _runtime.ClearBotPoi("SafeTurnIn released its exact turn-in POI");
            _installedPoi = null;
        }

        private void ReleaseOwnedAttempt()
        {
            if (!_attemptOwned || _attemptKey == null || _attemptGeneration <= 0)
                return;
            _runtime.AbandonAttempt(_attemptKey, _attemptGeneration);
            _attemptOwned = false;
            TryFlushRecovery("released turn-in attempt");
        }

        private void TryFlushRecovery(string reason)
        {
            if (!_runtime.TryFlush())
                Logging.Write(
                    Color.Orange,
                    "[SafeTurnIn] Recovery persistence remains pending after {0} for quest {1} ({2}).",
                    reason,
                    QuestName,
                    QuestId);
        }

        public override void Dispose()
        {
            if (_turnIn != null)
                _turnIn.Dispose();
            _turnIn = null;
            ClearInstalledPoi();
            if (!_terminalOutcomeReported)
                ReleaseOwnedAttempt();
            base.Dispose();
        }

        private enum OutcomeProcessing
        {
            None,
            Observed,
            Terminal
        }
    }
}
