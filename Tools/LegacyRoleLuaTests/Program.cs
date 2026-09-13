using Styx.Helpers;
using Styx.WoWInternals;

int cases = 0;
void Check(bool condition, string name)
{
    cases++;
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS role adapter: " + name);
}
foreach (var token in new[] { "player", "party1", "party4", "raid1", "raid40" })
{
    Lua.Queries.Clear();
    Lua.Result = "HEALER";
    string role = LegacyGroupRoles.GetAssignedRole(token);
    Check(role == "HEALER" && Lua.Queries.Count == 1 && Lua.Queries[0].Contains("UnitGroupRolesAssigned('" + token + "')"), "one query for " + token);
}
foreach (var token in new string?[] { null, "", "party", "raid", "party0", "party5", "raid0", "raid41", "raid01", "Party1", "party-1", "raid2147483648", "party1'); error('bad') --", "player\n", "playerName" })
{
    Lua.Queries.Clear();
    Check(LegacyGroupRoles.GetAssignedRole(token!) == "NONE" && Lua.Queries.Count == 0, "no query for invalid token " + (token ?? "<null>"));
}
foreach (var invalid in new string?[] { null, "", "true", "false", "UNKNOWN" })
{
    Lua.Result = invalid;
    Check(LegacyGroupRoles.GetAssignedRole("player") == "NONE", "unexpected response stays unknown " + (invalid ?? "<null>"));
}
Lua.Error = new ThreadInterruptedException("cancelled");
try { LegacyGroupRoles.GetAssignedRole("player"); throw new Exception("Interruption was swallowed"); }
catch (ThreadInterruptedException) { Check(true, "cancellation propagates"); }
finally { Lua.Error = null; }
Console.WriteLine($"Role adapter checks: {cases}/{cases}; actual helper, controlled Lua boundary; no client attached.");

namespace Styx.WoWInternals
{
    public static class Lua
    {
        internal static readonly List<string> Queries = new();
        internal static string? Result;
        internal static Exception? Error;
        public static T GetReturnVal<T>(string query, int index)
        {
            Queries.Add(query);
            if (Error != null) throw Error;
            return (T)(object)Result!;
        }
    }
}
