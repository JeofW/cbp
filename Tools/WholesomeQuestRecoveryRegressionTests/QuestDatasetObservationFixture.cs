using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Bots.Quest;
using Bots.Quest.Actions;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;

// Allocated observations consumed by the real Windows/x86 readers and behavior
// owners. Never attaches to WoW and never replaces a production decision method.
internal sealed class QuestDatasetObservationFixture : IDisposable
{
    private sealed class ObservedPlayer(uint address) : LocalPlayer(address)
    {
        public override string Name => "Dataset Fixture";
        public override string RealmName => "Controlled Observation Realm";
        public override Styx.Logic.Pathing.WoWPoint Location => new(10, 10, 10);
    }
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly IDisposable baseline;
    private readonly uint start, descriptor, node, inventory;
    private readonly ThreadLocal<Dictionary<IntPtr, byte[]>> cache;
    private readonly IntPtr nodeStorage, itemStorage;
    private readonly int nodeSize;
    private readonly Dictionary<ulong, WoWObject> objects;
    private readonly Dictionary<ulong, WoWObject> previousObjects;
    private readonly List<(FieldInfo Field, object? Value)> completionState = new();
    private readonly uint[] completedBefore;
    private readonly IList areas;
    private readonly int previousAreaCount;
    private readonly List<Bots.Quest.Objectives.QuestObjective> objectiveOwners = new();
    private readonly List<IDisposable> otherOwners = new();
    internal LocalPlayer Player { get; }
    internal PlayerQuest Quest { get; private set; } = null!;
    internal uint QuestId { get; private set; }

    internal QuestDatasetObservationFixture()
    {
        baseline = (IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", Hidden)!, true)!;
        var type = baseline.GetType();
        start = unchecked((uint)((IntPtr)type.GetField("storage", Hidden)!.GetValue(baseline)!).ToInt32());
        descriptor = (uint)type.GetField("descriptor", Hidden)!.GetValue(baseline)!;
        Player = new ObservedPlayer(start);
        typeof(ObjectManager).GetField("<Me>k__BackingField", Hidden)!.SetValue(null, Player);
        cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)type.GetField("cache", Hidden)!.GetValue(baseline)!;
        objects = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", Hidden)!.GetValue(null)!;
        previousObjects = new(objects);
        areas = (IList)StyxWoW.AreaManager.GetType().GetField("_areas", Hidden)!.GetValue(StyxWoW.AreaManager)!;
        previousAreaCount = areas.Count;
        completedBefore = ((List<uint>)typeof(QuestLog).GetField("_completedQuestIds", Hidden)!.GetValue(null)!).ToArray();
        foreach (string name in new[] { "_completedQuestCacheTime", "_completedQuestRefreshAttemptTime", "_completedQuestCacheStatus", "_completedQuestCacheIdentity" })
        {
            var field = typeof(QuestLog).GetField(name, Hidden)!; completionState.Add((field, field.GetValue(null)));
        }
        nodeSize = 24 + Marshal.SizeOf<WoWCache.QuestCacheEntry>();
        nodeStorage = Marshal.AllocHGlobal(nodeSize); node = unchecked((uint)nodeStorage.ToInt32());
        itemStorage = Marshal.AllocHGlobal(8192);
        Marshal.Copy(new byte[nodeSize], 0, nodeStorage, nodeSize);
        Marshal.Copy(new byte[8192], 0, itemStorage, 8192);
        // The reused minimal fixture accepted any nonzero low ID byte at offset
        // zero. A real arbitrary-ID cache node needs a separate loaded marker.
        typeof(WoWCache.Cache).GetField("_entryOffset", Hidden)!.SetValue(StyxWoW.Cache[CacheDb.Quest], 12U);
        Marshal.WriteByte(Pointer(node + 12), 1);
        Write32(start + 32768 + 8, node);
        inventory = start + 12000;
        Write32(start + 6384, 150); Write32(start + 6388, inventory);
        Write64(start + 6392, Player.Guid); Marshal.WriteByte(Pointer(start + 6400), 1);
        for (uint index = 0; index < 4; index++) cache.Value![Pointer(12727616U + index * 8)] = new byte[8];
        SetInventory(new());
        SetHistory(Array.Empty<uint>());
    }

