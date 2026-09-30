using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

#nullable disable

namespace WholesomeAQ
{
    // An observation is a planning hint, never permission to interact or accept.
    // Object type, actor, map and observation time travel with the coordinates.
    public sealed class QuestGiverObservation
    {
        public QuestObjectType ObjectType { get; init; }
        public int Entry { get; init; }
        public ulong Guid { get; init; }
        public string Name { get; init; } = "";
        public ulong PlayerGuid { get; init; }
        public DateTime ObservedUtc { get; init; }
        public int MapId { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
        public bool IsQuestGiver { get; init; }
        public uint? RawQuestStatus { get; init; }
        public bool? HasAvailableQuest { get; init; }
        public string QuestStatusEvidence { get; init; } = "not-observed";
    }

    public partial class QuestScheduler
    {
        private const double NearbyGiverRadius = 250.0;
        private static readonly TimeSpan DiagnosticInterval = TimeSpan.FromSeconds(30);
        private DateTime _lastDiagnosticUtc = DateTime.MinValue;

        private static bool DiagnosticLoggingEnabled => Logging.LoggingLevel >= LogLevel.Diagnostic ||
            (Logging.FileLogging && Logging.LogFileLevel >= LogLevel.Diagnostic);

        private sealed class RecoveryObservation
        {
            internal uint QuestId;
            internal string Owner, Scope, State, Status;
            internal bool MayAttempt;
            internal DateTime? RetryUtc;
        }

        private sealed class AdmissionDiagnosticCapture
        {
            internal readonly Dictionary<int, string> Rejections = new Dictionary<int, string>();
            internal readonly List<RecoveryObservation> Recovery = new List<RecoveryObservation>();
            internal readonly Dictionary<string, SpawnNavigationAssessment> Navigation = new Dictionary<string, SpawnNavigationAssessment>();
            internal readonly Dictionary<int, Dictionary<string, object>> Gates = new Dictionary<int, Dictionary<string, object>>();
            internal readonly Dictionary<int, string> SelectionReasons = new Dictionary<int, string>();

            internal void Reject(int questId, string reason) => Rejections[questId] = reason;
            internal void Gate(int questId, string name, object value)
            {
                if (!Gates.TryGetValue(questId, out var gates)) Gates[questId] = gates = new Dictionary<string, object>();
                gates[name] = value;
            }
            internal void Record(QuestRecoveryKey key, QuestRecoveryDecision decision)
            {
                Recovery.Add(new RecoveryObservation
                {
                    QuestId = key.QuestId, Owner = key.ToString(), Scope = key.Scope.ToString(),
                    State = decision.State.ToString(), Status = decision.Status,
                    MayAttempt = decision.MayAttempt, RetryUtc = decision.RetryUtc
                });
            }
        }

        // Shared by direct pickup, ancestor admission and its diagnostic.
        // TC 3.3.5 8fda442f6c30ca21a622638063ab8b28376f1b25,
        // Player::SatisfyQuestLevel: MinLevel (and explicit MaxLevel), not QuestLevel.
        // The lower QuestLevel filter remains the user's scheduling preference.
        private static string BasePickupRejection(QuestEntry quest, QuestSchedulerSnapshot snapshot, int minimumLevel,
            QuestStrategyPack strategyPack = null)
        {
            if (snapshot.PlayerLevel < quest.MinLevel) return "below-min-level";
            string requirementRejection = DeclaredRequirementRejection(quest, snapshot);
            if (requirementRejection != null) return requirementRejection;
            string availabilityRejection = QuestAvailabilityPolicy.Evaluate(quest, snapshot).Rejection;
            if (availabilityRejection != null) return availabilityRejection;
            string deliveryRejection = QuestDeliveryPolicy.PickupRejection(quest, snapshot.CarriedItemCounts);
            if (deliveryRejection != null) return deliveryRejection;
            if (quest.QuestLevel > 0 && quest.QuestLevel < minimumLevel) return "below-configured-quest-level";
            if (!RaceAllowed(quest.AllowableRaces, snapshot.PlayerRaceId)) return "race-not-allowed";
            if (!SupportedForPickup(quest, strategyPack)) return "unsupported-objective-or-missing-strategy";
            return null;
        }

