using System.Collections.Generic;
using Styx.Logic.POI;

namespace Styx.Logic.Profiles
{
    /// <summary>Failed service NPCs stay excluded for this process, across profile rebuilds.</summary>
    public static class VendorSafetyPolicy
    {
        private static readonly HashSet<int> RejectedEntries = new();

        public static bool Reject(int entry) => entry > 0 && RejectedEntries.Add(entry);
        public static bool IsRejected(int entry) => RejectedEntries.Contains(entry);

        public static bool IsService(PoiType type) => type == PoiType.Sell ||
            type == PoiType.Repair || type == PoiType.Buy || type == PoiType.Train;

        public static bool IsInvalidServiceNpc(PoiType type, bool dead, bool hostile,
            bool merchant, bool repairMerchant, bool trainer)
        {
            if (!IsService(type)) return false;
            if (dead || hostile) return true;
            return type switch
            {
                PoiType.Repair => !repairMerchant,
                PoiType.Train => !trainer,
                _ => !merchant
            };
        }
    }
}
