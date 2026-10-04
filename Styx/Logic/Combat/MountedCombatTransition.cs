using System;
using System.Collections.Generic;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

#nullable disable

namespace Styx.Logic.Combat
{
    /// <summary>
    /// Bridges committed/protective combat intent to the shared ground-transition
    /// owner. It never acknowledges landing or mount removal itself.
    /// </summary>
    public sealed class MountedCombatTransition : IDisposable
    {
        private readonly GroundTransition _ground = new(GroundTransitionPurpose.Combat);
        private Func<bool> _admitted;
        private WoWObject _subject;
        private WoWPoint _destination;
        private bool _active;
        private long _lifetime;

        public static bool IsMountedOrFlying(LocalPlayer actor)
            => actor != null && (actor.Mounted || actor.Shapeshift is ShapeshiftForm.FlightForm or ShapeshiftForm.EpicFlightForm);

        public static bool HasProtectiveCombat(LocalPlayer actor)
        {
            if (actor == null) return false;
            var pet = actor.GotAlivePet ? actor.Pet : null;
            return actor.Combat || pet != null && pet.IsAlive && pet.Combat;
        }

        /// <summary>
        /// Returns true when mounted travel must yield to the combat transition.
        /// A committed Kill always yields. Incidental combat flags cannot create
        /// mounted attack intent; after observed mount loss, ground admission owns
        /// any remaining falling/UNKNOWN transition before combat may resume.
        /// </summary>
        public static bool RequiresProtectiveHandoff(WoWPoint travelDestination)
        {
            var actor = ObjectManager.Me;
            if (actor == null || !actor.IsValid || !actor.IsAlive || actor.IsGhost) return false;
            var poi = BotPoi.Current;
            bool committed = poi != null && poi.Type == PoiType.Kill;
            bool threat = HasProtectiveCombat(actor);
            if (!committed && !threat) return false;

            // Known unmounted ground state can use the ordinary combat owner.
            // UNKNOWN/flying/falling remains a transition concern, never attack permission.
            if (!IsMountedOrFlying(actor))
                return !CanActUnmounted();
            return committed;
        }

        /// <summary>Transition the current committed Kill or protective threat.</summary>
        public GroundTransitionState TickCurrent(WoWPoint travelDestination)
        {
            if (_active) return Continue();

            var actor = ObjectManager.Me;
            var poi = BotPoi.Current;
            bool committed = poi != null && poi.Type == PoiType.Kill;
            bool playerThreat = actor?.Combat == true;
            WoWUnit protectivePet = actor?.Pet;
            bool petThreat = protectivePet != null && protectivePet.IsAlive && protectivePet.Combat;
            bool threat = playerThreat || petThreat;
            if (actor == null || !committed && !threat) return GroundTransitionState.Revoked;
            if (IsMountedOrFlying(actor) && !committed && !RequiresProtectiveHandoff(travelDestination))
                return GroundTransitionState.Revoked;

            var capturedPoi = poi;
            long poiWorkGeneration = BotPoi.CurrentWorkGeneration;
            if (committed)
            {
                WoWUnit target = capturedPoi?.AsObject as WoWUnit;
                ulong poiGuid = capturedPoi?.Guid ?? 0, targetGuid = target?.Guid ?? 0;
                uint poiEntry = capturedPoi?.Entry ?? 0, targetAddress = target?.BaseAddress ?? 0, targetEntry = target?.Entry ?? 0;
                _subject = target;
                _destination = target?.Location ?? (capturedPoi?.Location ?? actor.Location);
                _admitted = () => ReferenceEquals(BotPoi.Current, capturedPoi) && BotPoi.CurrentWorkGeneration == poiWorkGeneration
                    && capturedPoi?.Type == PoiType.Kill && capturedPoi.Guid == poiGuid && capturedPoi.Entry == poiEntry
                    && (target == null || targetGuid != 0 && targetAddress != 0 && target.IsValid && target.IsAlive
                        && target.Guid == targetGuid && target.BaseAddress == targetAddress && target.Entry == targetEntry
                        && ReferenceEquals(capturedPoi.AsObject, target));
            }
            else
            {
                // After observed mount loss, protective combat has no proven
                // enemy destination. Wait for supported ground at the actor's
                // handoff point before ordinary combat targeting can resume.
                // The travel destination and an unrelated displayed target are
                // neither landing evidence nor threat ownership.
                ulong actorGuid = actor.Guid;
                uint actorAddress = actor.BaseAddress, actorMap = actor.MapId;
                ulong petGuid = petThreat ? protectivePet.Guid : 0;
                uint petAddress = petThreat ? protectivePet.BaseAddress : 0;
                _subject = null;
                _destination = actor.Location;
                bool ActorCurrent() => actorGuid != 0 && actorAddress != 0
                    && ReferenceEquals(ObjectManager.Me, actor) && actor.IsValid && actor.IsAlive && !actor.IsGhost
                    && actor.Guid == actorGuid && actor.BaseAddress == actorAddress && actor.MapId == actorMap;
                bool PetThreatCurrent() => petThreat && petGuid != 0 && petAddress != 0
                    && ReferenceEquals(actor.Pet, protectivePet) && protectivePet.IsValid && protectivePet.IsAlive
                    && protectivePet.Guid == petGuid && protectivePet.BaseAddress == petAddress && protectivePet.Combat;
                _admitted = () => ActorCurrent() && !IsMountedOrFlying(actor)
                    && ((playerThreat && actor.Combat) || PetThreatCurrent())
                    && ActorCurrent() && !IsMountedOrFlying(actor);
            }
            unchecked { _lifetime++; }
            _active = true;
            return Continue(allowAlreadyGrounded: true);
        }

