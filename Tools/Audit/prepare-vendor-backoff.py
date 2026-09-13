"""Temporary hash-locked source preparation; does not create commits or refs."""
from pathlib import Path
import hashlib
import json

root = Path.cwd()
expected = {
    'Styx/Logic/Profiles/VendorManager.cs': ('899e13d404f15a2edb5ad1cda6612dd894c75c6e', '560a2456f0dcff229ad0cd22198d7771bf81e0a8'),
    'Styx/Logic/Profiles/VendorSafetyPolicy.cs': ('8af2d8dfb6edfa04510eabf16780a1bcb0ec7a94', 'aaf0f293f442db92e24e5073ab478360d5fbddcd'),
    'Styx/Logic/Profiles/VendorTravelBackoff.cs': (None, 'b3a97fdf308b76e963cd25ece6d5c070e23254f8'),
    'runtime-snapshot/Bots/WholesomeAutoQuest-master/VendorDataLoader.cs': ('617d4172bf171a3ee8fac4942ad2ca58a8b3663f', 'c0b0ae4b21fc2afebfd1662eb189fa76f28b50fd'),
    'runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs': ('c7cdb4854c3faefdf6867327f5f16bb930d4d36f', 'b2899a46fa9966a4d8f96002ede8516215757827'),
}
def blob(path):
    data = path.read_bytes()
    return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
def write(path, text):
    path.write_bytes(text.encode('utf-8'))
for path, (before, after) in expected.items():
    p = root / path
    assert (not p.exists()) if before is None else blob(p) == before, f'Unexpected preimage: {path}'

