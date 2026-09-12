using Bots.Quest.QuestOrder;
using Styx;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
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
    internal sealed class SafePickUpGiverCandidate
    {
        public SafePickUpGiverCandidate(
            uint entry,
            string name,
            WoWPoint location,
            QuestObjectType type,
            string source)
        {
            Entry = entry;
            Name = name;
            Location = location;
            Type = type;
            Source = source;
        }

        public uint Entry { get; private set; }
        public string Name { get; private set; }
        public WoWPoint Location { get; private set; }
        public QuestObjectType Type { get; private set; }
        public string Source { get; private set; }
    }

    internal interface ISafePickUpChild : IDisposable
    {
        bool IsDone { get; }
        bool IsExecutionDeferred { get; }
        void OnStart();
        RunStatus Tick(object context);
        bool TryConsumeOutcome(out QuestAttemptOutcome outcome);
    }

    internal abstract class SafePickUpRuntime
    {
        public abstract DateTime UtcNow { get; }
        public abstract WoWPoint CurrentPlayerLocation { get; }
        public abstract int CurrentMapId { get; }
        public abstract BotPoi CurrentBotPoi { get; }
        public abstract void EnsureConfigured();
        public abstract QuestRecoveryContext CaptureContext();
        public abstract QuestRecoveryDecision TryBeginAttempt(
            QuestRecoveryKey key,
            QuestRecoveryContext context);
        public abstract QuestCompletionSnapshot GetQuestCompletionSnapshot(uint questId);
        public abstract IReadOnlyList<SafePickUpGiverCandidate> FindLiveGivers(uint giverId);
        public abstract SafePickUpGiverCandidate FindDatabaseGiver(uint giverId, string giverName);
        public abstract ISafePickUpChild CreateChild(
            uint questId,
            string questName,
            SafePickUpGiverCandidate candidate);
        public abstract QuestRecoveryDecision Report(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context);
        public abstract bool TryReportGeneratedFailures(
            IReadOnlyList<QuestAttemptOutcome> outcomes,
            QuestRecoveryContext context,
            out IReadOnlyList<QuestRecoveryDecision> decisions);
        public abstract bool OwnsAttempt(QuestRecoveryKey key, long generation);
        public abstract void AbandonAttempt(QuestRecoveryKey key, long generation);
        public abstract bool TryFlush();
        public abstract void ClearBotPoi(string reason);
    }

    internal sealed class ProductionSafePickUpRuntime : SafePickUpRuntime
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

        public override QuestRecoveryContext CaptureContext()
        {
            return QuestRecoveryRuntime.Capture();
        }

        public override QuestRecoveryDecision TryBeginAttempt(
            QuestRecoveryKey key,
            QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.TryBeginAttempt(key, context);
        }

        public override QuestCompletionSnapshot GetQuestCompletionSnapshot(uint questId)
        {
            if (StyxWoW.Me == null)
                return new QuestCompletionSnapshot(false, QuestCompletionState.Unknown);
            return StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(questId);
        }

        public override IReadOnlyList<SafePickUpGiverCandidate> FindLiveGivers(uint giverId)
        {
            if (StyxWoW.Me == null)
                return Array.Empty<SafePickUpGiverCandidate>();

            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Cast<WoWObject>()
                .Concat(ObjectManager.GetObjectsOfType<WoWGameObject>())
                .Where(candidate => candidate.IsValid && candidate.Entry == giverId)
                .OrderBy(candidate => candidate.Location.DistanceSqr(StyxWoW.Me.Location))
                .Select(candidate => new SafePickUpGiverCandidate(
                    candidate.Entry,
                    candidate.Name,
                    candidate.Location,
                    candidate is WoWGameObject
                        ? QuestObjectType.GameObject
                        : QuestObjectType.Npc,
                    "live world object"))
                .ToArray();
        }

        public override SafePickUpGiverCandidate FindDatabaseGiver(uint giverId, string giverName)
        {
            NpcResult result = NpcQueries.GetNpcById(giverId);
            if (result == null || result.Location == WoWPoint.Zero)
                return null;
            return new SafePickUpGiverCandidate(
                giverId, giverName, result.Location, QuestObjectType.Npc, "primary NPC database");
        }

        public override ISafePickUpChild CreateChild(
            uint questId,
            string questName,
            SafePickUpGiverCandidate candidate)
        {
            return new ForcedQuestPickUpChild(new ForcedQuestPickUp(
                questId,
                questName,
                candidate.Entry,
                candidate.Name,
                candidate.Location,
                candidate.Type));
        }

        public override QuestRecoveryDecision Report(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.Report(outcome, context);
        }

        public override bool TryReportGeneratedFailures(
            IReadOnlyList<QuestAttemptOutcome> outcomes,
            QuestRecoveryContext context,
            out IReadOnlyList<QuestRecoveryDecision> decisions)
        {
            return QuestRecoveryManager.Instance.TryReportGeneratedFailures(
                outcomes, context, out decisions);
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

        public override void ClearBotPoi(string reason)
        {
            BotPoi.Clear(reason);
        }
    }

    internal sealed class ForcedQuestPickUpChild : ISafePickUpChild
    {
        private readonly ForcedQuestPickUp _inner;
        private Composite _activeBranch;

        public ForcedQuestPickUpChild(ForcedQuestPickUp inner)
        {
            _inner = inner;
        }

        public bool IsDone { get { return _inner.IsDone; } }
        public bool IsExecutionDeferred { get { return _inner.IsExecutionDeferred; } }

        public void OnStart()
        {
            _inner.OnStart();
        }

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

        public bool TryConsumeOutcome(out QuestAttemptOutcome outcome)
        {
            return _inner.TryConsumeOutcome(out outcome);
        }

        public void Dispose()
        {
            StopActive(null);
            _inner.Dispose();
        }

        private void StopActive(object context)
        {
            if (_activeBranch != null && _activeBranch.IsRunning)
                _activeBranch.Stop(context);
            _activeBranch = null;
        }
    }

    /// <summary>
    /// Runs the normal quest pickup behavior under one bounded recovery episode.
    /// </summary>
    public class SafePickUp : CustomForcedBehavior
    {
        private const double EndpointCellSize = 80.0;
        private const int MaximumCandidates = 5;
        private const int ConfirmedMismatchBoundary = 3;

        private readonly SafePickUpRuntime _runtime;
        private readonly List<SafePickUpGiverCandidate> _giverCandidates =
            new List<SafePickUpGiverCandidate>();
        private readonly List<QuestAttemptOutcome> _pendingNarrowFailures =
            new List<QuestAttemptOutcome>();
        private readonly HashSet<string> _consumedInteractionCycles =
            new HashSet<string>(StringComparer.Ordinal);
        private ISafePickUpChild _pickUp;
        private DateTime _candidateStartedAt;
        private DateTime? _dialogSilenceSince;
        private DateTime? _pausedAt;
        private Composite _root;
        private QuestRecoveryKey _attemptKey;
        private QuestRecoveryContext _recoveryContext;
        private SafePickUpGiverCandidate _currentCandidate;
        private BotPoi _installedPoi;
        private long _attemptGeneration;
        private int _episodeMapId;
        private int _candidateIndex = -1;
        private int _childSerial;
        private int _confirmedMismatchCycles;
        private bool _candidatesInitialized;
        private bool _attemptOwned;
        private bool _terminalOutcomeReported;
        private bool _questStateDiagnosticLogged;
        private bool _isDone;

        public SafePickUp(Dictionary<string, string> args)
            : this(args, new ProductionSafePickUpRuntime())
        {
        }

        internal SafePickUp(Dictionary<string, string> args, SafePickUpRuntime runtime)
            : base(args)
        {
            _runtime = runtime ?? throw new ArgumentNullException("runtime");
            try
            {
                QuestId = (uint)(GetAttributeAsNullable<int>(
                    "QuestId", true, ConstrainAs.QuestId(this), null) ?? 0);
                QuestName = GetAttributeAs<string>(
                    "QuestName", true, ConstrainAs.StringNonEmpty, null) ?? string.Empty;
                GiverId = (uint)(GetAttributeAsNullable<int>(
                    "GiverId", true, ConstrainAs.MobId, null) ?? 0);
                GiverName = GetAttributeAs<string>(
                    "GiverName", true, ConstrainAs.StringNonEmpty, null) ?? string.Empty;
                TimeoutSeconds = GetAttributeAsNullable<int>(
                    "TimeoutSeconds", false,
                    new ConstrainTo.Domain<int>(5, 300), null) ?? 20;
                ArrivalDistance = GetAttributeAsNullable<double>(
                    "ArrivalDistance", false,
                    new ConstrainTo.Domain<double>(5.0, 50.0), null) ?? 15.0;
                NavigationTimeoutSeconds = GetAttributeAsNullable<int>(
                    "NavigationTimeoutSeconds", false,
                    new ConstrainTo.Domain<int>(30, 900), null) ?? 180;
                AlternateGivers = GetAttributeAs<string>(
                    "AlternateGivers", false, ConstrainAs.StringNonEmpty, null) ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogMessage("error", "SafePickUp attribute error: " + ex.Message);
                IsAttributeProblem = true;
            }
        }

        public uint QuestId { get; private set; }
        public string QuestName { get; private set; }
        public uint GiverId { get; private set; }
        public string GiverName { get; private set; }
        public int TimeoutSeconds { get; private set; }
        public double ArrivalDistance { get; private set; }
        public int NavigationTimeoutSeconds { get; private set; }
        public string AlternateGivers { get; private set; }
        public override bool IsDone { get { return _isDone; } }

        internal static QuestRecoveryKey CreateEndpointKey(
            uint questId,
            int mapId,
            WoWPoint location)
        {
            int cellX = (int)Math.Floor(location.X / EndpointCellSize);
            int cellY = (int)Math.Floor(location.Y / EndpointCellSize);
            return QuestRecoveryKey.ForEndpoint(
                questId,
                QuestRecoveryStage.Navigation,
                mapId,
                string.Format(CultureInfo.InvariantCulture, "cell:{0}:{1}", cellX, cellY));
        }

        public static QuestAttemptOutcome CreateUnavailableOutcome(
            uint targetQuestId,
            uint shownQuestId,
            uint giverId,
            IReadOnlyList<uint> offeredQuestIds,
            QuestFailureReason reason,
            string evidence,
            bool isFailureEpisode,
            long interactionCycleId,
            QuestRecoveryKey attemptKey,
            long attemptGeneration)
        {
            ValidatePickupOwner(targetQuestId, attemptKey, attemptGeneration);
            if (interactionCycleId <= 0)
                throw new ArgumentOutOfRangeException("interactionCycleId");
            if (reason != QuestFailureReason.PickupTargetNotOffered &&
                reason != QuestFailureReason.PickupWrongQuestShown)
                throw new ArgumentOutOfRangeException("reason");

            return new QuestAttemptOutcome
            {
                Key = QuestRecoveryKey.ForNpc(
                    targetQuestId, QuestRecoveryStage.Pickup, giverId),
                AttemptKey = attemptKey,
                Kind = isFailureEpisode
                    ? QuestAttemptOutcomeKind.Failure
                    : QuestAttemptOutcomeKind.Observation,
                AttemptGeneration = attemptGeneration,
                InteractionCycleId = interactionCycleId,
                Reason = reason,
                IsFailureEpisode = isFailureEpisode,
                Evidence = evidence ?? string.Empty,
                ObservedQuestId = shownQuestId,
                OfferedQuestIds = offeredQuestIds ?? Array.Empty<uint>()
            };
        }

        public static QuestAttemptOutcome CreateInteractionTimeoutOutcome(
            uint targetQuestId,
            uint giverId,
            string evidence,
            QuestRecoveryKey attemptKey,
            long attemptGeneration)
        {
            ValidatePickupOwner(targetQuestId, attemptKey, attemptGeneration);
            return QuestAttemptOutcome.Failure(
                QuestRecoveryKey.ForNpc(targetQuestId, QuestRecoveryStage.Pickup, giverId),
                attemptKey,
                attemptGeneration,
                QuestFailureReason.InteractionTimedOut,
                evidence);
        }

        private static void ValidatePickupOwner(
            uint questId,
            QuestRecoveryKey attemptKey,
            long generation)
        {
            if (attemptKey == null)
                throw new ArgumentNullException("attemptKey");
            if (attemptKey.QuestId != questId ||
                attemptKey.Stage != QuestRecoveryStage.Pickup ||
                attemptKey.Scope != QuestRecoveryScope.QuestStage)
                throw new ArgumentException(
                    "Pickup outcomes require their exact pickup quest-stage owner.",
                    "attemptKey");
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
                _recoveryContext = _runtime.CaptureContext();
                _attemptKey = QuestRecoveryKey.ForQuestStage(
                    QuestId, QuestRecoveryStage.Pickup);
                QuestRecoveryDecision claim = _runtime.TryBeginAttempt(
                    _attemptKey, _recoveryContext);
                if (!claim.MayAttempt)
                {
                    Logging.Write(
                        Color.Orange,
                        "[SafePickUp] Quest {0} ({1}) pickup deferred by recovery state {2}: {3}",
                        QuestName, QuestId, claim.State, claim.Status);
                    _isDone = true;
                    return;
                }

                _attemptGeneration = claim.AttemptGeneration;
                _attemptOwned = true;
                _episodeMapId = _runtime.CurrentMapId;
                InitializeWhenAuthoritative();
            }
            catch (Exception ex)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafePickUp] Could not initialize quest {0} ({1}): {2}",
                    QuestName, QuestId, ex);
                ReportStageFailure(
                    QuestFailureReason.InternalBehaviorError,
                    "SafePickUp initialization failed: " + ex.Message);
            }
        }

        protected override Composite CreateBehavior()
        {
            return _root ?? (_root = new Action(context => TickCore(context)));
        }

        internal RunStatus TickForTesting()
        {
            return TickCore(null);
        }

        private RunStatus TickCore(object context)
        {
            if (_isDone || !_attemptOwned)
                return RunStatus.Success;

            QuestCompletionSnapshot completion = GetCompletionSnapshot();
            if (completion.State == QuestCompletionState.Unknown)
            {
                PauseTimers();
                return RunStatus.Running;
            }
            if (completion.IsAccepted || completion.State == QuestCompletionState.KnownComplete)
            {
                ResumeTimers();
                ReportSuccess("quest pickup completed");
                _isDone = true;
                return RunStatus.Success;
            }

            ResumeTimers();
            if (!_candidatesInitialized)
            {
                InitializeCandidates();
                if (_isDone || _pickUp == null)
                    return RunStatus.Success;
            }

            // IsDone refreshes ForcedQuestPickUp's coherent completion snapshot.
            bool childDone = _pickUp.IsDone;
            if (_pickUp.IsExecutionDeferred)
            {
                PauseTimers();
                return RunStatus.Running;
            }
            ResumeTimers();

            OutcomeProcessing result = ProcessPickupOutcome();
            if (result != OutcomeProcessing.None || _isDone)
                return RunStatus.Success;
            if (childDone)
            {
                QuestCompletionSnapshot refreshed = GetCompletionSnapshot();
                if (refreshed.IsAccepted || refreshed.State == QuestCompletionState.KnownComplete)
                {
                    ReportSuccess("quest pickup completed");
                    _isDone = true;
                }
                else if (refreshed.State == QuestCompletionState.Unknown)
                {
                    PauseTimers();
                    return RunStatus.Running;
                }
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
            RunStatus status = _pickUp.Tick(context);
            TrackInstalledPoi(before, _runtime.CurrentBotPoi);
            return status;
        }

        private void InitializeWhenAuthoritative()
        {
            QuestCompletionSnapshot completion = GetCompletionSnapshot();
            if (completion.State == QuestCompletionState.Unknown)
            {
                PauseTimers();
                return;
            }
            if (completion.IsAccepted || completion.State == QuestCompletionState.KnownComplete)
            {
                ReportSuccess("quest is already accepted or completed");
                _isDone = true;
                return;
            }
            InitializeCandidates();
        }

        private void InitializeCandidates()
        {
            if (_candidatesInitialized)
                return;
            _candidatesInitialized = true;
            BuildGiverCandidates();
            if (_giverCandidates.Count == 0)
            {
                ReportMissingGiver();
                return;
            }
            StartNextCandidate();
        }

        private QuestCompletionSnapshot GetCompletionSnapshot()
        {
            try
            {
                return _runtime.GetQuestCompletionSnapshot(QuestId);
            }
            catch (Exception ex)
            {
                if (!_questStateDiagnosticLogged)
                {
                    Logging.Write(
                        Color.Orange,
                        "[SafePickUp] Quest completion authority is temporarily unavailable for {0} ({1}): {2}",
                        QuestName, QuestId, ex.Message);
                    _questStateDiagnosticLogged = true;
                }
                return new QuestCompletionSnapshot(false, QuestCompletionState.Unknown);
            }
        }

        private void BuildGiverCandidates()
        {
            QuestRelationParseResult parsed = QuestRelationParser.Parse(AlternateGivers);
            foreach (string diagnostic in parsed.Diagnostics)
                Logging.Write(
                    Color.Orange,
                    "[SafePickUp] AlternateGivers for quest {0} ({1}): {2}",
                    QuestName, QuestId, diagnostic);

            try
            {
                foreach (SafePickUpGiverCandidate live in
                    _runtime.FindLiveGivers(GiverId) ?? Array.Empty<SafePickUpGiverCandidate>())
                    AddCandidate(live);
            }
            catch (Exception ex)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafePickUp] Live giver lookup failed for {0} ({1}): {2}",
                    GiverName, GiverId, ex.Message);
            }

            try
            {
                AddCandidate(_runtime.FindDatabaseGiver(GiverId, GiverName));
            }
            catch (Exception ex)
            {
                Logging.Write(
                    Color.Orange,
                    "[SafePickUp] NPC database lookup failed for giver {0} ({1}): {2}",
                    GiverName, GiverId, ex.Message);
            }

            foreach (QuestRelation alternate in parsed.Relations)
            {
                AddCandidate(new SafePickUpGiverCandidate(
                    alternate.Entry,
                    alternate.Entry == GiverId
                        ? GiverName
                        : "alternate giver " + alternate.Entry,
                    new WoWPoint((float)alternate.X, (float)alternate.Y, (float)alternate.Z),
                    QuestObjectType.Npc,
                    "AlternateGivers"));
            }
        }

        private void AddCandidate(SafePickUpGiverCandidate candidate)
        {
            if (candidate == null || candidate.Location == WoWPoint.Zero ||
                _giverCandidates.Count >= MaximumCandidates)
                return;

            QuestRecoveryKey endpoint = CreateEndpointKey(
                QuestId, _episodeMapId, candidate.Location);
            if (_giverCandidates.Any(existing =>
                    CreateEndpointKey(QuestId, _episodeMapId, existing.Location).Equals(endpoint)))
                return;
            _giverCandidates.Add(candidate);
        }

        private bool StartNextCandidate()
        {
            while (++_candidateIndex < _giverCandidates.Count)
            {
                SafePickUpGiverCandidate candidate = _giverCandidates[_candidateIndex];
                try
                {
                    ClearInstalledPoi();
                    if (_pickUp != null)
                        _pickUp.Dispose();
                    _pickUp = null;
                    _currentCandidate = candidate;
                    _childSerial++;
                    _candidateStartedAt = _runtime.UtcNow;
                    _dialogSilenceSince = null;
                    _pausedAt = null;
                    BotPoi before = _runtime.CurrentBotPoi;
                    _pickUp = _runtime.CreateChild(QuestId, QuestName, candidate);
                    _pickUp.OnStart();
                    TrackInstalledPoi(before, _runtime.CurrentBotPoi);
                    Logging.Write(
                        "[SafePickUp] Trying giver {0} ({1}) at {2} from {3} for quest {4} ({5}).",
                        candidate.Name,
                        candidate.Entry,
                        candidate.Location,
                        candidate.Source,
                        QuestName,
                        QuestId);
                    return true;
                }
                catch (Exception ex)
                {
                    AddPendingFailure(CreateOwnedFailure(
                        QuestRecoveryKey.ForNpc(
                            QuestId, QuestRecoveryStage.Pickup, candidate.Entry),
                        QuestFailureReason.InternalBehaviorError,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "giver={0}; source={1}; initialization={2}",
                            candidate.Entry,
                            candidate.Source,
                            ex.Message)));
                }
            }

            QuestAttemptOutcome finalFailure = _pendingNarrowFailures.LastOrDefault()
                ?? CreateOwnedFailure(
                    QuestRecoveryKey.ForNpc(QuestId, QuestRecoveryStage.Pickup, GiverId),
                    QuestFailureReason.InternalBehaviorError,
                    "all pickup giver candidates failed to initialize");
            ReportExhausted(
                finalFailure,
                QuestFailureReason.InternalBehaviorError,
                "all pickup giver candidates failed to initialize");
            return false;
        }

        private OutcomeProcessing ProcessPickupOutcome()
        {
            if (_pickUp == null || !_pickUp.TryConsumeOutcome(out QuestAttemptOutcome source))
                return OutcomeProcessing.None;
            if (source == null || source.InteractionCycleId <= 0)
                return OutcomeProcessing.Observed;

            QuestFailureReason reason = source.Reason == QuestFailureReason.PickupWrongQuestShown
                ? QuestFailureReason.PickupWrongQuestShown
                : QuestFailureReason.PickupTargetNotOffered;
            uint giverId = source.Key != null && source.Key.NpcEntry != 0
                ? source.Key.NpcEntry
                : _currentCandidate.Entry;
            string cycleKey = string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1}",
                _childSerial,
                source.InteractionCycleId);
            bool distinctRealCycle = source.InteractionCycleId > 0 &&
                _consumedInteractionCycles.Add(cycleKey);
            if (distinctRealCycle)
                _confirmedMismatchCycles++;

            _dialogSilenceSince = _runtime.UtcNow;
            QuestAttemptOutcome sample = CreateUnavailableOutcome(
                QuestId,
                source.ObservedQuestId,
                giverId,
                source.OfferedQuestIds,
                reason,
                source.Evidence,
                false,
                source.InteractionCycleId,
                _attemptKey,
                _attemptGeneration);
            _runtime.Report(sample, _recoveryContext);

            if (!distinctRealCycle)
                return OutcomeProcessing.Observed;

            if (_confirmedMismatchCycles < ConfirmedMismatchBoundary)
            {
                if (_candidateIndex + 1 < _giverCandidates.Count)
                {
                    AddPendingFailure(CreateUnavailableOutcome(
                        QuestId,
                        source.ObservedQuestId,
                        giverId,
                        source.OfferedQuestIds,
                        reason,
                        source.Evidence,
                        true,
                        source.InteractionCycleId,
                        _attemptKey,
                        _attemptGeneration));
                    StartNextCandidate();
                }
                return _isDone
                    ? OutcomeProcessing.Terminal
                    : OutcomeProcessing.Observed;
            }

            QuestAttemptOutcome terminalRelation = CreateUnavailableOutcome(
                QuestId,
                source.ObservedQuestId,
                giverId,
                source.OfferedQuestIds,
                reason,
                source.Evidence,
                true,
                source.InteractionCycleId,
                _attemptKey,
                _attemptGeneration);
            ReportExhausted(
                terminalRelation,
                reason,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "three confirmed pickup dialog cycles exhausted; {0}",
                    source.Evidence));
            return OutcomeProcessing.Terminal;
        }

        private bool HasNavigationTimedOut()
        {
            return _pickUp != null &&
                _runtime.UtcNow - _candidateStartedAt >=
                    TimeSpan.FromSeconds(NavigationTimeoutSeconds);
        }

        private bool HasInteractionTimedOut()
        {
            if (_pickUp == null)
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
            return _runtime.UtcNow - _dialogSilenceSince.Value >=
                TimeSpan.FromSeconds(TimeoutSeconds);
        }

        private void HandleInteractionTimeout()
        {
            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; giver={1}; interactionTimeoutSeconds={2}",
                QuestId,
                _currentCandidate.Entry,
                TimeoutSeconds);
            QuestAttemptOutcome failure = CreateInteractionTimeoutOutcome(
                QuestId,
                _currentCandidate.Entry,
                evidence,
                _attemptKey,
                _attemptGeneration);
            AddPendingFailure(failure);
            if (_candidateIndex + 1 < _giverCandidates.Count)
                StartNextCandidate();
            else
                ReportExhausted(
                    failure,
                    QuestFailureReason.InteractionTimedOut,
                    "all pickup giver interactions timed out; " + evidence);
        }

        private void HandleNavigationTimeout()
        {
            QuestRecoveryKey endpointKey = CreateEndpointKey(
                QuestId, _episodeMapId, _currentCandidate.Location);
            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; giver={1}; map={2}; endpoint={3}; timeoutSeconds={4}",
                QuestId,
                _currentCandidate.Entry,
                _episodeMapId,
                endpointKey.Endpoint,
                NavigationTimeoutSeconds);
            QuestAttemptOutcome failure = CreateOwnedFailure(
                endpointKey,
                QuestFailureReason.PathGenerationFailed,
                evidence);
            AddPendingFailure(failure);
            if (_candidateIndex + 1 < _giverCandidates.Count)
                StartNextCandidate();
            else
                ReportExhausted(
                    failure,
                    QuestFailureReason.EndpointUnreachable,
                    "all pickup giver endpoints exhausted; " + evidence);
        }

        private void ReportMissingGiver()
        {
            string evidence = string.Format(
                CultureInfo.InvariantCulture,
                "quest={0}; giver={1}; live=false; database=false; alternateCount=0",
                QuestId,
                GiverId);
            QuestAttemptOutcome relation = CreateOwnedFailure(
                QuestRecoveryKey.ForNpc(QuestId, QuestRecoveryStage.Pickup, GiverId),
                QuestFailureReason.NpcMissingFromDatabase,
                evidence);
            ReportExhausted(
                relation,
                QuestFailureReason.NpcMissingFromDatabase,
                evidence);
        }

        private QuestAttemptOutcome CreateOwnedFailure(
            QuestRecoveryKey key,
            QuestFailureReason reason,
            string evidence)
        {
            return QuestAttemptOutcome.Failure(
                key,
                _attemptKey,
                _attemptGeneration,
                reason,
                evidence);
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
            bool reported = _runtime.TryReportGeneratedFailures(
                failures,
                _recoveryContext,
                out IReadOnlyList<QuestRecoveryDecision> decisions);
            if (!reported)
                Logging.Write(
                    Color.Orange,
                    "[SafePickUp] Recovery rejected stale or contended terminal outcome for quest {0} ({1}).",
                    QuestName,
                    QuestId);

            _terminalOutcomeReported = reported;
            _attemptOwned = _runtime.OwnsAttempt(_attemptKey, _attemptGeneration);
            if (reported)
                TryFlushRecovery("accepted terminal pickup failure");
            ClearInstalledPoi();
            _isDone = true;
        }

        private void ReportStageFailure(QuestFailureReason reason, string evidence)
        {
            if (!_attemptOwned)
            {
                _isDone = true;
                return;
            }
            _runtime.Report(
                QuestAttemptOutcome.Failure(
                    _attemptKey,
                    _attemptKey,
                    _attemptGeneration,
                    reason,
                    evidence),
                _recoveryContext);
            _terminalOutcomeReported = true;
            _attemptOwned = false;
            TryFlushRecovery("accepted pickup stage failure");
            ClearInstalledPoi();
            _isDone = true;
        }

        private void ReportSuccess(string evidence)
        {
            if (!_attemptOwned)
                return;
            _runtime.Report(
                QuestAttemptOutcome.Success(
                    _attemptKey, _attemptGeneration, evidence),
                _recoveryContext);
            _terminalOutcomeReported = true;
            _attemptOwned = false;
            TryFlushRecovery("accepted pickup success");
            ClearInstalledPoi();
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
            PickUpNode node = poi == null ? null : poi.AsPickUp;
            return poi != null &&
                poi.Type == PoiType.QuestPickUp &&
                node != null &&
                _currentCandidate != null &&
                node.QuestId == QuestId &&
                node.GiverId == _currentCandidate.Entry &&
                node.GiverLocation.DistanceSqr(_currentCandidate.Location) < 0.01;
        }

        private void ClearInstalledPoi()
        {
            if (_installedPoi != null &&
                ReferenceEquals(_runtime.CurrentBotPoi, _installedPoi) &&
                IsExactCurrentCandidatePoi(_installedPoi))
                _runtime.ClearBotPoi("SafePickUp released its exact pickup POI");
            _installedPoi = null;
        }

        private void ReleaseOwnedAttempt()
        {
            if (!_attemptOwned || _attemptKey == null || _attemptGeneration <= 0)
                return;
            _runtime.AbandonAttempt(_attemptKey, _attemptGeneration);
            _attemptOwned = false;
            TryFlushRecovery("released pickup attempt");
        }

        private void TryFlushRecovery(string reason)
        {
            if (!_runtime.TryFlush())
                Logging.Write(
                    Color.Orange,
                    "[SafePickUp] Recovery persistence remains pending after {0} for quest {1} ({2}).",
                    reason,
                    QuestName,
                    QuestId);
        }

        public override void Dispose()
        {
            if (_pickUp != null)
                _pickUp.Dispose();
            _pickUp = null;
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