        /// <summary>Transition a caller-owned explicit pull/target intent.</summary>
        public GroundTransitionState TickExplicit(WoWUnit subject, Func<bool> admitted)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(admitted);
            bool starting = !_active;
            if (!_active)
            {
                _subject = subject;
                _destination = subject.Location;
                _admitted = admitted;
                unchecked { _lifetime++; }
                _active = true;
            }
            else if (!ReferenceEquals(_subject, subject))
            {
                Cancel();
                return GroundTransitionState.Revoked;
            }
            return Continue(allowAlreadyGrounded: starting);
        }

        private GroundTransitionState Continue(bool allowAlreadyGrounded = false)
        {
            var admitted = _admitted;
            var subject = _subject;
            var destination = _destination;
            long lifetime = _lifetime;
            bool OwnsLifetime() => _active && _lifetime == lifetime && ReferenceEquals(_admitted, admitted);
            bool OwnerAdmitted()
            {
                if (!OwnsLifetime() || admitted == null) return false;
                bool allowed = admitted();
                return allowed && OwnsLifetime();
            }

            if (!OwnerAdmitted())
            {
                if (OwnsLifetime()) Cancel();
                return GroundTransitionState.Revoked;
            }
            GroundTransitionState state;
            try
            {
                // A new ordinary combat tick needs complete unmounted admission,
                // not another landing route and collision search. An existing
                // transition still owns its descent/dismount acknowledgement.
                var initial = allowAlreadyGrounded
                    ? new GroundTransitionContext(subject, destination, false, OwnerAdmitted, combatRoute: true)
                    : null;
                bool SameInitialOwner() => OwnerAdmitted() && (initial == null || initial.Current);
                bool ready = allowAlreadyGrounded && CanActUnmounted(SameInitialOwner);
                if (!OwnsLifetime()) return GroundTransitionState.Revoked;
                // A failed ground observation can also mean the actor/session
                // changed. Never reacquire a new epoch as the old request's
                // fallback landing owner.
                if (!SameInitialOwner())
                {
                    Cancel();
                    return GroundTransitionState.Revoked;
                }
                state = ready ? GroundTransitionState.Ready : _ground.Tick(destination, subject, SameInitialOwner);
            }
            catch (InvalidProcessException error) { throw new OperationCanceledException("Mounted combat lost the game process.", error); }
            catch (InvalidExecutorException error) { throw new OperationCanceledException("Mounted combat lost the native executor.", error); }
            catch (ObservationUnavailableException error)
            {
                RecoveryActions.RethrowControlFlow(error);
                if (!OwnsLifetime()) return GroundTransitionState.Revoked;
                state = GroundTransitionState.Unavailable;
            }
            if (!OwnsLifetime()) return GroundTransitionState.Revoked;
            if (state is GroundTransitionState.Ready or GroundTransitionState.Revoked)
            {
                long detachedLifetime = unchecked(lifetime + 1);
                Cancel();
                if (_active || _lifetime != detachedLifetime)
                    return GroundTransitionState.Revoked;
            }
            return state;
        }

