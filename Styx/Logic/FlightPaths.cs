// FlightPaths.cs - Ported from HB 4.3.4
// Flight path (taxi) management - learn, update, and use flight paths
// Fully compatible with WotLK - Outland and Northrend zones support flying

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Inventory.Frames.Taxi;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
    /// <summary>
    /// Reason for flight path action
    /// </summary>
    public enum FlightPathReason
    {
        None,
        Learn,
        Update,
        Use
    }

    /// <summary>
    /// FlightPaths - Manages taxi/flight path learning and usage
    /// Ported from HB 4.3.4
    /// </summary>
    public static class FlightPaths
    {
        // Blacklisted flight masters (faction-specific)
        private static readonly HashSet<uint> _hordeBlacklist = new HashSet<uint> { 12617U };
        private static readonly HashSet<uint> _allianceBlacklist = new HashSet<uint> { 12636U, 18788U };
        private static WaitTimer _checkTimer = new WaitTimer(TimeSpan.FromSeconds(15.0));
        private static object _flightIntentOwner = new object();

        /// <summary>
        /// Check if flight master is blacklisted for current faction
        /// </summary>
        private static bool IsBlacklisted(uint entry)
        {
            return StyxWoW.Me.IsHorde ? _hordeBlacklist.Contains(entry) : _allianceBlacklist.Contains(entry);
        }

        /// <summary>
        /// Get nearest valid flight master
        /// </summary>
        public static WoWUnit NearestFlightMerchant
        {
            get
            {
                // use cached units for flight master lookup
                WoWUnit flightMaster = ObjectManager.CachedUnits
                    .Where(u => u.IsFlightMaster && 
                                !Blacklist.Contains(u) && 
                                !u.IsHostile && 
                                !IsBlacklisted(u.Entry))
                    .OrderBy(u => u.Distance)
                    .FirstOrDefault();

                if (flightMaster != null && !Navigator.CanNavigateFully(StyxWoW.Me.Location, flightMaster.Location))
                {
                    Logging.Write("Blacklisting {0} for 5 minutes because we can't navigate to it.", flightMaster.Name);
                    Blacklist.Add(flightMaster, new TimeSpan(0, 5, 0));
                    return null;
                }

                return flightMaster;
            }
        }

        /// <summary>
        /// Whether we need to visit a flight path
        /// </summary>
        public static bool NeedFlightPath { get; set; }

        /// <summary>
        /// Current reason for flight path action
        /// </summary>
        public static FlightPathReason Reason { get; set; }

        /// <summary>
        /// Known flight nodes from XML
        /// </summary>
        public static List<XmlFlightNode> XmlNodes { get; set; }

        /// <summary>
        /// Destination flight node
        /// </summary>
        public static XmlFlightNode TakingPathTo { get; set; }

        /// <summary>
        /// Origin flight node
        /// </summary>
        public static XmlFlightNode TakingPathFrom { get; set; }

        /// <summary>
        /// Whether flight paths are enabled in settings
        /// </summary>
        public static bool CanTakeFlightPaths => CharacterSettings.Instance.UseFlightPaths;

        /// <summary>
        /// Whether we're at the start flight point
        /// </summary>
        public static bool IsAtStart => TakingPathFrom != null && 
            StyxWoW.Me.Location.DistanceSqr(TakingPathFrom.Location) < 25.0;

        /// <summary>
        /// Whether we're at the end flight point
        /// </summary>
        public static bool IsAtEnd => TakingPathTo != null && 
            StyxWoW.Me.Location.DistanceSqr(TakingPathTo.Location) < 25.0;

        /// <summary>
        /// Path to flight paths XML file
        /// </summary>
        private static string XmlPath => $"{Logging.ApplicationPath}\\Settings\\FlightPaths_{StyxWoW.Me.Name}.xml";

        /// <summary>
        /// Reset flight path state
        /// </summary>
        public static void Reset()
        {
            _flightIntentOwner = new object();
            TakingPathFrom = null;
            TakingPathTo = null;
            Reason = FlightPathReason.None;
            Navigator.Clear();
            if (BotPoi.Current.Type == PoiType.Fly)
                BotPoi.Clear("FlightPaths.Reset()");
        }

        /// <summary>
        /// Set flight path usage from point A to B
        /// </summary>
        public static bool SetFlightPathUsage(WoWPoint from, WoWPoint to, out WoWPoint startFp, out WoWPoint endFp)
        {
            startFp = endFp = WoWPoint.Empty;
            var observation = new FlightPublicationObservation();
            if (!observation.IsCurrent())
                return false;

            FindClosestFlightNodes(from, to, out XmlFlightNode startNode, out XmlFlightNode endNode);
            if (startNode == null || endNode == null)
                return false;

            WoWUnit merchant = null;
            FlightPathReason nextReason = FlightPathReason.Use;
            if (startNode.MasterEntry == 0U)
            {
                merchant = NearestFlightMerchant;
                // Feasibility is a callback boundary, not a lease on the player,
                // settings, cached network, provider or existing travel intent.
                if (!observation.IsCurrent())
                    return false;
                if (merchant != null)
                {
                    if (!merchant.IsValid)
                        return false;
                    nextReason = FlightPathReason.Update;
                }
                else
                {
                    FindFlightNodes(from, to, out startNode, out endNode, true);
                    if (startNode == null || endNode == null)
                        return false;
                }
            }

            var start = startNode.Location;
            var end = endNode.Location;
            // Reuse the admitted merchant. SetPoi would query the provider again
            // and could turn one admission into three unrelated observations.
            var poi = merchant != null
                ? new BotPoi(merchant, PoiType.Fly)
                : new BotPoi(start, PoiType.Fly) { Entry = startNode.MasterEntry };
            if (poi.Type != PoiType.Fly || !observation.IsCurrent() ||
                (merchant != null && !merchant.IsValid))
                return false;

            var publication = new object();
            _flightIntentOwner = publication;
            TakingPathTo = endNode;
            TakingPathFrom = startNode;
            Reason = nextReason;
            BotPoi.Current = poi;
            // The POI setter logs synchronously. A subscriber can Reset, publish
            // another flight, or install service work. Never report the old call
            // as successful or overwrite that replacement after the callback.
            if (!ReferenceEquals(_flightIntentOwner, publication) ||
                !ReferenceEquals(TakingPathFrom, startNode) ||
                !ReferenceEquals(TakingPathTo, endNode) || Reason != nextReason ||
                !ReferenceEquals(BotPoi.Current, poi) || !observation.InputsCurrent())
                return false;

            startFp = start;
            endFp = end;
            return true;
        }

        // Observed managed ownership only: this is not a native frame/session
        // atomicity claim. Snapshot values as well as references because cached
        // nodes and their connection sets can be edited in place by callbacks.
        private sealed class FlightPublicationObservation
        {
            private readonly LocalPlayer player = StyxWoW.Me;
            private readonly CharacterSettings settings = CharacterSettings.Instance;
            private readonly NavigationProvider provider = Navigator.NavigationProvider;
            private readonly List<XmlFlightNode> network = XmlNodes;
            private readonly object intentOwner = _flightIntentOwner;
            private readonly XmlFlightNode previousFrom = TakingPathFrom, previousTo = TakingPathTo;
            private readonly FlightPathReason previousReason = Reason;
            private readonly bool previousNeed = NeedFlightPath;
            private readonly BotPoi previousPoi = BotPoi.Current;
            private readonly uint playerAddress;
            private readonly ulong playerGuid;
            private readonly uint map;
            private readonly FlightNodeObservation[] nodes;

            internal FlightPublicationObservation()
            {
                playerAddress = player?.BaseAddress ?? 0;
                playerGuid = player?.Guid ?? 0;
                map = player?.MapId ?? 0;
                nodes = network?.Select(node => new FlightNodeObservation(node)).ToArray();
            }

            internal bool IsCurrent() => InputsCurrent()
                && ReferenceEquals(_flightIntentOwner, intentOwner)
                && ReferenceEquals(TakingPathFrom, previousFrom)
                && ReferenceEquals(TakingPathTo, previousTo) && Reason == previousReason
                && NeedFlightPath == previousNeed && ReferenceEquals(BotPoi.Current, previousPoi);

            internal bool InputsCurrent()
            {
                if (player == null || !ReferenceEquals(StyxWoW.Me, player) ||
                    player.BaseAddress != playerAddress || player.Guid != playerGuid || player.MapId != map ||
                    settings == null || !ReferenceEquals(CharacterSettings.Instance, settings) || !settings.UseFlightPaths ||
                    !ReferenceEquals(Navigator.NavigationProvider, provider) ||
                    network == null || !ReferenceEquals(XmlNodes, network) || network.Count != nodes.Length)
                    return false;
                for (int i = 0; i < nodes.Length; i++)
                    if (!nodes[i].IsCurrent(network[i])) return false;
                return true;
            }
        }

        private sealed class FlightNodeObservation
        {
            private readonly XmlFlightNode node;
            private readonly string name;
            private readonly uint master, continent;
            private readonly int level;
            private readonly WoWPoint location;
            private readonly HashSet<string> connections;
            private readonly string[] destinations;

            internal FlightNodeObservation(XmlFlightNode node)
            {
                this.node = node;
                if (node == null) return;
                name = node.Name; master = node.MasterEntry; continent = node.Continent;
                level = node.UpdateLevel; location = node.Location;
                connections = node.Connections;
                destinations = connections?.ToArray();
            }

            internal bool IsCurrent(XmlFlightNode current) => ReferenceEquals(node, current)
                && (node == null || (node.Name == name && node.MasterEntry == master &&
                    node.Continent == continent && node.UpdateLevel == level &&
                    node.Location.X.Equals(location.X) && node.Location.Y.Equals(location.Y) && node.Location.Z.Equals(location.Z) &&
                    ReferenceEquals(node.Connections, connections) &&
                    (connections == null || connections.SetEquals(destinations))));
        }

        /// <summary>
        /// Returns whether the cached taxi network contains a usable connection toward a destination.
        /// This is a read-only route check for service-travel decisions.
        /// </summary>
        public static bool HasKnownConnection(WoWPoint from, WoWPoint to)
        {
            if (!CanTakeFlightPaths || StyxWoW.Me == null)
                return false;

            // Read-only queries require a known master on each candidate, not
            // just on the first connected marker returned by the search.
            FindFlightNodes(from, to, out XmlFlightNode startNode, out XmlFlightNode endNode, true);
            return startNode != null && endNode != null;
        }

        /// <summary>
        /// Take flight path if possible
        /// </summary>
        public static void TakeFlightPath()
        {
            if (!CanTakeFlightPaths)
                return;
            HandleTaxiMapOpened(null, null);
        }

        /// <summary>
        /// Get estimated flight time between two points
        /// </summary>
        public static TimeSpan GetFlightPathTime(WoWPoint from, WoWPoint to)
        {
            if (!IsFinitePoint(from) || !IsFinitePoint(to))
                return TimeSpan.MaxValue;
            // Retain the planar flight approximation; unknown evidence is not a free route.
            return GetBoundedTravelTime(from.Distance2D(to) * 1.2f / 19.6f);
        }

        /// <summary>
        /// Get total travel time including ground paths to/from flight points
        /// </summary>
        public static TimeSpan GetFullTravelTime(WoWPoint start, WoWPoint end, WoWPoint flightPathStart, WoWPoint flightPathEnd, float travelSpeed)
        {
            if (!IsValidTravelSpeed(travelSpeed))
                return TimeSpan.MaxValue;
            TimeSpan flightTime = GetFlightPathTime(flightPathStart, flightPathEnd);
            if (flightTime == TimeSpan.MaxValue)
                return TimeSpan.MaxValue;

            TimeSpan toStart = start.Distance(flightPathStart) < 5f
                ? TimeSpan.Zero
                : GetRunPathTime(Navigator.GeneratePath(start, flightPathStart), travelSpeed);

            TimeSpan fromEnd = flightPathEnd.Distance(end) < 5f
                ? TimeSpan.Zero
                : GetRunPathTime(Navigator.GeneratePath(flightPathEnd, end), travelSpeed);

            if (toStart == TimeSpan.MaxValue || fromEnd == TimeSpan.MaxValue)
                return TimeSpan.MaxValue;

            return flightTime + toStart + fromEnd;
        }

        /// <summary>
        /// Get time to run a path at given speed
        /// </summary>
        public static TimeSpan GetRunPathTime(IList<WoWPoint> path, float travelSpeed)
        {
            if (path == null || path.Count == 0 || !IsValidTravelSpeed(travelSpeed))
                return TimeSpan.MaxValue;
            for (int i = 0; i < path.Count; i++)
                if (!IsFinitePoint(path[i])) return TimeSpan.MaxValue;
            return GetBoundedTravelTime(GetPathLength(path) / travelSpeed);
        }

        private static bool IsValidTravelSpeed(float speed) =>
            speed > 0 && !float.IsNaN(speed) && !float.IsInfinity(speed);

        private static bool IsFinitePoint(WoWPoint point) =>
            !float.IsNaN(point.X) && !float.IsInfinity(point.X) &&
            !float.IsNaN(point.Y) && !float.IsInfinity(point.Y) &&
            !float.IsNaN(point.Z) && !float.IsInfinity(point.Z);

        private static TimeSpan GetBoundedTravelTime(float seconds)
        {
            // Preserve the existing whole-second/int estimator domain. Validate
            // before conversion: NaN, infinity and overflow can otherwise become
            // zero, a negative value or a saturated but falsely known duration.
            double rounded = Math.Ceiling(seconds);
            if (double.IsNaN(rounded) || double.IsInfinity(rounded) ||
                rounded < 0 || rounded > int.MaxValue)
                return TimeSpan.MaxValue;
            return new TimeSpan(0, 0, (int)rounded);
        }

        /// <summary>
        /// Calculate total path length
        /// </summary>
        private static float GetPathLength(IList<WoWPoint> path)
        {
            float length = 0.0f;
            for (int i = 0; i < path.Count - 1; ++i)
                length += path[i].Distance(path[i + 1]);
            return length;
        }

        /// <summary>
        /// Initialize flight paths system
        /// </summary>
        public static void Initialize()
        {
            Lua.Events.AttachEvent("TAXIMAP_OPENED", HandleTaxiMapOpened);
            BotEvents.OnBotStop += args => Reset();
            XmlNodes = new List<XmlFlightNode>();

            if (File.Exists(XmlPath))
            {
                try
                {
                    foreach (XElement element in XElement.Load(XmlPath).Elements("Node"))
                        XmlNodes.Add(new XmlFlightNode(element));
                }
                catch (Exception ex)
                {
                    Logging.WriteException(ex);
                }
            }
        }

        /// <summary>
        /// Set POI for flight path action
        /// </summary>
        public static void SetPoi(XmlFlightNode node = null)
        {
            switch (Reason)
            {
                case FlightPathReason.Learn:
                case FlightPathReason.Update:
                    if (NearestFlightMerchant != null)
                        BotPoi.Current = new BotPoi(NearestFlightMerchant, PoiType.Fly);
                    break;
                case FlightPathReason.Use:
                    if (node != null)
                    {
                        BotPoi.Current = new BotPoi(node.Location, PoiType.Fly)
                        {
                            Entry = node.MasterEntry
                        };
                    }
                    break;
            }
        }

        /// <summary>
        /// Check if we need to update nearby flight path info
        /// </summary>
        public static bool NeedNearbyUpdate()
        {
            if ((!CharacterSettings.Instance.UseFlightPaths && !CharacterSettings.Instance.LearnFlightPaths) ||
                StyxWoW.Me == null ||
                StyxWoW.Me.IsOnTransport ||
                StyxWoW.Me.MovementInfo.IsFlying ||
                StyxWoW.Me.MovementInfo.IsFalling ||
                StyxWoW.Me.MovementInfo.JumpingOrShortFalling)
                return false;

            if (!CharacterSettings.Instance.UseFlightPaths)
                return false;

            WoWUnit nearestFlightMerchant = NearestFlightMerchant;
            bool needsUpdate = nearestFlightMerchant != null && 
                               FindNodeByMasterEntry(nearestFlightMerchant.Entry, StyxWoW.Me.Level) == null;

            if (needsUpdate)
                Reason = FlightPathReason.Update;

            return needsUpdate;
        }

        /// <summary>
        /// Check if flight path would be faster than running
        /// </summary>
        public static bool ShouldTakeFlightpath(WoWPoint start, WoWPoint end, float travelSpeed)
        {
            if (!CharacterSettings.Instance.UseFlightPaths || Reason == FlightPathReason.Use || !_checkTimer.IsFinished)
                return false;

            _checkTimer.Reset();

            var runPath = Navigator.GeneratePath(start, end);
            TimeSpan runPathTime = GetRunPathTime(runPath, travelSpeed);

            FindClosestFlightNodes(start, end, out XmlFlightNode startNode, out XmlFlightNode endNode);

            if (startNode == null || endNode == null || 
                startNode.Location == WoWPoint.Empty || endNode.Location == WoWPoint.Empty ||
                startNode.Name == endNode.Name)
                return false;

            TimeSpan fullTravelTime = GetFullTravelTime(start, end, startNode.Location, endNode.Location, travelSpeed);

            if (fullTravelTime == TimeSpan.MaxValue)
                return false;
            if (runPathTime == TimeSpan.MaxValue)
                return true;

            int differenceSeconds = (int)Math.Abs((runPathTime - fullTravelTime).TotalSeconds);

            if (differenceSeconds <= 30)
                return false;

            Logging.WriteDebug("Flight time: {0}", fullTravelTime);
            Logging.WriteDebug("Run Time: {0}", runPathTime);
            Logging.WriteDebug("Difference: {0}s", differenceSeconds);

            return fullTravelTime < runPathTime;
        }

        /// <summary>
        /// Find closest flight nodes for start and end points.
        /// Prefers the nearest origin with a usable cached connection, then its
        /// connected node nearest to 'to'. Disconnected origins are skipped.
        /// Also guards against flight "going backwards" (start closer to dest than end).
        /// </summary>
        private static void FindClosestFlightNodes(WoWPoint from, WoWPoint to, out XmlFlightNode startNode, out XmlFlightNode endNode) =>
            FindFlightNodes(from, to, out startNode, out endNode, false);

        // Keep the original four-argument lookup contract, including reflection
        // callers. The known-master mode does not query merchants or mutate POI.
        private static void FindFlightNodes(WoWPoint from, WoWPoint to, out XmlFlightNode startNode,
            out XmlFlightNode endNode, bool requireKnownMaster)
        {
            startNode = endNode = null;
            var me = StyxWoW.Me;
            if (me == null || !IsFinitePoint(from) || !IsFinitePoint(to) ||
                XmlNodes == null || XmlNodes.Count == 0)
                return;

            uint continent = me.MapId;
            List<XmlFlightNode> continentNodes = XmlNodes
                .Where(n => n != null && IsFinitePoint(n.Location) && n.Continent == continent)
                .ToList();

            if (continentNodes.Count == 0)
            {
                startNode = endNode = null;
                return;
            }

            // Prefer the nearest usable cached origin, not merely the nearest
            // marker. A disconnected/unknown/backwards candidate must not hide
            // a later forward connection. This is still cached connectivity, not
            // proof of an approach path or a globally optimal travel route.
            foreach (XmlFlightNode sNode in continentNodes.OrderBy(n => n.Location.DistanceSqr(from)))
            {
                if (requireKnownMaster && sNode.MasterEntry == 0U)
                    continue;
                var connections = sNode.Connections;
                if (connections == null || connections.Count == 0)
                    continue;

                XmlFlightNode eNode = continentNodes
                    .Where(n => connections.Contains(n.Name))
                    .OrderBy(n => n.Location.DistanceSqr(to))
                    .FirstOrDefault();

                if (eNode == null || eNode.Name == sNode.Name)
                    continue;

                // Retain the existing no-backwards guard for every origin.
                if (sNode.Location.DistanceSqr(to) < eNode.Location.DistanceSqr(to))
                    continue;

                startNode = sNode;
                endNode = eNode;
                return;
            }
        }

        /// <summary>
        /// Find node by name
        /// </summary>
        private static XmlFlightNode FindNodeByName(string name)
        {
            return XmlNodes?.FirstOrDefault(n => n.Name == name);
        }

        /// <summary>
        /// Find node by master entry and level
        /// </summary>
        private static XmlFlightNode FindNodeByMasterEntry(uint entry, int level)
        {
            return XmlNodes?.FirstOrDefault(n => n.MasterEntry == entry && n.UpdateLevel <= level);
        }

        /// <summary>
        /// Handle taxi map opened event (fired by TAXIMAP_OPENED Lua event).
        /// Saves all visible nodes + connections to the per-character XML file,
        /// then takes the flight if Reason == Use.
        /// </summary>
        private static void HandleTaxiMapOpened(object sender, LuaEventArgs e)
        {
            try
            {
                if (!CharacterSettings.Instance.UseFlightPaths || BotPoi.Current.Type != PoiType.Fly)
                    return;

                Logging.Write("TaxiMap opened — updating known nodes list.");

                // dword_C0D7EC (0xC0D7EC) — verified via IDA, CGTaxiMap__TaxiNodeType. No Lua needed.
                var currentDbc = TaxiNodeInfo.GetCurrent();
                if (currentDbc == null || !currentDbc.IsValid || string.IsNullOrEmpty(currentDbc.Name))
                {
                    Logging.WriteDebug("HandleTaxiMapOpened: Could not read current taxi node from CGTaxiMap (dword_C0D7EC).");
                    return;
                }

                string currentNodeName = currentDbc.Name;
                WoWPoint currentLocation = currentDbc.Location != WoWPoint.Empty ? currentDbc.Location : StyxWoW.Me.Location;

                // Insert or update the current node record.
                XmlFlightNode currentNode = FindNodeByName(currentNodeName);
                if (currentNode == null)
                {
                    currentNode = new XmlFlightNode(
                        BotPoi.Current.Entry, StyxWoW.Me.Level,
                        currentNodeName, StyxWoW.Me.MapId, currentLocation);
                    XmlNodes.Add(currentNode);
                }
                else
                {
                    currentNode.MasterEntry = BotPoi.Current.Entry;
                    currentNode.UpdateLevel = StyxWoW.Me.Level;
                    if (currentNode.Location == WoWPoint.Empty)
                        currentNode.Location = currentLocation;
                }

                // Reachability still uses TaxiFrame Lua nodes (frameNodes[i].Reachable),
                // exactly as HB does with TaxiFrame.Instance.Nodes[(int)num].Reachable.
                var frameNodes = TaxiFrame.Instance?.Nodes;
                uint nodeCount = TaxiNodeInfo.GetNodeCount();
                for (uint i = 0; i < nodeCount; i++)
                {
                    try
                    {
                        var nodeDbc = TaxiNodeInfo.GetByTableIndex(i);
                        if (nodeDbc == null || !nodeDbc.IsValid || string.IsNullOrEmpty(nodeDbc.Name))
                            continue;

                        if (nodeDbc.Name == currentNodeName)
                            continue;

                        XmlFlightNode xmlNode = FindNodeByName(nodeDbc.Name);
                        if (xmlNode == null)
                        {
                            xmlNode = new XmlFlightNode(nodeDbc.Name, (uint)nodeDbc.MapId,
                                nodeDbc.Location != WoWPoint.Empty ? nodeDbc.Location : WoWPoint.Empty);
                            XmlNodes.Add(xmlNode);
                        }

                        bool reachable = frameNodes != null && (int)i < frameNodes.Count && frameNodes[(int)i].Reachable;
                        if (reachable)
                        {
                            currentNode.Connect(xmlNode.Name);
                            xmlNode.Connect(currentNode.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Write(ex.ToString());
                    }
                }

                SaveToXml();

                if (Reason == FlightPathReason.Use && TakingPathTo != null)
                {
                    Logging.Write("Taking flight path to {0} from {1}", TakingPathTo.Name, TakingPathFrom?.Name ?? StyxWoW.Me.Location.ToString());
                    var target = frameNodes?.FirstOrDefault(n => n.Name == TakingPathTo.Name);
                    target?.TakeNode();
                    StyxWoW.SleepForLagDuration();
                }
                else
                {
                    BotPoi.Clear("Learned/Updated Flight Path Information");
                }
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
            }
            finally
            {
                TaxiFrame.Instance?.Hide();
                if (BotPoi.Current.Type != PoiType.None && Reason != FlightPathReason.Use)
                    BotPoi.Clear("HandleTaxiMapOpened");
            }
        }

        /// <summary>
        /// Save flight nodes to XML
        /// </summary>
        private static void SaveToXml()
        {
            try
            {
                XElement root = new XElement("FlightNodes");
                foreach (XmlFlightNode node in XmlNodes)
                    root.Add(node.ToXml());
                root.Save(XmlPath);
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
            }
        }
    }

    /// <summary>
    /// Flight node data from XML
    /// </summary>
    public class XmlFlightNode
    {
        public string Name { get; set; }
        public uint MasterEntry { get; set; }
        public int UpdateLevel { get; set; }
        public uint Continent { get; set; }
        public WoWPoint Location { get; set; }
        public HashSet<string> Connections { get; set; }

        public XmlFlightNode()
        {
            Connections = new HashSet<string>();
        }

        public XmlFlightNode(string name, uint continent, WoWPoint location) : this()
        {
            Name = name;
            Continent = continent;
            Location = location;
        }

        public XmlFlightNode(uint masterEntry, int level, string name, uint continent, WoWPoint location) : this(name, continent, location)
        {
            MasterEntry = masterEntry;
            UpdateLevel = level;
        }

        public XmlFlightNode(XElement element) : this()
        {
            Name = (string)element.Attribute("Name") ?? "";
            MasterEntry = (uint?)element.Attribute("MasterEntry") ?? 0;
            UpdateLevel = (int?)element.Attribute("UpdateLevel") ?? 0;
            Continent = (uint?)element.Attribute("Continent") ?? 0;

            float x = (float?)element.Attribute("X") ?? 0;
            float y = (float?)element.Attribute("Y") ?? 0;
            float z = (float?)element.Attribute("Z") ?? 0;
            Location = new WoWPoint(x, y, z);

            var connectionsAttr = (string)element.Attribute("Connections");
            if (!string.IsNullOrEmpty(connectionsAttr))
            {
                foreach (var conn in connectionsAttr.Split(','))
                    Connections.Add(conn.Trim());
            }
        }

        public void Connect(string nodeName)
        {
            if (!string.IsNullOrEmpty(nodeName))
                Connections.Add(nodeName);
        }

        public XElement ToXml()
        {
            return new XElement("Node",
                new XAttribute("Name", Name ?? ""),
                new XAttribute("MasterEntry", MasterEntry),
                new XAttribute("UpdateLevel", UpdateLevel),
                new XAttribute("Continent", Continent),
                new XAttribute("X", Location.X),
                new XAttribute("Y", Location.Y),
                new XAttribute("Z", Location.Z),
                new XAttribute("Connections", string.Join(",", Connections))
            );
        }
    }
}