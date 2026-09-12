using System;
using System.Collections.Generic;

namespace Singular.Helpers
{
    public static class DungeonEngagementPolicy
    {
        private static readonly Dictionary<string, float> AreaEffectRadii =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                { "Consecration", 8f },
                { "Divine Storm", 8f },
                { "Holy Wrath", 10f },
                { "Thunder Clap", 8f },
                { "Whirlwind", 8f },
                { "Arcane Explosion", 10f },
                { "Fan of Knives", 10f },
                { "Swipe (Cat)", 8f },
                { "Swipe (Bear)", 8f },
                { "Howling Blast", 10f },
                { "Blood Boil", 10f },
                { "Pestilence", 10f },
                { "Death and Decay", 10f },
                { "Rain of Fire", 10f },
                { "Volley", 8f },
                { "Blizzard", 10f },
                { "Seed of Corruption", 15f },
                { "Chain Lightning", 12f },
                { "Multi-Shot", 10f },
                { "Starfall", 40f },
                { "Hellfire", 10f },
                { "Bladestorm", 8f }
            };

        public static bool IsRestricted(string currentBotName, bool isDungeon)
        {
            return isDungeon && string.Equals(currentBotName, "Combat Bot", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsEngaged(
            bool restricted,
            bool hasAggro,
            bool hasPetAggro,
            bool targetsMeOrPet,
            bool targetsAnyMinion,
            bool targetsPartyMember,
            bool targetsRaidMember,
            bool taggedByMe,
            bool isAssistTarget)
        {
            return !restricted || hasAggro || hasPetAggro || targetsMeOrPet || targetsAnyMinion ||
                   targetsPartyMember || targetsRaidMember || taggedByMe || isAssistTarget;
        }

        public static bool ShouldAllowAreaEffect(bool restricted, bool hasUnengagedEnemyInArea)
        {
            return !restricted || !hasUnengagedEnemyInArea;
        }

        public static float GetFallbackRange(bool restricted)
        {
            return restricted ? 40f : 70f;
        }

        public static bool TryGetAreaEffectRadius(string spellName, out float radius)
        {
            if (spellName == null)
            {
                radius = 0f;
                return false;
            }

            return AreaEffectRadii.TryGetValue(spellName, out radius);
        }
    }
}
