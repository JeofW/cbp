using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Plugins.PluginClass;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace MobPackAvoidance
{
    public class MobPackAvoidance : HBPlugin
    {
        public override string Name { get { return "MobPackAvoidance"; } }
        public override string Author { get { return "scy"; } }
        public override Version Version { get { return new Version(1, 0, 0); } }
        public override bool WantButton { get { return true; } }
        public override string ButtonText { get { return "Settings"; } }

        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private const string SettingsFile = "MobPackAvoidance.config";

        private readonly object _settingsLock = new object();
        private bool _enabled;
        private int _scanRadius;
        private int _clusterRadius;
        private int _maxPackSize;
        private int _blackspotDuration;
        private float _blackspotRadius;
        private int _levelAdvantageRequired;

        public bool Enabled
        {
            get { lock (_settingsLock) return _enabled; }
            private set { lock (_settingsLock) _enabled = value; }
        }

        public int ScanRadius
        {
            get { lock (_settingsLock) return _scanRadius; }
            private set { lock (_settingsLock) _scanRadius = value; }
        }

        public int ClusterRadius
        {
            get { lock (_settingsLock) return _clusterRadius; }
            private set { lock (_settingsLock) _clusterRadius = value; }
        }

        public int MaxPackSize
        {
            get { lock (_settingsLock) return _maxPackSize; }
            private set { lock (_settingsLock) _maxPackSize = value; }
        }

        public int BlackspotDuration
        {
            get { lock (_settingsLock) return _blackspotDuration; }
            private set { lock (_settingsLock) _blackspotDuration = value; }
        }

        public float BlackspotRadius
        {
            get { lock (_settingsLock) return _blackspotRadius; }
            private set { lock (_settingsLock) _blackspotRadius = value; }
        }

        public int LevelAdvantageRequired
        {
            get { lock (_settingsLock) return _levelAdvantageRequired; }
            private set { lock (_settingsLock) _levelAdvantageRequired = value; }
        }

        private bool _avoidHighLevelElites;
        private int _eliteLevelDifference;

        public bool AvoidHighLevelElites
        {
            get { lock (_settingsLock) return _avoidHighLevelElites; }
            private set { lock (_settingsLock) _avoidHighLevelElites = value; }
        }

        public int EliteLevelDifference
        {
            get { lock (_settingsLock) return _eliteLevelDifference; }
            private set { lock (_settingsLock) _eliteLevelDifference = value; }
        }

        private readonly Dictionary<WoWPoint, DateTime> _activeBlackspots = new Dictionary<WoWPoint, DateTime>();
        private readonly Dictionary<ulong, DateTime> _blacklistedGuids = new Dictionary<ulong, DateTime>();
        private DateTime _lastPulse;
        private readonly TimeSpan _pulseInterval = TimeSpan.FromMilliseconds(1000);

        public MobPackAvoidance()
        {
            _enabled = true;
            _scanRadius = 200;
            _clusterRadius = 14;
            _maxPackSize = 3;
            _blackspotDuration = 60000;
            _blackspotRadius = 40f;
            _levelAdvantageRequired = 3;
            _avoidHighLevelElites = true;
            _eliteLevelDifference = 2;
            LoadSettings();
        }

        public override void OnButtonPress()
        {
            using (var form = new MobPackAvoidanceSettingsForm(this))
            {
                form.ShowDialog();
            }
        }

        public override void Pulse()
        {
            if (!Enabled || Me == null || !StyxWoW.IsInGame || Me.IsDead || Me.IsGhost)
                return;

            if (DateTime.Now - _lastPulse < _pulseInterval)
                return;
            _lastPulse = DateTime.Now;

            try
            {
                PulseCleanup();
                PulseScan();
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[MobPackAvoidance] ERROR in Pulse: {0}", ex.Message);
            }
        }

        private void PulseCleanup()
        {
            DateTime now = DateTime.Now;

            List<WoWPoint> expired = new List<WoWPoint>();
            foreach (var kvp in _activeBlackspots)
            {
                if (now > kvp.Value)
                    expired.Add(kvp.Key);
            }

            foreach (var pt in expired)
            {
                try
                {
                    foreach (var bs in BlackspotManager.Blackspots)
                    {
                        if (bs.Location.Distance(pt) < 1f)
                        {
                            BlackspotManager.RemoveBlackspot(bs);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.WriteDebug("[MobPackAvoidance] Cleanup error at {0}: {1}", pt, ex.Message);
                }
                _activeBlackspots.Remove(pt);
            }

            List<ulong> expiredGuids = new List<ulong>();
            foreach (var kvp in _blacklistedGuids)
            {
                if (now > kvp.Value)
                    expiredGuids.Add(kvp.Key);
            }
            foreach (var guid in expiredGuids)
                _blacklistedGuids.Remove(guid);
        }

        private void PulseScan()
        {
            List<WoWUnit> allUnits = new List<WoWUnit>();
            foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                allUnits.Add(u);

            List<WoWUnit> aggressiveMobs = new List<WoWUnit>();
            foreach (WoWUnit u in allUnits)
            {
                if (!u.IsAlive) continue;
                if (!u.Attackable) continue;
                if (u.IsCritter) continue;
                if (u.IsPet) continue;
                if (u.CreatureType == WoWCreatureType.NonCombatPet) continue;
                if (u.Entry <= 0) continue;

                float dist = u.Location.Distance(Me.Location);
                if (dist > ScanRadius) continue;
                if (!u.IsHostile) continue;
                aggressiveMobs.Add(u);
            }

            var clusters = FindClusters(aggressiveMobs, ClusterRadius);

            for (int ci = 0; ci < clusters.Count; ci++)
            {
                var cluster = clusters[ci];

                bool avoid = false;
                if (cluster.Count >= MaxPackSize)
                {
                    avoid = true;
                }
                else if (cluster.Count >= 2)
                {
                    int maxMobLevel = cluster.Max(m => m.Level);
                    if (Me.Level < maxMobLevel + LevelAdvantageRequired)
                    {
                        avoid = true;
                    }
                }
                else if (cluster.Count == 1 && AvoidHighLevelElites)
                {
                    var mob = cluster[0];
                    if (mob.Elite && mob.Level >= Me.Level + EliteLevelDifference)
                    {
                        avoid = true;
                    }
                }

                if (!avoid)
                    continue;

                AbortUnsafeFleeingChase(cluster);

                TimeSpan blacklistTime = TimeSpan.FromMilliseconds(BlackspotDuration);
                DateTime expiresAt = DateTime.Now.Add(blacklistTime);
                foreach (var mob in cluster)
                {
                    if (_blacklistedGuids.ContainsKey(mob.Guid))
                        continue;

                    _blacklistedGuids[mob.Guid] = expiresAt;
                    Blacklist.Add(mob.Guid, blacklistTime);
                }

                float sumX = 0, sumY = 0, sumZ = 0;
                for (int i = 0; i < cluster.Count; i++)
                {
                    sumX += cluster[i].Location.X;
                    sumY += cluster[i].Location.Y;
                    sumZ += cluster[i].Location.Z;
                }
                WoWPoint center = new WoWPoint(sumX / cluster.Count, sumY / cluster.Count, sumZ / cluster.Count);

                float nearestBlackspotDistance = _activeBlackspots.Count == 0
                    ? float.MaxValue
                    : (float)_activeBlackspots.Keys.Min(point => point.Distance(center));
                if (!GrindSafetyPolicy.ShouldCreateAvoidanceBlackspot(nearestBlackspotDistance, BlackspotRadius))
                    continue;

                var names = cluster.Select(m => m.Name).Distinct();
                var nameStr = string.Join(", ", names);
                Logging.Write(Color.Yellow, "[MobPackAvoidance] Blackspoted a cluster of {0} mobs ({1})",
                    cluster.Count, nameStr);

                try
                {
                    BlackspotManager.AddBlackspot(center, BlackspotRadius, 60f,
                        string.Format("MobPack-{0}", cluster.First().Entry));
                    _activeBlackspots[center] = DateTime.Now.AddMilliseconds(BlackspotDuration);

                    BlackspotManager.EnsureBlackspotsMarked();
                    Navigator.Clear();
                }
                catch (Exception ex)
                {
                    Logging.WriteDebug("[MobPackAvoidance] Failed to process pack: {0}", ex.Message);
                }
            }
        }

        private void AbortUnsafeFleeingChase(List<WoWUnit> avoidedCluster)
        {
            WoWUnit target = Me.CurrentTarget;
            if (target == null || !avoidedCluster.Any(mob => mob.Guid == target.Guid))
                return;

            if (!GrindSafetyPolicy.ShouldAbortFleeingChase(
                    target.Fleeing,
                    target.TaggedByMe,
                    target.IsTargetingMeOrPet,
                    (float)target.Distance,
                    true))
            {
                return;
            }

            Logging.Write(Color.Orange,
                "[MobPackAvoidance] Stopping unsafe chase of fleeing {0}; waiting for combat to return or end.",
                target.Name);
            Navigator.Clear();
            WoWMovement.MoveStop();

            if (BotPoi.Current.Type == PoiType.Kill && BotPoi.Current.Guid == target.Guid)
                BotPoi.Clear("Fleeing target entered an avoided mob pack");

            Me.ClearTarget();
        }

        private static List<List<WoWUnit>> FindClusters(List<WoWUnit> mobs, float clusterRadius)
        {
            var clusters = new List<List<WoWUnit>>();
            var visited = new HashSet<WoWUnit>();

            foreach (var mob in mobs)
            {
                if (visited.Contains(mob))
                    continue;

                var cluster = new List<WoWUnit>();
                var queue = new Queue<WoWUnit>();
                queue.Enqueue(mob);
                visited.Add(mob);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cluster.Add(current);

                    foreach (var neighbor in mobs)
                    {
                        if (visited.Contains(neighbor))
                            continue;
                        if (current.Location.Distance(neighbor.Location) <= clusterRadius)
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private void LoadSettings()
        {
            string path = Path.Combine(Environment.CurrentDirectory, SettingsFile);
            if (!File.Exists(path))
            {
                SaveSettings();
                return;
            }

            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.SelectSingleNode("//MobPackAvoidance");
                if (root == null) return;

                Enabled = ParseBool(root, "Enabled", Enabled);
                ScanRadius = ParseInt(root, "ScanRadius", ScanRadius);
                ClusterRadius = ParseInt(root, "ClusterRadius", ClusterRadius);
                MaxPackSize = ParseInt(root, "MaxPackSize", MaxPackSize);
                BlackspotDuration = ParseInt(root, "BlackspotDuration", BlackspotDuration);
                BlackspotRadius = ParseFloat(root, "BlackspotRadius", BlackspotRadius);
                LevelAdvantageRequired = ParseInt(root, "LevelAdvantageRequired", LevelAdvantageRequired);
                AvoidHighLevelElites = ParseBool(root, "AvoidHighLevelElites", AvoidHighLevelElites);
                EliteLevelDifference = ParseInt(root, "EliteLevelDifference", EliteLevelDifference);
            }
            catch (Exception)
            {
                // Silent load failure
            }
        }

        private void SaveSettings()
        {
            try
            {
                var doc = new XmlDocument();
                var root = doc.CreateElement("MobPackAvoidance");
                doc.AppendChild(root);

                WriteElement(doc, root, "Enabled", Enabled.ToString());
                WriteElement(doc, root, "ScanRadius", ScanRadius.ToString());
                WriteElement(doc, root, "ClusterRadius", ClusterRadius.ToString());
                WriteElement(doc, root, "MaxPackSize", MaxPackSize.ToString());
                WriteElement(doc, root, "BlackspotDuration", BlackspotDuration.ToString());
                WriteElement(doc, root, "BlackspotRadius", BlackspotRadius.ToString());
                WriteElement(doc, root, "LevelAdvantageRequired", LevelAdvantageRequired.ToString());
                WriteElement(doc, root, "AvoidHighLevelElites", AvoidHighLevelElites.ToString());
                WriteElement(doc, root, "EliteLevelDifference", EliteLevelDifference.ToString());

                string path = Path.Combine(Environment.CurrentDirectory, SettingsFile);
                doc.Save(path);
            }
            catch (Exception)
            {
                // Silent save failure
            }
        }

        public void UpdateSettings(bool enabled, int scanRadius, int clusterRadius, int maxPackSize, int blackspotDuration, float blackspotRadius, int levelAdvantageRequired, bool avoidHighLevelElites, int eliteLevelDifference)
        {
            Enabled = enabled;
            ScanRadius = scanRadius;
            ClusterRadius = clusterRadius;
            MaxPackSize = maxPackSize;
            BlackspotDuration = blackspotDuration;
            BlackspotRadius = blackspotRadius;
            LevelAdvantageRequired = levelAdvantageRequired;
            AvoidHighLevelElites = avoidHighLevelElites;
            EliteLevelDifference = eliteLevelDifference;
 
            SaveSettings();
        }

        private static void WriteElement(XmlDocument doc, XmlNode parent, string name, string value)
        {
            var elem = doc.CreateElement(name);
            elem.InnerText = value;
            parent.AppendChild(elem);
        }

        private static bool ParseBool(XmlNode parent, string name, bool defaultValue)
        {
            var node = parent.SelectSingleNode(name);
            if (node == null) return defaultValue;
            bool result;
            return bool.TryParse(node.InnerText, out result) ? result : defaultValue;
        }

        private static int ParseInt(XmlNode parent, string name, int defaultValue)
        {
            var node = parent.SelectSingleNode(name);
            if (node == null) return defaultValue;
            int result;
            return int.TryParse(node.InnerText, out result) ? result : defaultValue;
        }

        private static float ParseFloat(XmlNode parent, string name, float defaultValue)
        {
            var node = parent.SelectSingleNode(name);
            if (node == null) return defaultValue;
            float result;
            return float.TryParse(node.InnerText, out result) ? result : defaultValue;
        }
    }
}
