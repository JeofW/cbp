using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Talented
{
    internal sealed class TalentObservation
    {
        public int Tab, Index, Tier, Rank, Maximum;
        public string Name;
        public bool MeetsPrerequisite;
    }

    internal static class TalentAllocationPolicy
    {
        // Original TalentFrameBase.lua checks the live name/rank/prerequisite
        // observation and five spent points per tier. Exported indices alone
        // are not identity and cannot authorize another expansion's talent.
        internal static bool TrySelect(TalentTree build, IList<TalentObservation> observed,
            out TalentObservation next, out string reason)
        {
            next = null;
            reason = "";
            if (build?.TalentPlacements == null || build.TalentPlacements.Count == 0 || observed == null || observed.Count == 0)
            { reason = "Talent template or current talent observation is incomplete."; return false; }
            var resolved = new List<(TalentPlacement Wanted, TalentObservation Actual)>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            int total = 0;
            foreach (var wanted in build.TalentPlacements)
            {
                if (wanted == null || wanted.Tab < 1 || wanted.Tab > 3 || string.IsNullOrWhiteSpace(wanted.Name)
                    || wanted.Name.Any(char.IsControl) || wanted.Count <= 0
                    || !identities.Add(wanted.Tab + ":" + wanted.Name))
                { reason = "Talent template has an invalid or ambiguous identity."; return false; }
                var matches = observed.Where(value => value.Tab == wanted.Tab
                    && string.Equals(value.Name, wanted.Name, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1 || wanted.Count > matches[0].Maximum)
                { reason = "Template talent does not match the current original-client tree: " + wanted.Name + "."; return false; }
                total += wanted.Count;
                if (total > 71) { reason = "Talent template exceeds the original level-80 allocation."; return false; }
                resolved.Add((wanted, matches[0]));
            }
            foreach (var value in resolved)
            {
                var actual = value.Actual;
                if (actual.Rank >= value.Wanted.Count || !actual.MeetsPrerequisite
                    || observed.Where(row => row.Tab == actual.Tab).Sum(row => row.Rank) < (actual.Tier - 1) * 5)
                    continue;
                next = actual;
                return true;
            }
            reason = "No unlearned template point currently meets its live tier and prerequisite requirements.";
            return false;
        }

        internal static string BuildRequest(TalentObservation point, int group, string playerGuid)
        {
            if (point == null || point.Tab < 1 || point.Tab > 3 || point.Index < 1 || group < 1 || group > 2
                || playerGuid == null || playerGuid.Length != 18 || !playerGuid.StartsWith("0x", StringComparison.Ordinal)
                || !playerGuid.Substring(2).All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(point.Name)
                || point.Name.Any(char.IsControl))
                throw new ArgumentException("A complete original-client talent identity is required.");
            string name = point.Name.Replace("\\", "\\\\").Replace("'", "\\'");
            // All dispatch fences and the original UI's LearnTalent request run
            // in one Lua evaluation. Never apply or clear somebody else's preview.
            return string.Format(CultureInfo.InvariantCulture,
                "local g=GetActiveTalentGroup(false,false); " +
                "if g~={0} or UnitGUID('player')~='{1}' then return 'stale' end; " +
                "if GetGroupPreviewTalentPointsSpent(false,g)~=0 then return 'preview' end; " +
                "local n,_,t,c,r,m,exceptional,p=GetTalentInfo({2},{3},false,false,g); " +
                "if n~='{4}' or t~={5} or r~={6} or m~={7} then return 'stale' end; " +
                "if not p or GetUnspentTalentPoints(false,false,g)<1 then return 'unavailable' end; " +
                "LearnTalent({2},{3},false,g); return 'submitted'",
                group, playerGuid, point.Tab, point.Index, name, point.Tier, point.Rank, point.Maximum);
        }
    }
}
