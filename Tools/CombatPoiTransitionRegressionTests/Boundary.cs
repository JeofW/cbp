#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using TreeSharp;

public delegate void ObjectInvalidateDelegate();

namespace Harness
{
    internal static class Control
    {
        internal static int FlightInvalidations;
        internal static int MeshInvalidations;
        internal static int TransitionTicks;
        internal static int TransitionCancels;
        internal static int NavigatorClears;
        internal static int PullCalls;
        internal static int PullBuffCalls;
        internal static Styx.Logic.Pathing.ICombatLandingLease ActiveLease;
        internal static readonly Styx.Logic.Pathing.MeshNavigator Mesh = new();

        internal static void Reset()
        {
            FlightInvalidations = MeshInvalidations = TransitionTicks = TransitionCancels = NavigatorClears = 0;
            PullCalls = PullBuffCalls = 0;
            ActiveLease = null;
            Styx.Logic.Pathing.Flightor.ResetControlled();
            Mesh.ResetControlled();
            Styx.Logic.Pathing.Navigator._meshNavigator = Mesh;
            Styx.Logic.Pathing.Navigator.NavigationProvider = Mesh;
            Styx.Logic.Pathing.Navigator.PlayerMover = new object();
            var memory = new GreenMagic.Memory { ProcessId = 10, ProcessHandle = new IntPtr(10) };
            Styx.WoWInternals.ObjectManager.Wow = memory;
            Styx.WoWInternals.ObjectManager.Executor = new GreenMagic.ExecutorRand { Memory = memory, IsOpen = true, IsInitialized = true };
            Styx.WoWInternals.ObjectManager.Objects.Clear();
            Styx.Logic.BehaviorTree.TreeRoot.RunIdentity = new object();
            Styx.Logic.BehaviorTree.TreeRoot.Current = new object();
            Styx.Logic.BehaviorTree.TreeRoot.IsRunning = true;
            Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot = new object();
            Styx.Logic.Targeting.Instance = new Styx.Logic.Targeting();
            Styx.Logic.Blacklist.Entries.Clear();
            Styx.Logic.Combat.RoutineManager.Current = new Styx.Logic.Combat.ControlledRoutine();
            Styx.StyxWoW.Me = null;
            Styx.WoWInternals.WoWMovement.ActiveMover = null;
            Styx.Logic.POI.BotPoi.Current = new Styx.Logic.POI.BotPoi(Styx.Logic.POI.PoiType.None);
            ResetRouteTokens();
        }

        internal static void ResetRouteTokens()
        {
            ActiveLease = null;
            Styx.Logic.Pathing.Flightor.ResetControlled();
            Mesh.ResetControlled();
            FlightInvalidations = MeshInvalidations = 0;
        }
    }
}

namespace GreenMagic
{
    public sealed class Memory
    {
        public int ProcessId { get; set; }
        public IntPtr ProcessHandle { get; set; } = new(1);
    }

    public sealed class ExecutorRand
    {
        public Memory Memory { get; set; }
        public bool IsOpen { get; set; } = true;
        public bool IsInitialized { get; set; } = true;
    }
}

