using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

#nullable disable
namespace WholesomeAQ
{
    public static partial class QuestDataRepairPackLoader
    {
        private static void ApplyDependentPreviousQuestRepairs(JsonElement root, QuestDatabase database)
        {
            // The caller is applying repairs to an unpublished clone. Only an
            // empty list may be filled, and all alternatives must have known
            // nonnegative groups. Their OR semantics do not depend on ordering.
            var lookup = QuestDependencyCatalog.CreateLookup(database);
            var subjects = new HashSet<int>();
            foreach (JsonElement row in Rows(root, "DependentPreviousQuestRepairs", 10000))
            {
                Exact(row, "QuestId", "ExpectedPrevQuestId", "ExpectedPreviousQuestIds",
                    "PreviousQuestIds", "ReferencedQuestGroups", "SourceRef");
                int id = Positive(row, "QuestId"), direct = Integer(row, "ExpectedPrevQuestId");
                _ = Text(row, "SourceRef");
                QuestEntry quest = database.Quests.SingleOrDefault(value => value.Id == id);
                if (!subjects.Add(id) || quest == null || direct == int.MinValue || quest.PrevQuestID != direct ||
                    quest.PreviousQuestsIds == null || quest.PreviousQuestsIds.Count != 0 ||
                    Rows(row, "ExpectedPreviousQuestIds", 256).Any())
                    throw new InvalidDataException("A predecessor repair requires one existing empty source list and its unchanged direct requirement.");
                int[] parents = Rows(row, "PreviousQuestIds", 256).Select(value =>
                    value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parent) && parent > 0
                        ? parent : throw new InvalidDataException("Invalid predecessor identity.")).ToArray();
                if (parents.Length == 0 || parents.Contains(id) || parents.Distinct().Count() != parents.Length)
                    throw new InvalidDataException("Predecessor alternatives must be complete, positive and distinct.");
                var references = new HashSet<int>();
                foreach (JsonElement reference in Rows(row, "ReferencedQuestGroups", 256))
                {
                    Exact(reference, "QuestId", "ExclusiveGroup", "SourceRef");
                    int parent = Positive(reference, "QuestId"), group = Integer(reference, "ExclusiveGroup");
                    _ = Text(reference, "SourceRef");
                    if (!references.Add(parent) || group < 0 || !parents.Contains(parent) ||
                        !lookup.TryGetValue((uint)parent, out QuestEntry known) || known.ExclusiveGroup != group)
                        throw new InvalidDataException("Every predecessor must have matching known nonnegative group metadata.");
                }
                if (!references.SetEquals(parents))
                    throw new InvalidDataException("Predecessor source references are incomplete.");
                quest.PreviousQuestsIds = parents.ToList();
            }
            foreach (int id in subjects)
            {
                var pending = new Queue<int>(lookup[(uint)id].PreviousQuestsIds);
                var seen = new HashSet<int>();
                while (pending.Count > 0)
                {
                    int parent = pending.Dequeue();
                    if (parent == id || seen.Count > 4096)
                        throw new InvalidDataException("A predecessor repair creates a cyclic or unbounded dependency.");
                    if (!seen.Add(parent) || !lookup.TryGetValue((uint)parent, out QuestEntry prior)) continue;
                    foreach (int earlier in prior.PreviousQuestsIds.Where(value => value > 0)) pending.Enqueue(earlier);
                    if (prior.PrevQuestID == int.MinValue) throw new InvalidDataException("Invalid signed predecessor identity.");
                    if (prior.PrevQuestID != 0) pending.Enqueue(Math.Abs(prior.PrevQuestID));
                }
            }
        }
    }
}
