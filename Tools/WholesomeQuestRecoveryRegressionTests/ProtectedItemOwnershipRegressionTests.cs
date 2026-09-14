using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bots.Quest.Objectives;
using Styx;
using Styx.Helpers;
using Styx.Logic.AreaManagement;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using QuestItem = Styx.Logic.Questing.Quest.QuestObjective;
using ObjectiveKind = Styx.Logic.Questing.Quest.QuestObjectiveType;

// Actual collector construction/disposal and production manager membership.
// The retained fixture supplies only raw accepted quest/cache observations; no
// native process, merchant dispatch, inventory mutation or game is exercised.
internal static class ProtectedItemOwnershipRegressionTests
{
    private const uint Item = 190008671, Other = 190008672;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Protected-item owner tests require Windows x86.");
        var cases = new List<(string Name, Action Test)>
        {
            ("single real collector protects until disposal", () => With(f =>
            { var a = f.Collector(Item); Check(ProtectedItemsManager.Contains(Item), "constructor did not protect item"); a.Dispose(); Check(!ProtectedItemsManager.Contains(Item), "last collector leaked protection"); })),
            ("first collector disposal cannot release a second collector", () => With(f =>
            { var a = f.Collector(Item); var b = f.Collector(Item); a.Dispose(); Check(ProtectedItemsManager.Contains(Item), "one owner released another owner's protection"); b.Dispose(); Check(!ProtectedItemsManager.Contains(Item), "last owner did not release"); })),
            ("reverse collector disposal order retains protection", () => With(f =>
            { var a = f.Collector(Item); var b = f.Collector(Item); b.Dispose(); Check(ProtectedItemsManager.Contains(Item), "reverse disposal released the surviving collector"); a.Dispose(); Check(!ProtectedItemsManager.Contains(Item), "last owner did not release"); })),
            ("preexisting manual protection survives collector disposal", () => With(f =>
            { Check(ProtectedItemsManager.Add(Item), "manual setup failed"); var a = f.Collector(Item); a.Dispose(); Check(ProtectedItemsManager.Contains(Item), "collector erased preexisting manual protection"); Check(ProtectedItemsManager.Remove(Item), "manual owner disappeared"); })),
            ("manual protection added during collection survives disposal", () => With(f =>
            { var a = f.Collector(Item); ProtectedItemsManager.Add(Item); a.Dispose(); Check(ProtectedItemsManager.Contains(Item), "collector erased the later manual owner"); ProtectedItemsManager.Remove(Item); })),
            ("legacy manual removal cannot release an active collector", () => With(f =>
            { var a = f.Collector(Item); ProtectedItemsManager.Add(Item); ProtectedItemsManager.Remove(Item); Check(ProtectedItemsManager.Contains(Item), "manual removal erased active collector protection"); a.Dispose(); Check(!ProtectedItemsManager.Contains(Item), "last collector remained after manual removal"); })),
            ("duplicate obsolete disposal cannot release a later collector", () => With(f =>
            { var a = f.Collector(Item); a.Dispose(); var b = f.Collector(Item); a.Dispose(); Check(ProtectedItemsManager.Contains(Item), "duplicate old disposal released replacement owner"); b.Dispose(); Check(!ProtectedItemsManager.Contains(Item), "replacement lease leaked"); })),
            ("different collected item IDs retain independent owners", () => With(f =>
            { var a = f.Collector(Item); var b = f.Collector(Other); a.Dispose(); Check(!ProtectedItemsManager.Contains(Item) && ProtectedItemsManager.Contains(Other), "different IDs interfered"); b.Dispose(); })),
            ("vendor list snapshot includes the surviving collector", () => With(f =>
            { var a = f.Collector(Item); var b = f.Collector(Item); a.Dispose(); Check(ProtectedItemsManager.GetAllItemIds().Contains(Item), "vendor snapshot lost surviving owner"); b.Dispose(); Check(!ProtectedItemsManager.GetAllItemIds().Contains(Item), "snapshot leaked last owner"); })),
            ("FILE protection survives the last collector and manual removal", () => With(f =>
            { f.FileItems.Add(Item); var a = f.Collector(Item); ProtectedItemsManager.Add(Item); ProtectedItemsManager.Remove(Item); a.Dispose(); Check(ProtectedItemsManager.Contains(Item), "runtime disposal erased FILE layer"); })),
            ("legacy manual set still has idempotent bool Add and Remove", () => With(f =>
            { Check(ProtectedItemsManager.Add(Item) && !ProtectedItemsManager.Add(Item), "legacy Add ceased to be set-idempotent"); Check(ProtectedItemsManager.Remove(Item) && !ProtectedItemsManager.Remove(Item), "legacy Remove ceased to be set-idempotent"); })),
            ("repeated overlapping collector replacement neither gaps nor leaks", () => With(f =>
            { var current = f.Collector(Item); for (int i = 0; i < 12; i++) { var next = f.Collector(Item); current.Dispose(); current.Dispose(); Check(ProtectedItemsManager.Contains(Item), "replacement lost protection"); current = next; } current.Dispose(); Check(!ProtectedItemsManager.Contains(Item), "replacement chain leaked protection"); })),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS protected-item ownership: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL protected-item ownership assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR protected-item ownership fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Protected-item ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual CollectItemObjective and ProtectedItemsManager; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Protected-item ownership regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable raw;
        private readonly List<CollectItemObjective> collectors = new();
        private readonly List<Area> areas;
        private readonly Area[] originalAreas;
        internal readonly HashSet<uint> FileItems;
        internal Fixture()
        {
            raw = (IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            areas = (List<Area>)typeof(AreaManager).GetField("_areas", Hidden)!.GetValue(StyxWoW.AreaManager)!;
            originalAreas = areas.ToArray();
            FileItems = ((DualHashSet<uint, string>)typeof(ProtectedItemsManager).GetField("_fileProtectedItems", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).HashSet1;
            Check(!ProtectedItemsManager.Contains(Item) && !ProtectedItemsManager.Contains(Other), "unique test IDs already protected");
        }
        internal CollectItemObjective Collector(uint item)
        {
            PlayerQuest quest = ObjectManager.Me.QuestLog.GetQuestById(867);
            Check(quest != null && quest.Id == 867 && ObjectManager.Me.QuestLog.ContainsQuest(867), "actual raw/materialized quest fixture failed");
            var owner = new CollectItemObjective(quest!, new List<WoWQuestStep>(),
                new QuestItem(0, checked((int)item), 5, "test collection", ObjectiveKind.CollectItem), new List<QuestObjective>());
            collectors.Add(owner); return owner;
        }
        public void Dispose()
        {
            try { foreach (var owner in collectors) owner.Dispose(); }
            finally
            {
                FileItems.Remove(Item); FileItems.Remove(Other);
                ProtectedItemsManager.Remove(Item); ProtectedItemsManager.Remove(Other);
                areas.Clear(); areas.AddRange(originalAreas); raw.Dispose();
            }
        }
    }
    private static void With(Action<Fixture> action) { using var fixture = new Fixture(); action(fixture); }
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
