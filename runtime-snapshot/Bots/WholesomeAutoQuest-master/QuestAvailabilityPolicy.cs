using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

#nullable disable
namespace WholesomeAQ
{
    public sealed class QuestAvailabilityPredicate
    {
        public int Type { get; init; }
        public int Value1 { get; init; }
        public int Value2 { get; init; }
        public int Value3 { get; init; }
        public bool Negative { get; init; }
        public string SourceRef { get; init; }
    }

    public sealed class QuestAvailabilityGroup
    {
        public int ElseGroup { get; init; }
        public IReadOnlyList<QuestAvailabilityPredicate> Conditions { get; init; }
    }

    public sealed class QuestAvailabilityReference
    {
        public int QuestId { get; init; }
        public int QuestType { get; init; }
        public int SpecialFlags { get; init; }
        public int QuestSortID { get; init; }
        public string SourceRef { get; init; }
    }

    public sealed class QuestAvailabilityItemReference
    {
        public int ItemId { get; init; }
        public string SourceRef { get; init; }
    }

    public sealed class QuestAvailabilitySpellReference
    {
        public int SpellId { get; init; }
        public string SourceRef { get; init; }
    }

    public sealed class QuestAvailabilityContract
    {
        public int QuestId { get; init; }
        public string SourceRef { get; init; }
        public IReadOnlyList<QuestAvailabilityReference> ReferencedQuests { get; init; }
        // Preserve the exact legacy non-item contract representation.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<QuestAvailabilityItemReference> ReferencedItems { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<QuestAvailabilitySpellReference> ReferencedSpells { get; init; }
        public IReadOnlyList<QuestAvailabilityGroup> Groups { get; init; }
    }

    public sealed class QuestAvailabilityPredicateResult
    {
        public int ElseGroup { get; init; }
        public int PredicateIndex { get; init; }
        public int Type { get; init; }
        public int ReferencedQuestId { get; init; }
        public int StateMask { get; init; }
        public bool Negative { get; init; }
        public bool? Met { get; init; }
        public int? RawAcceptedState { get; init; }
        public bool? PermanentlyRewarded { get; init; }
        public int? ItemId { get; init; }
        public int? RequiredItemCount { get; init; }
        public long? ObservedItemCount { get; init; }
        public int? RequiredLevel { get; init; }
        public int? ObservedLevel { get; init; }
        public int? LevelComparison { get; init; }
        public int? RequiredAreaId { get; init; }
        public int? ObservedAreaId { get; init; }
        public bool? DailyCompleted { get; init; }
        public int? RequiredSpellId { get; init; }
        public bool? SpellPositivelyKnown { get; init; }
        public string SourceRef { get; init; }
    }

    public sealed class QuestAvailabilityDecision
    {
        public string Status { get; init; }
        public string Rejection { get; init; }
        public string SourceRef { get; init; }
        public IReadOnlyList<QuestAvailabilityPredicateResult> Predicates { get; init; }
            = Array.Empty<QuestAvailabilityPredicateResult>();
    }

    public static class QuestAvailabilityPolicy
    {
        // Pinned TC335 ConditionMgr.cpp:835-878: AND within an ElseGroup,
        // OR between groups. Unknown observations stay unknown under negation.
        private static bool? All(IEnumerable<bool?> values) => values.Any(v => v == false) ? false
            : values.Any(v => !v.HasValue) ? null : true;
        private static bool? Any(IEnumerable<bool?> values) => values.Any(v => v == true) ? true
            : values.Any(v => !v.HasValue) ? null : false;

