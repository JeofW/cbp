using CommonBehaviors.Actions;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using TreeSharp;

int passed = 0, total = 0;
var failures = new List<string>();
void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
void Case(string name, System.Action<ActionMoveToPoi> body)
{
    total++; World.Reset(); var action = new ActionMoveToPoi(); action.Start(null!);
    try { body(action); Check(World.Errors.Count == 0, "production exception was swallowed: " + string.Join(";", World.Errors)); passed++; Console.WriteLine("PASS indoor integration: " + name); }
    catch (Exception error)
    {
        failures.Add(name + ": " + error + "\n" + string.Join("\n", World.Errors));
        Console.Error.WriteLine("FAIL indoor integration: " + name + ": " + error.Message);
        if (World.Errors.Count != 0 && failures.Count == 1) Console.Error.WriteLine("PRODUCTION ERROR: " + World.Errors[0]);
    }
    finally { World.Callback = null; World.ObservationError = null; action.Stop(null!); Styx.BotEvents.Stop(); }
}
RunStatus Tick(ActionMoveToPoi action) => action.Tick(null!);
void ReachExterior(ActionMoveToPoi action)
{
    for (int n = 0; n < 40 && World.ExteriorFlights.Count == 0; n++)
        Check(Tick(action) == RunStatus.Running, "interior approach ended before a validated exterior request");
    Check(World.ExteriorFlights.Count == 1 && World.RawFlights.Count == 0, "direct interior flight escaped the transition owner");
    Check(!World.UnderRoof(World.ExteriorFlights[0]), "covered coordinate was selected as an exterior flight region");
}
foreach (PoiType type in new[] { PoiType.QuestPickUp, PoiType.QuestTurnIn, PoiType.Buy, PoiType.Sell, PoiType.Repair, PoiType.Train, PoiType.Mail, PoiType.Fly, PoiType.InnKeeper })
    Case("indoor target/" + type, action => { BotPoi.Current.Type = type; ReachExterior(action); });

