// Acceptance boundary for the moving committed-target defect. Production
// BotPoi, MountedCombatTransition, GroundTransition/Context/Runtime/Machine,
// GroundApproach, Mesh request ownership, and exact Navigator/Flightor/Mesh
// invalidation members are linked/extracted unchanged. Only client/native
// observations and movement effects are controlled; this is not live proof.
using System.Numerics;
using Styx.Logic.Pathing;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using Tripper.Navigation;

public delegate void ObjectInvalidateDelegate();

internal static class World
{
    internal static LocalPlayer Actor = null!;
    internal static WoWUnit Target = null!;
    internal static MeshNavigator Mesh = null!;
    internal static readonly List<WoWPoint> RawFlights = new(), ExteriorFlights = new(), Walks = new();
    internal static readonly List<string> Diagnostics = new(), Errors = new();
    internal static readonly List<ulong> Interactions = new();
    internal static readonly List<(WorldLine Line, GameWorld.CGWorldFrameHitFlags Flags, bool Hit, WoWPoint Point)> Rays = new();
    internal static int Dismounts, Descents, Stops, PullCalls, PullBuffCalls, NavigatorClears;
    internal static bool Sight, MissingMesh, PartialPath, WrongFloor, Liquid, BlockedDoor, MissingSupport;
    internal static bool PreferFlight;
    internal static Exception? ObservationError;
    internal static System.Action<string>? Callback;
    internal static WoWPoint Door = new(205, 0, 0);