namespace Styx.Logic.Pathing
{
    public readonly struct WoWPoint : IEquatable<WoWPoint>
    {
        public readonly float X, Y, Z;
        public WoWPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static WoWPoint Empty => new(float.NaN, float.NaN, float.NaN);
        public static WoWPoint Zero => new(0, 0, 0);
        public float DistanceSqr(WoWPoint other)
        {
            float x = X - other.X, y = Y - other.Y, z = Z - other.Z;
            return x * x + y * y + z * z;
        }
        public float Distance2DSqr(WoWPoint other)
        {
            float x = X - other.X, y = Y - other.Y;
            return x * x + y * y;
        }
        public float Distance(WoWPoint other) => MathF.Sqrt(DistanceSqr(other));
        public bool Equals(WoWPoint other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is WoWPoint other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:F2},{Y:F2},{Z:F2})";
        public static bool operator ==(WoWPoint left, WoWPoint right) => left.Equals(right);
        public static bool operator !=(WoWPoint left, WoWPoint right) => !left.Equals(right);
    }

    internal interface ICombatLandingLease
    {
        bool Current { get; }
    }

    public static partial class Navigator
    {
        internal static MeshNavigator _meshNavigator;
        public static object NavigationProvider { get; set; }
        public static object PlayerMover { get; set; }
        public static void Clear() => Harness.Control.NavigatorClears++;
    }

    public static class Flightor
    {
        private static object _request = new();
        internal static object RequestIdentity => _request;

        internal static void ResetControlled() => _request = new object();

        internal static void InvalidateRouteContext()
        {
            Harness.Control.FlightInvalidations++;
            if (Harness.Control.ActiveLease?.Current == true)
                return;
            _request = new object();
        }
    }

    public sealed class MeshNavigator
    {
        private object _request = new();
        internal object RequestIdentity => _request;

        internal void ResetControlled() => _request = new object();

        internal void InvalidateRouteContext()
        {
            Harness.Control.MeshInvalidations++;
            if (Harness.Control.ActiveLease?.Current == true)
                return;
            _request = new object();
        }
    }

    public enum GroundTransitionState { Pending, Ready, Unavailable, Revoked }
    public enum GroundTransitionPurpose { Interaction, Combat }

    internal sealed class GroundTransitionContext
    {
        private readonly Styx.WoWInternals.WoWObjects.LocalPlayer _actor;
        private readonly Styx.WoWInternals.WoWObjects.WoWObject _subject;
        private readonly Func<bool> _admitted;
        private readonly object _provider, _input, _run, _bot, _profile;
        private readonly Styx.Logic.POI.BotPoi _poi;
        private readonly long _generation;
        private readonly ulong _actorGuid, _subjectGuid;
        private readonly uint _actorBase, _subjectBase, _subjectEntry, _map;

        internal GroundTransitionContext(Styx.WoWInternals.WoWObjects.WoWObject subject, WoWPoint destination,
            bool bindDestination, Func<bool> admitted)
        {
            _actor = Styx.WoWInternals.ObjectManager.Me ?? throw new Styx.Helpers.ObservationUnavailableException("ground-transition-context", "actor unavailable");
            _subject = subject;
            _admitted = admitted ?? (() => true);
            _provider = Navigator.NavigationProvider;
            _input = Navigator.PlayerMover;
            _run = Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;
            _bot = Styx.Logic.BehaviorTree.TreeRoot.Current;
            _profile = Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot;
            _poi = Styx.Logic.POI.BotPoi.Current;
            _generation = Styx.Logic.POI.BotPoi.CurrentGeneration;
            _actorGuid = _actor.Guid;
            _actorBase = _actor.BaseAddress;
            _map = _actor.MapId;
            _subjectGuid = subject?.Guid ?? 0;
            _subjectBase = subject?.BaseAddress ?? 0;
            _subjectEntry = subject?.Entry ?? 0;
        }

        internal bool Current => ReferenceEquals(Styx.WoWInternals.ObjectManager.Me, _actor)
            && _actor.IsValid && _actor.IsAlive && _actor.Guid == _actorGuid && _actor.BaseAddress == _actorBase && _actor.MapId == _map
            && ReferenceEquals(Navigator.NavigationProvider, _provider) && ReferenceEquals(Navigator.PlayerMover, _input)
            && ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.RunIdentity, _run)
            && ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.Current, _bot) && Styx.Logic.BehaviorTree.TreeRoot.IsRunning
            && ReferenceEquals(Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot, _profile)
            && ReferenceEquals(Styx.Logic.POI.BotPoi.Current, _poi) && Styx.Logic.POI.BotPoi.CurrentGeneration == _generation
            && (_subject == null || _subject.IsValid && _subject.IsAlive && _subject.Guid == _subjectGuid
                && _subject.BaseAddress == _subjectBase && _subject.Entry == _subjectEntry)
            && _admitted();
    }

    public sealed class GroundTransition : IDisposable
    {
        private readonly GroundTransitionPurpose _purpose;
        private Styx.WoWInternals.WoWObjects.LocalPlayer _actor;
        private Styx.WoWInternals.WoWObjects.WoWObject _subject;
        private Func<bool> _admitted;
        private Styx.Logic.POI.BotPoi _poi;
        private long _generation;
        private object _provider, _input, _run, _bot, _memory, _executor, _flightToken, _meshToken;
        private ulong _actorGuid, _subjectGuid;
        private uint _actorBase, _subjectBase, _subjectEntry, _map;
        private ICombatLandingLease _lease;
        private bool _active;

        public GroundTransition(GroundTransitionPurpose purpose) => _purpose = purpose;

        public GroundTransitionState Tick(WoWPoint destination, Styx.WoWInternals.WoWObjects.WoWObject subject, Func<bool> admitted)
            => TickCore(destination, subject, admitted, null);

        // Expected combat-only wiring. Current production does not call this overload;
        // the failing-before suite proves why the lease is necessary.
        internal GroundTransitionState Tick(WoWPoint destination, Styx.WoWInternals.WoWObjects.WoWObject subject,
            Func<bool> admitted, ICombatLandingLease lease)
            => TickCore(destination, subject, admitted, lease);

        private GroundTransitionState TickCore(WoWPoint destination, Styx.WoWInternals.WoWObjects.WoWObject subject,
            Func<bool> admitted, ICombatLandingLease lease)
        {
            if (admitted == null || !admitted()) { Cancel(); return GroundTransitionState.Revoked; }
            if (!_active)
            {
                _actor = Styx.WoWInternals.ObjectManager.Me;
                _subject = subject;
                _admitted = admitted;
                _poi = Styx.Logic.POI.BotPoi.Current;
                _generation = Styx.Logic.POI.BotPoi.CurrentGeneration;
                _provider = Navigator.NavigationProvider;
                _input = Navigator.PlayerMover;
                _run = Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;
                _bot = Styx.Logic.BehaviorTree.TreeRoot.Current;
                _memory = Styx.WoWInternals.ObjectManager.Wow;
                _executor = Styx.WoWInternals.ObjectManager.Executor;
                _flightToken = Flightor.RequestIdentity;
                _meshToken = Harness.Control.Mesh.RequestIdentity;
                _actorGuid = _actor?.Guid ?? 0;
                _actorBase = _actor?.BaseAddress ?? 0;
                _map = _actor?.MapId ?? 0;
                _subjectGuid = subject?.Guid ?? 0;
                _subjectBase = subject?.BaseAddress ?? 0;
                _subjectEntry = subject?.Entry ?? 0;
                _lease = lease;
                _active = true;
                if (_purpose == GroundTransitionPurpose.Combat && lease != null)
                    Harness.Control.ActiveLease = lease;
            }
            Harness.Control.TransitionTicks++;
            if (!Current()) { Cancel(); return GroundTransitionState.Revoked; }
            if (CanActUnmounted(() => Current())) return GroundTransitionState.Ready;
            return GroundTransitionState.Pending;
        }

        private bool Current()
        {
            if (!_active || _actor == null || _actorGuid == 0 || _actorBase == 0) return false;
            bool poiCurrent = ReferenceEquals(Styx.Logic.POI.BotPoi.Current, _poi)
                && (_lease?.Current == true || Styx.Logic.POI.BotPoi.CurrentGeneration == _generation);
            return poiCurrent && (_lease == null || _lease.Current)
                && ReferenceEquals(Styx.WoWInternals.ObjectManager.Me, _actor)
                && _actor.IsValid && _actor.IsAlive && _actor.Guid == _actorGuid && _actor.BaseAddress == _actorBase && _actor.MapId == _map
                && ReferenceEquals(Navigator.NavigationProvider, _provider) && ReferenceEquals(Navigator.PlayerMover, _input)
                && ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.RunIdentity, _run)
                && ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.Current, _bot) && Styx.Logic.BehaviorTree.TreeRoot.IsRunning
                && ReferenceEquals(Styx.WoWInternals.ObjectManager.Wow, _memory)
                && ReferenceEquals(Styx.WoWInternals.ObjectManager.Executor, _executor)
                && ReferenceEquals(Flightor.RequestIdentity, _flightToken)
                && ReferenceEquals(Harness.Control.Mesh.RequestIdentity, _meshToken)
                && (_subject == null || _subject.IsValid && _subject.IsAlive && _subject.Guid == _subjectGuid
                    && _subject.BaseAddress == _subjectBase && _subject.Entry == _subjectEntry)
                && (_admitted?.Invoke() ?? false);
        }

        public void Cancel()
        {
            if (!_active) return;
            _active = false;
            Harness.Control.TransitionCancels++;
            if (ReferenceEquals(Harness.Control.ActiveLease, _lease)) Harness.Control.ActiveLease = null;
            _lease = null;
        }

        public void Dispose() => Cancel();

        public static bool CanActUnmounted(Func<bool> admitted = null)
        {
            if (admitted != null && !admitted()) return false;
            var actor = Styx.WoWInternals.ObjectManager.Me;
            if (actor == null || !actor.IsValid || !actor.IsAlive) return false;
            if (!actor.TryGetMovementState(out uint flags, out ulong transport)) return false;
            bool mounted = actor.Mounted || actor.Shapeshift is Styx.ShapeshiftForm.FlightForm or Styx.ShapeshiftForm.EpicFlightForm;
            return !mounted && (flags & 0x02003000u) == 0 && transport == 0 && !actor.OnTaxi && !actor.InVehicle
                && (admitted?.Invoke() ?? true);
        }
    }
}