Case("exterior request, descent and dismount each await observations", action =>
{
    ReachExterior(action);
    World.Actor.Position = World.ExteriorFlights[0];
    Check(Tick(action) == RunStatus.Running && World.Descents == 1 && World.Dismounts == 0, "descent request became landing");
    World.Actor.Position = World.Actor.Position.Add(0, 0, -4); World.Actor.Flags = 0; World.Actor.MovementInfo.IsDescending = true;
    Check(Tick(action) == RunStatus.Running && World.Dismounts == 0, "landing did not stop retained descent before dismount");
    World.Actor.MovementInfo.IsDescending = false;
    Check(Tick(action) == RunStatus.Running && World.Dismounts == 1, "grounded mount did not acquire one removal request");
    Check(Tick(action) == RunStatus.Running && World.Dismounts == 1 && World.Walks.Count == 0, "submission was mistaken for mount removal");
    World.Actor.MountedValue = false;
    // The transition's normal command throttle is monotonic; the flight request
    // timestamp must be old enough before we assert an eventual ground dispatch.
    var clock = System.Diagnostics.Stopwatch.StartNew();
    while (World.Walks.Count == 0 && clock.ElapsedMilliseconds < 1000) { Tick(action); Thread.Yield(); }
    Check(World.Walks.SequenceEqual(new[] { World.Target.Position }), "observed unmount did not hand the final leg to ground mesh");
    Check(action.LastStatus == RunStatus.Running, "mesh dispatch was treated as interaction arrival");
    World.Actor.Position = World.Target.Position.Add(1, 0, 0); World.Sight = false;
    Check(Tick(action) == RunStatus.Running, "nearby target across a wall was considered interactable");
    World.Sight = true;
    Check(Tick(action) == RunStatus.Success, "current grounded, unmounted, in-range, visible target never became ready");
});
Case("ground mount close to indoor target still waits for removal", action =>
{
    World.Actor.Position = World.Target.Position; World.Actor.Flags = 0;
    Check(Tick(action) == RunStatus.Running && World.Dismounts == 1 && World.RawFlights.Count == 0, "close mounted target bypassed dismount acknowledgement");
    Check(Tick(action) == RunStatus.Running && World.Dismounts == 1, "ground handoff repeated dismount");
    World.Actor.MountedValue = false; Check(Tick(action) == RunStatus.Success, "observed removal failed to admit interaction");
});
Case("missing mesh never commands a final interior coordinate", action =>
{
    Navigator.IsNavigatorLoaded = false;
    Check(Tick(action) == RunStatus.Running, "missing mesh was mistaken for completion");
    Check(World.RawFlights.Count + World.ExteriorFlights.Count + World.Dismounts == 0, "missing mesh fabricated approach authority");
});
Case("diagnostics preserve XYZ and unavailable waypoint without aborting travel", action =>
{
    Navigator.IsNavigatorLoaded = false;
    Check(Tick(action) == RunStatus.Running, "diagnostic serialization aborted the pending owner");
    const string prefix = "[GroundTransition] ";
    string payload = World.Diagnostics.Single(line => line.StartsWith(prefix))[prefix.Length..];
    using var json = System.Text.Json.JsonDocument.Parse(payload);
    var row = json.RootElement;
    Check(row.GetProperty("ActorPosition").GetProperty("X").GetSingle() == World.Actor.Position.X
        && row.GetProperty("Destination").GetProperty("Z").GetSingle() == World.Target.Position.Z,
        "diagnostic omitted raw coordinate fields");
    Check(row.GetProperty("FlightWaypoint").ValueKind == System.Text.Json.JsonValueKind.Null,
        "missing waypoint became an invented zero coordinate");
});
Case("approach diagnostics retain collision segments and support observations", action =>
{
    ReachExterior(action);
    string line = World.Diagnostics.Last();
    using var json = System.Text.Json.JsonDocument.Parse(line["[GroundTransition] ".Length..]);
    var row = json.RootElement;
    Check(row.TryGetProperty("Rays", out var rays) && rays.GetArrayLength() > 0,
        "approach discarded the collision rays needed to explain wall/roof decisions");
    Check(row.TryGetProperty("Supported", out var support) && support.ValueKind == System.Text.Json.JsonValueKind.False,
        "flight diagnostic did not preserve the observed unsupported state");
    Check(row.TryGetProperty("ActorOutdoors", out var outdoors) && outdoors.GetBoolean()
        && !row.GetProperty("TargetOutdoors").GetBoolean(), "actor and target outdoor observations were conflated");
    foreach (var ray in rays.EnumerateArray())
    {
        Check(ray.GetProperty("Start").TryGetProperty("X", out _) && ray.GetProperty("End").TryGetProperty("Z", out _)
            && ray.GetProperty("Flags").GetUInt32() != 0, "trace diagnostic lacks its actual segment/mask");
        if (ray.GetProperty("Hit").ValueKind == System.Text.Json.JsonValueKind.False)
            Check(ray.GetProperty("Point").ValueKind == System.Text.Json.JsonValueKind.Null, "clear ray fabricated a hit coordinate");
    }
});
foreach (string unavailable in new[] { "partial", "wrong-floor", "liquid", "closed-door" })
    Case("unproven approach/" + unavailable, action =>
    {
        World.PartialPath = unavailable == "partial"; World.WrongFloor = unavailable == "wrong-floor";
        World.Liquid = unavailable == "liquid"; World.BlockedDoor = unavailable == "closed-door";
        for (int n = 0; n < 110; n++) Check(Tick(action) == RunStatus.Running, "unavailable approach became success/fallback");
        Check(World.RawFlights.Count + World.ExteriorFlights.Count + World.Descents + World.Dismounts == 0, "unproven landing or onward path dispatched an effect");
    });
