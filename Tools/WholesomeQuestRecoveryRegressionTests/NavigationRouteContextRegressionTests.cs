using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Helpers;

// Actual retained MeshNavigator state and descriptor-backed controlled actor.
// Queries/native movement are not called; stale native publication remains a
// separate route replay gate. Existing request/clear tests cover reentrancy.
internal static class NavigationRouteContextRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly WoWPoint Start = new(11, 22, 33), End = new(44, 55, 66);
    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0;
        foreach (string change in new[] { "unchanged", "poi", "guid", "entry", "combat", "z", "generation", "profile", "provider", "provider-connector" })
        {
            using var fixture = new Fixture();
            fixture.CheckReplacement(change); passed++;
        }
        using (var fixture = new Fixture()) { fixture.ReentrantSuccessor(); passed++; }
        using (var fixture = new Fixture()) { fixture.StaleFailedPathCallback(); passed++; }
        using (var fixture = new Fixture()) { fixture.StaleConnectorAvoidanceCallback(); passed++; }
        Console.WriteLine($"Navigation retained route contexts: {passed}/13; actual MeshNavigator, controlled input; no game attached.");
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException("Navigation route context: " + reason); }
    private sealed class Fixture : IDisposable
    {
        private readonly object source = Activator.CreateInstance(typeof(MeshMoveRequestOwnershipRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
        private readonly MeshNavigator mesh;
        private readonly object mover;
        private readonly object? oldMesh = typeof(Navigator).GetField("_meshNavigator", StaticHidden)!.GetValue(null);
        private readonly object? oldProfile = typeof(ProfileManager).GetField("_currentProfile", StaticHidden)!.GetValue(null);
        private readonly BotPoi oldPoi = BotPoi.Current;
        private readonly BotPoi poi = new(End, PoiType.Loot) { Guid = 11, Entry = 183396 };
        internal Fixture()
        {
            mesh = (MeshNavigator)source.GetType().GetField("mesh", Hidden)!.GetValue(source)!;
            mover = source.GetType().GetField("mover", Hidden)!.GetValue(source)!;
            BotPoi.Current = poi;
            typeof(Navigator).GetField("_meshNavigator", StaticHidden)!.SetValue(null, mesh);
            Seed();
        }
        private void Seed()
        {
            mesh.OverrideCurrentPath(new[] { Start, End });
            typeof(MeshNavigator).GetField("_destination", Hidden)!.SetValue(mesh, End);
        }
        private int Stops => (int)mover.GetType().GetField("Stops", Hidden)!.GetValue(mover)!;
        private void Invalidate() => typeof(MeshNavigator).GetMethod("InvalidateRouteContext", Hidden)!.Invoke(mesh, null);
        internal void CheckReplacement(string kind)
        {
            int before = Stops;
            switch (kind)
            {
                case "poi": BotPoi.Current = new BotPoi(End, PoiType.Loot) { Guid = 12, Entry = 183396 }; break;
                case "guid": poi.Guid = 12; break;
                case "entry": poi.Entry = 183394; break;
                case "combat": poi.Type = PoiType.Kill; break;
                case "z": poi.Location = new WoWPoint(44, 55, 67); break;
                case "generation": BotPoi.Current = new BotPoi(PoiType.None); BotPoi.Current = poi; break;
                case "profile": typeof(ProfileManager).GetField("_currentProfile", StaticHidden)!.SetValue(null, new Profile()); break;
                case "provider": typeof(Navigator).GetField("_currentProvider", StaticHidden)!.SetValue(null, new MeshNavigator()); break;
                case "provider-connector":
                    typeof(MeshNavigator).GetField("_localConnectorTarget", Hidden)!.SetValue(mesh, Start);
                    typeof(Navigator).GetField("_currentProvider", StaticHidden)!.SetValue(null, new MeshNavigator()); break;
            }
            Invalidate();
            if (kind == "unchanged") Check(mesh.HasActivePath && mesh.Destination == End && Stops == before, "unchanged route was discarded");
            else
            {
                Check(!mesh.HasActivePath && mesh.Destination == WoWPoint.Zero, kind + " retained an active obsolete route");
                int expectedStops = kind.StartsWith("provider", StringComparison.Ordinal) ? 0 : 1;
                Check(Stops == before + expectedStops, kind + " stopped replacement input or failed to stop owned input");
                Invalidate(); Check(Stops == before + expectedStops, kind + " repeated old cleanup");
            }
            Console.WriteLine("PASS retained navigation context: " + kind);
        }
        internal void ReentrantSuccessor()
        {
            int before = Stops;
            var next = new[] { new WoWPoint(80, 90, 100), new WoWPoint(110, 120, 130) };
            mover.GetType().GetField("OnStop", Hidden)!.SetValue(mover, (Action)(() => mesh.OverrideCurrentPath(next)));
            BotPoi.Current = new BotPoi(PoiType.Kill);
            Check(Stops == before + 1 && mesh.CurrentPath.SequenceEqual(next), "revocation cleanup overwrote the stop callback's successor route");
            Invalidate(); Check(mesh.CurrentPath.SequenceEqual(next) && Stops == before + 1, "old context revoked a current successor");
            Console.WriteLine("PASS retained navigation context: reentrant successor");
        }
        internal void StaleFailedPathCallback()
        {
            var player = source.GetType().GetField("player", Hidden)!.GetValue(source)!;
            player.GetType().GetField("ObservedLocation", Hidden)!.SetValue(player, Start);
            mesh.Clear();
            var next = new[] { new WoWPoint(80, 90, 100), new WoWPoint(110, 120, 130) };
            bool fired = false;
            void Replace(LogLevel level, string message)
            {
                if (fired || !message.Contains("Could not generate path from")) return;
                fired = true;
                BotPoi.Current = new BotPoi(PoiType.Kill);
                mesh.OverrideCurrentPath(next);
            }
            Logging.OnMessageLogged += Replace;
            try
            {
                Check(mesh.MoveTo(End) == MoveResult.Failed && fired, "controlled failed-query callback was not exercised");
                Check(mesh.CurrentPath.SequenceEqual(next), "stale query callback overwrote successor path");
                Check(mesh.LastRouteFailure == RouteFailureReason.None && mesh.NextRouteRetryUtc == DateTime.MinValue,
                    "obsolete failed path installed terminal failure/cooldown on successor route");
            }
            finally { Logging.OnMessageLogged -= Replace; }
            Console.WriteLine("PASS retained navigation context: failed query callback");
        }
        internal void StaleConnectorAvoidanceCallback()
        {
            var old = Navigator.NavAvoidWaypointProvider;
            var next = new[] { new WoWPoint(80, 90, 100), new WoWPoint(110, 120, 130) };
            int stops = Stops;
            Navigator.NavAvoidWaypointProvider = _ => { mesh.OverrideCurrentPath(next); return next; };
            try
            {
                var player = source.GetType().GetField("player", Hidden)!.GetValue(source)!;
                typeof(MeshNavigator).GetMethod("ContinueLocalConnector", Hidden)!.Invoke(mesh, new[] { player, (object)End });
                Check(mesh.CurrentPath.SequenceEqual(next) && Stops == stops, "old connector avoidance callback cleared/stopped successor route");
            }
            finally { Navigator.NavAvoidWaypointProvider = old; }
            Console.WriteLine("PASS retained navigation context: connector avoidance callback");
        }
        public void Dispose()
        {
            typeof(Navigator).GetField("_meshNavigator", StaticHidden)!.SetValue(null, oldMesh);
            typeof(ProfileManager).GetField("_currentProfile", StaticHidden)!.SetValue(null, oldProfile);
            typeof(BotPoi).GetField("_current", StaticHidden)!.SetValue(null, oldPoi);
            ((IDisposable)source).Dispose();
        }
    }
}