namespace Styx
{
    public sealed class InvalidProcessException : Exception { public InvalidProcessException(string message) : base(message) { } }
    public sealed class InvalidExecutorException : Exception { public InvalidExecutorException(string message) : base(message) { } }
    public enum ShapeshiftForm { Normal, FlightForm, EpicFlightForm }
    public enum WoWClass { None, Paladin }

    public static class StyxWoW
    {
        public static Styx.WoWInternals.WoWObjects.LocalPlayer Me { get; set; }
    }
}

namespace Styx.WoWInternals.WoWObjects
{
    using Styx.Logic.Pathing;

    public class WoWObject
    {
        public ulong Guid { get; set; }
        public uint Entry { get; set; }
        public uint BaseAddress { get; set; } = 1;
        public bool IsValid { get; set; } = true;
        public bool IsDisabled { get; set; }
        public string Name { get; set; } = "object";
        public virtual bool IsAlive { get; set; } = true;
        public WoWPoint Location { get; set; }
        public double DistanceSqr => Location.DistanceSqr(Styx.StyxWoW.Me?.Location ?? WoWPoint.Zero);
        public event ObjectInvalidateDelegate OnInvalidate;
        public virtual WoWUnit ToUnit() => this as WoWUnit;
        public void Invalidate() { IsValid = false; OnInvalidate?.Invoke(); }
    }

