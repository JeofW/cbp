using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Styx.Logic.Questing;

#nullable disable
namespace WholesomeAQ
{
    public partial class QuestScheduler
    {
        private static Func<bool> CreateInventoryRequirementGuard(
            IReadOnlyList<QuestPlanEntry> plan, Func<QuestInventorySnapshot> capture)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            // Source-supplied pickup can require no already-carried stock. Its
            // promise still never supplies the later turn-in inventory receipt.
            var constrained = plan.Where(entry => entry != null && entry.Quest != null &&
                (entry.Stage == QuestWorkStage.Pickup && (QuestDeliveryPolicy.HasContract(entry.Quest) || QuestRequiredStockPolicy.HasContract(entry.Quest)) &&
                    QuestDeliveryPolicy.PickupRejection(entry.Quest, null) != null ||
                 entry.Stage == QuestWorkStage.TurnIn && (QuestDeliveryPolicy.HasContract(entry.Quest) || entry.Quest.SupplementalSupply != null || QuestRequiredStockPolicy.HasContract(entry.Quest))))
                .ToArray();
            if (constrained.Length == 0) return null;
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            return () =>
            {
                try
                {
                    QuestInventorySnapshot inventory = capture();
                    return inventory != null && inventory.IsComplete && constrained.All(entry =>
                        (entry.Stage == QuestWorkStage.Pickup
                            ? QuestDeliveryPolicy.PickupRejection(entry.Quest, inventory.ItemCounts)
                            : QuestDeliveryPolicy.TurnInRejection(entry.Quest, inventory.ItemCounts)) == null)
                        && inventory.IsCurrent();
                }
                catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException)
                {
                    return false;
                }
            };
        }
    }
}
