using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using Bots.Quest.Actions;
using Bots.Quest.QuestOrder;
using CommonBehaviors.Actions;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using TargetType = Styx.Logic.Profiles.Quest.QuestObjectType;

// Real parsed nodes -> executor factories -> the real ActionSetPoi leaf ->
// descriptor-backed object lookup. Only that leaf is ticked: no native movement,
// gossip, quest acceptance/turn-in, or replacement production implementation.
internal static class QuestPoiTargetIdentityRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private const uint Entry = 54321, QuestId = 867;
    private static readonly WoWPoint Target = new(40, 50, 60), Override = new(70, 80, 90);
    private enum Stage { Pickup, TurnIn }
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Quest POI identity tests require Windows x86.");
        var cases = new List<(string Name, System.Action<Fixture> Test)>();
        foreach (Stage stage in Enum.GetValues<Stage>())
        {
            var s = stage;
            foreach (TargetType type in new[] { TargetType.Npc, TargetType.GameObject })
            {
                var t = type;
                cases.Add(($"{s} {t} does not borrow the other namespace with the same entry", f => f.Resolve(s, t, true, true)));
                cases.Add(($"{s} {t} retains the correct first-candidate control", f => f.Resolve(s, t, false, true)));
                cases.Add(($"{s} {t} missing target does not fall back to the other namespace", f => f.Resolve(s, t, true, false)));
                cases.Add(($"{s} {t} factory and actual POI leaf preserve declared type", f => f.Factory(s, t, false, true)));
                cases.Add(($"{s} {t} actual POI leaf selects only the declared target", f => f.Factory(s, t, false, false)));
                cases.Add(($"{s} {t} identical typed POI is not needlessly replaced", f => f.Refresh(s, t, "same", false)));
                cases.Add(($"{s} {t} different type with the same entry requires a new POI", f => f.Refresh(s, t, "type", true)));
                cases.Add(($"{s} {t} changed quest identity requires its own POI", f => f.Refresh(s, t, "quest", true)));
                cases.Add(($"{s} {t} changed declared location requires its own POI", f => f.Refresh(s, t, "location", true)));
                cases.Add(($"{s} {t} untyped same-entry POI cannot stand in for a typed owner", f => f.Refresh(s, t, "untyped", true)));
            }
            cases.Add(($"{s} unspecified type retains legacy unit-first resolution", f => f.ResolveLegacy(s, false)));
            cases.Add(($"{s} unspecified type retains legacy object-first resolution", f => f.ResolveLegacy(s, true)));
            cases.Add(($"{s} invalid explicit enum does not become legacy wildcard", f => f.ResolveUnsupported(s, (TargetType)99)));
            cases.Add(($"{s} item type cannot borrow a world NPC or gameobject", f => f.ResolveUnsupported(s, TargetType.Item)));
            cases.Add(($"{s} declared coordinates remain available before object discovery", f => f.Location(s)));
        }
        foreach (TargetType type in new[] { TargetType.Npc, TargetType.GameObject })
        {
            var t = type;
            cases.Add(($"turn-in {t} profile-objective location override retains type", f => f.Factory(Stage.TurnIn, t, true, true)));
            cases.Add(($"turn-in {t} profile-objective override resolves the correct namespace", f => f.Factory(Stage.TurnIn, t, true, false)));
        }
        cases.Add(("legacy five-argument turn-in constructor remains callable", f => f.LegacyTurnIn()));
        cases.Add(("invalid turn-in type text fails rather than erasing the type", _ => BadTurnInType("unrecognized")));
        cases.Add(("empty explicit turn-in type fails rather than becoming unspecified", _ => BadTurnInType("")));
        cases.Add(("item turn-in type retains existing explicit rejection", _ => BadTurnInType("Item")));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS quest POI identity: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL quest POI identity assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR quest POI identity fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Quest POI identity scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual XML/factory/POI leaf and object lookup; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Quest POI identity regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static XElement Xml(Stage stage, TargetType? type, uint quest = QuestId, WoWPoint? location = null)
    {
        bool pickup = stage == Stage.Pickup; var p = location ?? Target;
        var xml = new XElement(pickup ? "PickUp" : "TurnIn", new XAttribute("QuestId", quest), new XAttribute("QuestName", "typed target"),
            new XAttribute(pickup ? "GiverId" : "TurnInId", Entry), new XAttribute(pickup ? "GiverName" : "TurnInName", "collision target"),
            new XAttribute("X", p.X), new XAttribute("Y", p.Y), new XAttribute("Z", p.Z));
        if (type.HasValue) xml.Add(new XAttribute(pickup ? "GiverType" : "TurnInType", type == TargetType.Npc ? "Npc" : type == TargetType.GameObject ? "GameObject" : "Item"));
        return xml;
    }
    private static OrderNode Node(Stage stage, TargetType? type, uint quest = QuestId, WoWPoint? location = null) =>
        stage == Stage.Pickup ? PickUpNode.FromXml(Xml(stage, type, quest, location)) : TurnInNode.FromXml(Xml(stage, type, quest, location));
    private static BotPoi Poi(OrderNode node) => node is PickUpNode pickup ? new BotPoi(pickup) : new BotPoi((TurnInNode)node);
    private static IEnumerable<Composite> Walk(Composite root)
    { yield return root; foreach (var child in root.Children) foreach (var node in Walk(child)) yield return node; }
    private static BotPoi Publish(ForcedBehavior behavior)
    {
        var leaf = Walk(behavior.Branch).OfType<ActionSetPoi>().Single();
        var context = new object(); leaf.Start(context);
        try { Check(leaf.Tick(context) == RunStatus.Success, "actual POI leaf did not succeed"); }
        finally { leaf.Stop(context); }
        return BotPoi.Current;
    }
    private static object? Invoke(Type type, object? target, string method, params object[] args)
    {
        var found = type.GetMethod(method, target == null ? StaticHidden : Hidden) ?? throw new InvalidOperationException("Missing actual method: " + method);
        try { return found.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void BadTurnInType(string value)
    {
        var xml = Xml(Stage.TurnIn, null); xml.Add(new XAttribute("TurnInType", value));
        try { _ = TurnInNode.FromXml(xml); }
        catch (ProfileAttributeExpectedException) { return; }
        throw new AssertionFailure("explicit malformed/unsupported turn-in type was silently accepted");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object source;
        private readonly Dictionary<ulong, WoWObject> registry;
        private readonly KeyValuePair<ulong, WoWObject>[] previous;
        private readonly object registryLock;
        private readonly BotPoi oldPoi = BotPoi.Current;
        private readonly List<IntPtr> storage = new();
        private readonly List<WoWObject> objects = new();
        private readonly Profile profile = new();
        private bool installed;
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            registry = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", StaticHidden)!.GetValue(null)!;
            registryLock = typeof(ObjectManager).GetField("_updateLock", StaticHidden)!.GetValue(null)!;
            lock (registryLock) previous = registry.ToArray();
            try
            {
                if (ObjectManager.Executor != null || ObjectManager.Me.QuestLog.GetQuestById(QuestId) == null)
                    throw new InvalidOperationException("Actual accepted-quest fixture was not available");
                lock (registryLock) registry.Clear(); installed = true; ResetCaches();
                typeof(ProfileManager).GetField("_currentProfile", StaticHidden)!.SetValue(null, profile);
                // GetCompletionInfo reads this exact client-global buffer. Supply
                // controlled zero steps instead of reading an unrelated test address.
                var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)ObjectManager.Wow.GetType().GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!;
                cache.Value![new IntPtr(12729088)] = new byte[25 * Marshal.SizeOf<WoWQuestCompletionInfo>()];
                BotPoi.Current = new BotPoi(PoiType.None);
            }
            catch { Dispose(); throw; }
        }
        private WoWObject Add(bool gameObject, uint entry = Entry)
        {
            var ptr = Marshal.AllocHGlobal(8192); storage.Add(ptr); Marshal.Copy(new byte[8192], 0, ptr, 8192);
            uint address = unchecked((uint)ptr.ToInt32()), descriptor = address + 4096;
            ulong guid = 9812345000UL + (ulong)objects.Count;
            Write(address + 8, descriptor); Write(address + 20, gameObject ? 5U : 3U);
            Write64(address + 48, guid); Write64(descriptor, guid); Write(descriptor + 12, entry);
            Marshal.StructureToPtr(Target, new IntPtr(unchecked((int)(address + (gameObject ? 232U : 1944U)))), false);
            WoWObject obj = gameObject ? new WoWGameObject(address) : new WoWUnit(address);
            typeof(WoWObject).GetField("_cachedName", Hidden)!.SetValue(obj, "test target");
            objects.Add(obj); lock (registryLock) registry.Add(guid, obj); ResetCaches();
            if (!obj.IsValid || obj.Entry != entry || obj.Guid != guid || !ObjectManager.ObjectList.Contains(obj))
                throw new InvalidOperationException("Actual target descriptor/registry setup failed");
            return obj;
        }
        private WoWObject Setup(TargetType type, bool wrongFirst, bool includeTarget)
        {
            bool gameObject = type == TargetType.GameObject;
            if (wrongFirst) Add(!gameObject);
            var selected = includeTarget ? Add(gameObject) : null;
            if (!wrongFirst) Add(!gameObject);
            return selected!;
        }
        internal void Resolve(Stage stage, TargetType type, bool wrongFirst, bool includeTarget)
        {
            var expected = Setup(type, wrongFirst, includeTarget);
            var poi = Poi(Node(stage, type));
            Check(ReferenceEquals(poi.AsObject, expected), "typed POI borrowed the wrong object namespace");
            Check(ReferenceEquals(poi.AsObject, expected), "repeated lookup changed target identity");
        }
        internal void ResolveLegacy(Stage stage, bool gameObjectFirst)
        {
            var first = Add(gameObjectFirst); Add(!gameObjectFirst);
            Check(ReferenceEquals(Poi(Node(stage, null)).AsObject, first), "legacy unspecified lookup changed");
        }
        internal void ResolveUnsupported(Stage stage, TargetType type)
        {
            Add(false); Add(true);
            OrderNode node = stage == Stage.Pickup ? new PickUpNode(Target, Entry, "target", type, QuestId, "quest")
                : new TurnInNode(Target, Entry, "target", type, QuestId, "quest");
            Check(Poi(node).AsObject == null, "unsupported explicit type selected a world target");
        }
        internal void Location(Stage stage)
        { Check(Poi(Node(stage, TargetType.GameObject)).Location == Target, "declared location was lost before object discovery"); }
        private ForcedBehavior Create(Stage stage, TargetType type, bool useOverride)
        {
            if (useOverride)
            {
                var quest = new QuestInfo(QuestId, "typed override"); quest.Objectives.Add(new TurnInObjectiveInfo(Override)); profile.Quests.Add(quest);
            }
            var value = Invoke(typeof(ForcedBehaviorExecutor), null, stage == Stage.Pickup ? "CreateQuestPickUp" : "CreateQuestTurnIn", Node(stage, type));
            return value as ForcedBehavior ?? throw new InvalidOperationException("Actual executor factory unexpectedly rejected valid typed node");
        }
        internal void Factory(Stage stage, TargetType type, bool useOverride, bool metadataOnly)
        {
            var expected = Setup(type, true, true); using var behavior = Create(stage, type, useOverride);
            var poi = Publish(behavior);
            if (metadataOnly)
            {
                TargetType? actual = stage == Stage.Pickup ? poi.AsPickUp?.GiverType : poi.AsTurnIn?.TurnInType;
                uint? id = stage == Stage.Pickup ? poi.AsPickUp?.QuestId : poi.AsTurnIn?.QuestId;
                var declared = stage == Stage.Pickup ? poi.AsPickUp?.GiverLocation : poi.AsTurnIn?.TurnInLocation;
                Check(actual == type && id == QuestId && declared == (useOverride ? Override : Target), "factory/POI leaf erased declared identity or location");
            }
            else Check(ReferenceEquals(poi.AsObject, expected), "actual forced-behavior POI selected wrong namespace");
        }
        internal void Refresh(Stage stage, TargetType type, string change, bool expected)
        {
            using var behavior = Create(stage, type, false);
            TargetType? priorType = change == "type" ? (type == TargetType.Npc ? TargetType.GameObject : TargetType.Npc) : type;
            if (change == "untyped") BotPoi.Current = new BotPoi(Target, stage == Stage.Pickup ? PoiType.QuestPickUp : PoiType.QuestTurnIn) { Entry = Entry };
            else BotPoi.Current = Poi(Node(stage, priorType, change == "quest" ? QuestId + 1 : QuestId, change == "location" ? Override : Target));
            bool actual = (bool)Invoke(behavior.GetType(), behavior, "ShouldSetPoi", new object())!;
            Check(actual == expected, "same numeric entry hid changed typed POI ownership");
        }
        internal void LegacyTurnIn()
        {
            var constructor = typeof(ForcedQuestTurnIn).GetConstructor(new[] { typeof(uint), typeof(string), typeof(uint), typeof(string), typeof(WoWPoint) });
            Check(constructor != null, "legacy constructor disappeared");
            var first = Add(false); Add(true);
            using var behavior = (ForcedQuestTurnIn)constructor!.Invoke(new object[] { QuestId, "quest", Entry, "target", Target });
            var poi = Publish(behavior);
            Check(poi.Entry == Entry && ReferenceEquals(poi.AsObject, first), "legacy unspecified turn-in compatibility changed");
        }
        public void Dispose()
        {
            try
            {
                typeof(BotPoi).GetField("_current", StaticHidden)!.SetValue(null, oldPoi);
                if (installed) { lock (registryLock) { registry.Clear(); foreach (var entry in previous) registry.Add(entry.Key, entry.Value); } installed = false; ResetCaches(); }
            }
            finally
            {
                try { ((IDisposable)source).Dispose(); }
                finally { foreach (var ptr in storage) Marshal.FreeHGlobal(ptr); storage.Clear(); }
            }
        }
    }
    private static void ResetCaches() => typeof(ObjectManager).GetMethod("ResetCaches", StaticHidden)!.Invoke(null, null);
    private static void Write(uint address, uint value) => Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value));
    private static void Write64(uint address, ulong value) { Write(address, (uint)value); Write(address + 4, (uint)(value >> 32)); }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
