using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Styx.Logic.Inventory
{
    [Flags]
    public enum ConsumableKind
    {
        None = 0,
        Food = 1,
        Drink = 2
    }

    public sealed record ConsumableTooltipInfo(
        ConsumableKind Kind,
        int HealthRestored,
        int ManaRestored);

    public sealed record ConsumableCandidate(
        int MerchantIndex,
        uint ItemId,
        string Name,
        int RequiredLevel,
        int HealthRestored,
        int ManaRestored,
        ulong BuyPrice,
        ConsumableKind Kind = ConsumableKind.None);

    public static class ConsumableVendorPolicy
    {
        private static readonly Regex RestorationRegex = new(
            @"Restores\s+([\d,]+)\s+(health|mana)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ItemIdRegex = new(
            @"(?:^|\|H)item:(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static ConsumableTooltipInfo ParseTooltip(string tooltip)
        {
            ConsumableKind kind = ConsumableKind.None;
            int healthRestored = 0;
            int manaRestored = 0;

            foreach (Match match in RestorationRegex.Matches(tooltip ?? string.Empty))
            {
                if (!int.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out int restoration))
                {
                    continue;
                }

                if (match.Groups[2].Value.Equals("health", StringComparison.OrdinalIgnoreCase))
                {
                    kind |= ConsumableKind.Food;
                    healthRestored = Math.Max(healthRestored, restoration);
                }
                else
                {
                    kind |= ConsumableKind.Drink;
                    manaRestored = Math.Max(manaRestored, restoration);
                }
            }

            return new ConsumableTooltipInfo(kind, healthRestored, manaRestored);
        }

        public static uint ParseItemId(string itemLink)
        {
            Match match = ItemIdRegex.Match(itemLink ?? string.Empty);
            return match.Success && uint.TryParse(match.Groups[1].Value, out uint itemId)
                ? itemId
                : 0;
        }

        public static ConsumableCandidate? SelectBest(
            IEnumerable<ConsumableCandidate> candidates,
            ConsumableKind requestedKind,
            int playerLevel,
            int capacity)
        {
            if (requestedKind != ConsumableKind.Food && requestedKind != ConsumableKind.Drink)
                return null;

            var usable = (candidates ?? Enumerable.Empty<ConsumableCandidate>())
                .Where(candidate =>
                    (GetKind(candidate) & requestedKind) != 0 &&
                    candidate.RequiredLevel <= playerLevel)
                .ToList();

            if (usable.Count == 0)
                return null;

            Func<ConsumableCandidate, int> restoration = requestedKind == ConsumableKind.Food
                ? candidate => candidate.HealthRestored
                : candidate => candidate.ManaRestored;

            var measured = usable.Where(candidate => restoration(candidate) > 0).ToList();
            if (measured.Count == 0)
            {
                return usable
                    .OrderByDescending(candidate => candidate.RequiredLevel)
                    .ThenBy(candidate => candidate.BuyPrice)
                    .ThenBy(candidate => candidate.MerchantIndex)
                    .First();
            }

            var withinCapacity = measured
                .Where(candidate => restoration(candidate) <= capacity)
                .OrderByDescending(restoration)
                .ThenBy(candidate => candidate.BuyPrice)
                .ThenBy(candidate => candidate.MerchantIndex)
                .FirstOrDefault();

            if (withinCapacity != null)
                return withinCapacity;

            return measured
                .OrderBy(restoration)
                .ThenBy(candidate => candidate.BuyPrice)
                .ThenBy(candidate => candidate.MerchantIndex)
                .First();
        }

        public static bool ShouldProtectFromSale(ConsumableKind kind)
        {
            return (kind & (ConsumableKind.Food | ConsumableKind.Drink)) != 0;
        }

        private static ConsumableKind GetKind(ConsumableCandidate candidate)
        {
            if (candidate.Kind != ConsumableKind.None)
                return candidate.Kind;

            ConsumableKind kind = ConsumableKind.None;
            if (candidate.HealthRestored > 0)
                kind |= ConsumableKind.Food;
            if (candidate.ManaRestored > 0)
                kind |= ConsumableKind.Drink;
            return kind;
        }
    }
}