        public static QuestAvailabilityDecision Evaluate(QuestEntry quest, QuestSchedulerSnapshot snapshot)
        {
            QuestAvailabilityContract contract = quest?.AvailabilityConditions;
            if (contract == null) return new QuestAvailabilityDecision { Status = "not-declared" };
            if (contract.QuestId != quest.Id || snapshot == null || snapshot.PlayerGuid == 0)
                return new QuestAvailabilityDecision { Status = "observation-unknown", SourceRef = contract.SourceRef,
                    Rejection = "availability-condition-observation-unknown" };
            var results = new List<QuestAvailabilityPredicateResult>();
            var groupResults = new List<bool?>();
            foreach (QuestAvailabilityGroup group in contract.Groups)
            {
                var met = new List<bool?>();
                for (int index = 0; index < group.Conditions.Count; index++)
                {
                    QuestAvailabilityPredicate condition = group.Conditions[index];
                    uint id = (uint)condition.Value1;
                    bool? rewarded = snapshot.HasAuthoritativeCompletions && snapshot.CompletedQuestIds != null
                        ? snapshot.CompletedQuestIds.Contains(id) : (bool?)null;
                    int? state = State(snapshot, id);
                    long? carried = condition.Type == 2 ? CarriedCount(snapshot.CarriedItemCounts, condition.Value1) : null;
                    bool? value = condition.Type switch
                    {
                        2 => condition.Value2 > 0 && condition.Value3 == 0 && carried.HasValue
                            ? carried >= condition.Value2 : null,
                        8 => rewarded,
                        9 => state.HasValue ? state == 3 : null,
                        14 => state.HasValue ? state == 0 : null,
                        23 => snapshot.PlayerAreaId > 0 ? snapshot.PlayerAreaId == condition.Value1 : null,
                        25 => !condition.Negative ? PositiveSpell(snapshot.ConfirmedSpellIds, id) : null,
                        27 => snapshot.PlayerLevel > 0 ? CompareLevel(snapshot.PlayerLevel, condition.Value1, condition.Value2) : null,
                        28 => state.HasValue ? state != 1 ? false : rewarded.HasValue ? !rewarded.Value : null : null,
                        43 => DailyMembership(snapshot.DailyQuestIds, id),
                        47 => Any(new[] {
                            (condition.Value2 & 64) != 0 ? rewarded : (bool?)false,
                            (condition.Value2 & (1 | 2 | 8 | 32)) != 0
                                ? state.HasValue ? state != 6 && (condition.Value2 & (1 << state.Value)) != 0 : (bool?)null
                                : false }),
                        _ => null
                    };
                    if (condition.Negative && value.HasValue) value = !value.Value;
                    met.Add(value);
                    results.Add(new QuestAvailabilityPredicateResult { ElseGroup = group.ElseGroup, PredicateIndex = index,
                        Type = condition.Type, ReferencedQuestId = condition.Type is 2 or 23 or 25 or 27 ? 0 : condition.Value1,
                        StateMask = condition.Type == 47 ? condition.Value2 : 0,
                        Negative = condition.Negative, Met = value,
                        RawAcceptedState = condition.Type is not (2 or 23 or 25 or 27 or 43) && snapshot.RawQuestStates != null && snapshot.RawQuestStates.TryGetValue(id, out int raw) ? raw : null,
                        PermanentlyRewarded = condition.Type is 2 or 23 or 25 or 27 or 43 ? null : rewarded,
                        ItemId = condition.Type == 2 ? condition.Value1 : null,
                        RequiredItemCount = condition.Type == 2 ? condition.Value2 : null,
                        ObservedItemCount = carried,
                        RequiredLevel = condition.Type == 27 ? condition.Value1 : null,
                        ObservedLevel = condition.Type == 27 && snapshot.PlayerLevel > 0 ? snapshot.PlayerLevel : null,
                        LevelComparison = condition.Type == 27 ? condition.Value2 : null,
                        RequiredAreaId = condition.Type == 23 ? condition.Value1 : null,
                        ObservedAreaId = condition.Type == 23 && snapshot.PlayerAreaId > 0 ? snapshot.PlayerAreaId : null,
                        DailyCompleted = condition.Type == 43 ? DailyMembership(snapshot.DailyQuestIds, id) : null,
                        RequiredSpellId = condition.Type == 25 ? condition.Value1 : null,
                        SpellPositivelyKnown = condition.Type == 25 ? PositiveSpell(snapshot.ConfirmedSpellIds, id) : null,
                        SourceRef = condition.SourceRef });
                }
                groupResults.Add(All(met));
            }
            bool? passed = Any(groupResults);
            return new QuestAvailabilityDecision { Status = passed == true ? "satisfied" : passed == false ? "not-satisfied" : "observation-unknown",
                Rejection = passed == true ? null : passed == false ? "availability-condition-not-satisfied" : "availability-condition-observation-unknown",
                SourceRef = contract.SourceRef, Predicates = results.AsReadOnly() };
        }