Case("Stop invalidates retained exterior admission", action =>
{
    ReachExterior(action); int count = World.ExteriorFlights.Count; action.Stop(null!);
    Check(Tick(action) == RunStatus.Failure && World.ExteriorFlights.Count == count, "stopped action dispatched another route");
    Check(World.Stops > 0, "Stop left the transition's owned input running");
});
foreach (string change in new[] { "actor", "map", "profile", "provider", "poi", "run", "subject", "dead" })
    Case("retained route revoked by " + change, action =>
    {
        ReachExterior(action); int dispatched = World.ExteriorFlights.Count;
        switch (change)
        {
            case "actor": ObjectManager.Me = new() { Guid = 7, BaseAddress = 700 }; break;
            case "map": World.Actor.MapId++; break;
            case "profile": ProfileManager.CurrentProfileSnapshot = new(); break;
            case "provider": Navigator.NavigationProvider = new MeshNavigator(); break;
            case "poi": BotPoi.CurrentGeneration++; break;
            case "run": TreeRoot.RunIdentity = new(); break;
            case "subject": World.Target.Guid++; break;
            case "dead": World.Actor.IsAlive = false; break;
        }
        Check(Tick(action) == RunStatus.Failure && World.ExteriorFlights.Count == dispatched, "stale approach retained movement/arrival authority");
    });
Case("same NPC movement retains travel while final interaction remains unacknowledged", action =>
{
    ReachExterior(action);int stops=World.Stops;
    World.Target.Position=World.Target.Position.Add(0,1,0);
    Check(Tick(action)==RunStatus.Running&&World.Stops==stops,"same NPC coordinate movement revoked the continuing travel owner");
    Check(World.Interactions.Count==0&&World.Dismounts==0,"coordinate update acknowledged final interaction or landing");
});
Case("unknown mount observation holds the approach", action =>
{
    World.ObservationError = new ObservationUnavailableException("mount", "unavailable");
    Check(Tick(action) == RunStatus.Running && World.RawFlights.Count + World.Dismounts == 0, "unknown mount became unmounted permission");
});
Case("unloaded subject is denied without an exception", action =>
{
    Check(!GroundTransition.CanInteractWith(null!), "missing subject became interaction authority");
});
Case("same finite actor and subject point needs no zero-length native ray", action =>
{
    World.Actor.Position = World.Target.Position; World.Actor.MountedValue = false; World.Actor.Flags = 0;
    Check(GroundTransition.CanInteractWith(World.Target), "coincident finite positions dispatched an invalid zero-length query");
});
Case("a stopped bot cannot acquire a fresh interaction owner", action =>
{
    World.Actor.Position = World.Target.Position.Add(1, 0, 0); World.Actor.MountedValue = false; World.Actor.Flags = 0;
    TreeRoot.IsRunning = false;
    Check(!GroundTransition.CanInteractWith(World.Target), "stopped session acquired interaction permission");
});
Case("nonfinite position arriving during observation stops the old route", action =>
{
    ReachExterior(action); int issued = World.ExteriorFlights.Count;
    World.Callback = stage =>
    {
        if (stage != "outdoors") return;
        World.Callback = null; World.Actor.Position = new(float.NaN, 0, 0);
    };
    Check(Tick(action) == RunStatus.Running && World.ExteriorFlights.Count == issued && World.Dismounts + World.Descents == 0,
        "nonfinite actor observation dispatched movement or aborted its owner with a diagnostic error");
});
Case("grounded nearby interaction submits once without moving or inventing a UI reply", action =>
{
    World.Actor.Position = World.Target.Position.Add(1, 0, 0); World.Actor.MountedValue = false; World.Actor.Flags = 0;
    Check(GroundTransition.TryInteractWith(World.Target) && World.Interactions.SequenceEqual(new[] { World.Target.Guid }),
        "current observed ground approach did not submit its local interaction");
    Check(World.RawFlights.Count + World.ExteriorFlights.Count + World.Walks.Count + World.Dismounts == 0,
        "native interaction fabricated a travel or mount effect");
});
foreach (string change in new[] { "actor", "map", "profile", "provider", "poi", "run", "subject", "target-move", "actor-move", "mount", "airborne", "dead", "caller" })
    Case("native interaction admission rejects callback " + change, action =>
    {
        World.Actor.Position = World.Target.Position.Add(1, 0, 0); World.Actor.MountedValue = false; World.Actor.Flags = 0;
        bool permitted = true, fired = false;
        World.Callback = stage =>
        {
            if (stage != "interaction-prepare") return;
            World.Callback = null; fired = true;
            switch (change)
            {
                case "actor": ObjectManager.Me = new() { Guid = 7, BaseAddress = 700 }; break;
                case "map": World.Actor.MapId++; break;
                case "profile": ProfileManager.CurrentProfileSnapshot = new(); break;
                case "provider": Navigator.NavigationProvider = new MeshNavigator(); break;
                case "poi": BotPoi.CurrentGeneration++; break;
                case "run": TreeRoot.RunIdentity = new(); break;
                case "subject": World.Target.Guid++; break;
                case "target-move": World.Target.Position = World.Target.Position.Add(1, 0, 0); break;
                case "actor-move": World.Actor.Position = World.Actor.Position.Add(1, 0, 0); break;
                case "mount": World.Actor.MountedValue = true; break;
                case "airborne": World.Actor.Flags = 0x02000000; break;
                case "dead": World.Actor.IsAlive = false; break;
                case "caller": permitted = false; break;
            }
        };
        Check(!GroundTransition.TryInteractWith(World.Target, () => permitted) && fired && World.Interactions.Count == 0,
            "replaced native interaction owner reached entry");
    });