    public class WoWUnit : WoWObject
    {
        public bool Dead { get => !IsAlive; set => IsAlive = !value; }
        public bool Combat { get; set; }
        public bool IsPlayer { get; set; }
        public bool IsPet { get; set; }
        public bool TaggedByOther { get; set; }
        public bool TaggedByMe { get; set; }
        public bool InLineOfSpellSight { get; set; } = true;
        public WoWUnit OwnedByUnit { get; set; }
        public int Level { get; set; } = 60;
        public int Race { get; set; }
        public Styx.WoWClass Class { get; set; } = Styx.WoWClass.Paladin;
        public double Distance => Location.Distance(Styx.StyxWoW.Me?.Location ?? WoWPoint.Zero);
        public void Target()
        {
            if (Styx.StyxWoW.Me != null) Styx.StyxWoW.Me.CurrentTarget = this;
        }
    }

    public class WoWPlayer : WoWUnit { }
    public class WoWItem : WoWObject { }
    public enum WoWGameObjectType { Mailbox }
    public class WoWGameObject : WoWObject { public WoWGameObjectType SubType { get; set; } }

    public class LocalPlayer : WoWUnit
    {
        public bool Mounted { get; set; }
        public bool IsGhost { get; set; }
        public uint MapId { get; set; } = 530;
        public bool OnTaxi { get; set; }
        public bool InVehicle { get; set; }
        public bool Rooted { get; set; }
        public bool Stunned { get; set; }
        public double HealthPercent { get; set; } = 100;
        public WoWUnit Pet { get; set; }
        public bool GotAlivePet => Pet != null && Pet.IsAlive;
        public WoWUnit CurrentTarget { get; set; }
        public ulong CurrentTargetGuid => CurrentTarget?.Guid ?? 0;
        public bool GotTarget => CurrentTarget != null;
        public bool IsInParty { get; set; }
        public bool IsInRaid { get; set; }
        public Styx.ShapeshiftForm Shapeshift { get; set; }
        public bool MovementKnown { get; set; } = true;
        public uint ObservedMovementFlags { get; set; }
        public ulong ObservedTransportGuid { get; set; }
        public bool TryGetMovementState(out uint flags, out ulong transport)
        {
            flags = ObservedMovementFlags;
            transport = ObservedTransportGuid;
            return MovementKnown;
        }
        public void ClearTarget() => CurrentTarget = null;
    }
}

namespace Styx.WoWInternals
{
    using GreenMagic;
    using Styx.WoWInternals.WoWObjects;

