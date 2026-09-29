using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Bots.Quest.Actions;
using Bots.Quest.Objectives;
using Styx;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using HostObjective = Styx.Logic.Questing.Quest.QuestObjective;
using HostKind = Styx.Logic.Questing.Quest.QuestObjectiveType;

internal static class QuestObjectiveIdentityRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        foreach (bool gameObject in new[] { false, true })
        {
            bool go = gameObject;
            string kind = go ? "gameobject" : "creature";
            cases.Add(($"{kind} counter after a sparse raw slot acknowledges its own progress", () =>
                Observe(go, new[] { 0, 0, Encoded(go, 701), 0 }, new[] { 0, 0, 3, 0 }, new ushort[] { 0, 0, 3, 0 }, false, 0, true)));
            cases.Add(($"{kind} counter cannot borrow completed progress from another slot", () =>
                Observe(go, new[] { 0, 0, Encoded(go, 701), 0 }, new[] { 0, 0, 3, 0 }, new ushort[] { 3, 0, 0, 0 }, false, 0, false)));
            cases.Add(($"{kind} failed quest cannot acknowledge an objective", () =>
                Observe(go, new[] { Encoded(go, 701), 0, 0, 0 }, new[] { 3, 0, 0, 0 }, new ushort[] { 3, 0, 0, 0 }, true, 0, false)));
            cases.Add(($"{kind} changed required count invalidates old behavior metadata", () =>
                Observe(go, new[] { Encoded(go, 701), 0, 0, 0 }, new[] { 4, 0, 0, 0 }, new ushort[] { 3, 0, 0, 0 }, false, 0, false)));
            cases.Add(($"{kind} matching numeric ID in the other namespace is not completion", () =>
                Observe(go, new[] { Encoded(!go, 701), 0, 0, 0 }, new[] { 3, 0, 0, 0 }, new ushort[] { 3, 0, 0, 0 }, false, 0, false)));
            cases.Add(($"{kind} duplicate raw identity remains ambiguous", () =>
                Observe(go, new[] { Encoded(go, 701), Encoded(go, 701), 0, 0 }, new[] { 3, 3, 0, 0 }, new ushort[] { 3, 3, 0, 0 }, false, 0, false)));
            cases.Add(($"{kind} differing counts cannot disambiguate duplicate raw identities", () =>
                Observe(go, new[] { Encoded(go, 701), Encoded(go, 701), 0, 0 }, new[] { 3, 4, 0, 0 }, new ushort[] { 3, 0, 0, 0 }, false, 0, false)));
            cases.Add(($"{kind} displayed index outside four raw slots cannot cause an exception", () =>
                Observe(go, new[] { 0, 0, Encoded(go, 701), 0 }, new[] { 0, 0, 3, 0 }, new ushort[] { 0, 0, 3, 0 }, false, 7, true)));
        }
        foreach (var requested in new[] { ObjectiveType.KillMob, ObjectiveType.CollectItem, ObjectiveType.UseObject })
        {
            var requestedKind = requested;
            int expected = requested == ObjectiveType.KillMob ? 0 : requested == ObjectiveType.CollectItem ? 1 : 2;
            cases.Add(($"profile {requested} resolves type as well as the numeric identity", () =>
            {
                var values = new[] { new HostObjective(0, 701, 3, null, HostKind.KillMob),
                    new HostObjective(1, 701, 3, null, HostKind.CollectItem), new HostObjective(2, 701, 3, null, HostKind.UseGameObject) };
                var node = new ObjectiveNode(867, requestedKind, 701, "", 3, (expected + 1) % 3);
                Check(Resolve(node, values) == expected, "profile resolved the wrong objective namespace");
            }));
        }
        cases.Add(("a type-mismatched explicit index without ID is not a valid fallback", () =>
            Check(Resolve(new ObjectiveNode(867, ObjectiveType.UseObject, 0, "", 3, 0),
                new[] { new HostObjective(0, 701, 3, null, HostKind.KillMob) }) == -1, "explicit index bypassed objective type")));
        cases.Add(("an indexless ambiguous same-type identity is deferred", () =>
            Check(Resolve(new ObjectiveNode(867, ObjectiveType.KillMob, 701, "", 3, -1),
                new[] { new HostObjective(0, 701, 3, null, HostKind.KillMob), new HostObjective(1, 701, 3, null, HostKind.KillMob) }) == -1,
                "ambiguous objective was resolved by enumeration order")));
        cases.Add(("an exact type-consistent explicit index remains supported", () =>
            Check(Resolve(new ObjectiveNode(867, ObjectiveType.KillMob, 0, "", 3, 0),
                new[] { new HostObjective(0, 701, 3, null, HostKind.KillMob) }) == 0, "legacy explicit-index contract was removed")));

        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS objective identity: " + item.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL objective identity: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR objective identity: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Objective identity scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual generic owners and raw descriptor; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Objective identity regression");
    }

    private static int Encoded(bool gameObject, int entry) => gameObject ? unchecked((int)0x80000000) | entry : entry;
    private static int Resolve(ObjectiveNode node, IReadOnlyList<HostObjective> values) =>
        (int)typeof(ForcedBehaviorExecutor).GetMethod("ResolveQuestObjectiveIndex", Hidden)!.Invoke(null, new object[] { node, values })!;

    private static void Observe(bool gameObject, int[] ids, int[] required, ushort[] progress, bool failed, int displayedIndex, bool expected)
    {
        using var fixture = (IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests)
            .GetNestedType("Fixture", Hidden)!, true)!;
        var type = fixture.GetType();
        uint descriptor = (uint)type.GetField("descriptor", Hidden)!.GetValue(fixture)!;
        var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)type.GetField("cache", Hidden)!.GetValue(fixture)!;
        Marshal.WriteInt32(new IntPtr(unchecked((int)(descriptor + 636))), failed ? (int)WoWDescriptorQuestFlags.Failed : 0);
        for (int index = 0; index < 4; index++)
            Marshal.WriteInt16(new IntPtr(unchecked((int)(descriptor + 640 + index * 2))), unchecked((short)progress[index]));
        foreach (var address in cache.Value!.Keys.Where(key => unchecked((uint)key.ToInt32()) >= descriptor + 632 &&
            unchecked((uint)key.ToInt32()) < descriptor + 1132).ToArray()) cache.Value.Remove(address);
        var entry = new WoWCache.QuestCacheEntry
        {
            Id = 867, ObjectiveId = ids, ObjectiveRequiredCount = required,
            CollectItemId = new int[6], CollectItemCount = new int[6], IntermediateItemId = new int[4], IntermediateItemCount = new int[4],
            Name = new byte[512], ObjectiveText = new byte[3000], Description = new byte[3000], SubDescription = new byte[512],
            ObjectiveTexts = new byte[1024], CompletionText = new byte[2048]
        };
        var quest = (PlayerQuest)Activator.CreateInstance(typeof(PlayerQuest), Hidden, null, new object[] { entry }, null)!;
        var objective = new HostObjective(displayedIndex, 701, 3, null, gameObject ? HostKind.UseGameObject : HostKind.KillMob);
        var areas = (System.Collections.IList)StyxWoW.AreaManager.GetType().GetField("_areas", Hidden)!.GetValue(StyxWoW.AreaManager)!;
        int previousAreaCount = areas.Count;
        Bots.Quest.Objectives.QuestObjective? owner = null;
        try
        {
            owner = gameObject ? new UseGameObjectObjective(quest, new(), objective, new()) : new GrindObjective(quest, new(), objective, new());
            bool actual;
            try { actual = owner.IsCompleted; }
            catch (IndexOutOfRangeException) { throw new Failure("displayed list index was used as a raw counter slot"); }
            Check(actual == expected, "expected completion=" + expected + "; actual=" + actual);
        }
        finally
        {
            owner?.Dispose();
            while (areas.Count > previousAreaCount) areas.RemoveAt(areas.Count - 1);
        }
    }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