write(root / 'Styx/Logic/Profiles/VendorTravelBackoff.cs', '''using System;
using System.Collections.Generic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;

namespace Styx.Logic.Profiles
{
    /// <summary>A captured service trip, not live objects retained across ticks.</summary>
    public sealed class VendorTravelObservation
    {
        public DateTime NowUtc { get; init; }
        public DateTime LastMoveAttemptUtc { get; init; }
        public TimeSpan StationaryFor { get; init; }
        public uint MapId { get; init; }
        public int Entry { get; init; }
        public PoiType Type { get; init; }
        public WoWPoint PlayerLocation { get; init; }
        public WoWPoint Destination { get; init; }
        public WoWPoint LastMoveDestination { get; init; }
        public MoveResult? LastMoveResult { get; init; }
        public bool IsActiveWorld { get; init; }
        public bool IsPaused { get; init; }
        public bool IsCombat { get; init; }
        public bool IsDead { get; init; }
        // Intentional routine/food/drink rest, NOT the inn's rested-XP player flag.
        public bool IsResting { get; init; }
        public bool IsOnTaxi { get; init; }
        public bool IsOnTransport { get; init; }
        public bool IsElevatorTransit { get; init; }
        public bool HasActivePath { get; init; }
        public bool IsServiceFrameOpen { get; init; }
    }

    /// <summary>
    /// Temporary endpoint evidence shared by profile and automatic vendor selectors.
    /// Never mutates explicit exclusions or persists travel failure to disk.
    /// </summary>
    public sealed class VendorTravelBackoff
    {
        private const int Capacity = 128;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(3);
        private readonly object _sync = new();
        private readonly List<Lease> _leases = new();

        private sealed record Lease(Guid Owner, uint Map, int Entry, WoWPoint Destination,
            DateTime CreatedUtc, DateTime RetryUtc);

        public int Count { get { lock (_sync) return _leases.Count; } }

        public bool TryDefer(Guid owner, VendorTravelObservation sample)
        {
            if (owner == Guid.Empty || sample == null || !IsFailedTravel(sample)) return false;
            lock (_sync)
            {
                // Other owners expire their own records, so their next Pulse can
                // request a refresh exactly once. Never evict another active lease.
                _leases.RemoveAll(lease => lease.Owner == owner && !IsActive(lease, sample.NowUtc));
                if (_leases.Exists(lease => lease.Owner == owner && lease.Map == sample.MapId
                    && lease.Entry == sample.Entry && SameEndpoint(lease.Destination, sample.Destination)))
                    return false;
                // At capacity, decline new penalties rather than turning this into
                // an unbounded blacklist or invalidating another owner's evidence.
                if (_leases.Count >= Capacity) return false;
                _leases.Add(new Lease(owner, sample.MapId, sample.Entry, sample.Destination,
                    sample.NowUtc, sample.NowUtc + RetryDelay));
                return true;
            }
        }

        public bool IsDeferred(uint mapId, int entry, WoWPoint destination, DateTime nowUtc)
        {
            if (entry <= 0 || !IsFinite(destination)) return false;
            lock (_sync)
                return _leases.Exists(lease => lease.Map == mapId && lease.Entry == entry
                    && IsActive(lease, nowUtc) && SameEndpoint(lease.Destination, destination));
        }

        public bool ReleaseExpired(Guid owner, DateTime nowUtc)
        {
            lock (_sync)
                return _leases.RemoveAll(lease => lease.Owner == owner && !IsActive(lease, nowUtc)) != 0;
        }

        public void Reset(Guid owner)
        {
            lock (_sync) _leases.RemoveAll(lease => lease.Owner == owner);
        }

        private static bool IsActive(Lease lease, DateTime nowUtc) =>
            nowUtc >= lease.CreatedUtc && nowUtc < lease.RetryUtc;

        private static bool IsFailedTravel(VendorTravelObservation sample) =>
            sample.Entry > 0 && VendorSafetyPolicy.IsService(sample.Type)
            && sample.IsActiveWorld && !sample.IsPaused && !sample.IsCombat && !sample.IsDead
            && !sample.IsResting && !sample.IsOnTaxi && !sample.IsOnTransport
            && !sample.IsElevatorTransit && !sample.HasActivePath && !sample.IsServiceFrameOpen
            && sample.StationaryFor >= TimeSpan.FromSeconds(30)
            && sample.LastMoveResult is MoveResult.Failed or MoveResult.PathGenerationFailed
            && sample.NowUtc >= sample.LastMoveAttemptUtc
            && sample.NowUtc - sample.LastMoveAttemptUtc <= Freshness
            && sample.NowUtc <= DateTime.MaxValue - RetryDelay
            && IsFinite(sample.PlayerLocation) && IsFinite(sample.Destination)
            && IsFinite(sample.LastMoveDestination)
            && SameEndpoint(sample.Destination, sample.LastMoveDestination)
            && !SameEndpoint(sample.PlayerLocation, sample.Destination);

        public static bool SameEndpoint(WoWPoint first, WoWPoint second)
        {
            if (!IsFinite(first) || !IsFinite(second)) return false;
            double dx = (double)first.X - second.X, dy = (double)first.Y - second.Y;
            return dx * dx + dy * dy <= 4.5 * 4.5 && Math.Abs((double)first.Z - second.Z) < 3;
        }

        public static bool IsFinite(WoWPoint point) =>
            float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
    }
}
''')
p = root / 'Styx/Logic/Profiles/VendorSafetyPolicy.cs'
s = p.read_text(encoding='utf-8').replace('private static readonly HashSet<int> RejectedEntries = new();', 'private static readonly HashSet<int> RejectedEntries = new();\n\n        public static VendorTravelBackoff Travel { get; } = new();')
write(p, s)
p = root / 'Styx/Logic/Profiles/VendorManager.cs'
s = p.read_text(encoding='utf-8').replace('''        public bool IsBlacklisted(Vendor vendor) => VendorSafetyPolicy.IsRejected(vendor.Entry) ||
            Blacklist.Any(failed => failed.Entry == vendor.Entry);''', '''        public bool IsBlacklisted(Vendor vendor) => VendorSafetyPolicy.IsRejected(vendor.Entry) ||
            Blacklist.Any(failed => failed.Entry == vendor.Entry) ||
            (StyxWoW.Me is { } player && VendorSafetyPolicy.Travel.IsDeferred(
                player.MapId, vendor.Entry, vendor.Location, DateTime.UtcNow));''')