    internal void SetQuest(uint id, string name, int level, int[] normalIds, int[] normalCounts, int[] itemIds, int[] itemCounts)
    {
        ReleaseOwners(); QuestId = id;
        object boxed = new WoWCache.QuestCacheEntry();
        foreach (var field in typeof(WoWCache.QuestCacheEntry).GetFields())
        {
            var attribute = field.GetCustomAttribute<MarshalAsAttribute>();
            if (field.FieldType.IsArray && attribute?.SizeConst > 0)
                field.SetValue(boxed, Array.CreateInstance(field.FieldType.GetElementType()!, attribute.SizeConst));
        }
        var entry = (WoWCache.QuestCacheEntry)boxed;
        entry.Id = id; entry.ObjectiveId = normalIds.ToArray(); entry.ObjectiveRequiredCount = normalCounts.ToArray();
        entry.CollectItemId = itemIds.ToArray(); entry.CollectItemCount = itemCounts.ToArray();
        Encoding.UTF8.GetBytes(name).Take(entry.Name.Length - 1).ToArray().CopyTo(entry.Name, 0);
        Write32(node, id); Write32(node + 4, 0);
        Marshal.StructureToPtr(entry, Pointer(node + 24), false);
        Type fields = typeof(WoWUnit).Assembly.GetTypes().Single(value => value.IsEnum && value.Name == "UnitFields");
        Write32(descriptor + Convert.ToUInt32(Enum.Parse(fields, "Level")) * 4, (uint)level);
        Write32(descriptor + Convert.ToUInt32(Enum.Parse(fields, "Bytes0")) * 4, 0x020A); // Blood Elf / Paladin fixture
        SetAccepted(true); SetProgress(new int[4]); SetInventory(new()); SetHistory(Array.Empty<uint>());
        Quest = Player.QuestLog.GetQuestById(id) ?? throw new InvalidOperationException("Actual cache reader did not resolve quest " + id);
        if (!Quest.NormalObjectiveIDs.SequenceEqual(normalIds) || !Quest.CollectItemIds.SequenceEqual(itemIds))
            throw new InvalidOperationException("Raw cache fixture changed objective identities");
    }

    internal void SetAccepted(bool accepted, bool complete = false, bool failed = false)
    {
        Write32(descriptor + 632, accepted ? QuestId : 0);
        Write32(descriptor + 636, (complete ? (uint)WoWDescriptorQuestFlags.Completed : 0) | (failed ? (uint)WoWDescriptorQuestFlags.Failed : 0));
        Invalidate();
    }

    internal void SetProgress(int[] values)
    {
        if (values.Length != 4 || values.Any(value => value < 0 || value > ushort.MaxValue)) throw new ArgumentOutOfRangeException(nameof(values));
        for (int index = 0; index < 4; index++) Marshal.WriteInt16(Pointer(descriptor + 640 + (uint)index * 2), unchecked((short)values[index]));
        Invalidate();
    }