Case("post-entry revocation does not claim a current interaction receipt", action =>
{
    World.Actor.Position = World.Target.Position.Add(1, 0, 0); World.Actor.MountedValue = false; World.Actor.Flags = 0;
    World.Callback = stage => { if (stage == "interaction-entry") { World.Callback = null; BotPoi.CurrentGeneration++; } };
    Check(!GroundTransition.TryInteractWith(World.Target) && World.Interactions.Count == 1,
        "post-entry replacement fabricated a current acknowledgement or repeated the native entry");
});

GroundRuntimeCases.Run((name, body) => Case(name, _ => body()));
FlightCorridorCases.Run((name, body) => Case(name, body));

var vendor = new VendorApproachProbe(Directory.GetCurrentDirectory());
foreach (PoiType type in new[] { PoiType.Buy, PoiType.Sell, PoiType.Repair, PoiType.Train, PoiType.Mail, PoiType.Fly })
    foreach (string condition in new[] { "grounded", "mounted", "flying", "wall" })
        Case("actual vendor admission/" + type + "/" + condition, action =>
        {
            BotPoi.Current.Type = type;
            World.Actor.Position = World.Target.Position.Add(1, 0, 0);
            World.Actor.MountedValue = condition is "mounted" or "flying";
            World.Actor.Flags = condition == "flying" ? 0x02000000u : 0;
            World.Sight = condition != "wall";
            bool ready = condition == "grounded";
            Check(vendor.Travel == !ready && vendor.Arrived == ready,
                "nearby vendor predicates bypassed the actual ground approach");
            RunStatus result = vendor.Interact();
            Check(ready ? result == RunStatus.Success && World.Interactions.Count == 1
                : result == RunStatus.Failure && World.Interactions.Count == 0,
                "vendor native action dispatched without observed ground admission");
        });
Case("actual vendor action rejects a replacement combat POI", action =>
{
    BotPoi.Current.Type = PoiType.Kill; World.Actor.Position = World.Target.Position.Add(1, 0, 0);
    World.Actor.MountedValue = false; World.Actor.Flags = 0;
    Check(vendor.Interact() == RunStatus.Failure && World.Interactions.Count == 0,
        "resumed service action interacted with a combat subject");
});

Console.WriteLine($"Indoor approach integration: {passed}/{total}; linked production action/context/transition/geometry/query adapter and TreeSharp; controlled client/mesh/movement leaves; no live/native traversal proof.");
if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