        private static bool? PositiveSpell(IReadOnlyCollection<uint> ids, uint id)
        {
            if (ids == null || ids.Count > 64 || ids.Any(value => value == 0 || value > int.MaxValue) ||
                ids.Distinct().Count() != ids.Count) return null;
            return ids.Contains(id) ? true : null;
        }

        private static long? CarriedCount(IReadOnlyDictionary<int, long> counts, int item)
        {
            if (counts == null || item <= 0 || counts.Any(pair => pair.Key <= 0 || pair.Value < 0)) return null;
            return counts.TryGetValue(item, out long held) ? held : 0;
        }

        private static bool? DailyMembership(IReadOnlyCollection<uint> ids, uint id)
        {
            if (ids == null || ids.Count > 25 || ids.Any(value => value == 0 || value > int.MaxValue) ||
                ids.Distinct().Count() != ids.Count) return null;
            return ids.Contains(id);
        }

        // Pinned TC335 Util.h CompareValues: equality, greater, less, >=, <=.
        private static bool? CompareLevel(int observed, int required, int comparison) => comparison switch
        {
            0 => observed == required, 1 => observed > required, 2 => observed < required,
            3 => observed >= required, 4 => observed <= required, _ => null
        };

        private static int? State(QuestSchedulerSnapshot snapshot, uint id)
        {
            if (!snapshot.HasCompleteQuestLog || snapshot.RawQuestStates == null || snapshot.RawQuestStates.Count > 25 ||
                snapshot.RawQuestStates.Any(row => row.Key == 0 || row.Key > int.MaxValue || row.Value is not (1 or 3 or 5))) return null;
            if (snapshot.RawQuestStates.TryGetValue(id, out int state)) return state;
            if (!snapshot.HasAuthoritativeCompletions || snapshot.CompletedQuestIds == null) return null;
            // Only source-validated nonrepeatable, nonseasonal, ordinary references
            // reach this owner. Other reward/status lifecycles are not inferred.
            return snapshot.CompletedQuestIds.Contains(id) ? 6 : 0;
        }

        public static IReadOnlyDictionary<uint, int> RawStates(QuestLogSnapshot observation)
        {
            if (observation == null || !observation.IsIdentityComplete) return null;
            return FromRawFlags(observation.AcceptedQuestIds, observation.ReadyQuestIds, observation.FailedQuestIds);
        }

        public static IReadOnlyDictionary<uint, int> FromRawFlags(IEnumerable<uint> accepted, IEnumerable<uint> ready, IEnumerable<uint> failed)
        {
            if (accepted == null || ready == null || failed == null) return null;
            uint[] ids = accepted.ToArray(); var complete = new HashSet<uint>(ready); var failures = new HashSet<uint>(failed);
            if (ids.Length > 25 || ids.Any(id => id == 0 || id > int.MaxValue) || ids.Distinct().Count() != ids.Length ||
                complete.Any(id => !ids.Contains(id)) || failures.Any(id => !ids.Contains(id))) return null;
            // FailQuest sets the failed flag; a prior completed bit may still be set.
            // Build12340 GetQuestLogTitle also gives the failed bit priority.
            return new ReadOnlyDictionary<uint, int>(ids.ToDictionary(id => id, id => failures.Contains(id) ? 5 : complete.Contains(id) ? 1 : 3));
        }

