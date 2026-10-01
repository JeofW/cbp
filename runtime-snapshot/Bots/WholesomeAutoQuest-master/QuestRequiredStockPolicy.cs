using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

#nullable disable
namespace WholesomeAQ
{
    /// <summary>
    /// Additional source-declared return materials whose acquisition is not
    /// modeled. They must already be carried; this is never an item grant.
    /// Ordinary objectives and acceptance-supplied items retain their owners.
    /// </summary>
    public static class QuestRequiredStockPolicy
    {
        public static bool HasContract(QuestEntry quest) => quest?.RequiredStockItems != null;

        internal static string Invalid(QuestEntry quest)
        {
            if (!HasContract(quest)) return null;
            var items = quest.RequiredStockItems;
            if (items.Count == 0 || items.Count > 6 || items.Any(item => item == null || item.ItemId <= 0 || item.Count <= 0)
                || items.Select(item => item.ItemId).Distinct().Count() != items.Count
                || quest.Objectives == null || quest.Objectives.Count == 0 || quest.Objectives.Any(item => item == null)
                || quest.Objectives.All(item => item.Type == ObjectiveType.TurnInOnly)
                || quest.DeliveryItems != null || quest.AcceptanceSupplies != null || (quest.SpecialFlags & 0x22) != 0
                || items.Any(item => item.ItemId == quest.StartItem || item.ItemId == quest.SupplementalSupply?.ItemId
                    || quest.Objectives.Any(objective => objective.ItemId == item.ItemId)))
                return "invalid-required-stock-contract";
            return null;
        }

        public static string Rejection(QuestEntry quest, IReadOnlyDictionary<int, long> carried)
        {
            string invalid = Invalid(quest);
            if (invalid != null || !HasContract(quest)) return invalid;
            foreach (var item in quest.RequiredStockItems.OrderBy(item => item.ItemId))
            {
                if (carried == null) return "required-stock-item-unknown:" + item.ItemId;
                carried.TryGetValue(item.ItemId, out long count);
                if (count < 0) return "required-stock-item-unknown:" + item.ItemId;
                if (count < item.Count) return "required-stock-item-missing:" + item.ItemId;
            }
            return null;
        }
    }

    public static partial class QuestDataRepairPackLoader
    {
        private static void ApplyRequiredStock(JsonElement root, Dictionary<int, QuestEntry> quests)
        {
            var seen = new HashSet<int>();
            foreach (JsonElement row in Rows(root, "QuestRequiredStock", 10000))
            {
                Exact(row, "QuestId", "SourceRef", "Items");
                int id = Positive(row, "QuestId"); _ = Text(row, "SourceRef");
                if (!seen.Add(id) || !quests.TryGetValue(id, out QuestEntry quest) || quest.RequiredStockItems != null)
                    throw new InvalidDataException("Required stock needs a unique existing quest without an earlier stock contract.");
                QuestItemRequirement[] items = Requirements(row.GetProperty("Items"));
                if (items == null || items.Length == 0)
                    throw new InvalidDataException("Required stock cannot be an empty or unknown item list.");
                quest.RequiredStockItems = Array.AsReadOnly(items);
                if (QuestRequiredStockPolicy.Invalid(quest) != null)
                    throw new InvalidDataException("Required stock duplicates or replaces an existing objective, delivery or supplied-item owner.");
            }
        }
    }
}