        private static string DeclaredRequirementRejection(QuestEntry quest, QuestSchedulerSnapshot snapshot)
        {
            // TC335 Player::SatisfyQuestLevel/Class/Skill/Reputation. The first
            // faction objective is a completion target; the second is also an
            // upper pickup bound. Reputation maxima are exclusive, unlike MaxLevel.
            if (quest.MaxLevel < 0 || quest.RequiredSkillID < 0 || quest.RequiredSkillPoints < 0 ||
                quest.RequiredMinRepFaction < 0 || quest.RequiredMaxRepFaction < 0) return "invalid-eligibility-metadata";
            if (quest.MaxLevel > 0 && snapshot.PlayerLevel > quest.MaxLevel) return "above-max-level";
            if (quest.AllowableClasses.HasValue && quest.AllowableClasses.Value != 0)
            {
                if (snapshot.PlayerClassId < 1 || snapshot.PlayerClassId > 11) return "class-observation-unknown";
                if ((quest.AllowableClasses.Value & (1 << (snapshot.PlayerClassId - 1))) == 0) return "class-not-allowed";
            }
            if (quest.RequiredSkillID > 0)
            {
                if (!quest.RequiredSkillPoints.HasValue) return "skill-requirement-incomplete";
                if (quest.RequiredSkillPoints > 0)
                {
                    if (snapshot.SkillValues == null || !snapshot.SkillValues.TryGetValue(quest.RequiredSkillID.Value, out int skill))
                        return "skill-observation-unknown";
                    if (skill < quest.RequiredSkillPoints) return "below-required-skill";
                }
            }
            string Reputation(int? faction, int? threshold, bool upper, string scope)
            {
                if (!(faction > 0)) return null;
                if (!threshold.HasValue) return scope + "-requirement-incomplete";
                if (snapshot.ReputationValues == null || !snapshot.ReputationValues.TryGetValue(faction.Value, out int value))
                    return scope + "-observation-unknown";
                return upper ? (value >= threshold.Value ? scope + "-at-or-above-maximum" : null)
                    : (value < threshold.Value ? scope + "-below-minimum" : null);
            }
            return Reputation(quest.RequiredMinRepFaction, quest.RequiredMinRepValue, false, "reputation-min") ??
                Reputation(quest.RequiredMaxRepFaction, quest.RequiredMaxRepValue, true, "reputation-max") ??
                Reputation(quest.RequiredFactionId2, quest.RequiredFactionValue2, true, "reputation-objective-two");
        }

