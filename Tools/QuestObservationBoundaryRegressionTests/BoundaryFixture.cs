// Test boundaries only. No client, native executor, live inventory, or server is used.
// Real source-extracted owners are generated separately. Completion flag values,
// completed-history refresh, descriptors, metadata storage and dispatch are controlled.
using System.Collections.ObjectModel;
using Styx.Logic.Questing;
using Styx.WoWInternals.WoWCache;

namespace W42Fixture
{
    public static class World
    {
        public static bool HistoryValid;
        public static List<uint> HistoricalCompletions = new();
        public static HashSet<uint> CurrentCompleted = new();
        public static int HistoryReads;
        public static void Reset()
        {
            HistoryValid = true;
            HistoricalCompletions = new();
            CurrentCompleted = new();
            HistoryReads = 0;
            Styx.StyxWoW.Cache = new();
            Styx.StyxWoW.Me = new();
            WholesomeAQ.MerchantFrame.Instance = new();
            WholesomeAQ.Consumable.Food = null;
            WholesomeAQ.Consumable.Drink = null;
            WholesomeAQ.Consumable.BeforeFood = null;
            Styx.Logic.Profiles.ProfileManager.CurrentProfile = new();
        }
    }

    public sealed class ControlledCacheCollection
    {
        public ControlledQuestCache Quests = new();
        public ControlledQuestCache this[CacheDb kind] => Quests;
    }

    public sealed class ControlledQuestCache
    {
        public Dictionary<uint, WoWCache.InfoBlock> Entries = new();
        public Action<uint> BeforeRead;
        public void Hydrate(uint id) => Entries[id] = new WoWCache.InfoBlock
        {
            Quest = new WoWCache.QuestCacheEntry { Id = id }
        };
        public WoWCache.InfoBlock GetInfoBlockById(uint id)
        {
            BeforeRead?.Invoke(id);
            return Entries.TryGetValue(id, out var entry) ? entry : null;
        }
    }
}

namespace Styx.WoWInternals.WoWCache
{
    public enum CacheDb { Quest }
    public class WoWCache
    {
        public class QuestCacheEntry { public uint Id; }
        public class InfoBlock { public QuestCacheEntry Quest; }
    }
}

namespace Styx
{
    public static class StyxWoW
    {
        public static WholesomeAQ.Player Me;
        public static W42Fixture.ControlledCacheCollection Cache = new();
    }
}

namespace Styx.WoWInternals
{
    public static class ObjectManager
    {
        public static WholesomeAQ.Player Me => Styx.StyxWoW.Me;
    }
}

namespace Styx.Logic.Questing
{
    // Unrelated metadata fields and native completion reads are controlled.
    public class Quest
    {
        protected Quest(WoWCache.QuestCacheEntry entry) { Id = entry.Id; }
        public uint Id { get; }
    }
    public partial class PlayerQuest
    {
        public bool IsCompleted => W42Fixture.World.CurrentCompleted.Contains(Id);
    }
    public partial class QuestLog
    {
        // The historical-cache implementation has its own existing regressions;
        // this test only controls the observation at that boundary.
        public bool TryGetAuthoritativeCompletedQuests(out ReadOnlyCollection<uint> ids)
        {
            W42Fixture.World.HistoryReads++;
            ids = new ReadOnlyCollection<uint>(W42Fixture.World.HistoricalCompletions.ToList());
            return W42Fixture.World.HistoryValid;
        }
    }
}

namespace Styx.Helpers
{
    public static class Logging { public static void Write(string text) => Console.WriteLine(text); }
}

namespace Styx.Logic.Profiles
{
    public sealed class Profile { public Styx.Helpers.DualHashSet<uint, string> ProtectedItems { get; } = new(); }
    public static class ProfileManager { public static Profile CurrentProfile { get; set; } = new(); }
}

namespace WholesomeAQ
{
    [Flags] public enum ItemQuality { None = 0, Common = 1, Uncommon = 2, Rare = 4 }
    public enum WoWItemClass { Miscellaneous, Projectile, Quiver, Reagent, Key }
    public sealed class Item { public uint Entry; public WoWItemClass ItemClass; }
    public sealed class Player
    {
        public bool IsValid = true, IsAlive = true;
        public uint[] Slots = new uint[25];
        public QuestLog QuestLog { get; } = new();
        public List<Item> BagItems { get; } = new();
        public Action<uint> BeforeDescriptorRead;
        public T ReadDescriptor<T>(uint field)
        {
            BeforeDescriptorRead?.Invoke(field);
            if (typeof(T) != typeof(uint) || field < 158 || (field - 158) % 5 != 0)
                throw new InvalidOperationException("Fixture only supports the original quest-ID descriptor fields.");
            uint index = (field - 158) / 5;
            if (index >= 25) throw new ArgumentOutOfRangeException(nameof(field));
            return (T)(object)Slots[index];
        }
    }
    public sealed class Objective { public int ItemId; }
    public sealed class QuestEntry { public int Id, StartItem; public List<Objective> Objectives = new(); }
    public sealed class Database { public List<QuestEntry> Quests = new(); }
    public sealed class Loader { public Database Database = new(); }
    public sealed class Scheduler { public HashSet<int> ActiveQuestIds { get; set; } = new(); }
    public sealed class Settings { public bool SellWhite = true, SellGreen, SellBlue; }
    public static class Consumable
    {
        public static Item Food, Drink;
        public static Action BeforeFood;
        public static Item GetBestFood(bool _) { BeforeFood?.Invoke(); return Food; }
        public static Item GetBestDrink(bool _) => Drink;
    }
    public sealed class MerchantFrame
    {
        public static MerchantFrame Instance { get; set; } = new();
        public bool IsVisible = true;
        public int Calls;
        public ItemQuality Mask;
        public HashSet<uint> Ids = new();
        public HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
        public void SellItemQualities(ItemQuality mask, IEnumerable<string> names, IEnumerable<uint> ids)
        {
            Calls++;
            Mask = mask;
            Ids = ids?.ToHashSet() ?? new();
            Names = new(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }
    }
    public partial class WholesomeAutoQuest
    {
        public Settings _settings = new();
        public Loader _dataLoader = new();
        public Scheduler _scheduler = new();
        private bool _lastFrameVisible;
        private void Log(string text) { }
        public void RunSale() => SellByQuality();
    }
}
