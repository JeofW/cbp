using System;
using System.Globalization;
using Styx.WoWInternals;

namespace Styx.Helpers
{
    /// <summary>Normalizes original 3.3.5a role flags without changing existing role APIs.</summary>
    public static class LegacyGroupRoles
    {
        // Original FrameXML consumes three booleans, not a retail/Classic role string.
        // Keep this literal as the single query owner; Lua 5.1 regression tests execute it.
        internal const string QueryTemplate = @"if type(UnitExists) ~= 'function' or not UnitExists('{0}') or type(UnitGroupRolesAssigned) ~= 'function' then return 'NONE' end
local isTank, isHealer, isDamage = UnitGroupRolesAssigned('{0}')
if isTank == true then return 'TANK' elseif isHealer == true then return 'HEALER' elseif isDamage == true then return 'DAMAGER' end
return 'NONE'";

        public static string GetAssignedRole(string unitId)
        {
            // Never interpolate a player name, incomplete roster token or arbitrary Lua.
            if (!IsRosterToken(unitId)) return "NONE";
            string role = Lua.GetReturnVal<string>(
                string.Format(CultureInfo.InvariantCulture, QueryTemplate, unitId), 0);
            return role == "TANK" || role == "HEALER" || role == "DAMAGER" ? role : "NONE";
        }

        private static bool IsRosterToken(string unitId)
        {
            if (unitId == "player") return true;
            if (unitId == null) return false;
            string prefix;
            int maximum;
            if (unitId.StartsWith("party", StringComparison.Ordinal)) { prefix = "party"; maximum = 4; }
            else if (unitId.StartsWith("raid", StringComparison.Ordinal)) { prefix = "raid"; maximum = 40; }
            else return false;
            return int.TryParse(unitId.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index >= 1 && index <= maximum
                && unitId == prefix + index.ToString(CultureInfo.InvariantCulture);
        }
    }
}
