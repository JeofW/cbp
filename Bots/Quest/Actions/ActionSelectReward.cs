// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Actions.ActionSelectReward
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// Based on HB 4.3.4 ActionSelectReward

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Styx;
using Styx.Helpers;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

#nullable disable
namespace Bots.Quest.Actions;

/// <summary>
/// Selects a quest reward only from a complete live original-client choice observation.
/// Cache metadata may be stale or incomplete, so an unknown live choice identity defers
/// instead of clicking an arbitrary index.
/// </summary>
public class ActionSelectReward : Action
{
    private const int MaximumRewardChoices = 64;
    private readonly WeightSetEx _weightSet = WeightSetEx.CurrentWeightSet;

    internal sealed class LiveRewardChoice
    {
        public int Index { get; set; }
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public string ItemLink { get; set; }
    }

    internal static bool TryObserveLiveChoices(
        Func<int> observeCount,
        Func<int, string> observeLink,
        Func<int, int?> observeStackCount,
        out List<LiveRewardChoice> choices)
    {
        if (observeCount == null) throw new ArgumentNullException(nameof(observeCount));
        if (observeLink == null) throw new ArgumentNullException(nameof(observeLink));
        if (observeStackCount == null) throw new ArgumentNullException(nameof(observeStackCount));

        choices = new List<LiveRewardChoice>();
        int count = observeCount();
        if (count < 0 || count > MaximumRewardChoices)
            return false;

        for (int index = 0; index < count; index++)
        {
            string itemLink = observeLink(index);
            uint itemId = ConsumableVendorPolicy.ParseItemId(itemLink);
            int? stackCount = observeStackCount(index);
            if (itemId == 0 || string.IsNullOrEmpty(itemLink) ||
                !stackCount.HasValue || stackCount.Value <= 0)
            {
                choices.Clear();
                return false;
            }

            choices.Add(new LiveRewardChoice
            {
                Index = index,
                ItemId = itemId,
                Count = stackCount.Value,
                ItemLink = itemLink
            });
        }

        return true;
    }

    protected override RunStatus Run(object context)
    {
        var player = ObjectManager.Me;
        var frame = QuestFrame.Instance;
        if (!StyxWoW.IsInGame || player == null || !player.IsValid || !player.IsAlive ||
            player.Guid == 0 || frame == null || !frame.IsVisible)
            return RunStatus.Failure;

        ulong playerGuid = player.Guid;
        uint questId = frame.CurrentShownQuestId;
        bool OwnsContext() => questId != 0 && StyxWoW.IsInGame &&
            ReferenceEquals(ObjectManager.Me, player) && player.IsValid && player.IsAlive &&
            player.Guid == playerGuid && ReferenceEquals(QuestFrame.Instance, frame) &&
            frame.IsVisible && frame.CurrentShownQuestId == questId;
        if (!OwnsContext())
            return RunStatus.Failure;

        if (!TryReadChoices(out List<LiveRewardChoice> choices) || !OwnsContext())
        {
            Logging.Write("Quest reward choices could not be observed completely; deferring reward selection.");
            return RunStatus.Failure;
        }

        if (choices.Count == 0)
        {
            Logging.Write("No live quest reward choices are currently available; deferring reward selection.");
            return RunStatus.Failure;
        }

        float bestScore = float.MinValue;
        int bestIndex = -1;
        string bestName = "";

        // First pass: stat-weight evaluation on the exact live choice identity.
        foreach (LiveRewardChoice choice in choices)
        {
            ItemInfo itemInfo = ItemInfo.FromId(choice.ItemId);
            if (itemInfo == null || !player.CanEquipItem(itemInfo))
                continue;

            ItemStats itemStats = new ItemStats(choice.ItemLink);
            float score = _weightSet.EvaluateItem(itemInfo, itemStats);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = choice.Index;
                bestName = itemInfo.Name;
            }
        }

        // Second pass: vendor sell-price fallback, still using only the live choice set.
        if (bestIndex == -1)
        {
            float bestValue = float.MinValue;
            foreach (LiveRewardChoice choice in choices)
            {
                ItemInfo itemInfo = ItemInfo.FromId(choice.ItemId);
                if (itemInfo == null)
                    continue;

                float sellValue = (float)(itemInfo.SellPrice * choice.Count);
                Logging.Write("{0}{1} sells for {2}",
                    itemInfo.Name,
                    choice.Count > 1 ? ("x" + choice.Count) : "",
                    sellValue);

                if (sellValue > bestValue)
                {
                    bestName = itemInfo.Name;
                    bestValue = sellValue;
                    bestIndex = choice.Index;
                }
            }
        }

