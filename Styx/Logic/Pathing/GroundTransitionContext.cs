using System;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing;

/// <summary>Observed ownership for one travel handoff; no unobserved ABA claim.</summary>
internal sealed class GroundTransitionContext
{
    internal readonly LocalPlayer Actor;
    internal readonly WoWUnit Mover;
    internal readonly Memory Memory;
    internal readonly ExecutorRand Executor;
    internal readonly NavigationProvider Provider;
    internal readonly IPlayerMover Input;
    internal readonly WoWObject? Subject;
    internal readonly WoWPoint Destination;
    internal readonly ulong ActorGuid, SubjectGuid;
    internal readonly uint ActorAddress, SubjectAddress, Map, SubjectEntry;
    internal readonly int ProcessId;
    internal readonly IntPtr ProcessHandle;
    internal readonly object Run, Bot;
    internal readonly BotPoi Poi;
    internal readonly long PoiGeneration, PoiWorkGeneration;
    private readonly ulong _moverGuid, _poiGuid;
    private readonly uint _moverAddress, _poiEntry;
    private readonly PoiType _poiType;
    private readonly object _profile;
    private readonly bool _bindDestination, _combatRoute, _running;
    private readonly Func<bool> _admitted;

    internal GroundTransitionContext(WoWObject? subject, WoWPoint destination, bool bindDestination, Func<bool> admitted,
        bool combatRoute = false)
    {
        Actor = ObjectManager.Me ?? throw Unknown("player unavailable");
        Mover = WoWMovement.ActiveMover ?? throw Unknown("active mover unavailable");
        Memory = ObjectManager.Wow ?? throw Unknown("memory unavailable");
        Executor = ObjectManager.Executor ?? throw Unknown("executor unavailable");
        Provider = Navigator.NavigationProvider;
        Input = Navigator.PlayerMover;
        Subject = subject; Destination = destination; _bindDestination = bindDestination; _combatRoute = combatRoute; _admitted = admitted;
        ActorGuid = Actor.Guid; ActorAddress = Actor.BaseAddress; Map = Actor.MapId;
        SubjectGuid = subject?.Guid ?? 0; SubjectAddress = subject?.BaseAddress ?? 0; SubjectEntry = subject?.Entry ?? 0;
        _moverGuid = Mover.Guid; _moverAddress = Mover.BaseAddress;
        ProcessId = Memory.ProcessId; ProcessHandle = Memory.ProcessHandle;
        Run = TreeRoot.RunIdentity; Bot = TreeRoot.Current; _running = TreeRoot.IsRunning;
        _profile = ProfileManager.CurrentProfileSnapshot;
        Poi = BotPoi.Current; PoiGeneration = BotPoi.CurrentGeneration; PoiWorkGeneration = BotPoi.CurrentWorkGeneration;
        _poiGuid = Poi.Guid; _poiEntry = Poi.Entry; _poiType = Poi.Type;
        if (!Current) throw Unknown("ground transition owner unavailable");
    }

    internal bool SameActorSession(GroundTransitionContext other) => ReferenceEquals(Actor, other.Actor)
        && ActorGuid == other.ActorGuid && ActorAddress == other.ActorAddress && Map == other.Map
        && ReferenceEquals(Memory, other.Memory) && ProcessId == other.ProcessId && ProcessHandle == other.ProcessHandle
        && ReferenceEquals(Executor, other.Executor) && ReferenceEquals(Run, other.Run) && ReferenceEquals(Bot, other.Bot);

    internal bool InputCurrent
    {
        get
        {
            if (Memory.ProcessHandle == IntPtr.Zero) throw new InvalidProcessException("Ground transition process closed.");
            if (!Executor.IsOpen || !Executor.IsInitialized) throw new InvalidExecutorException("Ground transition executor closed.");
            return ReferenceEquals(ObjectManager.Me, Actor) && ReferenceEquals(ObjectManager.Wow, Memory)
                && ReferenceEquals(ObjectManager.Executor, Executor) && ReferenceEquals(Executor.Memory, Memory)
                && ProcessHandle != IntPtr.Zero && Memory.ProcessHandle == ProcessHandle && Memory.ProcessId == ProcessId
                && ActorGuid != 0 && ActorAddress != 0 && Actor.Guid == ActorGuid && Actor.BaseAddress == ActorAddress
                && Actor.IsValid && Actor.MapId == Map
                && ReferenceEquals(WoWMovement.ActiveMover, Mover) && Mover.IsValid && Mover.Guid == _moverGuid
                && Mover.BaseAddress == _moverAddress && _moverGuid != 0 && _moverAddress != 0
                && ReferenceEquals(Navigator.NavigationProvider, Provider) && ReferenceEquals(Navigator.PlayerMover, Input)
                && ReferenceEquals(TreeRoot.RunIdentity, Run) && ReferenceEquals(TreeRoot.Current, Bot)
                && TreeRoot.IsRunning == _running;
        }
    }

    private bool WorldCurrent => InputCurrent && _running && TreeRoot.IsRunning && Actor.IsAlive && !Actor.IsGhost
        && ReferenceEquals(ProfileManager.CurrentProfileSnapshot, _profile)
        && ReferenceEquals(BotPoi.Current, Poi)
        && (_combatRoute ? BotPoi.CurrentWorkGeneration == PoiWorkGeneration : BotPoi.CurrentGeneration == PoiGeneration)
        && !Poi.IsWorldSubjectBlacklisted
        && Poi.Type == _poiType && Poi.Guid == _poiGuid && Poi.Entry == _poiEntry
        && (Subject == null || SubjectGuid != 0 && SubjectAddress != 0 && Subject.IsValid
            && Subject.Guid == SubjectGuid && Subject.BaseAddress == SubjectAddress && Subject.Entry == SubjectEntry
            && (Subject is not WoWUnit unit || unit.IsAlive)
            && (!_bindDestination || Subject.Location.Equals(Destination)));

    internal bool Current => WorldCurrent && _admitted() && WorldCurrent;
    private static ObservationUnavailableException Unknown(string message) => new("ground-transition-context", message);
}
