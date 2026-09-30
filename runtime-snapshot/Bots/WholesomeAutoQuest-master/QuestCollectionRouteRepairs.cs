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
        private static void ApplyCollectionRouteRepairs(JsonElement root, QuestDatabase database,
            HashSet<(int, int)> countOwners, HashSet<(int, int)> objectOwners)
        {
            // The caller owns an unpublished clone. A source repair changes a
            // modeled target or adds an alternative, never inventory or progress.
            var quests = database.Quests.ToDictionary(quest => quest.Id);
            var owners = new HashSet<(int, int)>();
            var originalLengths = database.Quests.ToDictionary(quest => quest.Id, quest => quest.Objectives?.Count ?? 0);
            foreach (JsonElement row in Rows(root, "CollectionRouteRepairs", 30000))
            {
                Exact(row, "Operation", "QuestId", "RowIndex", "ObjectiveIndex", "ExpectedObjectiveType",
                    "ExpectedTargetId", "ItemId", "RequiredCount", "CreatureId", "NewObjectiveIndex", "SourceRef");
                string operation = Text(row, "Operation"), kind = Text(row, "ExpectedObjectiveType");
                int id = Positive(row, "QuestId"), rowIndex = Integer(row, "RowIndex"), index = Integer(row, "ObjectiveIndex"),
                    before = Positive(row, "ExpectedTargetId"), item = Positive(row, "ItemId"), required = Positive(row, "RequiredCount"),
                    creature = Positive(row, "CreatureId"), newIndex = Integer(row, "NewObjectiveIndex");
                _ = Text(row, "SourceRef");
                if (!quests.TryGetValue(id, out QuestEntry quest) || !owners.Add((id, rowIndex)) || countOwners.Contains((id, rowIndex)) ||
                    objectOwners.Contains((id, rowIndex)) || operation is not ("Replace" or "Append") || kind is not ("CollectItem" or "CollectFromGameObject") ||
                    quest.Objectives == null || quest.Objectives.Count >= 10000 || rowIndex < 0 || rowIndex >= originalLengths[id] ||
                    index < 0 || index >= 10000 || newIndex < 0 || newIndex >= 10000 || (quest.SpecialFlags & 0x22) != 0 ||
                    quest.DeliveryItems != null || quest.Objectives.Any(value => value == null || value.Index < 0 || value.Index >= 10000) ||
                    quest.Objectives.Select(value => value.Index).Distinct().Count() != quest.Objectives.Count)
                    throw new InvalidDataException("A collection route requires a unique ordinary source row and an unambiguous operation.");
                QuestObjective original = quest.Objectives[rowIndex];
                bool creatureSource = kind == "CollectItem";
                if (original.Index != index || original.ItemId != item || original.CollectCount != required || original.KillCount != 0 ||
                    !(creatureSource && original.Type == ObjectiveType.CollectItem && original.MobId == before && original.GameObjectId == 0 ||
                      !creatureSource && original.Type == ObjectiveType.CollectFromGameObject && original.GameObjectId == before && original.MobId == 0) ||
                    creatureSource && creature == before ||
                    quest.Objectives.Any(value => value != original && value.Type == ObjectiveType.CollectItem && value.MobId == creature && value.ItemId == item) ||
                    operation == "Replace" && newIndex != index || operation == "Append" && quest.Objectives.Any(value => value.Index == newIndex))
                    throw new InvalidDataException("The expected collection owner changed, or the repair would collide with another objective.");
                var oldLocations = creatureSource ? database.CreatureSpawns : database.GameObjectSpawns;
                if (oldLocations.TryGetValue(before.ToString(System.Globalization.CultureInfo.InvariantCulture), out var oldPoints) &&
                    (oldPoints == null || oldPoints.Any(point => point == null || point.IsKnownSafe == false || point.IsKnownReachable == false)))
                    throw new InvalidDataException("A collection repair cannot route around a recorded original-source veto.");
                if (!database.CreatureSpawns.TryGetValue(creature.ToString(System.Globalization.CultureInfo.InvariantCulture), out var points) ||
                    points == null || points.Count == 0 || points.Any(point => point == null || point.Map < 0 ||
                        double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.Y) ||
                        double.IsNaN(point.Z) || double.IsInfinity(point.Z) || point.IsKnownSafe == false || point.IsKnownReachable == false))
                    throw new InvalidDataException("A new ordinary collector requires existing non-vetoed reference geometry.");
                var replacement = new QuestObjective { Type = ObjectiveType.CollectItem, MobId = creature, ItemId = item,
                    CollectCount = required, Index = newIndex, KillCount = 0, GameObjectId = 0, GameObjectName = "" };
                if (operation == "Replace") quest.Objectives[rowIndex] = replacement;
                else quest.Objectives.Add(replacement);
            }
        }
    }
}