        public static bool RequirementsCurrent(IEnumerable<QuestPlanEntry> plan, QuestSchedulerSnapshot snapshot) =>
            plan != null && plan.Where(entry => entry.Stage == QuestWorkStage.Pickup && entry.Quest?.AvailabilityConditions != null)
                .All(entry => Evaluate(entry.Quest, snapshot).Rejection == null);
    }

    public static partial class QuestDataRepairPackLoader
    {
        private static void ApplyAvailabilityConditions(JsonElement root, Dictionary<int, QuestEntry> quests)
        {
            var subjects = new HashSet<int>();
            foreach (JsonElement row in Rows(root, "QuestAvailabilityConditions", 10000))
            {
                bool hasItems = row.TryGetProperty("ReferencedItems", out _);
                bool hasSpells = row.TryGetProperty("ReferencedSpells", out _);
                var fields = new List<string> { "QuestId", "SourceRef", "ReferencedQuests", "Groups" };
                if (hasItems) fields.Add("ReferencedItems");
                if (hasSpells) fields.Add("ReferencedSpells");
                Exact(row, fields.ToArray());
                int id = Positive(row, "QuestId"); string source = Text(row, "SourceRef");
                if (!subjects.Add(id) || !quests.TryGetValue(id, out QuestEntry quest) || quest.AvailabilityConditions != null)
                    throw new InvalidDataException("Availability conditions require a unique existing quest.");
                var references = new List<QuestAvailabilityReference>(); var ids = new HashSet<int>();
                foreach (JsonElement reference in Rows(row, "ReferencedQuests", 256))
                {
                    Exact(reference, "QuestId", "QuestType", "SpecialFlags", "QuestSortID", "SourceRef");
                    int referencedId = Positive(reference, "QuestId"), method = Integer(reference, "QuestType"),
                        flags = Integer(reference, "SpecialFlags"), sort = Integer(reference, "QuestSortID");
                    if (!ids.Add(referencedId) || method is not (0 or 2) || flags < 0 ||
                        quests.TryGetValue(referencedId, out QuestEntry existing) && (existing.SpecialFlags != flags || existing.QuestSortID != sort))
                        throw new InvalidDataException("Availability reference is invalid or disagrees with the source-bound quest.");
                    references.Add(new QuestAvailabilityReference { QuestId = referencedId, QuestType = method,
                        SpecialFlags = flags, QuestSortID = sort, SourceRef = Text(reference, "SourceRef") });
                }
                var itemReferences = new List<QuestAvailabilityItemReference>(); var itemIds = new HashSet<int>();
                if (hasItems)
                {
                    foreach (JsonElement item in Rows(row, "ReferencedItems", 256))
                    {
                        Exact(item, "ItemId", "SourceRef"); int itemId = Positive(item, "ItemId");
                        if (!itemIds.Add(itemId)) throw new InvalidDataException("Availability item source is duplicated.");
                        itemReferences.Add(new QuestAvailabilityItemReference { ItemId = itemId, SourceRef = Text(item, "SourceRef") });
                    }
                    if (itemReferences.Count == 0) throw new InvalidDataException("Availability item sources cannot be an empty declaration.");
                }
                var spellReferences = new List<QuestAvailabilitySpellReference>(); var spellIds = new HashSet<int>();
                if (hasSpells)
                {
                    foreach (JsonElement spell in Rows(row, "ReferencedSpells", 64))
                    {
                        Exact(spell, "SpellId", "SourceRef"); int spellId = Positive(spell, "SpellId");
                        if (!spellIds.Add(spellId)) throw new InvalidDataException("Availability spell reference is duplicated.");
                        spellReferences.Add(new QuestAvailabilitySpellReference { SpellId = spellId, SourceRef = Text(spell, "SourceRef") });
                    }
                    if (spellReferences.Count == 0) throw new InvalidDataException("Availability spell sources cannot be empty.");
                }
                var groups = new List<QuestAvailabilityGroup>(); var groupIds = new HashSet<int>(); int total = 0;
                foreach (JsonElement group in Rows(row, "Groups", 32))
                {
                    Exact(group, "ElseGroup", "Conditions"); int groupId = Integer(group, "ElseGroup");
                    if (groupId < 0 || !groupIds.Add(groupId)) throw new InvalidDataException("Availability group identity is invalid or repeated.");
                    var conditions = new List<QuestAvailabilityPredicate>(); var keys = new HashSet<(int, int, int, bool)>();
                    foreach (JsonElement condition in Rows(group, "Conditions", 128))
                    {
                        Exact(condition, "Type", "Value1", "Value2", "Value3", "Negative", "SourceRef");
                        int type = Integer(condition, "Type"), value1 = Positive(condition, "Value1"),
                            value2 = Integer(condition, "Value2"), value3 = Integer(condition, "Value3");
                        JsonValueKind negativeKind = condition.GetProperty("Negative").ValueKind;
                        if (negativeKind is not (JsonValueKind.True or JsonValueKind.False))
                            throw new InvalidDataException("Availability negation must be an explicit boolean.");
                        bool negative = condition.GetProperty("Negative").GetBoolean();
                        QuestAvailabilityReference referenced = references.FirstOrDefault(reference => reference.QuestId == value1);
                        bool permanent = referenced != null && (referenced.SpecialFlags & 1) == 0 &&
                            !new[] { -22, -284, -366, -369, -370, -374, -376 }.Contains(referenced.QuestSortID);
                        // Daily membership has its own current-reset-cycle observation.
                        // It does not grant permanent history to any other predicate.
                        bool referenceValid = type == 2 ? itemIds.Contains(value1) : type == 25 ? spellIds.Contains(value1) : type is 23 or 27 || referenced != null &&
                            (type == 43 || permanent && (type == 8 || referenced.QuestType == 2));
                        bool valuesInvalid = type == 2 ? value2 <= 0 : type == 47 ? value2 <= 0 || (value2 & ~107) != 0
                            : type == 27 ? value2 < 0 || value2 > 4 : value2 != 0;
                        if (type is not (2 or 8 or 9 or 14 or 23 or 25 or 27 or 28 or 43 or 47) || !referenceValid || value3 != 0 || valuesInvalid ||
                            (type == 25 && negative) ||
                            !keys.Add((type, value1, value2, negative)) || ++total > 256)
                            throw new InvalidDataException("Availability predicate is unsupported, ambiguous or missing its source reference.");
                        conditions.Add(new QuestAvailabilityPredicate { Type = type, Value1 = value1, Value2 = value2,
                            Value3 = value3, Negative = negative, SourceRef = Text(condition, "SourceRef") });
                    }
                    if (conditions.Count == 0) throw new InvalidDataException("An availability group cannot silently become empty.");
                    groups.Add(new QuestAvailabilityGroup { ElseGroup = groupId, Conditions = conditions.AsReadOnly() });
                }
                if (groups.Count == 0) throw new InvalidDataException("An availability contract needs all of its source groups.");
                quest.AvailabilityConditions = new QuestAvailabilityContract { QuestId = id, SourceRef = source,
                    ReferencedQuests = references.AsReadOnly(), ReferencedItems = hasItems ? itemReferences.AsReadOnly() : null,
                    ReferencedSpells = hasSpells ? spellReferences.AsReadOnly() : null,
                    Groups = groups.AsReadOnly() };
            }
        }
    }

