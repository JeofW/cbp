using System;
using System.Collections.Generic;
using Styx;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions
{
    /// <summary>
    /// Select one admitted candidate and publish combat work only after that
    /// exact target is observed. A void Target request is not acknowledgement.
    /// </summary>
    internal sealed class OwnedTargetHandoff : Composite
    {
        private readonly Func<bool> canContinue;
        private readonly bool clearNavigation;
        private readonly bool requiredMountedTarget;

        internal OwnedTargetHandoff(Func<bool> canContinue, bool clearNavigation = false, bool requiredMountedTarget = false)
        {
            this.canContinue = canContinue ?? throw new ArgumentNullException(nameof(canContinue));
            this.clearNavigation = clearNavigation;
            this.requiredMountedTarget = requiredMountedTarget;
        }

        protected override IEnumerable<RunStatus> Execute(object context)
        {
            var actor = StyxWoW.Me;
            ulong actorGuid = actor?.Guid ?? 0;
            uint map = actor?.MapId ?? 0;
            var mover = WoWMovement.ActiveMover;
            ulong moverGuid = mover?.Guid ?? 0;
            var targeting = Targeting.Instance;
            var selected = targeting?.FirstUnit;
            ulong selectedGuid = selected?.Guid ?? 0;
            var previousTarget = actor?.CurrentTarget;
            ulong previousGuid = previousTarget?.Guid ?? 0;
            var provider = Navigator.NavigationProvider;
            var poi = BotPoi.Current;
            var type = poi?.Type ?? PoiType.None;
            ulong poiGuid = poi?.Guid ?? 0;
            uint poiEntry = poi?.Entry ?? 0;
            var subject = poi?.AsObject;
            ulong subjectGuid = subject?.Guid ?? 0;

            bool ParticipantsCurrent() => actor != null && actorGuid != 0 && actor.IsValid && actor.IsAlive
                && ReferenceEquals(StyxWoW.Me, actor) && actor.Guid == actorGuid && actor.MapId == map
                && (requiredMountedTarget && MountedCombatTransition.IsMountedOrFlying(actor)
                    || !actor.Combat && (!actor.GotAlivePet || actor.Pet?.Combat != true))
                && !actor.IsCasting && actor.ChanneledCastingSpellId == 0 && !actor.OnTaxi && !actor.IsOnTransport
                && mover != null && moverGuid != 0 && mover.IsValid && mover.Guid == moverGuid
                && ReferenceEquals(WoWMovement.ActiveMover, mover)
                && ReferenceEquals(Targeting.Instance, targeting) && ReferenceEquals(targeting.FirstUnit, selected)
                && selected != null && selectedGuid != 0 && selected.IsValid && selected.IsAlive && selected.Guid == selectedGuid
                && ReferenceEquals(Navigator.NavigationProvider, provider);
            bool InRange()
            {
                if (!ParticipantsCurrent()) return false;
                double distance = requiredMountedTarget && MountedCombatTransition.IsMountedOrFlying(actor)
                    ? Math.Sqrt(actor.Location.Distance2DSqr(selected.Location)) : selected.Distance;
                double range = Targeting.PullDistance;
                return double.IsFinite(distance) && distance >= 0 && double.IsFinite(range) && range >= 0
                    && distance <= range && ParticipantsCurrent();
            }
            bool OwnsPriorPoi() => poi != null && ReferenceEquals(BotPoi.Current, poi) && poi.Type == type
                && poi.Guid == poiGuid && poi.Entry == poiEntry && ReferenceEquals(poi.AsObject, subject)
                && (subject == null || subject.Guid == subjectGuid);
            bool Current() => ParticipantsCurrent() && OwnsPriorPoi() && canContinue()
                && InRange() && OwnsPriorPoi() && ParticipantsCurrent();
            bool Acknowledged() => ReferenceEquals(actor?.CurrentTarget, selected)
                && actor?.CurrentTargetGuid == selectedGuid;
            bool ExpectedDisplay() => Acknowledged() || ReferenceEquals(actor?.CurrentTarget, previousTarget)
                && actor?.CurrentTargetGuid == previousGuid;

            if (!Current() || !ExpectedDisplay()) { yield return RunStatus.Failure; yield break; }
            if (clearNavigation)
            {
                Navigator.Clear();
                if (!Current() || !ExpectedDisplay()) { yield return RunStatus.Failure; yield break; }
            }
            selected.Target();
            DateTime expires = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                if (!Current() || !ExpectedDisplay()) { yield return RunStatus.Failure; yield break; }
                if (Acknowledged()) break;
                if (DateTime.UtcNow >= expires) { yield return RunStatus.Failure; yield break; }
                yield return RunStatus.Running;
            }

            var next = new BotPoi(selected, PoiType.Kill);
            if (!Current() || !Acknowledged()) { yield return RunStatus.Failure; yield break; }
            BotPoi.Current = next;
            // Publication itself can deliver a callback. Never overwrite or clear
            // a replacement, nor certify the old work after observable revocation.
            yield return ParticipantsCurrent() && InRange() && Acknowledged()
                && ReferenceEquals(BotPoi.Current, next) && next.Type == PoiType.Kill
                && next.Guid == selectedGuid && ReferenceEquals(next.AsObject, selected)
                ? RunStatus.Success : RunStatus.Failure;
        }
    }
}
