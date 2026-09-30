using System;
using System.Collections.Generic;
using System.Linq;

#nullable disable
namespace WholesomeAQ
{
    public static class QuestDeliveryPolicy
    {
        public static bool HasContract(QuestEntry quest) => quest != null &&
            (quest.DeliveryItems != null || quest.AcceptanceSupplies != null);

        private static string Invalid(QuestEntry quest)
        {
            if (!HasContract(quest)) return null;
            var required = quest.DeliveryItems; var supplied = quest.AcceptanceSupplies;
            if (required == null || supplied == null || required.Count == 0 || required.Count > 6 || supplied.Count > 1 ||
                required.Any(r => r == null || r.ItemId <= 0 || r.Count <= 0) ||
                supplied.Any(r => r == null || r.ItemId <= 0 || r.Count <= 0 || r.ItemId != quest.StartItem || !required.Any(d => d.ItemId == r.ItemId)) ||
                required.Select(r => r.ItemId).Distinct().Count() != required.Count ||
                quest.Objectives == null || quest.Objectives.Count == 0 || quest.Objectives.Any(o => o == null || o.Type != ObjectiveType.TurnInOnly) ||
                (quest.SpecialFlags & 0x22) != 0)
                return "invalid-delivery-contract";
            return null;
        }

        public static string PickupRejection(QuestEntry quest, IReadOnlyDictionary<int, long> carried) =>
            StockRejection(quest, carried, beforeAcceptance: true);

        public static string TurnInRejection(QuestEntry quest, IReadOnlyDictionary<int, long> carried) =>
            StockRejection(quest, carried, beforeAcceptance: false);

        private static string StockRejection(QuestEntry quest, IReadOnlyDictionary<int, long> carried, bool beforeAcceptance)
        {
            string requiredStock = QuestRequiredStockPolicy.Rejection(quest, carried);
            if (requiredStock != null) return requiredStock;
            var supplemental = quest?.SupplementalSupply;
            if (supplemental != null)
            {
                if (supplemental.ItemId <= 0 || supplemental.ItemId != quest.StartItem || supplemental.RequiredCount <= 0 ||
                    supplemental.ProvidedCount < supplemental.RequiredCount || HasContract(quest) || quest.Objectives == null ||
                    !quest.Objectives.Any(o => o != null && o.Type != ObjectiveType.TurnInOnly) ||
                    quest.Objectives.Any(o => o != null && o.ItemId == supplemental.ItemId))
                    return "invalid-supplemental-supply-contract";
                if (!beforeAcceptance)
                {
                    if (carried == null) return "supplied-required-item-stock-unknown";
                    carried.TryGetValue(supplemental.ItemId, out long held);
                    if (held < 0) return "supplied-required-item-stock-unknown";
                    if (held < supplemental.RequiredCount) return "supplied-required-item-receipt-missing";
                }
            }
            string invalid = Invalid(quest);
            if (invalid != null || !HasContract(quest)) return invalid;
            foreach (QuestItemRequirement item in quest.DeliveryItems)
            {
                // TC335 GiveQuestSourceItem supplies the declared item only on
                // acceptance. Its source promise is never an inventory receipt.
                // Subtract from the small requirement, not add to a long stock
                // count, so an oversized observation cannot overflow admission.
                int supplied = beforeAcceptance ? quest.AcceptanceSupplies.Where(r => r.ItemId == item.ItemId).Select(r => r.Count).FirstOrDefault() : 0;
                int needed = Math.Max(0, item.Count - supplied);
                if (needed == 0) continue;
                if (carried == null) return "delivery-stock-unknown";
                carried.TryGetValue(item.ItemId, out long count);
                if (count < 0) return "delivery-stock-unknown";
                if (count < needed) return "delivery-stock-missing";
            }
            return null;
        }
    }
}
