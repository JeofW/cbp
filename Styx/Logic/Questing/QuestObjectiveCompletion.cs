#nullable enable
using System;
using System.Threading;
using Styx.WoWInternals;

namespace Styx.Logic.Questing
{
    /// <summary>
    /// Positive completion evidence for one of the original client's four normal
    /// NPC/GameObject counter slots. The index is not a displayed Lua quest-log
    /// row or a collected-item index. False includes unknown/unreadable state;
    /// it is not permission to act or proof that a quest is unfinished.
    /// </summary>
    public static class QuestObjectiveCompletion
    {
        /// <summary>
        /// Resolve the unique typed identity before reading a counter. GetObjectives
        /// compresses display indexes; TC335 Player::SetQuestSlotCounter keeps the
        /// four physical slots, including empty slots, at sixteen bits each.
        /// </summary>
        public static bool IsTypedNormalObjectiveComplete(Quest? quest, Quest.QuestObjective objective)
        {
            if (quest == null || objective.ID <= 0 || objective.Count <= 0 ||
                (objective.Type != Quest.QuestObjectiveType.KillMob && objective.Type != Quest.QuestObjectiveType.UseGameObject))
                return false;
            int expected = objective.Type == Quest.QuestObjectiveType.UseGameObject
                ? unchecked((int)0x80000000) | objective.ID : objective.ID;
            int[] ids = quest.NormalObjectiveIDs;
            int[] counts = quest.NormalObjectiveRequiredCounts;
            if (ids == null || counts == null || ids.Length != 4 || counts.Length != 4) return false;
            int slot = -1;
            for (int index = 0; index < 4; index++)
            {
                if (ids[index] != expected) continue;
                if (slot >= 0) return false;
                slot = index;
            }
            return slot >= 0 && counts[slot] == objective.Count && IsNormalObjectiveComplete(quest, slot)
                && quest.NormalObjectiveIDs[slot] == expected
                && quest.NormalObjectiveRequiredCounts[slot] == objective.Count;
        }

        public static bool IsNormalObjectiveComplete(Quest? quest, int rawObjectiveIndex)
        {
            if (quest == null || rawObjectiveIndex < 0 || rawObjectiveIndex >= 4)
                return false;

            try
            {
                var player = ObjectManager.Me;
                var memory = ObjectManager.Wow;
                if (player == null || memory == null || !player.IsValid)
                    return false;

                uint questId = quest.Id;
                uint playerAddress = player.BaseAddress;
                ulong playerGuid = player.Guid;
                int[] ids = quest.NormalObjectiveIDs;
                int[] requirements = quest.NormalObjectiveRequiredCounts;
                if (questId == 0 || playerAddress == 0 || playerGuid == 0 ||
                    ids == null || requirements == null ||
                    rawObjectiveIndex >= ids.Length || rawObjectiveIndex >= requirements.Length)
                    return false;

                int objectiveId = ids[rawObjectiveIndex];
                int required = requirements[rawObjectiveIndex];
                if (objectiveId == 0 || objectiveId == int.MinValue || required <= 0)
                    return false;

                bool SameOwner() => ReferenceEquals(player, ObjectManager.Me)
                    && ReferenceEquals(memory, ObjectManager.Wow)
                    && player.BaseAddress == playerAddress && player.Guid == playerGuid
                    && quest.Id == questId;
                bool Complete(QuestDescriptorData data) => data.Id == questId
                    && (data.Flags & WoWDescriptorQuestFlags.Failed) == 0
                    && data.ObjectivesDone != null && rawObjectiveIndex < data.ObjectivesDone.Length
                    && data.ObjectivesDone[rawObjectiveIndex] >= required;

                // Reuse the existing original-client descriptor reader. Bypass its
                // cache only for these observations and restore the caller's state.
                // Matching samples are not a lock or proof against same-value ABA.
                using (memory.TemporaryCacheState(false))
                {
                    if (!SameOwner() || !quest.GetData(out QuestDescriptorData before) || !Complete(before)
                        || !SameOwner() || !quest.GetData(out QuestDescriptorData after) || !Complete(after))
                        return false;

                    return before.Flags == after.Flags
                        && before.ObjectivesDone[rawObjectiveIndex] == after.ObjectivesDone[rawObjectiveIndex]
                        && quest.NormalObjectiveIDs != null && quest.NormalObjectiveRequiredCounts != null
                        && rawObjectiveIndex < quest.NormalObjectiveIDs.Length
                        && rawObjectiveIndex < quest.NormalObjectiveRequiredCounts.Length
                        && quest.NormalObjectiveIDs[rawObjectiveIndex] == objectiveId
                        && quest.NormalObjectiveRequiredCounts[rawObjectiveIndex] == required
                        && SameOwner();
                }
            }
            catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException)
            {
                return false;
            }
        }
    }
}
