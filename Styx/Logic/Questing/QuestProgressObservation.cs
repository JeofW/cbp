#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Styx.WoWInternals;

namespace Styx.Logic.Questing;

/// <summary>
/// One current quest's normal counters plus required carried-item quantities.
/// An empty result means unknown, not zero. The supplied inventory must be the
/// existing complete observer's still-current receipt for the same player.
/// This observes progress; it does not attribute a gain to a particular action.
/// </summary>
public static class QuestProgressObservation
{
    public static IReadOnlyList<int> Capture(Quest? quest)
    {
        if (quest == null) return Array.Empty<int>();
        try
        {
            bool needsItems = quest.CollectItemIds.Any(id => id > 0)
                || quest.CollectIntermediateItemIds.Any(id => id > 0);
            return Read(quest, needsItems ? QuestInventorySnapshot.Capture(ObjectManager.Me) : null);
        }
        catch (Exception error) when (Ordinary(error)) { return Array.Empty<int>(); }
    }

    public static IReadOnlyList<int> Read(Quest? quest, QuestInventorySnapshot? inventory)
    {
        try
        {
            var actor = ObjectManager.Me;
            var memory = ObjectManager.Wow;
            if (quest == null || actor == null || memory == null || !actor.IsValid || actor.Guid == 0 || quest.Id == 0)
                return Array.Empty<int>();
            ulong guid = actor.Guid;
            uint questId = quest.Id;
            int[] normalIds = quest.NormalObjectiveIDs.ToArray(), normalRequired = quest.NormalObjectiveRequiredCounts.ToArray();
            int[] items = quest.CollectItemIds.ToArray(), required = quest.CollectItemCounts.ToArray();
            int[] intermediate = quest.CollectIntermediateItemIds.ToArray(), intermediateRequired = quest.CollectIntermediateItemCounts.ToArray();
            bool Pair(int[] ids, int[] amounts) => ids.Length == amounts.Length && ids.Length <= 16
                && ids.All(id => id >= 0) && amounts.All(count => count >= 0);
            if (normalIds.Length != 4 || normalRequired.Length != 4 || normalRequired.Any(value => value < 0)
                || !Pair(items, required) || !Pair(intermediate, intermediateRequired)) return Array.Empty<int>();
            bool itemDependent = items.Any(id => id > 0) || intermediate.Any(id => id > 0);
            bool SameOwner() => ReferenceEquals(ObjectManager.Me, actor) && ReferenceEquals(ObjectManager.Wow, memory)
                && actor.IsValid && actor.Guid == guid && quest.Id == questId;
            bool InventoryCurrent() => !itemDependent || inventory != null && inventory.IsComplete
                && inventory.PlayerGuid == guid && inventory.IsCurrent();
            bool Valid(QuestDescriptorData row) => row.Id == questId && !row.IsFailed
                && row.ObjectivesDone != null && row.ObjectivesDone.Length == 4;
            if (!SameOwner() || !InventoryCurrent()) return Array.Empty<int>();
            var result = new List<int>(4 + items.Length + intermediate.Length);
            using (memory.TemporaryCacheState(false))
            {
                if (!SameOwner() || !quest.GetData(out var before) || !Valid(before))
                    return Array.Empty<int>();
                for (int index = 0; index < 4; index++)
                    result.Add(normalIds[index] == 0 ? 0 : Math.Min(before.ObjectivesDone[index], normalRequired[index]));
                void Append(int[] ids, int[] amounts)
                {
                    for (int index = 0; index < ids.Length; index++)
                    {
                        long count = 0;
                        if (ids[index] > 0 && amounts[index] > 0)
                            inventory!.ItemCounts!.TryGetValue(ids[index], out count);
                        if (count < 0) throw new InvalidOperationException("Negative observed item quantity");
                        result.Add((int)Math.Min(count, amounts[index]));
                    }
                }
                Append(items, required); Append(intermediate, intermediateRequired);
                if (!SameOwner() || !quest.GetData(out var after) || !Valid(after)
                    || before.Flags != after.Flags || !before.ObjectivesDone.SequenceEqual(after.ObjectivesDone)
                    || !normalIds.SequenceEqual(quest.NormalObjectiveIDs) || !normalRequired.SequenceEqual(quest.NormalObjectiveRequiredCounts)
                    || !items.SequenceEqual(quest.CollectItemIds) || !required.SequenceEqual(quest.CollectItemCounts)
                    || !intermediate.SequenceEqual(quest.CollectIntermediateItemIds)
                    || !intermediateRequired.SequenceEqual(quest.CollectIntermediateItemCounts) || !SameOwner())
                    return Array.Empty<int>();
            }
            // Each observer owns its cache scope. Do not make the inventory
            // observer's world-state check inherit another reader's cache mode.
            return InventoryCurrent() && SameOwner() ? result.AsReadOnly() : Array.Empty<int>();
        }
        catch (Exception error) when (Ordinary(error)) { return Array.Empty<int>(); }
    }

    private static bool Ordinary(Exception error) => error is not OperationCanceledException && error is not ThreadInterruptedException;
}