    public partial class QuestScheduler
    {
        private static Func<bool> CreateAvailabilityGuard(IReadOnlyList<QuestPlanEntry> plan, LocalPlayer me, QuestLog log)
        {
            QuestPlanEntry[] constrained = plan.Where(entry => entry.Stage == QuestWorkStage.Pickup && entry.Quest.AvailabilityConditions != null).ToArray();
            if (constrained.Length == 0) return null;
            bool needsInventory = constrained.Any(entry => entry.Quest.AvailabilityConditions.Groups
                .Any(group => group.Conditions.Any(predicate => predicate.Type == 2)));
            int[] spells = RequiredSpellQueries(constrained.Select(entry => entry.Quest));
            return () => QuestAvailabilityPolicy.RequirementsCurrent(constrained,
                CaptureAvailabilitySnapshotForSpellInputs(me, log, needsInventory, spells));
        }

        // Preserve the existing observation-only reflection/caller contract.
        private static QuestSchedulerSnapshot CaptureAvailabilitySnapshot(LocalPlayer me, QuestLog log) =>
            CaptureAvailabilitySnapshotForInputs(me, log, false);

        private static QuestSchedulerSnapshot CaptureAvailabilitySnapshotForInputs(LocalPlayer me, QuestLog log, bool needsInventory) =>
            CaptureAvailabilitySnapshotForSpellInputs(me, log, needsInventory, Array.Empty<int>());

