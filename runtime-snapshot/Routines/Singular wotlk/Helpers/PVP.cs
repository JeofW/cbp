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
            return unit.GetRawAuras().Where(a => a.IsHarmful)
                .Select(a => (a.Spell ?? throw new Styx.Helpers.ObservationUnavailableException(
                    "aura-mechanic", "A harmful aura's control metadata is unavailable.")).Mechanic)
                .Any(mechanic => mechanic == WoWSpellMechanic.Shackled ||
                    mechanic == WoWSpellMechanic.Polymorphed ||
                    mechanic == WoWSpellMechanic.Horrified ||
                    mechanic == WoWSpellMechanic.Rooted ||
                    mechanic == WoWSpellMechanic.Frozen ||
                    mechanic == WoWSpellMechanic.Stunned ||
                    mechanic == WoWSpellMechanic.Fleeing ||
                    mechanic == WoWSpellMechanic.Banished ||
                    mechanic == WoWSpellMechanic.Sapped);
        }

        public static bool IsStunned(this WoWUnit unit)
        {
            return unit.HasHarmfulAuraWithMechanic(WoWSpellMechanic.Stunned, WoWSpellMechanic.Incapacitated);
        }

        public static bool IsRooted(this WoWUnit unit)
        {
            return unit.HasHarmfulAuraWithMechanic(WoWSpellMechanic.Rooted, WoWSpellMechanic.Shackled);
        }

        public static bool IsSilenced(WoWUnit unit)
        {
            return unit.GetRawAuras().Where(a => a.IsHarmful)
                .Select(a => (a.Spell ?? throw new Styx.Helpers.ObservationUnavailableException(
                    "aura-mechanic", "A harmful aura's silence metadata is unavailable.")).Mechanic)
                .Any(mechanic => mechanic == WoWSpellMechanic.Interrupted || mechanic == WoWSpellMechanic.Silenced);
        }
    }
}
