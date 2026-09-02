// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestManager
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Quest.Objectives;
using Styx;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Questing;
using System;
using System.Collections.Generic;
using System.Text;

#nullable disable
namespace Bots.Quest;

public static class QuestManager
{
    public static readonly GossipFrame GossipFrame = new GossipFrame();
    public static readonly QuestFrame QuestFrame = new QuestFrame();
    public static readonly List<QuestObjective> Objectives = new List<QuestObjective>();
    public static readonly List<PlayerQuest> Quests = new List<PlayerQuest>();
    private static readonly Random randomGenerator = new Random();
    private static readonly char[] alphaChars = "ABCDEFGHIJKLMNOPQRSTUVXYZabcdefghijklmnopqrstuvxyz".ToCharArray();

    [Obsolete("Use ObjectManager.Me.QuestLog.TryGetAuthoritativeCompletedQuests() instead.")]
    public static List<uint> GetCompletedQuests()
    {
        return StyxWoW.Me.QuestLog.TryGetAuthoritativeCompletedQuests(out var completedQuestIds)
            ? new List<uint>((IEnumerable<uint>)completedQuestIds)
            : new List<uint>();
    }

    private static string GenerateRandomString(int minLength, int maxLength)
    {
        int capacity = randomGenerator.Next(minLength, maxLength + 1);
        StringBuilder stringBuilder = new StringBuilder(capacity);
        for (int index = 0; index < capacity; ++index)
            stringBuilder.Append(alphaChars[randomGenerator.Next(0, alphaChars.Length)]);
        return stringBuilder.ToString();
    }

    internal static QuestObjective CreateObjectiveFromCache(
        Styx.Logic.Questing.Quest.QuestObjective cacheObjective,
        PlayerQuest quest,
        List<WoWQuestStep> questSteps,
        List<QuestObjective> objectivePool)
    {
        switch (cacheObjective.Type)
        {
            case Styx.Logic.Questing.Quest.QuestObjectiveType.CollectIntermediateItem:
            case Styx.Logic.Questing.Quest.QuestObjectiveType.CollectItem:
                return new CollectItemObjective(quest, questSteps, cacheObjective, objectivePool);
            case Styx.Logic.Questing.Quest.QuestObjectiveType.KillMob:
                return new GrindObjective(quest, questSteps, cacheObjective, objectivePool);
            case Styx.Logic.Questing.Quest.QuestObjectiveType.UseGameObject:
                return new UseGameObjectObjective(quest, questSteps, cacheObjective, objectivePool);
            default:
                return null;
        }
    }

    public static QuestObjective CreateQuestObjective(
        Styx.Logic.Questing.Quest.QuestObjective questObjective,
        PlayerQuest quest,
        List<WoWQuestStep> poiSteps,
        List<QuestObjective> objectivePool)
    {
        return CreateObjectiveFromCache(questObjective, quest, poiSteps, objectivePool);
    }

    internal delegate void Delegate9(QuestObjective objective);

    internal delegate void Delegate10(List<PlayerQuest> quests);
}
