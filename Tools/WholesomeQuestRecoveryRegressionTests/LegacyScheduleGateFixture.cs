using System;
using System.Reflection;
using TreeSharp;
using WholesomeAQ;

// These historical fixtures substitute the complete root child with a counted
// leaf and seed schedules without a real host publication. They exercise the
// production schedule policy and continuing execution gate, not root topology.
// W42 normalization explicitly records the seven factory migrations. No oracle
// is removed. QuestActionFreshness/QuestRootPreemption keep the actual Bot.Root.
internal static class LegacyScheduleGateFixture
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static GroupComposite Create(WholesomeAutoQuest bot)
    {
        if (bot == null) throw new ArgumentNullException(nameof(bot));
        return new WholesomeExecutionGate(
            _ => WholesomeRestPolicy.ShouldRunQuestRoot(Read<bool>(bot, "_restingPaused"))
                && WholesomeAutoQuest.ShouldExecuteQuestRoot(
                    Read<bool>(bot, "_stopped"), Read<QuestScheduler>(bot, "_scheduler")?.LastSchedule),
            new TreeSharp.Action(_ => RunStatus.Failure));
    }

    private static T Read<T>(WholesomeAutoQuest bot, string name) =>
        (T)(typeof(WholesomeAutoQuest).GetField(name, Fields)
            ?? throw new InvalidOperationException("Legacy gate fixture field changed: " + name)).GetValue(bot)!;
}
