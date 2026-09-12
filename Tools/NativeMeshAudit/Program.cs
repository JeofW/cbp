using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Tripper.Navigation;
using NativeNavigator = Tripper.Navigation.Navigator;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: NativeMeshAudit <repository-root> <results-directory>");
    return 2;
}
string root = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
Environment.CurrentDirectory = root;
var json = new JsonSerializerOptions { WriteIndented = true };
var nativeMessages = new List<string>();
var tiles = new List<object>();
var samples = new List<object>();
int pathsProduced = 0;
float[] XYZ(Vector3 point) => new[] { point.X, point.Y, point.Z };
string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
void Save(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, json));
try
{
    if (!Directory.Exists(Path.Combine(root, "mmaps"))) throw new DirectoryNotFoundException("Mesh directory is absent.");
    if (Styx.WoWInternals.ObjectManager.Wow != null || Styx.WoWInternals.ObjectManager.Me != null)
        throw new InvalidOperationException("This harness must never attach to a game client.");
    Save("environment.json", new
    {
        game_attached = false, process_bits = IntPtr.Size * 8, framework = Environment.Version.ToString(),
        native_dll_sha256 = Hash(Path.Combine(AppContext.BaseDirectory, "Navigation.dll")),
        map = 1, map_identity = "Kalimdor; captured Thunder Bluff witness",
        note = "Static native path replay only. No platform position, attachment or client collision is simulated as real."
    });
    using var navigator = new NativeNavigator();
    navigator.LogMessage += message => nativeMessages.Add(message);
    navigator.OnNavigatorLogMessage += message => nativeMessages.Add(message);
    navigator.TileLoaded += (_, tile) => tiles.Add(new { map = tile.MapId, x = tile.TileX, y = tile.TileY });
    if (!navigator.LoadMeshes()) throw new InvalidOperationException("Native wrapper failed to initialize.");
    if (!navigator.SetQueryFilterByStored("Horde")) throw new InvalidOperationException("Expected the production Horde filter.");
    var grod = new Vector3(-1152.76f, 71.41f, 145.87f);
    var lower = new Vector3(-1350.26f, 167.19f, 48.68f);
    var partialOrigin = new Vector3(-1135.72f, 142.12f, 91.15f);
    var routes = new (string Name, Vector3 Start, Vector3 End, string Provenance)[]
    {
        ("grod-lower-origin", lower, grod, "2026-09-12_1701_45740.log 18:20:46.675; vendor POI"),
        ("grod-partial-origin", partialOrigin, grod, "2026-09-12_1701_45740.log 18:21:20.653"),
        ("grod-reverse", grod, lower, "synthetic reverse of captured endpoints"),
        ("grod-local-control", grod, grod + new Vector3(2, 0, 0), "synthetic nearby control; not presumed walkable"),
        ("lower-local-control", lower, lower + new Vector3(2, 0, 0), "synthetic nearby control; not presumed walkable")
    };
    foreach (var route in routes)
    {
        // The first request in the process is cold; later first requests may share tiles.
        for (int repeat = 0; repeat < 4; repeat++)
        {
            int tileStart = tiles.Count;
            var timer = Stopwatch.StartNew();
            PathFindResult path = navigator.FindPath(1, route.Start, route.End);
            timer.Stop();
            if (path.Succeeded && path.Points.Length > 0) pathsProduced++;
            var points = path.Points ?? Array.Empty<Vector3>();
            if (points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
                throw new InvalidDataException("Native path contained non-finite coordinates.");
            samples.Add(new
            {
                route = route.Name, provenance = route.Provenance, repeat,
                cold_process = samples.Count == 0, requested_start = XYZ(route.Start), requested_end = XYZ(route.End),
                succeeded = path.Succeeded, partial = path.IsPartialPath, status = path.Status.Value,
                fail_step = path.FailStep.ToString(), count = points.Length,
                wall_ms = timer.Elapsed.TotalMilliseconds, wrapper_ms = path.Elapsed.TotalMilliseconds,
                new_tile_callbacks = tiles.Count - tileStart,
                endpoint_distance = points.Length > 0 ? (double?)Vector3.Distance(points[^1], route.End) : null,
                endpoint_z_gap = points.Length > 0 ? (double?)Math.Abs(points[^1].Z - route.End.Z) : null,
                points = points.Select(XYZ).ToArray(), flags = path.Flags.Select(f => f.ToString()).ToArray(),
                areas = path.PolyTypes.Select(f => f.ToString()).ToArray(), polygons = path.Polygons.Select(p => p.Id.ToString()).ToArray()
            });
            Console.WriteLine($"{route.Name}[{repeat}] success={path.Succeeded} partial={path.IsPartialPath} points={points.Length} wall={timer.Elapsed.TotalMilliseconds:F3}ms wrapper={path.Elapsed.TotalMilliseconds:F3}ms");
            Save("routes.json", samples);
        }
    }
    var nearest = new List<object>();
    foreach (var point in new[] { lower, partialOrigin, grod })
    {
        bool found = navigator.FindNearestPolyRef(1, point, out var polygon, out var location);
        nearest.Add(new { requested = XYZ(point), found, polygon = polygon.Id.ToString(), location = found ? XYZ(location) : null });
    }
    Save("nearest-polygons.json", nearest);
    Save("result.json", new { game_attached = false, native_calls = samples.Count, paths_produced = pathsProduced,
        asset_compatibility_demonstrated = pathsProduced > 0, live_route_acceptance = false });
    if (pathsProduced == 0) throw new InvalidOperationException("No path was produced. Loader/DLL/mesh compatibility is NOT established.");
    return 0;
}
catch (Exception error)
{
    File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString());
    Console.Error.WriteLine(error);
    return 1;
}
finally
{
    Save("routes.json", samples);
    Save("tile-callbacks.json", tiles);
    File.WriteAllLines(Path.Combine(output, "native-log.txt"), nativeMessages);
}
