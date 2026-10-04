// The production action, transition, context, geometry search and query adapter
// are linked unchanged. Only client/process/native mesh/movement leaves are
// controlled here. These observations are not native or live traversal proof.
using System.Numerics;
using Styx.Logic.Pathing;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using Tripper.Navigation;

internal static class World
{
    internal static LocalPlayer Actor = null!;
    internal static WoWUnit Target = null!;
    internal static MeshNavigator Mesh = null!;
    internal static readonly List<WoWPoint> RawFlights = new(), ExteriorFlights = new(), Walks = new();
    internal static readonly List<string> Diagnostics = new(), Errors = new();
    internal static readonly List<ulong> Interactions = new();
    internal static readonly List<(WorldLine Line, GameWorld.CGWorldFrameHitFlags Flags, bool Hit, WoWPoint Point)> Rays = new();
    internal static int Dismounts, Descents, Stops;
    internal static bool Sight, MissingMesh, PartialPath, WrongFloor, Liquid, BlockedDoor, MissingSupport;
    internal static bool PreferFlight, FlightMountObserved, FlightCostRequiresStop;
    internal static Exception? ObservationError;
    internal static float? FutureGeometryUnavailableAfterX;
    internal static System.Action<string>? Callback;
    internal static Func<WorldLine, GameWorld.CGWorldFrameHitFlags, (bool Hit, WoWPoint Point)>? CollisionOverride;
    internal static Func<WoWPoint, WoWPoint, WoWPoint>? AerialGoal;
    internal static Func<WoWPoint, WoWPoint, bool>? AerialSegment;
    internal static WoWPoint Door = new(205, 0, 0);

    internal static void Event(string stage) => Callback?.Invoke(stage);
    internal static void Reset()
    {
        Callback = null; CollisionOverride = null; ObservationError = null; FutureGeometryUnavailableAfterX = null;
        GroundTravelMount.Waiter = null;
        AerialGoal = null; AerialSegment = null;
        Styx.BotEvents.Stop();
        RawFlights.Clear(); ExteriorFlights.Clear(); Walks.Clear(); Diagnostics.Clear(); Errors.Clear(); Rays.Clear(); Interactions.Clear();
        Dismounts = Descents = Stops = 0;
        Sight = true; MissingMesh = PartialPath = WrongFloor = Liquid = BlockedDoor = MissingSupport = PreferFlight = FlightMountObserved = FlightCostRequiresStop = false;
        Door = new(205, 0, 0);
        Actor = new LocalPlayer { Guid = 1, BaseAddress = 100, Position = new(100, 10, 80), MountedValue = true, Flags = 0x02000000u };
        Target = new WoWUnit { Guid = 2, BaseAddress = 200, Entry = 70, Position = new(205, 15, 0), Outdoors = false };
        Styx.WoWInternals.ObjectManager.Me = Actor;
        Styx.WoWInternals.ObjectManager.Wow = new GreenMagic.Memory();
        Styx.WoWInternals.ObjectManager.Executor = new GreenMagic.ExecutorRand { Memory = Styx.WoWInternals.ObjectManager.Wow };
        Styx.WoWInternals.WoWMovement.ActiveMover = Actor;
        Styx.Logic.BehaviorTree.TreeRoot.RunIdentity = new object();
        Styx.Logic.BehaviorTree.TreeRoot.Current = new object();
        Styx.Logic.BehaviorTree.TreeRoot.IsRunning = true;
        Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot = new object();
        Mesh = new MeshNavigator(); Navigator.NavigationProvider = Mesh; Navigator.PlayerMover = new PlayerMover();
        Navigator.IsNavigatorLoaded = true; Flightor.RequestIdentity = new object(); Flightor.LastFlightWaypoint = WoWPoint.Empty;
        Styx.Logic.POI.BotPoi.Current = new Styx.Logic.POI.BotPoi { Type = Styx.Logic.POI.PoiType.QuestPickUp, Guid = Target.Guid, Entry = Target.Entry, Object = Target, Position = Target.Position };
        Styx.Logic.POI.BotPoi.CurrentGeneration++;
    }
    internal static bool UnderRoof(WoWPoint p) => p.X >= 190 && p.X <= 220 && p.Y >= 5 && p.Y <= 25;
    internal static (bool Hit, WoWPoint Point) Trace(WorldLine line, GameWorld.CGWorldFrameHitFlags flags)
    {
        Event("trace"); if (ObservationError != null) throw ObservationError;
        if (CollisionOverride != null)
        {
            var observed = CollisionOverride(line, flags);
            Rays.Add((line, flags, observed.Hit, observed.Point));
            return observed;
        }
        if (FutureGeometryUnavailableAfterX is float frontier && Math.Max(line.Start.X, line.End.X) > frontier)
            throw new Styx.Helpers.ObservationUnavailableException("future-flight-region", "controlled unavailable lookahead observation");
        bool liquid = (flags & (GameWorld.CGWorldFrameHitFlags.HitTestLiquid | GameWorld.CGWorldFrameHitFlags.HitTestLiquid2)) != 0;
        bool hit = false; WoWPoint point = WoWPoint.Empty;
        if (liquid) { hit = Liquid; point = line.End; }
        else if (line.Start.Distance2DSqr(line.End) <= .01f)
        {
            float high = Math.Max(line.Start.Z, line.End.Z), low = Math.Min(line.Start.Z, line.End.Z);
            if (UnderRoof(line.Start) && high >= 20 && low <= 20)
            { hit = true; point = new(line.Start.X, line.Start.Y, 20); }
            else if (!MissingSupport && high >= 0 && low <= 0)
            { hit = true; point = new(line.Start.X, line.Start.Y, 0); }
        }
        Rays.Add((line, flags, hit, point)); return (hit, point);
    }
}

