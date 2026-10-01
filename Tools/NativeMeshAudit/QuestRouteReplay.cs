using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GreenMagic;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Tripper.Navigation;
using NativeNavigator = Tripper.Navigation.Navigator;
using HostNavigator = Styx.Logic.Pathing.Navigator;
using HostPoint = Styx.Logic.Pathing.WoWPoint;

// Real native and MeshNavigator queries. Only the actor's map/location/alive
// observations are supplied locally; there is no executor, client attachment,
// movement command, physical collision world or server acknowledgement.
internal static class QuestRouteReplay
{
    internal static void Run(NativeNavigator native, string inputPath, string output)
    {
        byte[] input = File.ReadAllBytes(inputPath);
        using var document = JsonDocument.Parse(input);
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        if (cases.Length == 0 || cases.Length > 2048) throw new InvalidDataException("Bound route population is empty or unbounded");
        using var context = new MapObservationContext(native);
        var mesh = new MeshNavigator();
        var records = new List<object>();
        var accepted = new List<string>();
        var incomplete = new List<string>();
        int nativeCalls = 0;
        float[] Coordinates(Vector3 value) => new[] { value.X, value.Y, value.Z };
        Vector3 Point(JsonElement value)
        {
            if (value.GetArrayLength() != 3) throw new InvalidDataException("Expected exact XYZ coordinates");
            var point = new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new InvalidDataException("A route input is non-finite");
            return point;
        }
        foreach (var item in cases)
        {
            string id = item.GetProperty("case_id").GetString()!;
            uint map = item.GetProperty("map_id").GetUInt32();
            Vector3 start = Point(item.GetProperty("start")), end = Point(item.GetProperty("end"));
            for (int scenario = 0; scenario < 3; scenario++)
            {
                // A deliberately missing map and raised endpoint are fault
                // observations, never source-backed replacements for the route.
                uint selectedMap = scenario == 2 ? 999u : map;
                Vector3 target = scenario == 1 ? end + new Vector3(0, 0, 120) : end;
                context.Select(selectedMap, start);
                var from = new HostPoint(start.X, start.Y, start.Z);
                var to = new HostPoint(target.X, target.Y, target.Z);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var timer = Stopwatch.StartNew();
                    PathFindResult path = mesh.FindPath(from, to); nativeCalls++;
                    timer.Stop();
                    var points = path.Points ?? Array.Empty<Vector3>();
                    if (points.Any(value => !float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)))
                        throw new InvalidDataException("Native output contains non-finite waypoints");
                    bool resourceLimited = (path.Status.Value & ((1u << 2) | (1u << 4) | (1u << 5))) != 0;
                    double? startGap = points.Length > 0 ? Vector3.Distance(points[0], start) : null;
                    double? endGap = points.Length > 0 ? Vector3.Distance(points[^1], target) : null;
                    bool full = path.Succeeded && !path.IsPartialPath && !resourceLimited && points.Length > 0
                        && startGap <= 5 && endGap <= 5;
                    bool reportedFull = mesh.CanNavigateFully(from, to); nativeCalls++;
                    float? reportedDistance = mesh.PathDistance(from, to); nativeCalls++;
                    if (reportedDistance.HasValue && !float.IsFinite(reportedDistance.Value))
                        throw new InvalidDataException("MeshNavigator returned a non-finite route distance");
                    if (selectedMap == 999 && (full || reportedFull || reportedDistance.HasValue))
                        throw new InvalidDataException("An unprovided map was accepted as a complete route");
                    double length = 0;
                    for (int index = 1; index < points.Length; index++) length += Vector3.Distance(points[index - 1], points[index]);
                    double direct = Vector3.Distance(start, target);
                    string scope = scenario == 0 ? "source-chain" : scenario == 1 ? "raised-endpoint-fault" : "missing-map-fault";
                    string tier = scenario == 0 && full ? "NAVMESH-PROVEN" : scenario == 0 ? "DATA-PROVEN" : "FAULT-OBSERVATION";
                    if (scenario == 0 && repeat == 0) (full ? accepted : incomplete).Add(id);
                    records.Add(new { case_id = id, quest_id = item.GetProperty("quest_id").GetUInt32(),
                        leg = item.GetProperty("leg").GetString(), input_kind = scope, repeat, map_id = selectedMap,
                        requested_start = Coordinates(start), requested_end = Coordinates(target),
                        points = points.Select(Coordinates).ToArray(), status = path.Status.Value,
                        succeeded = path.Succeeded, partial = path.IsPartialPath, resource_limited = resourceLimited,
                        fail_step = path.FailStep.ToString(), start_gap = startGap, endpoint_gap = endGap,
                        endpoint_z_gap = points.Length > 0 ? (double?)Math.Abs(points[^1].Z - target.Z) : null,
                        path_length = length, direct_distance = direct, detour_ratio = direct > 0 ? (double?)(length / direct) : null,
                        zero_displacement = direct <= 0.001, actual_mesh_can_navigate_fully = reportedFull,
                        actual_mesh_path_distance = reportedDistance, strict_requested_endpoints_complete = full,
                        reported_full_needs_endpoint_review = reportedFull && !full,
                        flags = path.Flags.Select(value => value.ToString()).ToArray(),
                        areas = path.PolyTypes.Select(value => value.ToString()).ToArray(),
                        wall_ms = timer.Elapsed.TotalMilliseconds, native_ms = path.NativeCallElapsed.TotalMilliseconds,
                        proof_tier = tier, physical_collision_route_replay_proven = false, live_accepted = false });
                    Console.WriteLine($"QUEST_NAV {id}/{scope}/{repeat}: full={full},reportedFull={reportedFull},partial={path.IsPartialPath},points={points.Length},tier={tier}");
                }
            }
        }
        if (ObjectManager.Executor != null) throw new InvalidOperationException("An executor appeared in the offline query process");
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "quest-routes.json"), JsonSerializer.Serialize(records, options));
        File.WriteAllText(Path.Combine(output, "quest-route-summary.json"), JsonSerializer.Serialize(new {
            input_sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(),
            exact_quest_ids = cases.Select(value => value.GetProperty("quest_id").GetUInt32()).Distinct().Order().ToArray(),
            source_chain_legs = cases.Length, native_calls = nativeCalls, source_legs_with_complete_endpoints = accepted,
            source_legs_requiring_route_resolution = incomplete, real_mesh_navigator_used = true,
            synthetic_actor_observation_only = true, query_memory_access = "read-only current test process; map served from explicit observation cache",
            movement_or_interaction_issued = false, physical_collision_proven = false, live_completion_proven = false,
            note = "Navmesh connectivity does not establish support, slopes, lava safety, collision clearance or transport execution. Fault inputs and synthetic origins are explicitly labelled." }, options));
    }

    private sealed class QueryPlayer : LocalPlayer
    {
        internal QueryPlayer() : base(0) { }
        internal HostPoint Position;
        public override HostPoint Location => Position;
        public override bool IsAlive => true;
    }

    private sealed class MapObservationContext : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        private readonly Memory memory = (Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));
        private readonly ThreadLocal<Dictionary<IntPtr, byte[]>> cache = new(() => new());
        private readonly ThreadLocal<bool> enabled = new(() => true);
        private readonly QueryPlayer player = new();
        private readonly FieldInfo nativeField = typeof(HostNavigator).GetField("_navigator", Hidden)!;
        private object? previousNative;
        private IntPtr handle;
        private readonly uint mapAddress = (uint)typeof(LocalPlayer).GetField("MapIdPtr", Hidden)!.GetRawConstantValue()!;
        internal MapObservationContext(NativeNavigator native)
        {
            if (ObjectManager.Wow != null || ObjectManager.Me != null || ObjectManager.Executor != null)
                throw new InvalidOperationException("The query fixture requires an unattached process");
            try
            {
                previousNative = nativeField.GetValue(null);
                SetMemory("_cache", cache); SetMemory("_cacheEnabled", enabled);
                handle = OpenProcess(0x0010, false, Environment.ProcessId);
                if (handle == IntPtr.Zero) throw new InvalidOperationException("Read-only self-process handle was unavailable");
                SetMemory("_hProcess", handle);
                // Landmarks initialization may request these existing observed
                // globals. They are empty local observations, never client reads.
                cache.Value![new IntPtr(12488416)] = BitConverter.GetBytes(0);
                cache.Value![new IntPtr(12488476)] = BitConverter.GetBytes(0u);
                typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, memory);
                ObjectManager.Me = player;
                nativeField.SetValue(null, native);
            }
            catch { Dispose(); throw; }
        }
        internal void Select(uint map, Vector3 start)
        {
            cache.Value![new IntPtr(unchecked((int)mapAddress))] = BitConverter.GetBytes(map);
            player.Position = new HostPoint(start.X, start.Y, start.Z);
            if (player.MapId != map || !ReferenceEquals(ObjectManager.Me, player) || ObjectManager.Executor != null)
                throw new InvalidOperationException("The real map reader did not retain the controlled query observation");
        }
        private void SetMemory(string name, object value) => typeof(Memory).GetField(name, Hidden)!.SetValue(memory, value);
        public void Dispose()
        {
            if (ReferenceEquals(ObjectManager.Me, player)) ObjectManager.Me = null;
            if (ReferenceEquals(ObjectManager.Wow, memory)) typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, null);
            nativeField.SetValue(null, previousNative);
            SetMemory("_hProcess", IntPtr.Zero);
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
            cache.Dispose(); enabled.Dispose(); GC.SuppressFinalize(memory);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