        if (bestIndex == -1)
        {
            Logging.Write("Live quest reward choices have no usable item identity; deferring reward selection.");
            return RunStatus.Failure;
        }

        // Scoring, metadata and diagnostic callbacks can change the actor or UI.
        // Reobserve after them; an index is meaningful only in the captured set.
        Logging.Write("Choosing {0}", bestName);
        if (!OwnsContext() || !TryReadChoices(out List<LiveRewardChoice> current) ||
            !OwnsContext() || !SameChoices(choices, current))
            return RunStatus.Failure;

        string script = BuildSelectionLua(choices, bestIndex);
        if (!OwnsContext())
            return RunStatus.Failure;
        bool selected = Lua.GetReturnVal<bool>(script, 0U);
        return selected && OwnsContext() ? RunStatus.Success : RunStatus.Failure;
    }

    private static bool TryReadChoices(out List<LiveRewardChoice> choices) =>
        TryObserveLiveChoices(
            () => Lua.GetReturnVal<int>("return GetNumQuestChoices()", 0U),
            index => Lua.GetReturnVal<string>(
                string.Format(CultureInfo.InvariantCulture,
                    "return GetQuestItemLink('choice', {0})", index + 1), 0U),
            index => Lua.GetReturnVal<int>(
                string.Format(CultureInfo.InvariantCulture,
                    "return select(3, GetQuestItemInfo('choice', {0}))", index + 1), 0U),
            out choices);

    private static bool SameChoices(List<LiveRewardChoice> expected, List<LiveRewardChoice> current)
    {
        if (expected.Count != current.Count)
            return false;
        for (int i = 0; i < expected.Count; i++)
            if (expected[i].Index != current[i].Index || expected[i].ItemId != current[i].ItemId ||
                expected[i].Count != current[i].Count ||
                !string.Equals(expected[i].ItemLink, current[i].ItemLink, StringComparison.Ordinal))
                return false;
        return true;
    }

    private static string BuildSelectionLua(List<LiveRewardChoice> choices, int selectedIndex)
    {
        if (choices == null || choices.Count == 0 || choices.Count > MaximumRewardChoices ||
            selectedIndex < 0 || selectedIndex >= choices.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        // Original 3.3.5 QuestInfo/QuestFrame contract. No quest completion or
        // server acknowledgement is inferred from selecting this local button.
        var script = new StringBuilder(
            "if not (QuestFrame and QuestFrame:IsShown() and QuestFrameRewardPanel and " +
            "QuestFrameRewardPanel:IsShown() and QuestInfoFrame and QuestInfoFrame.chooseItems " +
            "and not QuestInfoFrame.questLog) then return false end; ");
        script.AppendFormat(CultureInfo.InvariantCulture,
            "if GetNumQuestChoices()~={0} then return false end; ", choices.Count);
        foreach (LiveRewardChoice choice in choices)
        {
            script.AppendFormat(CultureInfo.InvariantCulture,
                "if GetQuestItemLink('choice', {0})~={1} or " +
                "tonumber((select(3, GetQuestItemInfo('choice', {0}))))~={2} then return false end; ",
                choice.Index + 1, QuoteLua(choice.ItemLink), choice.Count);
        }
        script.AppendFormat(CultureInfo.InvariantCulture,
            "local b=_G['QuestInfoItem{0}']; " +
            "if not b or not b:IsShown() or b.type~='choice' or b:GetID()~={0} then return false end; " +
            "b:Click(); return (QuestInfoFrame.itemChoice=={0}) and 1 or 0", selectedIndex + 1);
        return script.ToString();
    }

    private static string QuoteLua(string value)
    {
        var quoted = new StringBuilder("\"");
        foreach (char c in value)
        {
            if (c == '\\' || c == '"')
                quoted.Append('\\').Append(c);
            else if (c < ' ' || c == '\u007f')
                quoted.Append('\\').Append(((int)c).ToString("D3", CultureInfo.InvariantCulture));
            else
                quoted.Append(c);
        }
        return quoted.Append('"').ToString();
    }
}