write(p, s)
p = root / 'runtime-snapshot/Bots/WholesomeAutoQuest-master/VendorDataLoader.cs'
s = p.read_text(encoding='utf-8').replace('using Styx.WoWInternals;', 'using Styx.WoWInternals;\nusing Styx.Combat.CombatRoutine;\nusing Styx.Logic.Profiles;')
a = s.index('        public List<VendorEntry> GetNearestVendors(')
b = s.index('        private static string FindDataFile()', a)
s = s[:a] + '''        public List<VendorEntry> GetNearestVendors(LocalPlayer me, string type, int count = 5, HashSet<int> blacklist = null)
        {
            if (me == null) return new List<VendorEntry>();
            return SelectNearestVendors(me.MapId, me.Class, me.Location, type, count, blacklist);
        }

        private List<VendorEntry> SelectNearestVendors(uint map, WoWClass playerClass,
            WoWPoint origin, string type, int count, HashSet<int> blacklist)
        {
            if (_database == null || count <= 0 || !VendorTravelBackoff.IsFinite(origin))
                return new List<VendorEntry>();
            DateTime now = DateTime.UtcNow;
            string className = playerClass.ToString();
            return _database.Vendors
                .Where(v => v.Map == map && v.Type == type)
                .Where(v => type != "Train" || string.IsNullOrEmpty(v.TrainClass) || v.TrainClass == className)
                .Where(v => blacklist == null || !blacklist.Contains(v.Entry))
                .Where(v => !VendorSafetyPolicy.IsRejected(v.Entry))
                .Where(v => VendorTravelBackoff.IsFinite(new WoWPoint((float)v.X, (float)v.Y, (float)v.Z)))
                .Where(v => !VendorSafetyPolicy.Travel.IsDeferred(map, v.Entry,
                    new WoWPoint((float)v.X, (float)v.Y, (float)v.Z), now))
                .OrderBy(v => (v.X - origin.X) * (v.X - origin.X) + (v.Y - origin.Y) * (v.Y - origin.Y))
                .ThenBy(v => v.Entry)
                .Take(count)
                .ToList();
        }

''' + s[b:]
write(p, s)
p = root / 'runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs'
s = p.read_text(encoding='utf-8').replace('private DateTime _lastMovedTime = DateTime.Now;', '''private DateTime _lastMovedTime = DateTime.UtcNow;
        private readonly Guid _vendorTravelOwner = Guid.NewGuid();
        private DateTime _lastVendorObservationUtc = DateTime.MinValue;
        private uint _lastVendorMap;
        private uint _lastVendorEntry;
        private WoWPoint _lastVendorDestination;''')
s = s.replace('''            _nextProgressSampleUtc = DateTime.MinValue;
        }

        private bool DoScan''', '''            _nextProgressSampleUtc = DateTime.MinValue;
            VendorSafetyPolicy.Travel.Reset(_vendorTravelOwner);
            _lastVendorObservationUtc = DateTime.MinValue;
            _lastMovedTime = DateTime.UtcNow;
            _wasStuck = false;
            _stuckLogged = false;
        }

        private bool DoScan''')
s = s.replace('''            CapturePreDeathAttribution();
            MaybeRequestTimedRetry();''', '''            if (VendorSafetyPolicy.Travel.ReleaseExpired(_vendorTravelOwner, DateTime.UtcNow))
                RequestRefresh("Temporary vendor travel retry is due; queuing one scheduler rebuild.");
            CapturePreDeathAttribution();
            MaybeRequestTimedRetry();''')
s = s.replace('            base.Pulse();\n', '            base.Pulse();\n            ObserveVendorTravelContext();\n')
s = s.replace('_lastMovedTime = DateTime.Now;', '_lastMovedTime = DateTime.UtcNow;').replace('(DateTime.Now - _lastMovedTime)', '(DateTime.UtcNow - _lastMovedTime)')
a = s.index('''                        var poi = BotPoi.Current;
                        bool poiIsVendor''')
