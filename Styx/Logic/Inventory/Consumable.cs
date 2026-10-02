#nullable disable
using System.Collections.Generic;
using System.Linq;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Inventory
{
    /// <summary>
    /// Helper class for finding consumable items (food and drink).
    /// </summary>
    public static class Consumable
    {
        public sealed class SelectionObservation
        {
            public WoWItem Item { get; internal set; }
            public bool IsComplete { get; internal set; }
            public string Reason { get; internal set; }
            public string Details { get; internal set; } = "";
        }

        public static SelectionObservation ObserveBestFood(bool includeSpecialtyItems) => ObserveBest(false, includeSpecialtyItems);
        public static SelectionObservation ObserveBestDrink(bool includeSpecialtyItems) => ObserveBest(true, includeSpecialtyItems);
        public static SelectionObservation ObserveNamedRestItem(bool drinking, string name) => ObserveBest(drinking, true, name);

        private static SelectionObservation ObserveBest(bool drinking, bool includeSpecialtyItems, string configuredName = null)
        {
            var player = ObjectManager.Me;
            if (player == null)
                return new SelectionObservation { Reason = "actor-unavailable" };
            bool complete = player.TryGetBagItems(out var items, out string reason);
            var details = new List<string>();
            int playerClass = (int)player.Class;
            if (playerClass <= 0 || playerClass > 31)
                return new SelectionObservation { Reason = "actor-class-unavailable" };
            int playerLevel = player.Level;
            if (playerLevel <= 0)
                return new SelectionObservation { Reason = "actor-level-unavailable" };
            WoWItem best = null;
            int bestLevel = -1;
            uint bestStack = 0;
            foreach (var item in items)
            {
                if (item == null || !item.IsValid)
                { complete = false; reason = "item-object-unavailable"; continue; }
                if (!item.TryGetStackCount(out uint stack))
                { complete = false; reason = "item-stack-unavailable"; continue; }
                void Describe(string decision)
                {
                    if (details.Count < 12) details.Add("item=" + item.Entry + ",stack=" + stack + "," + decision);
                }
                if (stack == 0) { Describe("empty-stack"); continue; }
                var info = item.ItemInfo;
                if (info == null)
                { complete = false; reason = "item-info-unavailable"; Describe(reason); continue; }
                if (configuredName != null)
                {
                    // A generic object label (for example Object_123) is not a
                    // complete item-cache name and cannot prove named absence.
                    string observedName = info.Name;
                    if (string.IsNullOrEmpty(observedName))
                    { complete = false; reason = "item-name-unavailable"; Describe(reason); continue; }
                    if (!string.Equals(configuredName, observedName, System.StringComparison.OrdinalIgnoreCase)) continue;
                }
                if ((int)info.ItemClass != (int)WoWItemClass.Consumable) continue;
                if (info.RequiredLevel > playerLevel) { Describe("required-level=" + info.RequiredLevel); continue; }
                if ((info.AllowedClasses & (1 << (playerClass - 1))) == 0) { Describe("class-mask=" + info.AllowedClasses); continue; }
                var spells = item.ItemSpells;
                bool recognized = false, specialty = false, unknown = false;
                foreach (var spell in spells)
                {
                    var actual = spell?.ActualSpell;
                    if (actual == null) { unknown = true; continue; }
                    string name = actual.Name;
                    recognized |= name == "Refreshment" || (drinking ? name == "Drink" || name == "Starfire Espresso" : name == "Food");
                    specialty |= name != "Food" && name != "Drink" && name != "Refreshment";
                }
                if (unknown) { complete = false; reason = "item-spell-metadata-unavailable"; Describe(reason); }
                if (!recognized) { Describe("no-recognized-" + (drinking ? "drink" : "food") + "-effect"); continue; }
                if (!includeSpecialtyItems && (specialty || unknown)) { Describe("specialty-filter"); continue; }
                Describe("eligible,required-level=" + info.RequiredLevel);
                if (best == null || info.RequiredLevel > bestLevel || (info.RequiredLevel == bestLevel && stack > bestStack))
                { best = item; bestLevel = info.RequiredLevel; bestStack = stack; }
            }
            if (!ReferenceEquals(player, ObjectManager.Me))
                return new SelectionObservation { Reason = "actor-replaced" };
            return new SelectionObservation { Item = best, IsComplete = complete,
                Reason = best != null ? "candidate-selected" : complete ? "no-usable-consumable" : reason,
                Details = string.Join(";", details) };
        }

        /// <summary>
        /// Gets all food items in the player's bags.
        /// </summary>
        public static List<WoWItem> GetFood()
        {
            var player = ObjectManager.Me;
            if (player == null) return new List<WoWItem>();
            var result = player.BagItems
                .Where(IsFood)
                .Distinct()
                .ToList();
            return ReferenceEquals(player, ObjectManager.Me) ? result : new List<WoWItem>();
        }

        /// <summary>
        /// Gets all drink items in the player's bags.
        /// </summary>
        public static List<WoWItem> GetDrinks()
        {
            var player = ObjectManager.Me;
            if (player == null) return new List<WoWItem>();
            var result = player.BagItems
                .Where(IsDrink)
                .Distinct()
                .ToList();
            return ReferenceEquals(player, ObjectManager.Me) ? result : new List<WoWItem>();
        }

        /// <summary>
        /// Gets the best food item.
        /// Picks the highest RequiredLevel consumable the player can use; on tie, the largest stack.
        /// HB 4.3.4 Consumable.GetBestFood uses `StackCount > num && RequiredLevel > num2` (both strict)
        /// which is logically broken — a single item cannot have BOTH more stack AND higher required
        /// level than itself, so the loop only ever accepts the very first item that passes against
        /// the initial `num=0, num2=-1` and never upgrades to a better one with the same stack count.
        /// We port the same intent (pick the best consumable the player can use) but with correct
        /// ordering: highest RequiredLevel first, largest StackCount on tie.
        /// </summary>
        /// <param name="includeSpecialtyItems">Include items with special effects.</param>
        public static WoWItem GetBestFood(bool includeSpecialtyItems)
        => ObserveBestFood(includeSpecialtyItems).Item;

        /// <summary>
        /// Gets the best drink item.
        /// Same fix as GetBestFood: HB 4.3.4 has the same broken `> && >` strict-greater condition.
        /// We use highest RequiredLevel with largest StackCount tie-break.
        /// </summary>
        /// <param name="includeSpecialtyItems">Include items with special effects.</param>
        public static WoWItem GetBestDrink(bool includeSpecialtyItems)
        => ObserveBestDrink(includeSpecialtyItems).Item;

        /// <summary>
        /// Checks if an item is food.
        /// HB 4.3.4 Consumable.smethod_1: spell name "Food" OR "Refreshment".
        /// "Refreshment" covers WotLK Mage conjured food (Conjured Mana Strudel, etc.).
        /// </summary>
        private static bool IsFood(WoWItem item)
        {
            if (item == null || !item.IsValid || item.StackCount == 0 || item.ItemInfo == null
                || (int)item.ItemInfo.ItemClass != (int)WoWItemClass.Consumable)
                return false;

            return item.ItemSpells.Any(s =>
                s?.ActualSpell is { } actual &&
                (actual.Name == "Food" || actual.Name == "Refreshment"));
        }

        /// <summary>
        /// Checks if an item is a drink.
        /// HB 4.3.4 Consumable.smethod_3: spell name "Drink", "Starfire Espresso" or "Refreshment".
        /// "Refreshment" covers WotLK Mage conjured water; "Starfire Espresso" is a buff food.
        /// </summary>
        private static bool IsDrink(WoWItem item)
        {
            if (item == null || !item.IsValid || item.StackCount == 0 || item.ItemInfo == null
                || (int)item.ItemInfo.ItemClass != (int)WoWItemClass.Consumable)
                return false;

            return item.ItemSpells.Any(s =>
                s?.ActualSpell is { } actual &&
                (actual.Name == "Drink" ||
                 actual.Name == "Starfire Espresso" ||
                 actual.Name == "Refreshment"));
        }

        /// <summary>
        /// Checks if a spell is basic food or drink (no special effects).
        /// </summary>
        private static bool IsBasicFoodOrDrink(WoWItem.WoWItemSpell spell)
        {
            // Unhydrated secondary metadata cannot prove that an item has no
            // specialty effects. Skip this observation and retry fresh next time.
            var actual = spell?.ActualSpell;
            return actual != null && (actual.Name == "Food" || actual.Name == "Drink" || actual.Name == "Refreshment");
        }
    }
}
