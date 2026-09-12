using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Helpers;
using Styx.Logic.AreaManagement;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.Plugins.PluginClass;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ZygorProfileRecovery
{
    public enum ZygorDeathAction
    {
        Ignore,
        NormalRecovery,
        RequestAlternateCluster,
        ReportRepeatedDeaths
    }

    public sealed class ZygorDeathClassification
    {
        public ZygorDeathAction Action { get; internal set; }
        public QuestFailureReason Reason { get; internal set; }
        public bool IsFailureEpisode { get; internal set; }
    }

    public static class ZygorProfileRecovery
    {
        private const double EndpointCellSize = 80.0;

        public static ZygorDeathClassification ClassifyDeath(
            bool questOwnedExecution,
            bool excludedPoi,
            int deathsInWindow,
            bool madeProgress)
        {
            if (!questOwnedExecution || excludedPoi || deathsInWindow <= 0)
                return Classification(ZygorDeathAction.Ignore);
            if (madeProgress || deathsInWindow == 1)
                return Classification(ZygorDeathAction.NormalRecovery);
            if (deathsInWindow == 2)
                return Classification(ZygorDeathAction.RequestAlternateCluster);
            return Classification(
                ZygorDeathAction.ReportRepeatedDeaths,
                QuestFailureReason.RepeatedDeaths,
                true);
        }

        public static bool CanCapturePreDeath(
            bool exactOwnedObjective,
            bool managerOwnsAttempt,
            bool inOrApproachingArea,
            bool inWorld,
            bool deadOrGhost,
            bool onTaxiOrTransport,
            bool resting,
            bool paused,
            bool excludedPoi,
            bool inCombat,
            bool combatOwnedByQuest)
        {
            return exactOwnedObjective && managerOwnsAttempt && inOrApproachingArea &&
                inWorld && !deadOrGhost && !onTaxiOrTransport && !resting &&
                !paused && !excludedPoi && (!inCombat || combatOwnedByQuest);
        }

        public static bool IsExcludedOrUnrelatedPoi(
            PoiType poiType,
            uint poiEntry,
            uint questId,
            bool targetOwnedByObjective)
        {
            if (poiType == PoiType.None || poiType == PoiType.Hotspot)
                return false;
            if (poiType == PoiType.Quest)
                return poiEntry != questId;
            if (poiType == PoiType.Kill || poiType == PoiType.Loot ||
                poiType == PoiType.Skin || poiType == PoiType.Harvest)
                return !targetOwnedByObjective;
            return true;
        }

        public static bool TryAdvanceAlternateCluster(
            GrindArea area, out Hotspot previous, out Hotspot current)
        {
            previous = null;
            current = null;
            return area != null && area.TryAdvanceCurrentHotspot(out previous, out current);
        }

        public static string GetClusterIdentity(int mapId, WoWPoint hotspot)
        {
            if (hotspot == WoWPoint.Zero)
                return string.Empty;
            int x = (int)Math.Floor(hotspot.X / EndpointCellSize);
            int y = (int)Math.Floor(hotspot.Y / EndpointCellSize);
            return string.Format(
                CultureInfo.InvariantCulture, "{0}:cell:{1}:{2}", mapId, x, y);
        }

        private static ZygorDeathClassification Classification(
            ZygorDeathAction action,
            QuestFailureReason reason = QuestFailureReason.None,
            bool failureEpisode = false)
        {
            return new ZygorDeathClassification
            {
                Action = action,
                Reason = reason,
                IsFailureEpisode = failureEpisode
            };
        }
    }

    internal sealed class ZygorObjectiveExecution
    {
        private readonly IReadOnlyList<int> _counts;

        public ZygorObjectiveExecution(
            object behavior,
            QuestRecoveryKey key,
            string questName,
            IReadOnlyList<int> counts,
            object ownedPoi,
            string clusterIdentity,
            bool isActiveWork,
            bool deathAttributable)
        {
            Behavior = behavior ?? throw new ArgumentNullException("behavior");
            Key = key ?? throw new ArgumentNullException("key");
            QuestName = questName ?? string.Empty;
            _counts = Array.AsReadOnly((counts ?? Array.Empty<int>()).ToArray());
            OwnedPoi = ownedPoi;
            ClusterIdentity = clusterIdentity ?? string.Empty;
            IsActiveWork = isActiveWork;
            DeathAttributable = deathAttributable;
        }

        public object Behavior { get; private set; }
        public QuestRecoveryKey Key { get; private set; }
        public string QuestName { get; private set; }
        public IReadOnlyList<int> Counts { get { return _counts; } }
        public object OwnedPoi { get; private set; }
        public string ClusterIdentity { get; private set; }
        public bool IsActiveWork { get; private set; }
        public bool DeathAttributable { get; private set; }

        internal ZygorObjectiveExecution WithActive(bool active)
        {
            return new ZygorObjectiveExecution(
                Behavior, Key, QuestName, Counts, OwnedPoi, ClusterIdentity,
                active, active && DeathAttributable);
        }
    }

    internal abstract class ZygorRecoveryRuntime
    {
        public abstract DateTime UtcNow { get; }
        public abstract bool IsResilientProfileLoaded { get; }
        public abstract bool IsBotRunning { get; }
        public abstract void EnsureConfigured();
        public abstract void SubscribePlayerDied(BotEvents.Player.PlayerDiedDelegate handler);
        public abstract void UnsubscribePlayerDied(BotEvents.Player.PlayerDiedDelegate handler);
        public abstract ZygorObjectiveExecution CaptureObjectiveExecution();
        public abstract QuestRecoveryContext CaptureContext(IReadOnlyList<int> counts);
        public abstract QuestRecoveryDecision TryBeginAttempt(
            QuestRecoveryKey key, QuestRecoveryContext context);
        public abstract bool OwnsAttempt(QuestRecoveryKey key, long generation);
        public abstract bool AbandonAttempt(QuestRecoveryKey key, long generation);
        public abstract QuestRecoveryReportResult TryReportOwnedProgress(
            QuestRecoveryKey key,
            long attemptGeneration,
            IReadOnlyList<int> previousCounts,
            IReadOnlyList<int> currentCounts,
            string evidence,
            QuestRecoveryContext context);
        public abstract QuestRecoveryReportResult TryReportOwnedOutcome(
            QuestAttemptOutcome outcome, QuestRecoveryContext context);
        public abstract bool RequestAlternateCluster(ZygorObjectiveExecution execution);
        public abstract bool ContinueObjective(
            ZygorObjectiveExecution execution, object capturedPoi, string reason);
        public abstract QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
            QuestRecoveryKey key, Func<QuestAbandonmentLiveSnapshot> recapture);
        public abstract QuestAbandonmentLiveSnapshot CaptureAbandonmentSnapshot(uint questId);
        public abstract bool TryFlush();
    }

    internal sealed class ProductionZygorRecoveryRuntime : ZygorRecoveryRuntime
    {
        public override DateTime UtcNow { get { return DateTime.UtcNow; } }
        public override bool IsResilientProfileLoaded
        {
            get
            {
                string path = ProfileManager.XmlLocation ?? string.Empty;
                return path.IndexOf("Zygor Horde Resilient", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
        public override bool IsBotRunning { get { return TreeRoot.IsRunning; } }

        public override void EnsureConfigured()
        {
            QuestRecoveryRuntime.EnsureConfigured();
        }

        public override void SubscribePlayerDied(BotEvents.Player.PlayerDiedDelegate handler)
        {
            BotEvents.Player.OnPlayerDied += handler;
        }

        public override void UnsubscribePlayerDied(BotEvents.Player.PlayerDiedDelegate handler)
        {
            BotEvents.Player.OnPlayerDied -= handler;
        }

        public override ZygorObjectiveExecution CaptureObjectiveExecution()
        {
            ForcedQuestObjective behavior = QuestOrder.Instance == null
                ? null
                : QuestOrder.Instance.CurrentBehavior as ForcedQuestObjective;
            LocalPlayer me = StyxWoW.Me;
            if (behavior == null || behavior.Objective == null ||
                behavior.Objective.Quest == null || me == null)
                return null;

            try
            {
                PlayerQuest quest = behavior.Objective.Quest;
                int objectiveIndex = GetObjectiveIndex(behavior.Objective);
                if (objectiveIndex < 0)
                    return null;
                IReadOnlyList<int> counts = ReadObjectiveAndRequiredItemCounts(quest);
                if (counts == null)
                    return null;

                BotPoi poi = BotPoi.Current;
                bool poiTargetOwned = IsQuestTargetEntry(behavior.Objective, poi.Entry);
                bool excludedPoi = ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                    poi == null ? PoiType.None : poi.Type,
                    poi == null ? 0 : poi.Entry,
                    quest.Id,
                    poiTargetOwned);
                bool deadOrGhost = me.Dead || me.IsGhost;
                bool onTaxiOrTransport = me.OnTaxi || me.IsOnTransport;
                bool resting = me.IsResting || me.HasAura("Food") || me.HasAura("Drink");
                bool paused = TreeRoot.IsPaused;
                bool inCombat = me.Combat;
                bool combatOwnedByQuest = IsQuestCombat(behavior.Objective, me.CurrentTarget);
                GrindArea area = StyxWoW.AreaManager.CurrentGrindArea;
                bool inOrApproachingArea = IsInOrApproachingArea(me, area);
                bool active = ZygorProfileRecovery.CanCapturePreDeath(
                    true, true, true, StyxWoW.IsInWorld, deadOrGhost,
                    onTaxiOrTransport, resting, paused, excludedPoi, inCombat,
                    combatOwnedByQuest);
                bool deathAttributable = ZygorProfileRecovery.CanCapturePreDeath(
                    true, true, inOrApproachingArea, StyxWoW.IsInWorld, deadOrGhost,
                    onTaxiOrTransport, resting, paused, excludedPoi, inCombat,
                    combatOwnedByQuest);
                object ownedPoi = IsExactQuestPoi(poi, quest.Id) ? poi : null;
                return new ZygorObjectiveExecution(
                    behavior,
                    QuestRecoveryKey.ForObjective(quest.Id, objectiveIndex),
                    quest.Name,
                    counts,
                    ownedPoi,
                    GetClusterIdentity(me, area),
                    active,
                    deathAttributable);
            }
            catch (Exception ex)
            {
                Logging.WriteDiagnostic("[ZygorRecovery] Objective capture failed: {0}", ex);
                return null;
            }
        }

        public override QuestRecoveryContext CaptureContext(IReadOnlyList<int> counts)
        {
            return QuestRecoveryRuntime.Capture(counts);
        }

        public override QuestRecoveryDecision TryBeginAttempt(
            QuestRecoveryKey key, QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.TryBeginAttempt(key, context);
        }

        public override bool OwnsAttempt(QuestRecoveryKey key, long generation)
        {
            return QuestRecoveryManager.Instance.OwnsAttempt(key, generation);
        }

        public override bool AbandonAttempt(QuestRecoveryKey key, long generation)
        {
            return QuestRecoveryManager.Instance.AbandonAttempt(key, generation);
        }

        public override QuestRecoveryReportResult TryReportOwnedProgress(
            QuestRecoveryKey key,
            long attemptGeneration,
            IReadOnlyList<int> previousCounts,
            IReadOnlyList<int> currentCounts,
            string evidence,
            QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.TryReportOwnedProgress(
                key,
                attemptGeneration,
                previousCounts,
                currentCounts,
                evidence,
                context);
        }

        public override QuestRecoveryReportResult TryReportOwnedOutcome(
            QuestAttemptOutcome outcome, QuestRecoveryContext context)
        {
            return QuestRecoveryManager.Instance.TryReportOwnedOutcome(outcome, context);
        }

        public override bool RequestAlternateCluster(ZygorObjectiveExecution execution)
        {
            if (!IsExactCurrentBehavior(execution))
                return false;
            GrindArea area = StyxWoW.AreaManager.CurrentGrindArea;
            try
            {
                return ZygorProfileRecovery.TryAdvanceAlternateCluster(
                    area, out Hotspot previous, out Hotspot current);
            }
            catch (Exception ex)
            {
                Logging.WriteDiagnostic("[ZygorRecovery] Alternate cluster request failed: {0}", ex);
                return false;
            }
        }

        public override bool ContinueObjective(
            ZygorObjectiveExecution execution, object capturedPoi, string reason)
        {
            if (!IsExactCurrentBehavior(execution))
                return false;
            QuestOrder order = QuestOrder.Instance;
            BotPoi currentPoi = BotPoi.Current;
            if (capturedPoi != null && ReferenceEquals(capturedPoi, currentPoi) &&
                IsExactQuestPoi(currentPoi, execution.Key.QuestId))
                BotPoi.Clear(reason);

            ForcedBehavior behavior = order.CurrentBehavior;
            behavior.Dispose();
            if (!ReferenceEquals(order.CurrentBehavior, behavior))
                return false;
            order.CurrentBehavior = null;
            order.Advance();
            return true;
        }

        public override QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
            QuestRecoveryKey key, Func<QuestAbandonmentLiveSnapshot> recapture)
        {
            return QuestRecoveryManager.Instance.TryExecuteAutomaticAbandonment(
                key,
                recapture,
                delegate { StyxWoW.Me.QuestLog.AbandonQuestById(key.QuestId); });
        }

        public override QuestAbandonmentLiveSnapshot CaptureAbandonmentSnapshot(uint questId)
        {
            LocalPlayer me = StyxWoW.Me;
            if (me == null)
                return UncertainAbandonmentSnapshot();
            try
            {
                QuestCompletionSnapshot completion = me.QuestLog.GetQuestCompletionSnapshot(questId);
                int freeSlots = 25 - me.QuestLog.GetAllQuests().Count;
                if (!completion.IsAccepted)
                {
                    return new QuestAbandonmentLiveSnapshot
                    {
                        IsAccepted = false,
                        IsCompleted = completion.State == QuestCompletionState.KnownComplete,
                        StateIsCertain = completion.State != QuestCompletionState.Unknown && freeSlots >= 0,
                        HasObjectiveProgress = false,
                        PrerequisiteStatus = QuestPrerequisiteStatus.Unknown,
                        FreeQuestLogSlots = freeSlots,
                        RecoveryContext = QuestRecoveryRuntime.Capture()
                    };
                }

                PlayerQuest quest = me.QuestLog.GetQuestById(questId);
                IReadOnlyList<int> counts = ReadObjectiveAndRequiredItemCounts(quest);
                return new QuestAbandonmentLiveSnapshot
                {
                    IsAccepted = true,
                    IsCompleted = completion.State == QuestCompletionState.KnownComplete,
                    StateIsCertain = completion.State != QuestCompletionState.Unknown &&
                        counts != null && freeSlots >= 0,
                    HasObjectiveProgress = counts != null && counts.Any(value => value > 0),
                    PrerequisiteStatus = QuestPrerequisiteAuthority.Capture(quest),
                    FreeQuestLogSlots = freeSlots,
                    RecoveryContext = QuestRecoveryRuntime.Capture(counts)
                };
            }
            catch (Exception ex)
            {
                Logging.WriteDiagnostic("[ZygorRecovery] Abandonment snapshot failed: {0}", ex);
                return UncertainAbandonmentSnapshot();
            }
        }

        public override bool TryFlush()
        {
            return QuestRecoveryManager.Instance.TryFlush();
        }

        private static QuestAbandonmentLiveSnapshot UncertainAbandonmentSnapshot()
        {
            return new QuestAbandonmentLiveSnapshot
            {
                StateIsCertain = false,
                FreeQuestLogSlots = -1
            };
        }

        private static bool IsExactCurrentBehavior(ZygorObjectiveExecution execution)
        {
            ForcedQuestObjective current = QuestOrder.Instance == null
                ? null
                : QuestOrder.Instance.CurrentBehavior as ForcedQuestObjective;
            return current != null && ReferenceEquals(current, execution.Behavior) &&
                current.Objective != null && current.Objective.Quest != null &&
                current.Objective.Quest.Id == execution.Key.QuestId &&
                GetObjectiveIndex(current.Objective) == execution.Key.ObjectiveIndex;
        }

        private static int GetObjectiveIndex(Bots.Quest.Objectives.QuestObjective objective)
        {
            GrindObjective grind = objective as GrindObjective;
            if (grind != null)
                return grind.Objective.Index;
            CollectItemObjective collect = objective as CollectItemObjective;
            if (collect != null)
                return collect.Objective.Index;
            UseGameObjectObjective useObject = objective as UseGameObjectObjective;
            if (useObject != null)
                return useObject.Objective.Index;
            return -1;
        }

        private static IReadOnlyList<int> ReadObjectiveAndRequiredItemCounts(PlayerQuest quest)
        {
            if (quest == null || !quest.GetData(out WoWDescriptorQuest data) ||
                data.ObjectivesDone == null || StyxWoW.Me == null)
                return null;
            var counts = data.ObjectivesDone.Select(value => (int)value).ToList();
            IEnumerable<int> requiredItems = quest.CollectItemIds
                .Concat(quest.CollectIntermediateItemIds)
                .Where(itemId => itemId > 0);
            foreach (int itemId in requiredItems)
            {
                long count = StyxWoW.Me.CarriedItems
                    .Where(item => item.Entry == unchecked((uint)itemId))
                    .Sum(item => (long)item.StackCount);
                counts.Add(checked((int)count));
            }
            return Array.AsReadOnly(counts.ToArray());
        }

        private static bool IsQuestCombat(
            Bots.Quest.Objectives.QuestObjective objective, WoWUnit target)
        {
            return target != null && IsQuestTargetEntry(objective, target.Entry);
        }

        private static bool IsQuestTargetEntry(
            Bots.Quest.Objectives.QuestObjective objective, uint entry)
        {
            if (entry == 0)
                return false;
            GrindObjective grind = objective as GrindObjective;
            if (grind != null && entry == unchecked((uint)grind.Objective.ID))
                return true;
            GrindArea area = StyxWoW.AreaManager.CurrentGrindArea;
            return area != null && area.MobIDs != null &&
                area.MobIDs.Contains(unchecked((int)entry));
        }

        private static bool IsInOrApproachingArea(LocalPlayer me, GrindArea area)
        {
            if (me == null || area == null || area.Hotspots == null || area.Hotspots.Count == 0)
                return false;
            WoWPoint nearest = area.Hotspots
                .Select(hotspot => hotspot.Position)
                .OrderBy(point => point.DistanceSqr(me.Location))
                .FirstOrDefault();
            if (nearest == WoWPoint.Zero)
                return false;
            double approachRadius = area.MaxDistance.HasValue
                ? Math.Max(40.0, area.MaxDistance.Value)
                : 200.0;
            if (nearest.Distance(me.Location) <= approachRadius)
                return true;
            return Navigator.NavigationProvider != null &&
                Navigator.NavigationProvider.PathDistance(me.Location, nearest).HasValue;
        }

        private static string GetClusterIdentity(LocalPlayer me, GrindArea area)
        {
            if (me == null || area == null || area.Hotspots == null || area.Hotspots.Count == 0)
                return string.Empty;
            Hotspot current = area.CurrentHotSpot;
            if (current == null)
                return string.Empty;
            return ZygorProfileRecovery.GetClusterIdentity(
                unchecked((int)me.MapId), current.Position);
        }

        private static bool IsExactQuestPoi(BotPoi poi, uint questId)
        {
            return poi != null && poi.Type == PoiType.Quest && poi.Entry == questId;
        }

    }

    /// <summary>
    /// Attributes Zygor objective progress and repeated deaths to the shared
    /// quest recovery manager without restarting the bot or writing a legacy blacklist.
    /// </summary>
    public sealed class ZygorProfileRecoveryPlugin : HBPlugin
    {
        private static readonly TimeSpan AlternateClusterDelay = TimeSpan.FromMinutes(4);
        private static readonly TimeSpan NoProgressLimit = TimeSpan.FromMinutes(8);
        private static readonly TimeSpan DeathWindow = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan MaximumSampleGap = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MaximumPreDeathAge = TimeSpan.FromSeconds(10);

        private readonly object _sync = new object();
        private readonly ZygorRecoveryRuntime _runtime;
        private readonly BotEvents.Player.PlayerDiedDelegate _deathHandler;
        private readonly HashSet<string> _clusters = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<DateTime> _deaths = new List<DateTime>();
        private ZygorObjectiveExecution _execution;
        private ZygorObjectiveExecution _preDeathExecution;
        private ZygorObjectiveExecution _pendingAlternate;
        private ZygorObjectiveExecution _pendingContinuation;
        private object _pendingContinuationPoi;
        private IReadOnlyList<int> _lastCounts = Array.Empty<int>();
        private long _attemptGeneration;
        private DateTime _lastSampleUtc;
        private DateTime _preDeathCapturedUtc;
        private TimeSpan _activeWork;
        private bool _previousSampleActive;
        private bool _enabled;
        private bool _claimDenied;
        private DateTime? _deniedRetryUtc;
        private string _deniedContextSignature = string.Empty;
        private bool _alternateRequested;
        private bool _failureReported;

        public ZygorProfileRecoveryPlugin()
            : this(new ProductionZygorRecoveryRuntime()) { }

        internal ZygorProfileRecoveryPlugin(ZygorRecoveryRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException("runtime");
            _deathHandler = OnPlayerDied;
        }

        public override string Name { get { return "Zygor Profile Recovery"; } }
        public override string Author { get { return "Codex"; } }
        public override Version Version { get { return new Version(2, 0, 0); } }
        public override bool WantButton { get { return false; } }

        public override void OnEnable()
        {
            lock (_sync)
            {
                if (_enabled)
                    return;
                _runtime.EnsureConfigured();
                _runtime.SubscribePlayerDied(_deathHandler);
                _enabled = true;
                ResetLocalState(false);
            }
            Logging.Write("[ZygorRecovery] Enabled with shared objective recovery.");
        }

        public override void OnDisable()
        {
            lock (_sync)
            {
                if (!_enabled)
                    return;
                _enabled = false;
                try
                {
                    _runtime.UnsubscribePlayerDied(_deathHandler);
                }
                finally
                {
                    ResetLocalState(true);
                }
            }
        }

        public override void Pulse()
        {
            lock (_sync)
            {
                if (!_enabled)
                    return;
                try
                {
                    if (!_runtime.IsResilientProfileLoaded || !_runtime.IsBotRunning)
                    {
                        ResetLocalState(true);
                        return;
                    }
                    ApplyPendingRequests();

                    ZygorObjectiveExecution current = _runtime.CaptureObjectiveExecution();
                    if (current == null)
                    {
                        ResetTrackedAttempt(true);
                        return;
                    }
                    if (_execution == null || !ReferenceEquals(_execution.Behavior, current.Behavior) ||
                        !_execution.Key.Equals(current.Key))
                    {
                        ResetTrackedAttempt(true);
                        if (!TryClaim(current))
                            return;
                    }
                    else if (_claimDenied)
                    {
                        if (!ShouldReevaluateDeniedClaim(current))
                            return;
                        _claimDenied = false;
                        if (!TryClaim(current))
                            return;
                    }
                    else if (_attemptGeneration <= 0 ||
                        !_runtime.OwnsAttempt(current.Key, _attemptGeneration))
                    {
                        ResetTrackedAttempt(false);
                        if (!TryClaim(current))
                            return;
                    }

                    _execution = current;
                    SampleProgress(current);
                    CapturePreDeath(current);
                }
                catch (Exception ex)
                {
                    Logging.WriteDiagnostic("[ZygorRecovery] Pulse error: {0}", ex);
                }
            }
        }

        private bool TryClaim(ZygorObjectiveExecution current)
        {
            QuestRecoveryContext context = _runtime.CaptureContext(current.Counts);
            QuestRecoveryDecision decision = _runtime.TryBeginAttempt(current.Key, context);
            if (!decision.MayAttempt || decision.State != QuestRecoveryState.Attempting ||
                decision.AttemptGeneration <= 0)
            {
                _execution = current;
                _claimDenied = true;
                _deniedRetryUtc = decision.RetryUtc;
                _deniedContextSignature = DeniedContextSignature(current);
                if (decision.State != QuestRecoveryState.Attempting)
                    QueueContinuation(current, current.OwnedPoi);
                return false;
            }

            _execution = current;
            _claimDenied = false;
            _deniedRetryUtc = null;
            _deniedContextSignature = string.Empty;
            _attemptGeneration = decision.AttemptGeneration;
            _lastCounts = current.Counts.ToArray();
            ResetEpisodeState(_runtime.UtcNow);
            Logging.Write(
                "[ZygorRecovery] Monitoring {0} ({1}) objective {2}, generation {3}.",
                current.QuestName,
                current.Key.QuestId,
                current.Key.ObjectiveIndex,
                _attemptGeneration);
            return true;
        }

        private void SampleProgress(ZygorObjectiveExecution current)
        {
            DateTime now = _runtime.UtcNow;
            IReadOnlyList<int> previousCounts = _lastCounts;
            bool madeProgress = HasIncrease(previousCounts, current.Counts);
            _lastCounts = current.Counts.ToArray();
            if (madeProgress)
            {
                QuestRecoveryContext context = _runtime.CaptureContext(current.Counts);
                QuestRecoveryReportResult result = _runtime.TryReportOwnedProgress(
                    current.Key,
                    _attemptGeneration,
                    previousCounts,
                    current.Counts,
                    "Zygor locally observed an objective counter or required-item increase.",
                    context);
                if (!result.Accepted)
                {
                    ResetEpisodeState(now);
                    Logging.WriteDiagnostic(
                        "[ZygorRecovery] Rejected stale owned progress for quest {0} objective {1}, generation {2}.",
                        current.Key.QuestId,
                        current.Key.ObjectiveIndex,
                        _attemptGeneration);
                    return;
                }
                _attemptGeneration = 0;
                ResetEpisodeState(now);
                TryFlushSafely("accepted objective progress");
                Logging.Write(
                    "[ZygorRecovery] Objective counter/item progress detected for {0} ({1}).",
                    current.QuestName,
                    current.Key.QuestId);
                return;
            }

            TimeSpan gap = now - _lastSampleUtc;
            if (_previousSampleActive && current.IsActiveWork && gap > TimeSpan.Zero &&
                gap <= MaximumSampleGap)
                _activeWork += gap;
            _lastSampleUtc = now;
            _previousSampleActive = current.IsActiveWork;
            if (current.IsActiveWork && !string.IsNullOrEmpty(current.ClusterIdentity))
                _clusters.Add(current.ClusterIdentity);

            if (!_alternateRequested && _activeWork >= AlternateClusterDelay)
            {
                _alternateRequested = true;
                QueueAlternate(current);
            }
            if (!_failureReported && _activeWork >= NoProgressLimit)
            {
                ReportFailure(
                    current,
                    QuestFailureReason.NoObjectiveProgress,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "No objective counter/item progress after {0:F1} active minutes across {1} clusters.",
                        _activeWork.TotalMinutes,
                        _clusters.Count));
            }
        }

        private void CapturePreDeath(ZygorObjectiveExecution current)
        {
            bool managerOwns = _attemptGeneration > 0 &&
                _runtime.OwnsAttempt(current.Key, _attemptGeneration);
            if (!current.DeathAttributable || !managerOwns)
            {
                ClearPreDeath();
                return;
            }
            _preDeathExecution = current;
            _preDeathCapturedUtc = _runtime.UtcNow;
        }

        private void OnPlayerDied()
        {
            lock (_sync)
            {
                if (!_enabled || _failureReported || _preDeathExecution == null)
                    return;
                DateTime now = _runtime.UtcNow;
                bool exact = _execution != null &&
                    ReferenceEquals(_execution.Behavior, _preDeathExecution.Behavior) &&
                    _execution.Key.Equals(_preDeathExecution.Key) &&
                    _attemptGeneration > 0 &&
                    _runtime.OwnsAttempt(_execution.Key, _attemptGeneration) &&
                    now - _preDeathCapturedUtc >= TimeSpan.Zero &&
                    now - _preDeathCapturedUtc <= MaximumPreDeathAge;
                ZygorObjectiveExecution captured = _preDeathExecution;
                ClearPreDeath();
                if (!exact)
                    return;

                _deaths.RemoveAll(value => now - value > DeathWindow);
                _deaths.Add(now);
                ZygorDeathClassification classification = ZygorProfileRecovery.ClassifyDeath(
                    true, false, _deaths.Count, false);
                if (classification.Action == ZygorDeathAction.RequestAlternateCluster)
                {
                    _alternateRequested = true;
                    QueueAlternate(captured);
                    return;
                }
                if (classification.Action == ZygorDeathAction.ReportRepeatedDeaths)
                {
                    ReportFailure(
                        captured,
                        classification.Reason,
                        _deaths.Count +
                            " attributable deaths occurred inside 15 minutes without objective progress.");
                }
            }
        }

        private void ReportFailure(
            ZygorObjectiveExecution execution, QuestFailureReason reason, string evidence)
        {
            if (_failureReported || _attemptGeneration <= 0)
                return;
            QuestRecoveryKey key = execution.Key;
            QuestAttemptOutcome outcome = QuestAttemptOutcome.Failure(
                key, key, _attemptGeneration, reason, evidence);
            QuestRecoveryContext context = _runtime.CaptureContext(execution.Counts);
            QuestRecoveryReportResult result = _runtime.TryReportOwnedOutcome(outcome, context);
            if (!result.Accepted)
            {
                ResetTrackedAttempt(true);
                return;
            }

            _failureReported = true;
            _attemptGeneration = 0;
            TryFlushSafely("accepted terminal objective outcome");
            if (result.Decision.State == QuestRecoveryState.Quarantined)
            {
                QuestAbandonmentDecision abandonment = _runtime.TryExecuteAutomaticAbandonment(
                    key,
                    delegate { return _runtime.CaptureAbandonmentSnapshot(key.QuestId); });
                Logging.WriteDiagnostic(
                    "[ZygorRecovery] Quest {0} automatic abandonment: {1}",
                    key.QuestId,
                    abandonment.Reason);
            }
            QueueContinuation(execution, execution.OwnedPoi);
            Logging.Write(
                "[ZygorRecovery] Quest {0} objective {1} reported {2}; yielding through the next normal pulse.",
                key.QuestId,
                key.ObjectiveIndex,
                reason);
        }

        private void QueueAlternate(ZygorObjectiveExecution execution)
        {
            if (_pendingAlternate == null)
                _pendingAlternate = execution;
        }

        private void QueueContinuation(ZygorObjectiveExecution execution, object capturedPoi)
        {
            if (_pendingContinuation != null)
                return;
            _pendingContinuation = execution;
            _pendingContinuationPoi = capturedPoi;
        }

        private void ApplyPendingRequests()
        {
            if (_pendingAlternate != null)
            {
                ZygorObjectiveExecution alternate = _pendingAlternate;
                _pendingAlternate = null;
                _runtime.RequestAlternateCluster(alternate);
            }
            if (_pendingContinuation != null)
            {
                ZygorObjectiveExecution continuation = _pendingContinuation;
                object poi = _pendingContinuationPoi;
                _pendingContinuation = null;
                _pendingContinuationPoi = null;
                bool continued = _runtime.ContinueObjective(
                    continuation,
                    poi,
                    "Zygor recovery released its exact failed objective POI");
                if (continued)
                    ResetTrackedAttempt(false);
            }
        }

        private void ResetLocalState(bool releaseAttempt)
        {
            ResetTrackedAttempt(releaseAttempt);
            _pendingAlternate = null;
            _pendingContinuation = null;
            _pendingContinuationPoi = null;
        }

        private void ResetTrackedAttempt(bool releaseAttempt)
        {
            QuestRecoveryKey key = _execution == null ? null : _execution.Key;
            long generation = _attemptGeneration;
            _execution = null;
            _attemptGeneration = 0;
            _claimDenied = false;
            _deniedRetryUtc = null;
            _deniedContextSignature = string.Empty;
            _lastCounts = Array.Empty<int>();
            ResetEpisodeState(default(DateTime));
            ClearPreDeath();
            if (releaseAttempt && key != null && generation > 0 &&
                _runtime.AbandonAttempt(key, generation))
                TryFlushSafely("released owned attempt");
        }

        private bool ShouldReevaluateDeniedClaim(ZygorObjectiveExecution current)
        {
            if (!string.Equals(
                    _deniedContextSignature,
                    DeniedContextSignature(current),
                    StringComparison.Ordinal))
                return true;
            return _deniedRetryUtc.HasValue && _runtime.UtcNow >= _deniedRetryUtc.Value;
        }

        private static string DeniedContextSignature(ZygorObjectiveExecution execution)
        {
            return execution.ClusterIdentity + "|" +
                string.Join(",", execution.Counts.Select(value =>
                    value.ToString(CultureInfo.InvariantCulture)));
        }

        private void TryFlushSafely(string reason)
        {
            if (!_runtime.TryFlush())
                Logging.WriteDiagnostic(
                    "[ZygorRecovery] Recovery persistence remains pending after {0}.",
                    reason);
        }

        private void ResetEpisodeState(DateTime now)
        {
            _deaths.Clear();
            _clusters.Clear();
            _activeWork = TimeSpan.Zero;
            _lastSampleUtc = now;
            _previousSampleActive = false;
            _alternateRequested = false;
            _failureReported = false;
        }

        private void ClearPreDeath()
        {
            _preDeathExecution = null;
            _preDeathCapturedUtc = default(DateTime);
        }

        private static bool HasIncrease(
            IReadOnlyList<int> previous, IReadOnlyList<int> current)
        {
            IReadOnlyList<int> before = previous ?? Array.Empty<int>();
            IReadOnlyList<int> after = current ?? Array.Empty<int>();
            return after.Select((value, index) =>
                value > (index < before.Count ? before[index] : 0)).Any(increased => increased);
        }
    }
}
