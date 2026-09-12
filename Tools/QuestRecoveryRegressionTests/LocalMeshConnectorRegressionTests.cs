using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.World;

internal static class LocalMeshConnectorRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var origin = new WoWPoint(10, 10, 10);
        var landing = new WoWPoint(12, 10, 9);
        Assert(LocalMeshConnector.IsCandidateWithinLimits(origin, landing, landing), "short nearby landing should qualify");
        Assert(!LocalMeshConnector.IsCandidateWithinLimits(origin, landing, landing.Add(2, 0, 0)), "large horizontal snaps are unsafe");
        Assert(!LocalMeshConnector.IsCandidateWithinLimits(origin, new WoWPoint(16, 10, 10), new WoWPoint(16, 10, 10)), "long connectors are unsafe");
        Assert(!LocalMeshConnector.IsCandidateWithinLimits(origin, new WoWPoint(12, 10, 7), new WoWPoint(12, 10, 7)), "large height drops are unsafe");
        Assert(LocalMeshConnector.ValidateGroundCorridor(origin, landing, SafeTrace, _ => false), "continuous supported descending ground should qualify");
        Assert(!LocalMeshConnector.ValidateGroundCorridor(origin, landing, MissingSupport, _ => false), "unsupported gaps must be rejected");
        Assert(!LocalMeshConnector.ValidateGroundCorridor(origin, landing, UnsafeStep, _ => false), "steep local steps must be rejected");
        Assert(!LocalMeshConnector.ValidateGroundCorridor(origin, landing, BlockedTrace, _ => false), "body clearance collision must be rejected");
        Assert(!LocalMeshConnector.ValidateGroundCorridor(origin, landing, LiquidTrace, _ => false), "liquid crossings must be rejected");
        Assert(!LocalMeshConnector.ValidateGroundCorridor(origin, landing, SafeTrace, _ => true), "blackspotted corridors must be rejected");
        Assert(!LocalMeshConnector.ValidateGroundCorridor(origin, landing, UnavailableTrace, _ => false), "unavailable collision results must fail closed");
        Assert(!LocalMeshConnector.HasUsableOnwardPath(new Tripper.Navigation.PathFindResult(), landing, new WoWPoint(30,10,9)), "missing native path must reject connector");
        var destination = new WoWPoint(30, 10, 9);
        var route = new Tripper.Navigation.PathFindResult
        {
            Status = Tripper.Navigation.Status.Success,
            Points = new[] { new System.Numerics.Vector3(landing.X, landing.Y, landing.Z),
                new System.Numerics.Vector3(destination.X, destination.Y, destination.Z) }
        };
        Assert(LocalMeshConnector.HasUsableOnwardPath(route, landing, destination), "a complete native route should qualify");
        route.IsPartialPath = true;
        Assert(!LocalMeshConnector.HasUsableOnwardPath(route, landing, destination), "another partial native path cannot justify the connector");
        Assert(LocalMeshConnector.ValidateGroundCorridor(new WoWPoint(11.3f, 10, 9.35f), landing, SafeTrace, _ => false),
            "an active connector must remain valid through the last 0.65 to 0.75 yards before arrival");
    }

    private static bool SafeTrace(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags, out bool[] hits, out WoWPoint[] points)
    {
        hits = new bool[lines.Length]; points = new WoWPoint[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            bool vertical = lines[i].Start.Distance2DSqr(lines[i].End) < 0.001f;
            hits[i] = vertical && flags != GameWorld.CGWorldFrameHitFlags.HitTestLiquid;
            points[i] = new WoWPoint(lines[i].Start.X, lines[i].Start.Y, 10 - (lines[i].Start.X - 10) * 0.5f);
        }
        return true;
    }
    private static bool MissingSupport(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags, out bool[] hits, out WoWPoint[] points)
    { SafeTrace(lines, flags, out hits, out points); if (hits.Length > 1) hits[1] = false; return true; }
    private static bool UnsafeStep(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags, out bool[] hits, out WoWPoint[] points)
    { SafeTrace(lines, flags, out hits, out points); if (points.Length > 1) points[1] = points[1].Add(0, 0, -2); return true; }
    private static bool BlockedTrace(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags, out bool[] hits, out WoWPoint[] points)
    { SafeTrace(lines, flags, out hits, out points); if (lines.Length > 0 && lines[0].Start.Distance2DSqr(lines[0].End) > 0.001f) hits[0] = true; return true; }
    private static bool LiquidTrace(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags, out bool[] hits, out WoWPoint[] points)
    { SafeTrace(lines, flags, out hits, out points); if (flags == GameWorld.CGWorldFrameHitFlags.HitTestLiquid && hits.Length > 0) hits[0] = true; return true; }
    private static bool UnavailableTrace(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags, out bool[] hits, out WoWPoint[] points)
    { hits = Array.Empty<bool>(); points = Array.Empty<WoWPoint>(); return false; }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Local mesh connector regression: " + message); }
}