b = s.index('''                        else if (TryRecoverFailedPickupTravel''', a)
s = s[:a] + '''                        if (TryRecoverFailedVendorTravel(loc, TimeSpan.FromSeconds(stuckSec)))
                        {
                            _wasStuck = true;
                            _stuckLogged = true;
                        }
''' + s[b:]
a = s.index('        private bool TryRecoverFailedPickupTravel(')
s = s[:a] + '''        // Do not charge a new trip for time spent on another POI, paused, loading,
        // in combat or intentionally resting. A long sampling gap is unknown time.
        private void ObserveVendorTravelContext()
        {
            var me = StyxWoW.Me;
            var poi = BotPoi.Current;
            DateTime now = DateTime.UtcNow;
            if (me == null || !StyxWoW.IsInWorld || poi == null || !VendorSafetyPolicy.IsService(poi.Type))
            {
                _lastVendorObservationUtc = DateTime.MinValue;
                return;
            }
            bool newContext = _lastVendorObservationUtc == DateTime.MinValue
                || now < _lastVendorObservationUtc || now - _lastVendorObservationUtc > TimeSpan.FromSeconds(5)
                || me.MapId != _lastVendorMap || poi.Entry != _lastVendorEntry
                || !VendorTravelBackoff.SameEndpoint(poi.Location, _lastVendorDestination);
            if (newContext || TreeRoot.IsPaused || !TreeRoot.IsRunning || me.Combat
                || me.Dead || me.IsGhost || me.OnTaxi || me.IsOnTransport
                || Navigator.IsRidingElevator || _restingPaused)
            {
                _lastMovedTime = now;
                _wasStuck = false;
                _stuckLogged = false;
            }
            _lastVendorObservationUtc = now;
            _lastVendorMap = me.MapId;
            _lastVendorEntry = poi.Entry;
            _lastVendorDestination = poi.Location;
        }

        private bool TryRecoverFailedVendorTravel(WoWPoint playerLocation, TimeSpan stationaryFor)
        {
            var me = StyxWoW.Me;
            var poi = BotPoi.Current;
            if (me == null || poi == null || poi.Entry > int.MaxValue || !VendorSafetyPolicy.IsService(poi.Type)
                || Navigator.NavigationProvider is not MeshNavigator mesh)
                return false;
            bool intentionalRest = _restingPaused || me.HasAura("Food") || me.HasAura("Drink");
            bool frameOpen = MerchantFrame.Instance.IsVisible || TrainerFrame.Instance.IsVisible
                || Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance.IsVisible;
            if (intentionalRest || frameOpen) _lastMovedTime = DateTime.UtcNow;
            var sample = new VendorTravelObservation
            {
                NowUtc = DateTime.UtcNow, MapId = me.MapId, Entry = (int)poi.Entry, Type = poi.Type,
                PlayerLocation = playerLocation, Destination = poi.Location,
                LastMoveDestination = mesh.LastMoveDestination, LastMoveResult = mesh.LastMoveResult,
                LastMoveAttemptUtc = mesh.LastMoveAttemptUtc, StationaryFor = stationaryFor,
                IsActiveWorld = !_stopped && StyxWoW.IsInWorld && TreeRoot.IsRunning,
                IsPaused = TreeRoot.IsPaused, IsCombat = me.Combat, IsDead = me.Dead || me.IsGhost,
                IsResting = intentionalRest, IsOnTaxi = me.OnTaxi, IsOnTransport = me.IsOnTransport,
                IsElevatorTransit = mesh.IsRidingElevator, HasActivePath = mesh.HasActivePath,
                IsServiceFrameOpen = frameOpen
            };
            if (!VendorSafetyPolicy.Travel.TryDefer(_vendorTravelOwner, sample)) return false;
            Log($"Vendor travel deferred for 120s: {poi.Name} (Entry:{poi.Entry}), map={me.MapId}, "
                + $"destination={poi.Location}; movement={mesh.LastMoveResult}, reason={mesh.LastRouteFailure}. No saved blacklist change.");
            BotPoi.Clear("Wholesome temporary vendor travel retry");
            RequestRefresh("Vendor travel temporarily deferred; selecting another eligible endpoint.");
            return true;
        }

''' + s[a:]
write(p, s)
records = []
for path, (before, after) in expected.items():
    p = root / path
    assert blob(p) == after, f'Unexpected postimage: {path}'
    records.append({'path': path, 'sha': after, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
print(json.dumps(records, indent=2))