    public static class ObjectManager
    {
        public static LocalPlayer Me => Styx.StyxWoW.Me;
        public static Memory Wow { get; set; } = new();
        public static ExecutorRand Executor { get; set; } = new();
        public static readonly List<WoWObject> Objects = new();
        public static IEnumerable<WoWObject> ObjectList => Objects;
        public static IEnumerable<WoWUnit> CachedUnits => Objects.OfType<WoWUnit>();
        public static T GetObjectByGuid<T>(ulong guid) where T : WoWObject => Objects.OfType<T>().FirstOrDefault(o => o.Guid == guid);
    }

    public static class WoWMovement
    {
        public static WoWUnit ActiveMover { get; set; }
    }
}

namespace Styx.Logic
{
    using Styx.Logic.POI;
    using Styx.WoWInternals.WoWObjects;

    public sealed class Targeting
    {
        public static Targeting Instance { get; set; } = new();
        public WoWUnit FirstUnit { get; set; }
        public List<WoWUnit> TargetList { get; } = new();
    }

    public static class Blacklist
    {
        internal static readonly HashSet<ulong> Entries = new();
        public static bool Contains(ulong guid, bool flush) => Entries.Contains(guid);
        public static void Add(ulong guid, TimeSpan duration) => Entries.Add(guid);
    }

    public static class BotEvents
    {
        public static class Player
        {
            public static event System.Action OnPlayerDied;
            internal static void RaiseDeath() => OnPlayerDied?.Invoke();
        }
    }

    public static class VendorSafetyPolicy { public static bool IsService(PoiType type) => false; }
    public static class VendorManager { public static void RejectVendor(int entry, string reason) { } }
    public static class FlightPaths
    {
        public static void ResetOwnedState(bool clear, string reason)
        {
            if (clear) BotPoi.Current = new BotPoi(PoiType.None);
        }
    }
}

namespace Styx.Logic.POI
{
    public enum PoiType
    {
        None, Kill, Loot, Skin, Harvest, Sell, Repair, Train, Buy, Mail, Fly, Hotspot, Quest, QuestPickUp, QuestTurnIn
    }
}

namespace Styx.Logic.Profiles
{
    using Styx.Logic.Pathing;
    public static class ProfileManager { public static object CurrentProfileSnapshot { get; set; } = new(); }
    public sealed class Vendor { public string Name { get; set; } = "vendor"; public int Entry { get; set; } public WoWPoint Location { get; set; } }
    public sealed class Mailbox { public WoWPoint Location { get; set; } }
}

namespace Styx.Logic.Profiles.Quest
{
    using Styx.Logic.Pathing;
    public enum QuestObjectType { Npc, GameObject, Item }
    public sealed class PickUpNode
    {
        public uint GiverId { get; set; }
        public WoWPoint GiverLocation { get; set; }
        public string GiverName { get; set; } = "giver";
        public QuestObjectType? GiverType { get; set; }
    }
    public sealed class TurnInNode
    {
        public uint TurnInId { get; set; }
        public WoWPoint TurnInLocation { get; set; }
        public string TurnInName { get; set; } = "ender";
        public QuestObjectType? TurnInType { get; set; }
    }
}

namespace Styx.Logic.Questing
{
    public sealed class Quest { public uint Id { get; set; } public string Name { get; set; } = "quest"; }
}

namespace Styx.Helpers
{
    public sealed class ObservationUnavailableException : Exception
    {
        public ObservationUnavailableException(string owner, string reason) : base(owner + ": " + reason) { }
    }

    public static class Logging
    {
        public static void Write(string format, params object[] args) { }
        public static void WriteDebug(string format, params object[] args) { }
        public static void WriteDiagnostic(string format, params object[] args) { }
        public static void WriteException(Exception error) { }
    }
}

namespace Styx.Logic.BehaviorTree
{
    public static class TreeRoot
    {
        public static string StatusText { get; set; }
        public static object RunIdentity { get; set; } = new();
        public static object Current { get; set; } = new();
        public static bool IsRunning { get; set; } = true;
    }
}

namespace Styx.Logic.Combat
{
    public static class RecoveryActions
    {
        public static void RethrowControlFlow(Exception error)
        {
            if (error is OperationCanceledException or ThreadInterruptedException or Styx.InvalidProcessException or Styx.InvalidExecutorException)
                throw error;
        }
    }

    public sealed class ControlledRoutine
    {
        public bool NeedPullBuffs { get; set; }
        public void PullBuff() => Harness.Control.PullBuffCalls++;
        public void Pull() => Harness.Control.PullCalls++;
    }

    public static class RoutineManager
    {
        public static ControlledRoutine Current { get; set; } = new();
    }
}
