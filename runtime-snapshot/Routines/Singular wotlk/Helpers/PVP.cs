using System.Linq;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;

namespace Singular.Helpers
{
    internal static class PVP
    {
        public static bool IsCrowdControlled(WoWUnit unit)
        {
            // Resolve mechanics only inside the declared harmful mask. Missing
            // harmful metadata still throws UNKNOWN; helpful markers do not.
            return unit.GetRawAuras().Any(a => a.IsHarmful &&
                (a.Spell.Mechanic == WoWSpellMechanic.Shackled ||
                a.Spell.Mechanic == WoWSpellMechanic.Polymorphed ||
                a.Spell.Mechanic == WoWSpellMechanic.Horrified ||
                a.Spell.Mechanic == WoWSpellMechanic.Rooted ||
                a.Spell.Mechanic == WoWSpellMechanic.Frozen ||
                a.Spell.Mechanic == WoWSpellMechanic.Stunned ||
                a.Spell.Mechanic == WoWSpellMechanic.Fleeing ||
                a.Spell.Mechanic == WoWSpellMechanic.Banished ||
                a.Spell.Mechanic == WoWSpellMechanic.Sapped));
        }

        public static bool IsStunned(this WoWUnit unit)
        {
            return unit.HasAuraWithMechanic(WoWSpellMechanic.Stunned, WoWSpellMechanic.Incapacitated);
        }

        public static bool IsRooted(this WoWUnit unit)
        {
            return unit.HasAuraWithMechanic(WoWSpellMechanic.Rooted, WoWSpellMechanic.Shackled);
        }

        public static bool IsSilenced(WoWUnit unit)
        {
            return unit.GetRawAuras().Any(a => a.IsHarmful &&
                (a.Spell.Mechanic == WoWSpellMechanic.Interrupted || 
                a.Spell.Mechanic == WoWSpellMechanic.Silenced));
        }
    }
}
