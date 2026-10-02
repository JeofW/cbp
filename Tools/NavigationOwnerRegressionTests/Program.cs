using Styx.Logic.POI;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Linked production BotPoi and movement ownership. Native observations are
// controlled leaves; no host build, client, navmesh or live proof is implied.
internal static class Program
{
    private static int failed, passed;
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Case(string name, Action body)
    {
        ObjectManager.Objects.Clear(); ObjectManager.Me = new LocalPlayer { Guid = 1, MapId = 530, BaseAddress = 10 };
        ObjectManager.Wow = new object(); Navigator.PlayerMover = new Mover(); Navigator.NavigationProvider = new NavigationProvider();
        Styx.Logic.Blacklist.Entries.Clear();
        ProfileManager.CurrentProfile = new Profile(); BotPoi.Current = new BotPoi(PoiType.None);
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    internal static int Main()
    {
        foreach (PoiType type in new[] { PoiType.Loot, PoiType.Kill, PoiType.Skin })
        {
            Case(type + " exact GUID cannot borrow same-entry successor", () =>
            {
                var original = new WoWGameObject { Guid = 11, Entry = 183396, Location = new(1, 2, 3) };
                var successor = new WoWGameObject { Guid = 12, Entry = 183396, Location = new(80, 90, 10) };
                ObjectManager.Objects.Add(original); var poi = new BotPoi(original, type); BotPoi.Current = poi;
                Check(ReferenceEquals(poi.AsObject, original), "original object was not resolved");
                original.IsValid = false; ObjectManager.Objects.Clear(); ObjectManager.Objects.Add(successor);
                Check(poi.AsObject == null, "selected GUID silently rebound to another object's GUID");
                Check(poi.Guid == 11 && poi.Location.Equals(new WoWPoint(1, 2, 3)), "missing original borrowed successor coordinates");
            });
            Case(type + " entry-only legacy selection can still resolve", () =>
            {
                var target = new WoWGameObject { Guid = 22, Entry = 183396 }; ObjectManager.Objects.Add(target);
                var poi = new BotPoi(type) { Entry = 183396 };
                Check(ReferenceEquals(poi.AsObject, target), "legacy entry lookup was removed");
            });
        }
        foreach (string kind in new[] { "actor", "actor-guid", "map", "memory", "mover", "provider", "profile", "poi", "poi-guid", "poi-entry", "poi-type", "poi-z", "poi-generation", "request-generation", "blacklist" })
        {
            Case("request revokes " + kind, () =>
            {
                var poi = new BotPoi(new WoWPoint(10, 20, 30), PoiType.Loot) { Guid = 11, Entry = 183396 };
                BotPoi.Current = poi; var mesh = new MeshNavigator(); Func<bool> current = mesh.Observe();
                Check(current(), "unchanged owner not current");
                switch (kind)
                {
                    case "actor": ObjectManager.Me = new LocalPlayer { Guid = 1, MapId = 530, BaseAddress = 10 }; break;
                    case "actor-guid": ObjectManager.Me!.Guid = 2; break;
                    case "map": ObjectManager.Me!.MapId = 571; break;
                    case "memory": ObjectManager.Wow = new object(); break;
                    case "mover": Navigator.PlayerMover = new Mover(); break;
                    case "provider": Navigator.NavigationProvider = new NavigationProvider(); break;
                    case "profile": ProfileManager.CurrentProfile = new Profile(); break;
                    case "poi": BotPoi.Current = new BotPoi(PoiType.Loot) { Guid = 12, Entry = 183396 }; break;
                    case "poi-guid": poi.Guid = 12; break;
                    case "poi-entry": poi.Entry = 183394; break;
                    case "poi-type": poi.Type = PoiType.Kill; break;
                    case "poi-z": poi.Location = new WoWPoint(10, 20, 31); break;
                    case "poi-generation": BotPoi.Current = new BotPoi(PoiType.Kill); BotPoi.Current = poi; break;
                    case "request-generation": mesh.ReplaceRequest(); break;
                    case "blacklist": Styx.Logic.Blacklist.Entries.Add(11); break;
                }
                Check(!current(), "old request retained authority after " + kind);
            });
        }
        Case("stable observations retain authority", () => { var mesh = new MeshNavigator(); var current = mesh.Observe(); Check(current() && current(), "unchanged request revoked"); });
        Case("ordinary unleased moving target coordinate revision still revokes", () =>
        {
            var target = new WoWUnit { Guid = 11, Entry = 183396, Location = new WoWPoint(10, 20, 30) };
            ObjectManager.Objects.Add(target); var poi = new BotPoi(target, PoiType.Kill); BotPoi.Current = poi;
            var mesh = new MeshNavigator(); var current = mesh.Observe(); long work = BotPoi.CurrentWorkGeneration;
            target.Location = new WoWPoint(12, 22, 30); _ = poi.Location;
            Check(BotPoi.CurrentWorkGeneration == work, "coordinate-only target refresh changed work identity");
            Check(!current(), "ordinary unleased request survived coordinate route revision");
        });
        Case("explicit combat lease survives moving target coordinate revision", () =>
        {
            var target = new WoWUnit { Guid = 11, Entry = 183396, Location = new WoWPoint(10, 20, 30) };
            ObjectManager.Objects.Add(target); var poi = new BotPoi(target, PoiType.Kill); BotPoi.Current = poi;
            long work = BotPoi.CurrentWorkGeneration; Func<bool> lease = () => ReferenceEquals(BotPoi.Current, poi) && BotPoi.CurrentWorkGeneration == work;
            var mesh = new MeshNavigator(); var current = mesh.ObserveLeased(lease); long route = BotPoi.CurrentGeneration;
            target.Location = new WoWPoint(13, 23, 30); _ = poi.Location;
            Check(BotPoi.CurrentGeneration == route + 1 && BotPoi.CurrentWorkGeneration == work, "moving target did not remain a route-only revision");
            Check(current(), "explicit combat lease was revoked by coordinate-only route revision");
        });
        foreach (string semantic in new[] { "poi", "guid", "entry", "type", "wrapper-loss" })
            Case("explicit combat lease revokes semantic " + semantic, () =>
            {
                var target = new WoWUnit { Guid = 11, Entry = 183396, Location = new WoWPoint(10, 20, 30) };
                ObjectManager.Objects.Add(target); var poi = new BotPoi(target, PoiType.Kill); BotPoi.Current = poi;
                long work = BotPoi.CurrentWorkGeneration; Func<bool> lease = () => ReferenceEquals(BotPoi.Current, poi) && BotPoi.CurrentWorkGeneration == work;
                var mesh = new MeshNavigator(); var current = mesh.ObserveLeased(lease);
                switch (semantic)
                {
                    case "poi": BotPoi.Current = new BotPoi(target, PoiType.Kill); break;
                    case "guid": poi.Guid++; break;
                    case "entry": poi.Entry++; break;
                    case "type": poi.Type = PoiType.Loot; break;
                    case "wrapper-loss": target.Invalidate(); break;
                }
                Check(!current(), "leased request survived semantic " + semantic);
            });
        Case("same-entry object despawn revokes captured request", () =>
        {
            var target = new WoWGameObject { Guid = 11, Entry = 183396 };
            ObjectManager.Objects.Add(target); BotPoi.Current = new BotPoi(target, PoiType.Loot);
            var mesh = new MeshNavigator(); var current = mesh.Observe(); target.Invalidate();
            Check(!current(), "invalidated selected object retained route authority");
        });
        foreach (PoiType type in new[] { PoiType.Loot, PoiType.Kill, PoiType.Skin, PoiType.Harvest })
            Case(type + " explicit GUID replacement cannot retain cached old subject", () =>
            {
                var original = new WoWGameObject { Guid = 11, Entry = 183396 };
                var replacement = new WoWGameObject { Guid = 12, Entry = 183396 };
                ObjectManager.Objects.AddRange(new[] { original, replacement });
                var poi = new BotPoi(original, type); BotPoi.Current = poi;
                Check(ReferenceEquals(poi.AsObject, original), "original was not cached");
                poi.Guid = 12;
                Check(ReferenceEquals(poi.AsObject, replacement), "changed GUID retained old cached live wrapper");
            });
        Case("explicit entry replacement cannot borrow old selected template", () =>
        {
            var original = new WoWGameObject { Guid = 11, Entry = 183396 };
            ObjectManager.Objects.Add(original); var poi = new BotPoi(original, PoiType.Loot); BotPoi.Current = poi;
            poi.Entry = 183394;
            Check(poi.AsObject == null, "changed entry retained mismatching cached template");
        });
        Case("stale callback cannot become current after replacement returns to same POI", () =>
        {
            var poi = new BotPoi(PoiType.Loot) { Guid = 11 }; BotPoi.Current = poi;
            var mesh = new MeshNavigator(); var staleCallback = mesh.Observe();
            BotPoi.Current = new BotPoi(PoiType.Kill); BotPoi.Current = poi;
            var currentCallback = mesh.Observe();
            Check(!staleCallback() && currentCallback(), "observed generation loss reacquired old callback authority");
        });
        Case("Moved commands without displacement still request bounded recovery", () =>
        {
            var tracker = new CommandedMovementProgressTracker();
            var origin = new WoWPoint(-1046.455f, 2529.787f, 12.758f);
            var destination = new WoWPoint(-1050.680f, 2519.950f, 14.213f);
            var now = new DateTime(2026, 10, 2, 4, 41, 0, DateTimeKind.Utc);
            Check(!tracker.ObserveCommand(origin, destination, now), "first command fabricated failure");
            Check(!tracker.ObserveCommand(origin, destination, now.AddSeconds(1)), "insufficient time fabricated failure");
            Check(!tracker.ObserveCommand(origin, destination, now.AddSeconds(2)), "insufficient time fabricated failure");
            Check(tracker.ObserveCommand(origin, destination, now.AddSeconds(3)), "stationary commands never requested recovery");
            tracker.Reset();
            Check(!tracker.ObserveCommand(origin, destination, now.AddSeconds(4)), "revoked owner donated elapsed stall time");
        });
        Console.WriteLine($"Navigation owner linked production scenarios: {passed}/{passed + failed}; failures={failed}");
        return failed == 0 ? 0 : 1;
    }
}
