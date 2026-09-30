using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Plugins.PluginClass;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Talented.Forms;

namespace Talented
{
    public class Talented : HBPlugin
    {
        private TalentTree _talentBuild;
        private bool _initialized, _processing, _enabled = true;
        private DateTime _nextAllocationUtc, _pendingUntilUtc;
        private TalentObservation _pending;
        private ulong _pendingGuid;
        private int _pendingGroup;
        private string _lastDiagnostic;

        private static void Log(string text, params object[] args) =>
            Logging.Write(Color.YellowGreen, "[Talented] " + text, args);

        private void Explain(string reason)
        {
            if (string.Equals(reason, _lastDiagnostic, StringComparison.Ordinal)) return;
            _lastDiagnostic = reason;
            Log(reason);
        }

        // Retained API: original 3.3.5a has no Cataclysm primary-tree selection.
        public static void SelectMajorTalentTree(int index) { }

        private static bool TryObserveTalents(int group, out List<TalentObservation> observed)
        {
            observed = new List<TalentObservation>();
            for (int tab = 1; tab <= 3; tab++)
            {
                int count = Lua.GetReturnVal<int>("return GetNumTalents(" + tab + ",false,false)", 0);
                if (count < 1 || count > 100) return false;
                for (int index = 1; index <= count; index++)
                {
                    var values = Lua.GetReturnValues("return GetTalentInfo(" + tab + "," + index + ",false,false," + group + ")");
                    if (values == null || values.Count < 8 || string.IsNullOrWhiteSpace(values[0]) || values[0] == "nil"
                        || !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier)
                        || !int.TryParse(values[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rank)
                        || !int.TryParse(values[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maximum)
                        || tier < 1 || tier > 11 || maximum < 1 || maximum > 5 || rank < 0 || rank > maximum)
                        return false;
                    observed.Add(new TalentObservation
                    {
                        Tab = tab, Index = index, Name = values[0], Tier = tier, Rank = rank, Maximum = maximum,
                        MeetsPrerequisite = string.Equals(values[7], "true", StringComparison.OrdinalIgnoreCase) || values[7] == "1"
                    });
                }
            }
            return true;
        }

        private void HandleTalentPointsChanged(object sender, LuaEventArgs args)
        {
            if (_processing || DateTime.UtcNow < _nextAllocationUtc) return;
            var actor = StyxWoW.Me;
            var memory = ObjectManager.Wow;
            var build = _talentBuild;
            ulong guid = actor?.Guid ?? 0;
            uint map = actor?.MapId ?? 0;
            bool Current() => _enabled && TreeRoot.IsRunning && StyxWoW.IsInGame && guid != 0
                && ReferenceEquals(StyxWoW.Me, actor) && actor.Guid == guid && actor.MapId == map
                && actor.IsValid && actor.IsAlive && !actor.Combat && ReferenceEquals(ObjectManager.Wow, memory)
                && ReferenceEquals(_talentBuild, build) && build != null && actor.Class == build.Class
                && TalentedSettings.Instance.ChoosenTalentBuildName == build.BuildName;
            if (!Current()) return;
            _processing = true;
            _nextAllocationUtc = DateTime.UtcNow.AddSeconds(2);
            try
            {
                using (new FrameLock())
                {
                    int group = Lua.GetReturnVal<int>("return GetActiveTalentGroup(false,false)", 0);
                    if (!Current() || group < 1 || group > 2) return;
                    int available = Lua.GetReturnVal<int>("return GetUnspentTalentPoints(false,false," + group + ")", 0);
                    if (!Current() || available <= 0) return;
                    if (Lua.GetReturnVal<int>("return GetGroupPreviewTalentPointsSpent(false," + group + ")", 0) != 0)
                    { Explain("A talent preview is already present; leaving it under its current owner's control."); return; }
                    if (!TryObserveTalents(group, out var observed) || !Current()
                        || Lua.GetReturnVal<int>("return GetActiveTalentGroup(false,false)", 0) != group || !Current())
                    { Explain("Talent observations changed or are incomplete; retrying after a fresh observation."); return; }

                    if (_pending != null && _pendingGuid == guid && _pendingGroup == group)
                    {
                        var seen = observed.FirstOrDefault(row => row.Tab == _pending.Tab && row.Index == _pending.Index
                            && row.Name == _pending.Name);
                        if (seen == null || seen.Rank <= _pending.Rank)
                        {
                            if (DateTime.UtcNow < _pendingUntilUtc) return;
                            _pending = null;
                            _nextAllocationUtc = DateTime.UtcNow.AddSeconds(10);
                            Explain("The submitted talent point has not been observed as learned; allocation is deferred.");
                            return;
                        }
                    }
                    _pending = null;
                    if (!TalentAllocationPolicy.TrySelect(build, observed, out var point, out string reason))
                    { Explain(reason); return; }
                    string playerGuid = Lua.GetReturnVal<string>("return UnitGUID('player')", 0);
                    if (!Current() || playerGuid == null || playerGuid.Length != 18
                        || !ulong.TryParse(playerGuid.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed)
                        || parsed != guid) return;
                    string request = TalentAllocationPolicy.BuildRequest(point, group, playerGuid);
                    if (!Current()) return;
                    string result = Lua.GetReturnVal<string>(request, 0);
                    if (!Current()) return;
                    if (result != "submitted")
                    { Explain("Talent allocation was not admitted by the current client observation: " + (result ?? "unknown") + "."); return; }
                    _pending = point; _pendingGuid = guid; _pendingGroup = group;
                    _pendingUntilUtc = DateTime.UtcNow.AddSeconds(5);
                    Explain("Submitted one point for " + point.Name + "; awaiting learned rank " + (point.Rank + 1) + ".");
                }
            }
            catch (Exception error)
            { Explain("Talent observation/request unavailable: " + error.GetType().Name + ". A fresh observation is required."); }
            finally { _processing = false; }
        }

        public override void Pulse()
        {
            if (!_enabled || StyxWoW.Me == null || StyxWoW.Me.Level < 10 || !TreeRoot.IsRunning) return;
            if (DateTime.UtcNow < _nextAllocationUtc) return;
            var settings = TalentedSettings.Instance;
            if (_talentBuild == null || _talentBuild.BuildName != settings.ChoosenTalentBuildName || settings.FirstUseAfterChange)
            {
                _talentBuild = settings.ChoosenTalentBuild;
                _pending = null;
                settings.FirstUseAfterChange = false;
            }
            if (_talentBuild == null)
            { Explain("No talent template is selected."); _nextAllocationUtc = DateTime.UtcNow.AddSeconds(5); return; }
            if (_talentBuild.Class != StyxWoW.Me.Class)
            { Explain("Selected talent template belongs to another class."); _nextAllocationUtc = DateTime.UtcNow.AddSeconds(5); return; }
            if (!_initialized)
            {
                Lua.Events.AttachEvent("CHARACTER_POINTS_CHANGED", HandleTalentPointsChanged);
                _initialized = true;
            }
            HandleTalentPointsChanged(null, null);
        }

        public override void OnEnable() { _enabled = true; _pending = null; _nextAllocationUtc = DateTime.MinValue; }
        public override void OnDisable()
        {
            _enabled = false; _pending = null;
            if (_initialized) Lua.Events.DetachEvent("CHARACTER_POINTS_CHANGED", HandleTalentPointsChanged);
            _initialized = false;
        }
        public override void Dispose() => OnDisable();
        public override void OnButtonPress() => new FormConfig().ShowDialog();
        public override bool WantButton => true;
        public override string ButtonText => "Choose Talent Build";
        public override string Name => "Talented";
        public override string Author => "Apoc";
        public override Version Version => new Version(1, 0, 0, 2);
    }
}