        private static int[] RequiredSpellQueries(IEnumerable<QuestEntry> quests) => quests
            .Where(quest => quest.AvailabilityConditions != null)
            .SelectMany(quest => quest.AvailabilityConditions.Groups).SelectMany(group => group.Conditions)
            .Where(predicate => predicate.Type == 25 && !predicate.Negative).Select(predicate => predicate.Value1)
            .Distinct().OrderBy(id => id).Take(65).ToArray();

        private static QuestSchedulerSnapshot CaptureAvailabilitySnapshotForSpellInputs(LocalPlayer me, QuestLog log, bool needsInventory, int[] requestedSpells)
        {
            var memory = ObjectManager.Wow;
            try
            {
                if (me == null || log == null || memory == null || !ReferenceEquals(me, ObjectManager.Me) ||
                    !Styx.StyxWoW.IsInWorld || !me.IsValid) return null;
                ulong guid = me.Guid;
                if (guid == 0) return null;
                int level = me.Level;
                QuestAreaSnapshot area = QuestAreaSnapshot.Capture(me);
                QuestDailySnapshot daily = QuestDailySnapshot.Capture(me);
                QuestLogSnapshot raw = log.CaptureSnapshot();
                var states = QuestAvailabilityPolicy.RawStates(raw);
                var pendingCompletions = QuestTurnInCompletion.ObservePending(raw);
                bool authoritative = log.TryGetAuthoritativeCompletedQuests(out var rewarded);
                // An ordinary history-only predicate must not scan every bag at
                // each permission check. Item constraints use the complete owner.
                QuestInventorySnapshot inventory = needsInventory ? QuestInventorySnapshot.Capture(me) : null;
                QuestSpellKnowledgeSnapshot spells = requestedSpells.Length != 0 ? QuestSpellKnowledgeSnapshot.Capture(me, requestedSpells) : null;
                var snapshot = new QuestSchedulerSnapshot { PlayerGuid = guid, PlayerLevel = level, UtcNow = DateTime.UtcNow,
                    PlayerAreaId = area.IsCurrent() && area.MapId == (int)me.MapId ? area.AreaId : null,
                    AreaObservationStatus = area.Status,
                    DailyQuestIds = daily.IsCurrent() ? daily.QuestIds : null,
                    DailyObservationStatus = daily.Status,
                    HasCompleteQuestLog = raw.IsIdentityComplete, RawQuestStates = states,
                    HasAuthoritativeCompletions = authoritative,
                    PendingCompletionQuestIds = pendingCompletions,
                    CarriedItemCounts = inventory?.IsCurrent() == true ? inventory.ItemCounts : null,
                    InventoryObservationStatus = inventory?.Status ?? "not-required-for-availability",
                    ConfirmedSpellIds = spells?.ConfirmedSpellIds,
                    SpellObservationStatus = spells?.Status ?? "not-required-for-availability",
                    CompletedQuestIds = authoritative ? rewarded.ToArray() : Array.Empty<uint>() };
                return states != null && ReferenceEquals(me, ObjectManager.Me) && ReferenceEquals(memory, ObjectManager.Wow) &&
                    Styx.StyxWoW.IsInWorld && me.IsValid && me.Guid == guid && me.Level == level && log.IsSnapshotCurrent(raw) ? snapshot : null;
            }
            catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException) { return null; }
        }
    }
}