        private static QuestGiverObservation[] CaptureNearbyQuestGivers(
            QuestDatabase db, LocalPlayer me, DateTime utcNow, out string status)
        {
            var captured = new List<QuestGiverObservation>();
            status = "not-observed";
            var memory = ObjectManager.Wow;
            if (memory == null || me == null) return captured.ToArray();
            int failures = 0;
            try
            {
                ulong playerGuid = me.Guid;
                int mapId = (int)me.MapId;
                var origin = me.Location;
                var relations = new HashSet<(QuestObjectType Type, int Entry)>(
                    db.QuestGivers.Select(giver => (giver.GiverType, giver.GiverId))
                    .Concat(db.QuestEnders.Select(ender => (ender.EnderType, ender.EnderId))));
                foreach (WoWObject obj in ObjectManager.GetObjectsOfType<WoWObject>(true, false))
                {
                    try
                    {
                        if (obj is WoWPlayer || !obj.IsValid) continue;
                        QuestObjectType type;
                        bool isGiver;
                        if (obj is WoWUnit unit) { type = QuestObjectType.Creature; isGiver = unit.IsQuestGiver; }
                        else if (obj is WoWGameObject gameObject) { type = QuestObjectType.GameObject; isGiver = gameObject.IsQuestGiver; }
                        else continue;
                        int entry = checked((int)obj.Entry);
                        if (!isGiver && !relations.Contains((type, entry))) continue;
                        ulong guid = obj.Guid;
                        var point = obj.Location;
                        if (guid == 0 || entry <= 0 || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) ||
                            origin.Distance(point) > NearbyGiverRadius) continue;
                        uint? rawStatus = null;
                        if (DiagnosticLoggingEnabled) rawStatus = (uint)obj.QuestGiverStatus;
                        if (!obj.IsValid || obj.Guid != guid || obj.Entry != (uint)entry) { failures++; continue; }
                        captured.Add(new QuestGiverObservation
                        {
                            ObjectType = type, Entry = entry, Guid = guid, Name = DiagnosticText(obj.Name),
                            PlayerGuid = playerGuid, ObservedUtc = utcNow, MapId = mapId,
                            X = point.X, Y = point.Y, Z = point.Z, IsQuestGiver = isGiver,
                            RawQuestStatus = rawStatus,
                            HasAvailableQuest = rawStatus.HasValue && rawStatus.Value <= 10
                                ? rawStatus == 2 || rawStatus == 4 || rawStatus == 7 || rawStatus == 8 : (bool?)null,
                            QuestStatusEvidence = rawStatus.HasValue
                                ? "build12340 cached server dialog status: 0x6D11C0/0x6D1230 -> 0x744400 -> object+0x90; not a per-quest offer or freshness proof"
                                : "debug-status-read-disabled"
                        });
                    }
                    catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException)
                    {
                        failures++;
                    }
                }
                if (!ReferenceEquals(memory, ObjectManager.Wow) || !ReferenceEquals(me, ObjectManager.Me) ||
                    !Styx.StyxWoW.IsInWorld || !me.IsValid || me.Guid != playerGuid || (int)me.MapId != mapId)
                {
                    status = "actor-or-world-changed";
                    return Array.Empty<QuestGiverObservation>();
                }
                status = failures == 0 ? "loaded-object-sample" : "partial-object-sample;unreadable=" + failures;
                return captured.OrderBy(value => value.ObjectType).ThenBy(value => value.Entry).ThenBy(value => value.Guid).ToArray();
            }
            catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException)
            {
                status = "capture-unavailable:" + error.GetType().Name;
                return Array.Empty<QuestGiverObservation>();
            }
        }

        private static (IReadOnlyDictionary<int, int> Skills, IReadOnlyDictionary<int, int> Reputations)
            CaptureRequirementObservations(QuestDatabase db, LocalPlayer me)
        {
            var skills = new Dictionary<int, int>();
            var reputations = new Dictionary<int, int>();
            if (me == null || !me.IsValid || me.Guid == 0) return (skills, reputations);
            ulong owner = me.Guid;
            var memory = ObjectManager.Wow;
            foreach (int id in db.Quests.Where(quest => quest.RequiredSkillID > 0)
                .Select(quest => quest.RequiredSkillID.Value).Distinct())
            {
                try
                {
                    var skill = me.GetSkill(id);
                    if (skill != null && skill.IsValid && skill.SkillLineId == id)
                        skills[id] = Math.Max(0, skill.CurrentValue + skill.Modifier + unchecked((short)skill.Bonus));
                    // A missing skill can also mean an incomplete memory read.
                    // Keep it unknown; never manufacture an observed zero.
                }
                catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException) { }
            }
            foreach (int id in db.Quests.SelectMany(quest => new[]
                { quest.RequiredMinRepFaction ?? 0, quest.RequiredMaxRepFaction ?? 0, quest.RequiredFactionId1, quest.RequiredFactionId2 })
                .Where(id => id > 0).Distinct())
            {
                try
                {
                    // Build12340 IDA: GetFactionInfoByID 0x5D11E0 -> 0x5D0DA0;
                    // six is the total standing. Invalid IDs have a nil name even
                    // though the numeric slots contain zero, so require both fields.
                    string script = string.Format(CultureInfo.InvariantCulture,
                        "if UnitGUID('player') ~= '0x{0:X16}' or type(GetFactionInfoByID) ~= 'function' then return end " +
                        "local n,_,_,_,_,v=GetFactionInfoByID({1}); if type(n)=='string' and type(v)=='number' then return tostring(v) end", owner, id);
                    var values = Lua.GetReturnValues(script);
                    if (values != null && values.Count == 1 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                        reputations[id] = value;
                }
                catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException) { }
            }
            if (!ReferenceEquals(memory, ObjectManager.Wow) || !ReferenceEquals(me, ObjectManager.Me) ||
                !me.IsValid || me.Guid != owner) return (new Dictionary<int, int>(), new Dictionary<int, int>());
            return (skills, reputations);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static IEnumerable<SpawnPoint> GetObservedRelationSpawns(
            int entry, QuestObjectType type, QuestDatabase db, QuestSchedulerSnapshot snapshot)
        {
            var stored = GetRelationSpawns(entry, type, db).ToArray();
            if (entry <= 0 || snapshot.PlayerGuid == 0 ||
                (type != QuestObjectType.Creature && type != QuestObjectType.GameObject)) return stored;
            var observed = (snapshot.NearbyQuestGivers ?? Array.Empty<QuestGiverObservation>())
                .Where(value => value != null && value.Entry == entry && value.ObjectType == type && value.Guid != 0 &&
                    value.PlayerGuid == snapshot.PlayerGuid && value.ObservedUtc == snapshot.UtcNow &&
                    value.MapId == snapshot.MapId && Finite(value.X) && Finite(value.Y) && Finite(value.Z) &&
                    Math.Pow(value.X - snapshot.X, 2) + Math.Pow(value.Y - snapshot.Y, 2) + Math.Pow(value.Z - snapshot.Z, 2)
                        <= NearbyGiverRadius * NearbyGiverRadius)
                .GroupBy(value => (value.MapId, value.X, value.Y, value.Z)).Select(group => group.First()).ToArray();
            if (observed.Length == 0) return stored;

            // A fresh object sample locates this typed relation for this actor/scan.
            // It is not a quest offer or permission to bypass navigation/recovery.
            // Prefer current nearby instances over stale copies of their geometry;
            // retain a negative assessment attached to the identical stored point.
            return observed.Select(value =>
            {
                var exact = stored.Where(point => point.Map == value.MapId && point.X == value.X &&
                    point.Y == value.Y && point.Z == value.Z).ToArray();
                return new SpawnPoint
                {
                    Map = value.MapId, X = value.X, Y = value.Y, Z = value.Z,
                    IsKnownSafe = exact.Any(point => point.IsKnownSafe == false) ? false : (bool?)null,
                    IsKnownReachable = exact.Any(point => point.IsKnownReachable == false) ? false : (bool?)null,
                    SafetyScore = exact.Length == 0 ? 0 : exact.Min(point => point.SafetyScore)
                };
            }).ToArray();
        }

        private static double? DiagnosticNumber(double value) => Finite(value) ? value : null;
        private static string DiagnosticText(string value) => value == null ? "" : value.Length <= 512 ? value : value.Substring(0, 512);
        private static string DiagnosticPointKey(SpawnPoint point) => string.Format(CultureInfo.InvariantCulture,
            "{0}:{1:R}:{2:R}:{3:R}", point.Map, point.X, point.Y, point.Z);
        private static object DiagnosticPoint(SpawnPoint point) => point == null ? null : new
        {
            mapId = point.Map, x = DiagnosticNumber(point.X), y = DiagnosticNumber(point.Y), z = DiagnosticNumber(point.Z)
        };

        private static void EmitAdmissionDiagnostics(
            QuestDatabase db, QuestSchedulerSnapshot snapshot, QuestScheduleResult result,
            AdmissionDiagnosticCapture capture, int scanThreshold, int minQuestLevelOffset,
            QuestStrategyPack strategyPack, Action<string> log, string globalReason = null)
        {
            if (log == null) return;
            var nearby = snapshot.NearbyQuestGivers ?? Array.Empty<QuestGiverObservation>();
            var itemObservations = snapshot.ItemStarters ?? Array.Empty<QuestItemStarterObservation>();
            var creditObservations = snapshot.CreatureCredits ?? Array.Empty<QuestCreatureCreditObservation>();
            if (result.Selected.Count != 0 && nearby.Count == 0 && itemObservations.Count == 0 && creditObservations.Count == 0 && globalReason == null) return;
            try
            {
                capture ??= new AdmissionDiagnosticCapture();
                var accepted = (snapshot.AcceptedQuests ?? Array.Empty<QuestSchedulerAcceptedQuest>())
                    .GroupBy(value => value.QuestId).ToDictionary(group => group.Key, group => group.Last());
                var completed = new HashSet<uint>(snapshot.CompletedQuestIds ?? Array.Empty<uint>());
                int minimumLevel = Math.Max(1, snapshot.PlayerLevel - minQuestLevelOffset);
                var loaded = new HashSet<(QuestObjectType Type, int Entry)>(nearby.Select(value => (value.ObjectType, value.Entry)));
                var currentItems = ValidItemStarters(snapshot);
                bool Relevant(QuestEntry quest) => accepted.ContainsKey((uint)quest.Id) ||
                    itemObservations.Any(value => value != null && value.QuestId == quest.Id) ||
                    db.QuestGivers.Where(giver => giver.QuestId == quest.Id).Any(giver =>
                        loaded.Contains((giver.GiverType, giver.GiverId)) ||
                        GetRelationSpawns(giver.GiverId, giver.GiverType, db).Any(point => InRange(point, snapshot, scanThreshold))) ||
                    (!db.QuestGivers.Any(giver => giver.QuestId == quest.Id) && BasePickupRejection(quest, snapshot, minimumLevel, strategyPack) == null);
                var relevant = db.Quests.Where(Relevant).OrderBy(quest => quest.Id).ToArray();
                int sequence = 0;
                void Emit(object row) => log("quest-audit " + JsonSerializer.Serialize(row));
                Emit(new
                {
                    schema = "wholesome-quest-diagnostic-v1", kind = "player", sequence = sequence++, utc = snapshot.UtcNow,
                    trigger = globalReason ?? (result.Selected.Count == 0 ? "zero-work" : "nearby-giver-comparison"),
                    playerGuid = snapshot.PlayerGuid.ToString("X16", CultureInfo.InvariantCulture),
                    level = snapshot.PlayerLevel, raceId = snapshot.PlayerRaceId, classId = snapshot.PlayerClassId,
                    mapId = snapshot.MapId, x = DiagnosticNumber(snapshot.X), y = DiagnosticNumber(snapshot.Y), z = DiagnosticNumber(snapshot.Z),
                    hasCompleteQuestLog = snapshot.HasCompleteQuestLog, hasAuthoritativeCompletions = snapshot.HasAuthoritativeCompletions,
                    acceptedCount = accepted.Count, questLogCapacity = snapshot.QuestLogCapacity, completedCount = completed.Count,
                    completionCountAuthority = snapshot.HasAuthoritativeCompletions ? "authoritative" : "unknown",
                    scanThreshold, lowQuestLevelPreference = minimumLevel, nearbyRadius3D = NearbyGiverRadius,
                    giverObservation = snapshot.GiverObservationStatus, datasetFingerprint = snapshot.DatasetFingerprint,
                    inventoryObservation = snapshot.InventoryObservationStatus,
                    currentAreaId = snapshot.PlayerAreaId, areaObservation = snapshot.AreaObservationStatus,
                    dailyObservation = snapshot.DailyObservationStatus,
                    spellObservation = snapshot.SpellObservationStatus,
                    positivelyKnownSpellIds = snapshot.ConfirmedSpellIds,
                    dailyCompletedQuestIds = snapshot.DailyQuestIds,
                    datasetSourceStatus = snapshot.DatasetSourceStatus, strategyFile = "quest_strategies.json",
                    datasetRepairSource = snapshot.DatasetRepairSource,
                    strategyStatus = strategyPack?.Status.ToString() ?? "not-supplied", strategyRecipeCount = strategyPack?.Recipes?.Count ?? 0,
                    selectedCount = result.Selected.Count, relevantQuestCount = relevant.Length, loadedGiverCount = nearby.Count,
                    observedItemStarterCount = itemObservations.Count, observedCreatureCreditCount = creditObservations.Count,
                    note = "Each relevant quest has a separate bounded JSON row; NPC status is not a quest-specific server offer."
                });
                foreach (QuestGiverObservation giver in nearby)
                {
                    var staticSpawns = GetRelationSpawns(giver.Entry, giver.ObjectType, db);
                    var nearest = staticSpawns.Where(point => point.Map == giver.MapId)
                        .OrderBy(point => Math.Pow(point.X - giver.X, 2) + Math.Pow(point.Y - giver.Y, 2) + Math.Pow(point.Z - giver.Z, 2)).FirstOrDefault();
                    Emit(new
                    {
                        kind = "giver", sequence = sequence++, objectType = giver.ObjectType.ToString(), entry = giver.Entry,
                        guid = giver.Guid.ToString("X16", CultureInfo.InvariantCulture), name = DiagnosticText(giver.Name),
                        observedUtc = giver.ObservedUtc, mapId = giver.MapId,
                        x = DiagnosticNumber(giver.X), y = DiagnosticNumber(giver.Y), z = DiagnosticNumber(giver.Z),
                        distance3D = DiagnosticNumber(Math.Sqrt(Math.Pow(giver.X - snapshot.X, 2) + Math.Pow(giver.Y - snapshot.Y, 2) + Math.Pow(giver.Z - snapshot.Z, 2))),
                        isQuestGiver = giver.IsQuestGiver, rawQuestStatus = giver.RawQuestStatus, hasAvailableQuest = giver.HasAvailableQuest,
                        statusEvidence = giver.QuestStatusEvidence,
                        relationStatus = db.QuestGivers.Any(value => value.GiverId == giver.Entry && value.GiverType == giver.ObjectType)
                            ? "database-giver-relations-found" : "no-database-giver-relations",
                        questIds = db.QuestGivers.Where(value => value.GiverId == giver.Entry && value.GiverType == giver.ObjectType)
                            .Select(value => value.QuestId).Distinct().OrderBy(value => value).ToArray(),
                        staticSpawnCount = staticSpawns.Count(), nearestStoredSameMap = DiagnosticPoint(nearest),
                        storedMaps = staticSpawns.Select(point => point.Map).Distinct().OrderBy(value => value).ToArray(),
                        liveStaticDelta3D = nearest == null ? (double?)null : DiagnosticNumber(Math.Sqrt(
                            Math.Pow(nearest.X - giver.X, 2) + Math.Pow(nearest.Y - giver.Y, 2) + Math.Pow(nearest.Z - giver.Z, 2)))
                    });
                }
                foreach (QuestItemStarterObservation item in itemObservations.Where(value => value != null))
                    Emit(new { kind = "item-starter", sequence = sequence++, questId = item.QuestId, itemEntry = item.ItemEntry,
                        itemGuid = item.ItemGuid.ToString("X16", CultureInfo.InvariantCulture),
                        playerGuid = item.PlayerGuid.ToString("X16", CultureInfo.InvariantCulture), name = DiagnosticText(item.Name),
                        observedUtc = item.ObservedUtc, mapId = item.MapId, active = item.IsActive,
                        usableForPlanning = currentItems.Any(value => value.ItemGuid == item.ItemGuid && value.QuestId == item.QuestId && value.ItemEntry == item.ItemEntry),
                        source = "original-client:GetContainerItemQuestInfo", note = "Observed item association; dispatch revalidates slot, quest, history and capacity." });
                foreach (QuestCreatureCreditObservation credit in creditObservations.Where(value => value != null))
                    Emit(new { kind = "creature-credit", sequence = sequence++, entry = credit.Entry, credit1 = credit.Credit1, credit2 = credit.Credit2,
                        guid = credit.Guid.ToString("X16", CultureInfo.InvariantCulture), playerGuid = credit.PlayerGuid.ToString("X16", CultureInfo.InvariantCulture),
                        observedUtc = credit.ObservedUtc, mapId = credit.MapId, x = DiagnosticNumber(credit.X), y = DiagnosticNumber(credit.Y), z = DiagnosticNumber(credit.Z),
                        aliveAttackableSelectable = credit.AliveAttackableSelectable,
                        source = "original-client:loaded-creature-entry-and-cache-credits", note = "A direct entry or cached credit alias identifies an observed actor; neither proves a scripted action, navigation or completion." });
                foreach (QuestEntry quest in relevant)
                {
                    uint id = (uint)quest.Id;
                    accepted.TryGetValue(id, out var active);
                    bool enderRole = active?.IsCompleted == true;
                    var diagnosticRelations = enderRole
                        ? db.QuestEnders.Where(ender => ender.QuestId == quest.Id).Select(ender => new QuestGiverEntry
                            { QuestId = ender.QuestId, GiverId = ender.EnderId, GiverType = ender.EnderType, GiverName = ender.EnderName })
                        : GetPickupRelations(quest.Id, db, snapshot);
                    var relations = diagnosticRelations
                        .GroupBy(giver => (giver.GiverType, giver.GiverId)).Select(group => group.First()).ToArray();
                    IEnumerable<SpawnPoint> RelationGeometry(QuestGiverEntry giver) => enderRole
                        ? GetObservedRelationSpawns(giver.GiverId, giver.GiverType, db, snapshot)
                        : GetObservedPickupSpawns(quest.Id, giver.GiverId, giver.GiverType, db, snapshot);
                    var spawns = relations.SelectMany(RelationGeometry).ToArray();
                    var recovery = capture.Recovery.Where(value => value.QuestId == id).ToArray();
                    string reason = globalReason;
                    if (reason == null && !snapshot.HasCompleteQuestLog) reason = "quest-log-incomplete";
                    if (reason == null && active == null && !snapshot.HasAuthoritativeCompletions) reason = "completion-history-unknown";
                    if (reason == null && active == null && accepted.Count >= Math.Max(1, snapshot.QuestLogCapacity)) reason = "quest-log-full";
                    if (reason == null && result.Plan.Any(plan => plan.Quest.Id == quest.Id)) reason = "selected";
                    if (reason == null && active == null && capture.Rejections.TryGetValue(quest.Id, out string rejection)) reason = rejection;
                    if (reason == null && active == null && completed.Contains(id)) reason = "already-rewarded";
                    if (reason == null && active == null) reason = BasePickupRejection(quest, snapshot, minimumLevel, strategyPack);
                    if (reason == null && recovery.Any(value => !value.MayAttempt)) reason = "recovery-blocked";
                    if (reason == null && active?.IsFailed == true) reason = "accepted-failed";
                    if (reason == null && active != null && (QuestDeliveryPolicy.HasContract(quest) || active.IsCompleted && (quest.SupplementalSupply != null || QuestRequiredStockPolicy.HasContract(quest))))
                        reason = QuestDeliveryPolicy.TurnInRejection(quest, snapshot.CarriedItemCounts)
                            ?? (active.IsCompleted ? null : "delivery-awaiting-server-completion");
                    if (reason == null && capture.SelectionReasons.TryGetValue(quest.Id, out string selectionReason)) reason = selectionReason;
                    if (reason == null && active != null && !enderRole) reason = "accepted-objective-work-not-selected";
                    if (reason == null && relations.Length == 0) reason = enderRole ? "no-ender-relations" : "no-giver-relations";
                    if (reason == null && spawns.Length == 0) reason = "no-relation-spawns";
                    if (reason == null && !spawns.Any(point => point.Map == snapshot.MapId)) reason = "no-spawn-on-player-map";
                    if (reason == null && !spawns.Any(point => InRange(point, snapshot, scanThreshold))) reason = "outside-scan-radius";
                    var inRange = spawns.Where(point => InRange(point, snapshot, scanThreshold)).ToArray();
                    bool Unsafe(SpawnPoint point) => point.IsKnownSafe == false ||
                        (capture.Navigation.TryGetValue(DiagnosticPointKey(point), out var assessment) && assessment.IsKnownSafe == false);
                    bool Unreachable(SpawnPoint point) => point.IsKnownReachable == false ||
                        (capture.Navigation.TryGetValue(DiagnosticPointKey(point), out var assessment) && assessment.IsKnownReachable == false);
                    if (reason == null && inRange.Length > 0 && inRange.All(Unsafe)) reason = "navigation-known-unsafe";
                    if (reason == null && inRange.Length > 0 && inRange.All(Unreachable)) reason = "navigation-known-unreachable";
                    if (reason == null && inRange.Length > 0 && inRange.All(point => Unsafe(point) || Unreachable(point))) reason = "navigation-known-blocked";
                    if (reason == null) reason = enderRole ? "no-selected-ender-endpoint" : "no-selected-giver-endpoint";
                    Emit(new
                    {
                        kind = "quest", sequence = sequence++, questId = quest.Id, name = DiagnosticText(quest.Name),
                        minLevel = quest.MinLevel, questLevel = quest.QuestLevel, finalReason = reason,
                        relationRole = enderRole ? "ender" : "giver",
                        gates = new
                        {
                            completeLog = snapshot.HasCompleteQuestLog, completionAuthority = snapshot.HasAuthoritativeCompletions,
                            admissionDecisions = capture.Gates.TryGetValue(quest.Id, out var gates) ? gates : new Dictionary<string, object>(),
                            admissionDecisionEvidence = "Only gates actually reached by pickup admission are present; absent keys were not evaluated.",
                            accepted = active != null, acceptedComplete = active?.IsCompleted, acceptedFailed = active?.IsFailed,
                            rewarded = snapshot.HasAuthoritativeCompletions ? (bool?)completed.Contains(id) : null,
                            minimumLevel = snapshot.PlayerLevel >= quest.MinLevel,
                            lowQuestLevelPreferencePassed = quest.QuestLevel <= 0 || quest.QuestLevel >= minimumLevel,
                            basePickupRejection = BasePickupRejection(quest, snapshot, minimumLevel, strategyPack),
                            allowableRaces = quest.AllowableRaces, raceAllowed = RaceAllowed(quest.AllowableRaces, snapshot.PlayerRaceId),
                            declaredRequirementRejection = DeclaredRequirementRejection(quest, snapshot),
                            availabilityConditions = QuestAvailabilityPolicy.Evaluate(quest, snapshot),
                            allowableClasses = quest.AllowableClasses, maxLevel = quest.MaxLevel,
                            requiredSkillId = quest.RequiredSkillID, requiredSkillPoints = quest.RequiredSkillPoints,
                            requiredMinRepFaction = quest.RequiredMinRepFaction, requiredMinRepValue = quest.RequiredMinRepValue,
                            requiredMaxRepFaction = quest.RequiredMaxRepFaction, requiredMaxRepValue = quest.RequiredMaxRepValue,
                            requiredFactionValue1 = quest.RequiredFactionValue1, requiredFactionValue2 = quest.RequiredFactionValue2,
                            deliveryRequirements = quest.DeliveryItems, acceptanceSupplies = quest.AcceptanceSupplies,
                            supplementalSupply = quest.SupplementalSupply,
                            requiredStockItems = quest.RequiredStockItems,
                            requiredStock = quest.RequiredStockItems?.Select(item => new { item.ItemId, required = item.Count,
                                carried = snapshot.CarriedItemCounts != null ? (long?)(snapshot.CarriedItemCounts.TryGetValue(item.ItemId, out long stock) ? stock : 0) : null }).ToArray(),
                            deliveryStock = quest.DeliveryItems?.Select(item => new { item.ItemId,
                                carried = snapshot.CarriedItemCounts != null ? (long?)(snapshot.CarriedItemCounts.TryGetValue(item.ItemId, out long stock) ? stock : 0) : null }).ToArray(),
                            skillObservations = snapshot.SkillValues, reputationObservations = snapshot.ReputationValues,
                            optionalRequirementEvidence = "Null means absent/unknown; zero is an explicit source value. Server offer/acceptance remains required.",
                            requiredFactionId1 = quest.RequiredFactionId1, requiredFactionId2 = quest.RequiredFactionId2,
                            prevQuestId = quest.PrevQuestID, previousQuestIds = quest.PreviousQuestsIds, nextQuestId = quest.NextQuestID,
                            exclusiveGroup = quest.ExclusiveGroup, supportedGeneric = Supported(quest),
                            supportedForPickup = SupportedForPickup(quest, strategyPack),
                            strategyStatus = strategyPack?.Status.ToString() ?? "not-supplied",
                            strategyRecipes = strategyPack?.Recipes?.Where(recipe => recipe.QuestId == quest.Id)
                                .Select(recipe => new { recipe.ObjectiveIndex, kind = recipe.Kind.ToString(), recipe.SourceRef }).ToArray(),
                            flags = quest.Flags, specialFlags = quest.SpecialFlags, startItem = quest.StartItem,
                            carriedStartItem = snapshot.CarriedItemCounts != null && snapshot.CarriedItemCounts.TryGetValue(quest.StartItem, out long count) ? (long?)count : null
                        },
                        relations = relations.Select(giver => new
                        {
                            entry = giver.GiverId, objectType = giver.GiverType.ToString(), name = DiagnosticText(giver.GiverName),
                            source = enderRole ? "quest_data.json:QuestEnders" : giver.GiverType == QuestObjectType.Item
                                ? "original-client:observed-item-starter" : "quest_data.json:QuestGivers", loadedNearby = loaded.Contains((giver.GiverType, giver.GiverId)),
                            storedSpawnCount = GetRelationSpawns(giver.GiverId, giver.GiverType, db).Count(),
                            nearestStored = DiagnosticPoint(GetRelationSpawns(giver.GiverId, giver.GiverType, db)
                                .OrderBy(point => point.Map == snapshot.MapId ? 0 : 1).ThenBy(point => Distance(point, snapshot)).FirstOrDefault()),
                            storedInScanRange = GetRelationSpawns(giver.GiverId, giver.GiverType, db).Any(point => InRange(point, snapshot, scanThreshold)),
                            effectiveSpawnCount = RelationGeometry(giver).Count(),
                            effectiveSpawns = RelationGeometry(giver)
                                .OrderBy(point => point.Map == snapshot.MapId ? 0 : 1).ThenBy(point => Distance(point, snapshot)).Take(8).Select(DiagnosticPoint).ToArray(),
                            geometrySampleLimit = 8,
                            effectiveInScanRange = RelationGeometry(giver).Any(point => InRange(point, snapshot, scanThreshold))
                        }).ToArray(),
                        objectiveGeometry = active == null || enderRole ? null : quest.Objectives.Select(objective => new
                        {
                            index = objective.Index, kind = objective.Type.ToString(), creditEntry = objective.MobId,
                            storedSpawnCount = GetObjectiveSpawns(objective, db).Count(),
                            sourceCreditProducerCount = QuestCreditSourceCatalog.ForObjective(quest, objective, db).Count(),
                            sourceCreditProducers = QuestCreditSourceCatalog.ForObjective(quest, objective, db).Take(8)
                                .Select(source => new { source.CreatureId, source.CreditId, source.CreditField, source.SourceRef,
                                    searchPointCount = source.Points.Count,
                                    liveActorObserved = (snapshot.CreatureCredits ?? Array.Empty<QuestCreatureCreditObservation>()).Any(value =>
                                        value.Entry == source.CreatureId && value.Guid != 0 && value.PlayerGuid == snapshot.PlayerGuid &&
                                        value.MapId == snapshot.MapId && value.ObservedUtc == snapshot.UtcNow && value.AliveAttackableSelectable &&
                                        (value.Credit1 == source.CreditId || value.Credit2 == source.CreditId)),
                                    sourcePointsAreSearchHints = true }).ToArray(),
                            effectiveSpawnCount = GetObservedObjectiveSpawns(quest, objective, active, db, snapshot).Count(),
                            effectiveSpawns = GetObservedObjectiveSpawns(quest, objective, active, db, snapshot).Take(8).Select(DiagnosticPoint).ToArray(),
                            geometrySampleLimit = 8
                        }).ToArray(),
                        navigation = spawns.Where(point => capture.Navigation.ContainsKey(DiagnosticPointKey(point)))
                            .GroupBy(DiagnosticPointKey).Select(group => group.First()).Select(point => new
                            {
                                point = DiagnosticPoint(point),
                                safe = capture.Navigation[DiagnosticPointKey(point)].IsKnownSafe,
                                reachable = capture.Navigation[DiagnosticPointKey(point)].IsKnownReachable,
                                safetyScore = capture.Navigation[DiagnosticPointKey(point)].SafetyScore
                            }).ToArray(),
                        navigationEvidence = "Only assessments actually used by this scan are included; an empty array means not assessed.",
                        recovery = recovery.Select(value => new
                        {
                            owner = value.Owner, scope = value.Scope, state = value.State,
                            mayAttempt = value.MayAttempt, retryUtc = value.RetryUtc, status = DiagnosticText(value.Status)
                        }).ToArray()
                    });
                }
                Emit(new { kind = "end", sequence, utc = snapshot.UtcNow, emittedQuestCount = relevant.Length,
                    loadedGiverCount = nearby.Count, omittedRelevantQuestCount = 0 });
            }
            catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException)
            {
                // Diagnostic failure must not change admission, recovery or execution.
                try { log("quest-audit " + JsonSerializer.Serialize(new { kind = "diagnostic-error", error = error.GetType().Name })); }
                catch (Exception nested) when (nested is not ThreadInterruptedException && nested is not OperationCanceledException) { }
            }
        }
    }
}
