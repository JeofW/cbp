using System;
using System.Collections.Generic;
using System.Linq;

namespace Styx.Logic.Inventory
{
    public static class TalentWeightSetPolicy
    {
        public static int? SelectSpecialization(IReadOnlyList<int> talentPoints)
        {
            if (talentPoints == null || talentPoints.Count == 0)
                return null;

            int maximum = talentPoints.Max();
            if (maximum <= 0)
                return null;

            return talentPoints.ToList().IndexOf(maximum) + 1;
        }
    }
}
