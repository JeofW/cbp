using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public sealed class QuestAvailabilityContract
    {
        public int QuestId { get; init; }
        public string SourceRef { get; init; }
        public IReadOnlyList<QuestAvailabilityReference> ReferencedQuests { get; init; }
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
        public int? RequiredLevel { get; init; }
        public int? ObservedLevel { get; init; }
        public int? LevelComparison { get; init; }
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
                    bool? value = condition.Type switch
                    {
                        8 => rewarded,
                        9 => state.HasValue ? state == 3 : null,
                        14 => state.HasValue ? state == 0 : null,
                        27 => snapshot.PlayerLevel > 0 ? CompareLevel(snapshot.PlayerLevel, condition.Value1, condition.Value2) : null,
                        28 => state.HasValue ? state != 1 ? false : rewarded.HasValue ? !rewarded.Value : null : null,
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
                        Type = condition.Type, ReferencedQuestId = condition.Type == 27 ? 0 : condition.Value1,
                        StateMask = condition.Type == 47 ? condition.Value2 : 0,
                        Negative = condition.Negative, Met = value,
                        RawAcceptedState = condition.Type != 27 && snapshot.RawQuestStates != null && snapshot.RawQuestStates.TryGetValue(id, out int raw) ? raw : null,
                        PermanentlyRewarded = condition.Type == 27 ? null : rewarded,
                        RequiredLevel = condition.Type == 27 ? condition.Value1 : null,
                        ObservedLevel = condition.Type == 27 && snapshot.PlayerLevel > 0 ? snapshot.PlayerLevel : null,
                        LevelComparison = condition.Type == 27 ? condition.Value2 : null, SourceRef = condition.SourceRef });
                }
                groupResults.Add(All(met));
            }
            bool? passed = Any(groupResults);
            return new QuestAvailabilityDecision { Status = passed == true ? "satisfied" : passed == false ? "not-satisfied" : "observation-unknown",
                Rejection = passed == true ? null : passed == false ? "availability-condition-not-satisfied" : "availability-condition-observation-unknown",
                SourceRef = contract.SourceRef, Predicates = results.AsReadOnly() };
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
                Exact(row, "QuestId", "SourceRef", "ReferencedQuests", "Groups");
                int id = Positive(row, "QuestId"); string source = Text(row, "SourceRef");
                if (!subjects.Add(id) || !quests.TryGetValue(id, out QuestEntry quest) || quest.AvailabilityConditions != null)
                    throw new InvalidDataException("Availability conditions require a unique existing quest.");
                var references = new List<QuestAvailabilityReference>(); var ids = new HashSet<int>();
                foreach (JsonElement reference in Rows(row, "ReferencedQuests", 256))
                {
                    Exact(reference, "QuestId", "QuestType", "SpecialFlags", "QuestSortID", "SourceRef");
                    int referencedId = Positive(reference, "QuestId"), method = Integer(reference, "QuestType"),
                        flags = Integer(reference, "SpecialFlags"), sort = Integer(reference, "QuestSortID");
                    if (!ids.Add(referencedId) || method is not (0 or 2) || flags < 0 || (flags & 1) != 0 ||
                        new[] { -22, -284, -366, -369, -370, -374, -376 }.Contains(sort) ||
                        quests.TryGetValue(referencedId, out QuestEntry existing) && (existing.SpecialFlags != flags || existing.QuestSortID != sort))
                        throw new InvalidDataException("Availability reference does not establish permanent quest reward history.");
                    references.Add(new QuestAvailabilityReference { QuestId = referencedId, QuestType = method,
                        SpecialFlags = flags, QuestSortID = sort, SourceRef = Text(reference, "SourceRef") });
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
                        bool referenceValid = type == 27 || ids.Contains(value1) &&
                            (type == 8 || references.Single(reference => reference.QuestId == value1).QuestType == 2);
                        bool valuesInvalid = type == 47 ? value2 <= 0 || (value2 & ~107) != 0
                            : type == 27 ? value2 < 0 || value2 > 4 : value2 != 0;
                        if (type is not (8 or 9 or 14 or 27 or 28 or 47) || !referenceValid || value3 != 0 || valuesInvalid ||
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
                    ReferencedQuests = references.AsReadOnly(), Groups = groups.AsReadOnly() };
            }
        }
    }

    public partial class QuestScheduler
    {
        private static Func<bool> CreateAvailabilityGuard(IReadOnlyList<QuestPlanEntry> plan, LocalPlayer me, QuestLog log)
        {
            QuestPlanEntry[] constrained = plan.Where(entry => entry.Stage == QuestWorkStage.Pickup && entry.Quest.AvailabilityConditions != null).ToArray();
            if (constrained.Length == 0) return null;
            return () => QuestAvailabilityPolicy.RequirementsCurrent(constrained, CaptureAvailabilitySnapshot(me, log));
        }

        private static QuestSchedulerSnapshot CaptureAvailabilitySnapshot(LocalPlayer me, QuestLog log)
        {
            var memory = ObjectManager.Wow;
            try
            {
                if (me == null || log == null || memory == null || !ReferenceEquals(me, ObjectManager.Me) ||
                    !Styx.StyxWoW.IsInWorld || !me.IsValid) return null;
                ulong guid = me.Guid;
                if (guid == 0) return null;
                int level = me.Level;
                QuestLogSnapshot raw = log.CaptureSnapshot();
                var states = QuestAvailabilityPolicy.RawStates(raw);
                bool authoritative = log.TryGetAuthoritativeCompletedQuests(out var rewarded);
                var snapshot = new QuestSchedulerSnapshot { PlayerGuid = guid, PlayerLevel = level, UtcNow = DateTime.UtcNow,
                    HasCompleteQuestLog = raw.IsIdentityComplete, RawQuestStates = states,
                    HasAuthoritativeCompletions = authoritative,
                    CompletedQuestIds = authoritative ? rewarded.ToArray() : Array.Empty<uint>() };
                return states != null && ReferenceEquals(me, ObjectManager.Me) && ReferenceEquals(memory, ObjectManager.Wow) &&
                    Styx.StyxWoW.IsInWorld && me.IsValid && me.Guid == guid && me.Level == level && log.IsSnapshotCurrent(raw) ? snapshot : null;
            }
            catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException) { return null; }
        }
    }
}