    internal static void Event(string stage) => Callback?.Invoke(stage);
    internal static void Reset()
    {
        Callback = null; ObservationError = null;
        Styx.BotEvents.Stop();
        RawFlights.Clear(); ExteriorFlights.Clear(); Walks.Clear(); Diagnostics.Clear(); Errors.Clear(); Rays.Clear(); Interactions.Clear();
        Dismounts = Descents = Stops = PullCalls = PullBuffCalls = NavigatorClears = 0;
        Sight = true; MissingMesh = PartialPath = WrongFloor = Liquid = BlockedDoor = MissingSupport = PreferFlight = false;
        Door = new(205, 0, 0);
        Actor = new LocalPlayer { Guid = 1, BaseAddress = 100, Position = new(100, 10, 80), MountedValue = true, Flags = 0x02000000u, HealthPercent = 100 };
        Target = new WoWUnit { Guid = 2, BaseAddress = 200, Entry = 70, Position = new(205, 15, 0), Outdoors = false };
        Styx.WoWInternals.ObjectManager.Me = Actor;
        Styx.WoWInternals.ObjectManager.Wow = new GreenMagic.Memory();
        Styx.WoWInternals.ObjectManager.Executor = new GreenMagic.ExecutorRand { Memory = Styx.WoWInternals.ObjectManager.Wow };
        Styx.WoWInternals.WoWMovement.ActiveMover = Actor;
        Styx.Logic.BehaviorTree.TreeRoot.RunIdentity = new object();
        Styx.Logic.BehaviorTree.TreeRoot.Current = new object();
        Styx.Logic.BehaviorTree.TreeRoot.IsRunning = true;
        Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot = new Styx.Logic.Profiles.Profile();
        Styx.Logic.Targeting.Instance = new Styx.Logic.Targeting();
        Styx.Logic.Combat.RoutineManager.Current = new Styx.Logic.Combat.ControlledRoutine();
        Styx.WoWInternals.ObjectManager.Objects.Clear(); Styx.WoWInternals.ObjectManager.Objects.Add(Target);
        Mesh = new MeshNavigator(); Navigator.NavigationProvider = Mesh; Navigator.PlayerMover = new PlayerMover();
        Navigator.IsNavigatorLoaded = true; Flightor.ResetControlled();
        Styx.Logic.POI.BotPoi.Current = new Styx.Logic.POI.BotPoi(Target, Styx.Logic.POI.PoiType.QuestPickUp);
        Styx.Logic.Targeting.Instance.FirstUnit = Target;
        Styx.Logic.Targeting.Instance.TargetList.Add(Target);
        Actor.CurrentTarget = Target;
    }
    internal static bool UnderRoof(WoWPoint p) => p.X >= 190 && p.X <= 220 && p.Y >= 5 && p.Y <= 25;
    internal static (bool Hit, WoWPoint Point) Trace(WorldLine line, GameWorld.CGWorldFrameHitFlags flags)
    {
        Event("trace"); if (ObservationError != null) throw ObservationError;
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
        public static void WriteDebug(string format, params object[] args) { World.Event("debug"); }
        public static void WriteDiagnostic(string format, params object[] args) { World.Diagnostics.Add(string.Format(format, args)); World.Event("report"); }
        public static void WriteException(Exception error) => World.Errors.Add(error.ToString());
    }
}
namespace Styx
{
    public static class StyxWoW { public static LocalPlayer Me => WoWInternals.ObjectManager.Me!; }
    public enum ShapeshiftForm { Normal, FlightForm, EpicFlightForm }
    public enum WoWClass { None, Paladin }
    public sealed class InvalidProcessException(string reason) : InvalidOperationException(reason);
    public sealed class InvalidExecutorException(string reason) : InvalidOperationException(reason);
    public static class BotEvents { public static event System.Action<object?>? OnBotStop; public static void Stop() { OnBotStop?.Invoke(null); Styx.Logic.BotEvents.Stop(); } }
}
namespace GreenMagic
{
    public sealed class Memory { public int ProcessId = 1; public IntPtr ProcessHandle = new(1); }
    public sealed class ExecutorRand { public bool IsOpen = true, IsInitialized = true; public Memory Memory = null!; }
}
namespace Styx.Logic.BehaviorTree
{
    public static class TreeRoot { public static object RunIdentity = new(), Current = new(); public static bool IsRunning = true; public static string StatusText = ""; }
}
namespace Styx.Logic.Profiles
{
    public sealed class Profile { }
    public static class ProfileManager { public static Profile CurrentProfileSnapshot = new(); }
    public sealed class Vendor { public string Name = "vendor"; public int Entry; public WoWPoint Location; }
    public sealed class Mailbox { public WoWPoint Location; }
}
namespace Styx.Logic.Profiles.Quest
{
    public enum QuestObjectType { Npc, GameObject, Item }
    public sealed class PickUpNode { public uint GiverId; public WoWPoint GiverLocation; public string GiverName = "giver"; public QuestObjectType? GiverType; }
    public sealed class TurnInNode { public uint TurnInId; public WoWPoint TurnInLocation; public string TurnInName = "ender"; public QuestObjectType? TurnInType; }
}
namespace Styx.Logic.Questing { public sealed class Quest { public uint Id; public string Name = "quest"; } }
namespace Styx.WoWInternals.WoWObjects
{
    public class WoWObject
    {
        public ulong Guid; public uint BaseAddress, Entry; public bool IsValid = true, Outdoors = true, IsDisabled;
        public string Name = "object";
        public WoWPoint Position;
        public WoWPoint Location { get { var value = Position; global::World.Event("location"); return value; } }
        public double DistanceSqr => global::World.Actor == null ? double.MaxValue : Position.DistanceSqr(global::World.Actor.Position);
        public bool IsOutdoors { get { global::World.Event("outdoors"); if (global::World.ObservationError != null) throw global::World.ObservationError; return Outdoors; } }
        public bool WithinInteractRange => global::World.Actor.Position.DistanceSqr(Position) <= 25;
        public WoWUnit? ToUnit() => this as WoWUnit;
        public event ObjectInvalidateDelegate? OnInvalidate;
        public void Invalidate() { IsValid = false; OnInvalidate?.Invoke(); }
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
    public class WoWUnit : WoWObject
    {
        public bool IsAlive = true, IsMoving, Combat, IsPlayer, TaggedByOther, TaggedByMe;
        public WoWUnit? OwnedByUnit;
        public int Level = 60, Race;
        public Styx.WoWClass Class = Styx.WoWClass.Paladin;
        public bool Dead { get => !IsAlive; set => IsAlive = !value; }
        public double Distance => global::World.Actor == null ? double.MaxValue : Position.Distance(global::World.Actor.Position);
        public void Target()
        {
            if (global::World.Actor != null) global::World.Actor.CurrentTarget = this;
        }
    }
    public class WoWPlayer : WoWUnit { }
    public class WoWItem : WoWObject { }
    public enum WoWGameObjectType { Mailbox }
    public class WoWGameObject : WoWObject
    {
        public WoWGameObjectType SubType;
        public float InteractRange = 5;
        public bool Usable = true;
        public bool CanUse() => Usable && !IsDisabled;
        public bool CanUseNow() => CanUse() && WithinInteractRange;
    }
    public sealed class Movement { public bool IsDescending; }
    public sealed class LocalPlayer : WoWUnit
    {
        public uint MapId = 530, Flags; public ulong Transport; public bool MovementKnown = true, MountedValue;
        public bool IsGhost, OnTaxi, IsOnTransport, IsCasting, IsSwimming, InVehicle, Rooted, Stunned;
        public double HealthPercent = 100;
        public WoWUnit? Pet;
        public bool GotAlivePet => Pet != null && Pet.IsValid && Pet.IsAlive;
        public WoWUnit? CurrentTarget;
        public ulong CurrentTargetGuid => CurrentTarget?.Guid ?? 0;
        public bool GotTarget => CurrentTarget != null;
        public bool IsInParty, IsInRaid;
        public int ChanneledCastingSpellId; public float BoundingRadius = .6f, BoundingHeight = 2;
        public ShapeshiftForm Shapeshift; public Movement MovementInfo = new();
        public bool IsFlying => (Flags & 0x02000000u) != 0;
        public bool Mounted { get { global::World.Event("mounted"); if (global::World.ObservationError != null) throw global::World.ObservationError; return MountedValue; } }
        public bool TryGetMovementState(out uint flags, out ulong transport) { flags = Flags; transport = Transport; global::World.Event("movement"); return MovementKnown; }
        public void ClearTarget() => CurrentTarget = null;
    }
}
namespace Styx.WoWInternals
{
    public static class ObjectManager
    {
        public static LocalPlayer? Me; public static GreenMagic.Memory? Wow; public static GreenMagic.ExecutorRand? Executor;
        public static readonly List<WoWObject> Objects = new();
        public static IEnumerable<WoWObject> ObjectList => Objects;
        public static IEnumerable<WoWUnit> CachedUnits => Objects.OfType<WoWUnit>();
        public static T? GetObjectByGuid<T>(ulong guid) where T : WoWObject => Objects.OfType<T>().FirstOrDefault(o => o.Guid == guid);
    }
    public static class WoWMovement
    {
        public enum MovementDirection { Descend }
        public static WoWUnit? ActiveMover;
        public static void Move(MovementDirection direction) { global::World.Descents++; global::World.Event("descend"); }
    }
}
namespace Styx.Logic
{
    public sealed class Targeting
    {
        public static Targeting Instance = new();
        public WoWUnit? FirstUnit;
        public List<WoWUnit> TargetList { get; } = new();
    }
    public static class Blacklist
    {
        public static readonly HashSet<ulong> Entries = new();
        public static bool Contains(ulong guid, bool flush = false) => Entries.Contains(guid);
        public static void Add(ulong guid, TimeSpan duration) => Entries.Add(guid);
    }
    public static class VendorSafetyPolicy { public static bool IsService(Styx.Logic.POI.PoiType type) => false; }
    public static class VendorManager { public static void RejectVendor(int entry, string reason) { } }
    public static class FlightPaths
    {
        public static void ResetOwnedState(bool clear, string reason)
        {
            if (clear) Styx.Logic.POI.BotPoi.Current = new Styx.Logic.POI.BotPoi(Styx.Logic.POI.PoiType.None);
        }
    }
    public static class BotEvents
    {
        public static event System.Action<object?>? OnBotStop;
        public static void Stop() => OnBotStop?.Invoke(null);
        public static class Player
        {
            public static event System.Action? OnPlayerDied;
            public static void Die() => OnPlayerDied?.Invoke();
        }
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
    public partial class MeshNavigator : NavigationProvider
    {
        private object _routeOwner = new();
        private MovementRequestObservation? _pathRequest;
        public float PathPrecision = 2;
        public PathFindResult FindPath(WoWPoint from, WoWPoint to)
        {
            World.Event("path");
            return new() { Succeeded = !World.MissingMesh && !World.BlockedDoor, IsPartialPath = World.PartialPath,
                Points = new[] { (Vector3)from, (Vector3)World.Door, (Vector3)to.Add(0, 0, World.WrongFloor ? 8 : 0) },
                PolyTypes = new[] { AreaType.Ground, AreaType.KnownBuilding } };
        }
        public bool ReleaseOwned(object expected, Func<bool> admitted, System.Action<object> registered)
        {
            if (!ReferenceEquals(_routeOwner, expected) || !admitted()) return false;
            object next = _routeOwner = new(); _routeAdmission = admitted; _pathRequest = null;
            registered(next); World.Event("mesh-release");
            return ReferenceEquals(_routeOwner, next) && admitted();
        }
        public void MoveToOwned(WoWPoint target, float precision, string reason, Func<bool> admitted, System.Action<object> registered,
            Func<bool>? routeLease = null)
        {
            if (!admitted()) return;
            _routeOwner = new(); _routeAdmission = admitted; _routeLease = routeLease; registered(_routeOwner); _pathRequest = new MovementRequestObservation(this);
            if (!admitted()) return;
            World.Walks.Add(target); World.Event("walk");
        }
        internal void PrimeControlledRoute(Func<bool>? admitted = null)
        {
            _routeOwner = new object(); _routeAdmission = admitted; _pathRequest = new MovementRequestObservation(this);
        }
        private bool ClearRoute(bool? stopOwnedInput)
        {
            bool stop = stopOwnedInput == true;
            _routeOwner = new object(); _routeAdmission = null; _routeLease = null; _pathRequest = null;
            if (stop) Navigator.PlayerMover.MoveStop();
            return true;
        }
    }
    public sealed class NativeNavigator
    {
        public void EnsureTilesAroundPosition(uint map, Vector3 position, float radius) { World.Event("tiles"); }
        public bool FindNearestPolyRef(uint map, Vector3 position, out ulong polygon, out Vector3 snapped)
        { polygon = 1; snapped = position; World.Event("snap"); return !World.MissingMesh && Math.Abs(position.Z) <= .1f; }
        public int GetPolyArea(uint map, ulong polygon, out byte area) { area = (byte)AreaType.Ground; World.Event("area"); return 0; }
    }
    public static partial class Navigator
    {
        private static NavigationProvider _provider = new MeshNavigator();
        internal static MeshNavigator? _meshNavigator = _provider as MeshNavigator;
        public static NavigationProvider NavigationProvider
        {
            get => _provider;
            set { _provider = value; _meshNavigator = value as MeshNavigator; }
        }
        public static IPlayerMover PlayerMover = new PlayerMover();
        public static bool IsNavigatorLoaded = true; public static NativeNavigator TripperNavigator = new(); public static float LoadTilesAroundRadius = 64;
        public static MoveResult MoveTo(WoWPoint point) => throw new InvalidOperationException("unexpected base movement");
        public static TreeSharp.RunStatus GetRunStatusFromMoveResult(MoveResult value) => TreeSharp.RunStatus.Success;
        public static void Clear() => World.NavigatorClears++;
    }
    internal sealed class FlightPath { }
    public static partial class Flightor
    {
        public static WoWPoint GetFlightRouteWaypoint(WoWPoint from, WoWPoint to) => to;
        public static bool CanFollowFlightSegment(WoWPoint from, WoWPoint to) => true;
        public static bool CanFly => World.PreferFlight;
        public static class MountHelper { public static bool Mounted => (World.Actor.Flags & 0x02000000u) != 0; }
        private static LocalPlayer? _antiStuckPlayer;
        private static WoWUnit? _antiStuckOwner;
        private static ulong _antiStuckPlayerGuid, _antiStuckOwnerGuid;
        private static uint _antiStuckMap, _antiStuckPlayerAddress, _antiStuckOwnerAddress;
        private static bool _antiStuckAlive, _antiStuckGhost;
        private static Styx.Logic.POI.BotPoi? _antiStuckPoi;
        private static long _antiStuckPoiGeneration;
        private static Func<bool>? _antiStuckRouteLease;
        private static object? _antiStuckProfile, _antiStuckProvider, _antiStuckInput, _antiStuckRequestOwner, _antiStuckMemory, _antiStuckExecutor;
        private static bool _asAscended, _asStrafedLeft, _asStrafedRight;
        private static WoWPoint _antiStuckStartPos = WoWPoint.Empty;
        private static FlightPath? _flightPath;
        private static LocalPlayer? _pathPlayer;
        private static WoWUnit? _pathMover;
        private static ulong _pathPlayerGuid, _pathMoverGuid;
        private static uint _pathMap, _pathPlayerAddress, _pathMoverAddress;
        private static bool _pathAlive, _pathGhost;
        private static Styx.Logic.POI.BotPoi? _pathPoi;
        private static long _pathPoiGeneration;
        private static Func<bool>? _pathRouteLease;
        private static object? _pathProfile, _pathProvider, _pathInput, _flightRequestOwner, _pathMemory, _pathExecutor;
        private static WoWPoint _lastFlightWaypoint = WoWPoint.Empty, _lastDestination = WoWPoint.Empty;
        private static WoWPoint _takeoffSpot = WoWPoint.Empty, _takeoffDestination = WoWPoint.Empty;
        internal static object? RequestIdentity => _flightRequestOwner;
        internal static WoWPoint LastFlightWaypoint => _lastFlightWaypoint;
        internal static void ResetControlled()
        {
            _flightRequestOwner = new object(); _pathPlayer = null; _pathMover = null; _flightPath = null;
            _pathRouteLease = null; _antiStuckRouteLease = null;
            _antiStuckPlayer = null; _antiStuckOwner = null; _lastFlightWaypoint = _lastDestination = WoWPoint.Empty;
            _takeoffSpot = _takeoffDestination = WoWPoint.Empty;
        }
        internal static void PrimeControlledRoute()
        {
            var me = StyxWoW.Me; var mover = Styx.WoWInternals.WoWMovement.ActiveMover!;
            _flightRequestOwner = new object(); _flightPath = new FlightPath();
            _pathPlayer = me; _pathMover = mover; _pathPlayerGuid = me.Guid; _pathMoverGuid = mover.Guid;
            _pathMap = me.MapId; _pathPlayerAddress = me.BaseAddress; _pathMoverAddress = mover.BaseAddress;
            _pathAlive = me.IsAlive; _pathGhost = me.IsGhost; _pathPoi = Styx.Logic.POI.BotPoi.Current;
            _pathPoiGeneration = Styx.Logic.POI.BotPoi.CurrentGeneration; _pathProfile = Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot;
            _pathProvider = Navigator.NavigationProvider; _pathInput = Navigator.PlayerMover;
            _pathMemory = Styx.WoWInternals.ObjectManager.Wow; _pathExecutor = Styx.WoWInternals.ObjectManager.Executor;
        }
        public static void MoveTo(WoWPoint point) { World.RawFlights.Add(point); World.Event("raw-flight"); }
        public static bool PreferFlightForGroundInteraction(WoWPoint destination, float range, bool retainDeparture = false) => World.PreferFlight;
        public static bool ReleaseOwned(object expected, Func<bool> admitted, System.Action<object> registered)
        {
            if (!ReferenceEquals(expected, RequestIdentity) || !admitted()) return false;
            object next = _flightRequestOwner = new(); _pathPlayer = null; _pathMover = null; _flightPath = null; _pathRouteLease = null;
            registered(next); World.Event("flight-release");
            return ReferenceEquals(next, RequestIdentity) && admitted();
        }
        public static void MoveToOwnedExterior(WoWPoint point, Func<bool> admitted, System.Action<object> registered,
            Func<bool>? routeLease = null)
        {
            if (!admitted()) return; PrimeControlledRoute(); _pathRouteLease = routeLease; registered(RequestIdentity!); if (!admitted()) return;
            _lastFlightWaypoint = point; _lastDestination = point; World.ExteriorFlights.Add(point); World.Event("exterior-flight");
        }
    }
}
namespace Styx.Logic
{
    public static class Mount
    {
        public static bool TryDismountOwned(string reason, Func<bool> admitted, System.Action? submitted)
        {
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
    public sealed class ControlledRoutine
    {
        public bool NeedPullBuffs { get; set; }
        public void PullBuff() => World.PullBuffCalls++;
        public void Pull() => World.PullCalls++;
    }
    public static class RoutineManager { public static ControlledRoutine Current { get; set; } = new(); }
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