    internal void SetInventory(Dictionary<int, long> amounts)
    {
        foreach (ulong id in objects.Keys.Where(id => id >= 99000001 && id <= 99000016).ToArray()) objects.Remove(id);
        for (uint index = 0; index < 150; index++) Write64(inventory + index * 8, 0);
        int slot = 0;
        foreach (var item in amounts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key))
        {
            if (slot >= 16 || item.Key <= 0 || item.Value > uint.MaxValue) throw new InvalidOperationException("Invalid carried-item fixture");
            uint address = unchecked((uint)itemStorage.ToInt32()) + (uint)slot * 512;
            uint fields = address + 256; ulong guid = (ulong)(99000001 + slot);
            Write32(address + 8, fields); Write32(address + 0x14, 1); Write64(address + 0x30, guid);
            Write64(fields, guid); Write32(fields + 8, 3); Write32(fields + 12, (uint)item.Key); Write32(fields + 56, (uint)item.Value);
            Write64(inventory + (uint)(23 + slot) * 8, guid);
            objects[guid] = new WoWItem(address); slot++;
        }
        Invalidate();
        var actual = Player.CarriedItems.GroupBy(item => (int)item.Entry).ToDictionary(group => group.Key, group => group.Sum(item => (long)item.StackCount));
        if (!amounts.Where(pair => pair.Value > 0).All(pair => actual.TryGetValue(pair.Key, out long value) && value == pair.Value)
            || actual.Count != amounts.Count(pair => pair.Value > 0))
            throw new InvalidOperationException("Actual inventory reader disagreed with supplied carried-item observations");
    }

    internal void SetHistory(IEnumerable<uint> ids, bool authoritative = true)
    {
        string identity = (string)typeof(QuestLog).GetMethod("CaptureCompletedQuestCacheIdentity", Hidden)!.Invoke(null, null)!;
        if (string.IsNullOrWhiteSpace(identity)) throw new InvalidOperationException("Completion fixture requires the actual actor/realm identity");
        var list = (List<uint>)typeof(QuestLog).GetField("_completedQuestIds", Hidden)!.GetValue(null)!;
        list.Clear(); list.AddRange(ids);
        typeof(QuestLog).GetField("_completedQuestCacheIdentity", Hidden)!.SetValue(null, identity);
        typeof(QuestLog).GetField("_completedQuestCacheTime", Hidden)!.SetValue(null, DateTime.Now);
        typeof(QuestLog).GetField("_completedQuestRefreshAttemptTime", Hidden)!.SetValue(null, DateTime.Now);
        var status = typeof(QuestLog).GetField("_completedQuestCacheStatus", Hidden)!;
        status.SetValue(null, Enum.Parse(status.FieldType, authoritative ? "Valid" : "Unknown"));
    }

    internal void LoadProfile(string xml)
    {
        var profile = new Profile(System.Xml.Linq.XDocument.Parse(xml).Root!, null);
        typeof(ProfileManager).GetField("_currentProfile", Hidden)!.SetValue(null, profile);
        typeof(ProfileManager).GetField("_currentOuterProfile", Hidden)!.SetValue(null, profile);
    }

    internal ForcedQuestObjective CreateObjective(ObjectiveNode node)
    {
        var objectives = Quest.GetObjectives();
        int index = (int)typeof(ForcedBehaviorExecutor).GetMethod("ResolveQuestObjectiveIndex", Hidden)!
            .Invoke(null, new object[] { node, objectives })!;
        if (index < 0) throw new InvalidOperationException("Generated profile has no unambiguous typed runtime objective");
        var owner = QuestManager.CreateQuestObjective(objectives[index], Quest, new(), new());
        objectiveOwners.Add(owner);
        return new ForcedQuestObjective(owner);
    }

    internal void ReleaseOwners()
    {
        foreach (var owner in objectiveOwners) owner.Dispose(); objectiveOwners.Clear();
        foreach (var owner in otherOwners) owner.Dispose(); otherOwners.Clear();
        while (areas.Count > previousAreaCount) areas.RemoveAt(areas.Count - 1);
    }

    private void Invalidate()
    {
        uint items = unchecked((uint)itemStorage.ToInt32());
        foreach (var address in cache.Value!.Keys.Where(key =>
        {
            uint value = unchecked((uint)key.ToInt32());
            return (value >= start && value < start + 65536) || (value >= node && value < node + nodeSize)
                || (value >= items && value < items + 8192);
        }).ToArray()) cache.Value.Remove(address);
    }
    private static IntPtr Pointer(uint address) => new(unchecked((int)address));
    private static void Write32(uint address, uint value) => Marshal.WriteInt32(Pointer(address), unchecked((int)value));
    private static void Write64(uint address, ulong value) => Marshal.WriteInt64(Pointer(address), unchecked((long)value));
    public void Dispose()
    {
        ReleaseOwners();
        objects.Clear(); foreach (var entry in previousObjects) objects[entry.Key] = entry.Value;
        var list = (List<uint>)typeof(QuestLog).GetField("_completedQuestIds", Hidden)!.GetValue(null)!;
        list.Clear(); list.AddRange(completedBefore);
        foreach (var entry in completionState) entry.Field.SetValue(null, entry.Value);
        baseline.Dispose(); Marshal.FreeHGlobal(nodeStorage); Marshal.FreeHGlobal(itemStorage);
    }
}
