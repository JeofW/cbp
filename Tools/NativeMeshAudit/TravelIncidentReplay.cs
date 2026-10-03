using System.Numerics;
using System.Text.Json;
using Tripper.Navigation;

/// <summary>Real map530 path/endpoint observations for the reported journeys; no client attachment.</summary>
internal static class TravelIncidentReplay
{
    internal static void Run(Navigator native, string output)
    {
        const uint map = 530;
        const string logHash = "f2836bc1a31b16b8b4977cead7da816d509ba499430feba7fdaeae41a55c4783";
        var journeys = new[]
        {
            (Name: "Hagash-repair", Start: new Vector3(-989.65204f,3218.9448f,45.25728f), End: new Vector3(-1329.01f,2397.58f,89.1584f)),
            (Name: "Aledis-turn-in", Start: new Vector3(-1328.8708f,2398.1316f,89.101585f), End: new Vector3(-689.583f,4167.8f,58.5228f))
        };
        var records = new List<object>();
        int positiveEndpointObservations = 0, completedLocalControls = 0;
        float[] XYZ(Vector3 point) => new[] { point.X, point.Y, point.Z };
        foreach (var journey in journeys)
        {
            // The long route is the actual log input. The nearby two-yard control
            // is explicitly synthetic and uses a separately observed mesh snap.
            native.EnsureTilesAroundPosition(map, journey.Start, 64);
            if (!native.FindNearestPolyRef(map, journey.Start, out _, out var localStart)
                || !native.FindNearestPolyRef(map, journey.Start + new Vector3(2,0,0), out _, out var localEnd))
                throw new InvalidDataException("Missing local incident mesh control: " + journey.Name);
            foreach (bool local in new[] { false, true })
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    Vector3 start = local ? localStart : journey.Start, end = local ? localEnd : journey.End;
                    PathFindResult path = native.FindPath(map, start, end);
                    var points = path.Points ?? Array.Empty<Vector3>();
                    bool full = path.Succeeded && !path.IsPartialPath && !path.Aborted
                        && (path.Status.Value & ((1u<<2)|(1u<<4)|(1u<<5))) == 0
                        && points.Length >= 2 && Vector3.Distance(points[0], start) <= 5
                        && Vector3.Distance(points[^1], end) <= 5;
                    bool marker = points.Length >= 2 && path.Flags.Length == points.Length
                        && path.Polygons.Length == points.Length && path.PolyTypes.Length == points.Length
                        && path.Flags[^1] == StraightPathFlags.End && !path.Polygons[^1].IsValid && path.PolyTypes[^1] == 0;
                    Vector3? snapped = null; byte? observedArea = null; bool positive = false;
                    if (full && marker && native.FindNearestPolyRef(map, points[^1], out var polygon, out var point) && polygon.IsValid)
                    {
                        var status = new Status(native.GetPolyArea(map, polygon, out byte area));
                        snapped = point;
                        if (status.Succeeded) observedArea = area;
                        positive = status.Succeeded && Vector3.DistanceSquared(point, points[^1]) <= .25f
                            && (AreaType)area is AreaType.Ground or AreaType.Road or AreaType.Horde or AreaType.Alliance or AreaType.KnownBuilding;
                    }
                    if (positive) positiveEndpointObservations++;
                    if (local && full) completedLocalControls++;
                    records.Add(new {
                        journey = journey.Name, repeat, map,
                        input_kind = local ? "synthetic-nearby-mesh-control" : "reported-production-coordinates",
                        log_sha256 = logHash, requested_start = XYZ(start), requested_end = XYZ(end),
                        succeeded = path.Succeeded, partial = path.IsPartialPath, status = path.Status.Value,
                        complete_requested_route = full, points = points.Select(XYZ).ToArray(),
                        flags = path.Flags.Select(value => value.ToString()).ToArray(),
                        polygons = path.Polygons.Select(value => value.Id.ToString()).ToArray(),
                        areas = path.PolyTypes.Select(value => value.ToString()).ToArray(),
                        terminal_end_without_polygon = marker,
                        independently_snapped_endpoint = snapped.HasValue ? XYZ(snapped.Value) : null,
                        independently_observed_endpoint_area = observedArea,
                        positive_endpoint_observation = positive,
                        physical_collision_proven = false, movement_issued = false, live_accepted = false
                    });
                    Console.WriteLine($"TRAVEL_NATIVE {journey.Name}/{(local?"local-control":"reported-route")}/{repeat}: full={full}, terminalMarker={marker}, endpointObserved={positive}");
                }
        }
        File.WriteAllText(Path.Combine(output, "travel-incident-routes.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "travel-incident-summary.json"), JsonSerializer.Serialize(new {
            map, log_sha256 = logHash, native_path_calls = records.Count, completed_local_controls = completedLocalControls,
            independently_observed_terminal_areas = positiveEndpointObservations, game_attached = false,
            physical_collision_proven = false, live_completion_proven = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (completedLocalControls != 4 || positiveEndpointObservations < 4)
            throw new InvalidDataException("The real native endpoint premise was not established for both incident regions.");
    }
}