        public void Cancel()
        {
            unchecked { _lifetime++; }
            _admitted = null; _subject = null; _destination = WoWPoint.Empty; _active = false;
            _ground.Cancel();
        }

        public void Dispose() => Cancel();

        /// <summary>
        /// Guard an attack composite with one captured execution epoch and a fresh
        /// unmounted observation before and after every resumed child tick.
        /// </summary>
        public static Composite GuardAction(Composite child, Func<bool> admitted = null)
            => new UnmountedActionGuard(child, admitted ?? (() => true));

        public static bool CanActUnmounted(Func<bool> admitted = null)
        {
            if (!TreeRoot.IsRunning) throw new OperationCanceledException("Mounted combat session stopped.");
            try { return GroundTransition.CanActUnmounted(admitted); }
            catch (InvalidProcessException error) { throw new OperationCanceledException("Mounted combat lost the game process.", error); }
            catch (InvalidExecutorException error) { throw new OperationCanceledException("Mounted combat lost the native executor.", error); }
        }

        internal static ActionLease CaptureActionLease(WoWObject subject, Func<bool> admitted)
        {
            ArgumentNullException.ThrowIfNull(admitted);
            try { return new ActionLease(subject, admitted); }
            catch (Exception error)
            {
                if (error is InvalidProcessException process) throw new OperationCanceledException("Mounted combat lost the game process.", process);
                if (error is InvalidExecutorException executor) throw new OperationCanceledException("Mounted combat lost the native executor.", executor);
                RecoveryActions.RethrowControlFlow(error);
                if (error is ObservationUnavailableException) return null;
                throw;
            }
        }

        internal sealed class ActionLease
        {
            private readonly GroundTransitionContext _context;
            internal ActionLease(WoWObject subject, Func<bool> admitted)
                => _context = new GroundTransitionContext(subject, subject?.Location ?? WoWPoint.Empty, false, admitted);
            internal bool Current
            {
                get
                {
                    if (!TreeRoot.IsRunning) throw new OperationCanceledException("Mounted combat session stopped.");
                    try { return _context.Current && CanActUnmounted(() => _context.Current); }
                    catch (InvalidProcessException error) { throw new OperationCanceledException("Mounted combat lost the game process.", error); }
                    catch (InvalidExecutorException error) { throw new OperationCanceledException("Mounted combat lost the native executor.", error); }
                }
            }
        }

        private sealed class UnmountedActionGuard : Decorator
        {
            private readonly Func<bool> _admitted;
            private ActionLease _lease;
            private long _version;
            internal UnmountedActionGuard(Composite child, Func<bool> admitted) : base(child) => _admitted = admitted;

            public override void Start(object context)
            {
                unchecked { _version++; }
                _lease = CaptureActionLease(null, _admitted);
                base.Start(context);
            }

            public override RunStatus Tick(object context)
            {
                var lease = _lease;
                long version = _version;
                if (lease?.Current == true)
                {
                    RunStatus result = base.Tick(context);
                    // Composite.Tick invokes Stop on terminal results. That
                    // normal cleanup must not turn a successful pull into a
                    // failure and let the parent execute a travel fallback.
                    if (_version == version && lease.Current
                        && (ReferenceEquals(_lease, lease) || result != RunStatus.Running)) return result;
                }
                LastStatus = RunStatus.Failure;
                base.Stop(context);
                return RunStatus.Failure;
            }

            public override void Stop(object context)
            {
                // An explicit stop of a running/starting lifetime still revokes
                // it; a successor Start gets a different version independently.
                if (!LastStatus.HasValue || LastStatus == RunStatus.Running)
                    unchecked { _version++; }
                _lease = null;
                base.Stop(context);
            }
        }

        private sealed class CurrentTransitionBehavior : Composite
        {
            private readonly MountedCombatTransition _owner = new();
            protected override IEnumerable<RunStatus> Execute(object context)
            {
                while (true)
                {
                    GroundTransitionState state = _owner.TickCurrent(BotPoi.Current?.Location ?? WoWPoint.Empty);
                    if (state is GroundTransitionState.Pending or GroundTransitionState.Unavailable)
                    { yield return RunStatus.Running; continue; }
                    yield return RunStatus.Failure;
                    yield break;
                }
            }
            public override void Stop(object context)
            {
                _owner.Cancel();
                base.Stop(context);
            }
        }

        public static Composite CreateBehavior() => new CurrentTransitionBehavior();
    }
}