namespace Styx.Helpers
{
    public interface IRangeAble { Range GetRange(); }
    public readonly record struct Range(int Minimum, int Maximum);
    public sealed class ObservationUnavailableException(string owner, string reason) : InvalidOperationException(owner + ": " + reason);
    public static class BlackspotManager { public static bool IsBlackspotted(WoWPoint point, float radius) => false; }
    public static class Logging
    {
        public static void Write(string format, params object[] args) { World.Event("log"); }
        public static void WriteDiagnostic(string format, params object[] args) { World.Diagnostics.Add(string.Format(format, args)); World.Event("report"); }
        public static void WriteException(Exception error) => World.Errors.Add(error.ToString());
    }
}
namespace Styx
{
    public static class StyxWoW { public static LocalPlayer Me => WoWInternals.ObjectManager.Me!; }
    public enum ShapeshiftForm { Normal, FlightForm, EpicFlightForm }
    public sealed class InvalidProcessException(string reason) : InvalidOperationException(reason);
    public sealed class InvalidExecutorException(string reason) : InvalidOperationException(reason);
    public static class BotEvents { public static event System.Action<object?>? OnBotStop; public static void Stop() => OnBotStop?.Invoke(null); }
}
namespace GreenMagic
{
    public sealed class Memory { public int ProcessId = 1; public IntPtr ProcessHandle = new(1); }
    public sealed class ExecutorRand { public bool IsOpen = true, IsInitialized = true; public Memory Memory = null!; }
}
namespace Styx.Logic.BehaviorTree
{
    public static class TreeRoot { public static object RunIdentity = new(), Current = new(); public static bool IsRunning = true; }
}
namespace Styx.Logic.Profiles { public static class ProfileManager { public static object CurrentProfileSnapshot = new(); } }
namespace Styx.Logic.POI
{
    public enum PoiType { None, Quest, QuestTurnIn, QuestPickUp, Hotspot, Kill, Loot, Skin, Harvest, Corpse, Buy, Sell, Repair, Train, Mail, Fly, InnKeeper }
    public sealed class BotPoi
    {
        public static BotPoi Current = new(); public static long CurrentGeneration;
        // This leaf's explicit generation mutations represent semantic replacement.
        // Coordinate-only distinction is tested with real BotPoi in CombatPoiTransition.
        public static long CurrentWorkGeneration => CurrentGeneration;
        public PoiType Type; public ulong Guid; public uint Entry; public bool IsWorldSubjectBlacklisted;
        public WoWObject? Object; public WoWPoint Position;
        public WoWObject? AsObject { get { var value = Object; World.Event("object"); return value; } }
        public WoWPoint Location => Position;
    }
}
namespace Styx.WoWInternals.WoWObjects
{
    public class WoWObject
    {
        public ulong Guid; public uint BaseAddress, Entry; public bool IsValid = true, Outdoors = true;
        public WoWPoint Position;
        public WoWPoint Location { get { var value = Position; global::World.Event("location"); return value; } }
        public bool IsOutdoors { get { global::World.Event("outdoors"); if (global::World.ObservationError != null) throw global::World.ObservationError; return Outdoors; } }
        public bool WithinInteractRange => global::World.Actor.Position.DistanceSqr(Position) <= 25;
        public WoWUnit? ToUnit() => this as WoWUnit;
        public void Interact() { global::World.Event("interaction-prepare"); global::World.Interactions.Add(Guid); global::World.Event("interaction-entry"); }
        internal bool TryInteractOwned(Func<bool> admitted, bool ignoreTimer)
        {
            global::World.Event("interaction-prepare");
            if (!admitted()) throw new Styx.Helpers.ObservationUnavailableException("interaction", "entry owner changed");
            global::World.Interactions.Add(Guid);
            global::World.Event("interaction-entry");
            if (!admitted()) throw new Styx.Helpers.ObservationUnavailableException("interaction", "post-entry owner changed");
            return true;
        }
    }
    public class WoWUnit : WoWObject { public bool IsAlive = true, IsMoving, IsQuestGiver; }
    // This suite exercises NPC approaches; GO effects have a separate actual
    // collection integration suite and must not be silently simulated here.
    public sealed class WoWGameObject : WoWObject
    {
        public bool IsDisabled;
        public float InteractRange = 5;
        public bool CanUse() => throw new InvalidOperationException("Unexpected game-object usability in NPC suite");
        public bool CanUseNow() => throw new InvalidOperationException("Unexpected game-object usability in NPC suite");
    }
    public sealed class Movement { public bool IsDescending; }
    public sealed class LocalPlayer : WoWUnit
    {
        public uint MapId = 530, Flags; public ulong Transport; public bool MovementKnown = true, MountedValue;
        public bool IsGhost, OnTaxi, IsOnTransport, IsCasting, IsSwimming, InVehicle, Rooted, Stunned, Combat;
        public int ChanneledCastingSpellId; public float BoundingRadius = .6f, BoundingHeight = 2;
        public ShapeshiftForm Shapeshift; public Movement MovementInfo = new();
        public bool IsFlying => (Flags & 0x02000000u) != 0;
        public bool Mounted { get { global::World.Event("mounted"); if (global::World.ObservationError != null) throw global::World.ObservationError; return MountedValue; } }
        public bool TryGetMovementState(out uint flags, out ulong transport) { flags = Flags; transport = Transport; global::World.Event("movement"); return MovementKnown; }
    }
}
namespace Styx.WoWInternals
{
    public static class ObjectManager
    {
        public static LocalPlayer? Me; public static GreenMagic.Memory? Wow; public static GreenMagic.ExecutorRand? Executor;
    }
    public static class WoWMovement
    {
        public enum MovementDirection { Descend }
        public static WoWUnit? ActiveMover;
        public static void Move(MovementDirection direction) { global::World.Descents++; global::World.Event("descend"); }
    }
}
namespace Tripper.Navigation { public readonly struct Status(int value) { public bool Succeeded => value == 0; } }
namespace Styx.Logic.Pathing
{
    public enum MoveResult { Moved, Failed }
    public interface IPlayerMover { void MoveStop(); }
    public sealed class PlayerMover : IPlayerMover { public void MoveStop() { World.Stops++; World.Event("stop"); } }
    public class NavigationProvider { }
    public sealed class PathFindResult
    {
        public bool Succeeded, IsPartialPath, Aborted; public string Status = "controlled", FailStep = "none";
        public Vector3[] Points = Array.Empty<Vector3>(); public AreaType[] PolyTypes = Array.Empty<AreaType>();
        public PolygonReference[] Polygons = Array.Empty<PolygonReference>();
        public StraightPathFlags[] Flags = Array.Empty<StraightPathFlags>();
    }
    public sealed class MeshNavigator : NavigationProvider
    {
        public object RequestIdentity = new(); public float PathPrecision = 2;
        public PathFindResult FindPath(WoWPoint from, WoWPoint to)
        {
            World.Event("path");
            return new() { Succeeded = !World.MissingMesh && !World.BlockedDoor, IsPartialPath = World.PartialPath,
                Points = new[] { (Vector3)from, (Vector3)World.Door, (Vector3)to.Add(0, 0, World.WrongFloor ? 8 : 0) },
                PolyTypes = new[] { AreaType.Ground, AreaType.KnownBuilding } };
        }
        public bool ReleaseOwned(object expected, Func<bool> admitted, System.Action<object> registered)
        {
            if (!ReferenceEquals(RequestIdentity, expected) || !admitted()) return false;
            object next = RequestIdentity = new(); registered(next); World.Event("mesh-release");
            return ReferenceEquals(RequestIdentity, next) && admitted();
        }
        public void MoveToOwned(WoWPoint target, float precision, string reason, Func<bool> admitted, System.Action<object> registered, Func<bool>? routeLease = null)
        {
            if (!admitted()) return;
            RequestIdentity = new(); registered(RequestIdentity); World.Event("mesh-prepare"); if (!admitted()) return;
            World.Walks.Add(target); World.Event("walk");
        }
    }
    public sealed class NativeNavigator
    {
        public void EnsureTilesAroundPosition(uint map, Vector3 position, float radius) { World.Event("tiles"); }
        public bool FindNearestPolyRef(uint map, Vector3 position, out ulong polygon, out Vector3 snapped)
        { polygon = 1; snapped = position; World.Event("snap"); return !World.MissingMesh && Math.Abs(position.Z) <= .1f; }
        public int GetPolyArea(uint map, ulong polygon, out byte area) { area = (byte)AreaType.Ground; World.Event("area"); return 0; }
    }
    public static class Navigator
    {
        public static NavigationProvider NavigationProvider = new MeshNavigator(); public static IPlayerMover PlayerMover = new PlayerMover();
        public static bool IsNavigatorLoaded = true; public static NativeNavigator TripperNavigator = new(); public static float LoadTilesAroundRadius = 64;
        public static MoveResult MoveTo(WoWPoint point) => throw new InvalidOperationException("unexpected base movement");
        public static TreeSharp.RunStatus GetRunStatusFromMoveResult(MoveResult value) => TreeSharp.RunStatus.Success;
    }
    public static class Flightor
    {
        public static WoWPoint GetFlightRouteWaypoint(WoWPoint from, WoWPoint to) => World.AerialGoal?.Invoke(from, to) ?? to;
        public static bool CanFollowFlightSegment(WoWPoint from, WoWPoint to) => World.AerialSegment?.Invoke(from, to) ?? true;
        public static bool CanFly => World.PreferFlight;
        public static class MountHelper
        {
            public static bool Mounted
            {
                get { World.Event("flight-mount"); return World.FlightMountObserved || (World.Actor.Flags & 0x02000000u) != 0; }
            }
        }
        public static object RequestIdentity = new(); public static WoWPoint LastFlightWaypoint;
        public static void MoveTo(WoWPoint point) { World.RawFlights.Add(point); World.Event("raw-flight"); }
        public static bool PreferFlightForGroundInteraction(WoWPoint destination, float range, bool retainDeparture = false)
            => World.PreferFlight && (retainDeparture || !World.FlightCostRequiresStop || !World.Actor.IsMoving);
        public static bool ReleaseOwned(object expected, Func<bool> admitted, System.Action<object> registered)
        {
            if (!ReferenceEquals(expected, RequestIdentity) || !admitted()) return false;
            object next = RequestIdentity = new(); registered(next); World.Event("flight-release");
            return ReferenceEquals(next, RequestIdentity) && admitted();
        }
        public static void MoveToOwnedExterior(WoWPoint point, Func<bool> admitted, System.Action<object> registered, Func<bool>? routeLease = null)
        {
            if (!admitted()) return; RequestIdentity = new(); registered(RequestIdentity); if (!admitted()) return;
            LastFlightWaypoint = point; World.ExteriorFlights.Add(point); World.Event("exterior-flight");
        }
    }
}
namespace Styx.Logic
{
    public static class Blacklist { public static bool Contains(ulong guid) => false; }
    public static class Mount
    {
        public static bool TryDismountOwned(string reason, Func<bool> admitted, System.Action? submitted)
        {
            World.Event("dismount-prepare");
            if (!admitted() || (World.Actor.Flags & 0x02003000u) != 0 || !World.Actor.MountedValue) return false;
            submitted?.Invoke(); World.Dismounts++; World.Event("dismount"); return true;
        }
    }
}
namespace Styx.Logic.Combat
{
    public static class RecoveryActions
    {
        public static void RethrowControlFlow(Exception error)
        {
            if (error is OperationCanceledException or ThreadInterruptedException or Styx.InvalidProcessException or Styx.InvalidExecutorException)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
namespace Styx.WoWInternals.World
{
    internal static class WorldQueryObservation
    {
        internal readonly record struct GroundUnitState(bool Mounted, Styx.ShapeshiftForm Form, bool OnTaxi, bool Rooted, bool Stunned);
        internal static GroundUnitState ReadGroundUnitState(LocalPlayer actor)
        {
            global::World.Event("descriptors");
            if (global::World.ObservationError != null) throw global::World.ObservationError;
            return new(actor.Mounted || actor.Shapeshift is Styx.ShapeshiftForm.FlightForm or Styx.ShapeshiftForm.EpicFlightForm,
                actor.Shapeshift, actor.OnTaxi, actor.Rooted, actor.Stunned);
        }
        internal static bool ReadLocalVehicle(LocalPlayer actor)
        {
            global::World.Event("vehicle");
            if (global::World.ObservationError != null) throw global::World.ObservationError;
            return actor.InVehicle;
        }
    }
    public static class GameWorld
    {
        [Flags] public enum CGWorldFrameHitFlags : uint { HitTestGroundAndStructures = 0x100111, HitTestLiquid = 0x10000, HitTestLiquid2 = 0x20000 }
        public static bool TraceLine(WoWPoint from, WoWPoint to, CGWorldFrameHitFlags flags, out WoWPoint point)
        { var result = global::World.Trace(new(from, to), flags); point = result.Point; return result.Hit; }
        public static bool TraceLine(WoWPoint from, WoWPoint to, CGWorldFrameHitFlags flags) => TraceLine(from, to, flags, out _);
        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags[] flags, out bool[] hits, out WoWPoint[] points)
        {
            var result = lines.Select((line, index) => global::World.Trace(line, flags[index])).ToArray();
            hits = result.Select(r => r.Hit).ToArray(); points = result.Select(r => r.Point).ToArray();
        }
        public static bool IsInLineOfSight(WoWPoint from, WoWPoint to)
        {
            global::World.Event("sight");
            if (from.DistanceSqr(to) <= 0) throw new Styx.Helpers.ObservationUnavailableException("world-collision", "invalid zero-length native segment");
            return global::World.Sight;
        }
    }
}
