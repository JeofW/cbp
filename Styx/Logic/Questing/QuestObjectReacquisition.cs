using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Questing;

internal sealed class QuestObjectReacquisition : IDisposable
{
    private readonly Func<object?> _active;
    private readonly Func<WoWGameObject, bool> _eligible;
    private readonly Func<WoWGameObject, float?> _routeDistance;
    private readonly LootTargeting _targeting;
    private Func<bool>? _current;
    private ulong _guid;
    private uint _entry;
    private WoWPoint _destination;
    private bool _disposed;
    private bool _suspended;
    private long _generation;

    internal QuestObjectReacquisition(Func<object?> active, Func<WoWGameObject, bool> eligible, Func<WoWGameObject, float?> routeDistance)
    {
        _active = active; _eligible = eligible; _routeDistance = routeDistance; _targeting = LootTargeting.Instance;
        BotPoi.CurrentChanged += ObserveTransition;
        BotEvents.Player.OnPlayerDied += Clear;
        _targeting.WeighTargetsFilter += Weigh;
    }

    private void ObserveTransition(BotPoi previous, BotPoi next)
    {
        long generation = _generation;
        try
        {
            if (_disposed || !ReferenceEquals(BotPoi.Current, next)) return;
            bool selectedObject = next.Type == PoiType.Loot && next.AsObject is WoWGameObject;
            // Corpse handling may temporarily publish Loot. A new GameObject
            // selection or service/quest handoff retires the old intent.
            if (!selectedObject && next.Type != PoiType.Kill)
            {
                if (next.Type != PoiType.None && !(next.Type == PoiType.Loot && next.AsObject is WoWUnit)) Clear();
                else if (_current != null && ObjectManager.Me is { } player && (player.Combat || player.IsActuallyInCombat))
                    _suspended = true;
                return;
            }
            if (!selectedObject && previous.Type != PoiType.Loot) return;
            var source = selectedObject ? next : previous;
            Clear();
            generation = _generation;
            var executor = _active();
            if (_disposed || executor == null || source.AsObject is not WoWGameObject subject
                || source.Guid == 0 || subject.Guid != source.Guid || !_eligible(subject)) return;
            var actor = ObjectManager.Me;
            var memory = ObjectManager.Wow;
            var profile = ProfileManager.CurrentProfileSnapshot;
            var provider = Navigator.NavigationProvider;
            ulong actorGuid = actor?.Guid ?? 0;
            uint map = actor?.MapId ?? 0, address = actor?.BaseAddress ?? 0;
            DateTime expires = DateTime.UtcNow.AddMinutes(2);
            bool ParticipantsCurrent() => !_disposed && actor != null && actorGuid != 0 && address != 0 && memory != null
                && ReferenceEquals(ObjectManager.Me, actor) && actor.BaseAddress == address && actor.Guid == actorGuid
                && actor.MapId == map && actor.IsValid && actor.IsAlive
                && ReferenceEquals(ObjectManager.Wow, memory) && ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile)
                && ReferenceEquals(Navigator.NavigationProvider, provider) && ReferenceEquals(LootTargeting.Instance, _targeting)
                && DateTime.UtcNow < expires;
            bool Current() => ParticipantsCurrent() && ReferenceEquals(_active(), executor) && ParticipantsCurrent();
            ulong guid = subject.Guid;
            uint entry = subject.Entry;
            WoWPoint destination = subject.Location;
            if (!Current() || generation != _generation || !ReferenceEquals(BotPoi.Current, next)) return;
            if (source.Guid != guid || subject.Guid != guid || subject.Entry != entry || !subject.Location.Equals(destination)) return;
            _guid = guid; _entry = entry; _destination = destination; _current = Current;
            _suspended = !selectedObject;
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        { if (generation == _generation) Clear(); }
    }

    private void Weigh(List<Targeting.TargetPriority> targets)
    {
        var current = _current;
        try
        {
            if (current == null) return;
            if (!current()) { if (ReferenceEquals(_current, current)) Clear(); return; }
            var actor = ObjectManager.Me!;
            // Combat and its immediate corpse loot keep priority. Empty combat
            // candidate lists do not mean the suspended object despawned.
            if (actor.Combat || actor.IsActuallyInCombat) { _suspended = true; return; }
            if (!_suspended || targets.Any(t => t.Object is WoWUnit u && u.CanLoot)) return;
            var selected = targets.FirstOrDefault(t => t.Object is WoWGameObject && t.Object.Guid == _guid && t.Object.Entry == _entry);
            if (selected?.Object is not WoWGameObject subject || !subject.IsValid || subject.IsDisabled
                || !subject.Location.Equals(_destination) || Blacklist.Contains(_guid, false) || !_eligible(subject)) { if (ReferenceEquals(_current, current)) Clear(); return; }
            WoWPoint origin = actor.Location;
            float? distance = subject.WithinInteractRange ? 0f : _routeDistance(subject);
            if (!current() || !ReferenceEquals(_current, current) || !actor.Location.Equals(origin)) return;
            if (!distance.HasValue || !float.IsFinite(distance.Value) || distance.Value < 0)
            { Clear(); return; }
            // Probe only the highest-ranked alternative, keeping resumption
            // bounded. A threefold route plus forty extra yards is materially
            // worse; a modest distance improvement does not discard the intent.
            var alternative = targets.Where(t => t.Object is WoWGameObject && t.Object.Guid != _guid)
                .OrderByDescending(t => t.Score).FirstOrDefault()?.Object as WoWGameObject;
            if (alternative != null && _eligible(alternative))
            {
                ulong alternativeGuid = alternative.Guid;
                uint alternativeEntry = alternative.Entry;
                WoWPoint alternativeLocation = alternative.Location;
                float? alternateDistance = alternative.WithinInteractRange ? 0f : _routeDistance(alternative);
                if (!current() || !ReferenceEquals(_current, current) || !actor.Location.Equals(origin)) return;
                if (alternative.Guid != alternativeGuid || alternative.Entry != alternativeEntry
                    || !alternative.Location.Equals(alternativeLocation) || !alternative.IsValid || alternative.IsDisabled) return;
                if (alternateDistance.HasValue && float.IsFinite(alternateDistance.Value) && alternateDistance.Value >= 0
                    && distance.Value > alternateDistance.Value * 3f && distance.Value > alternateDistance.Value + 40f)
                { Clear(); return; }
            }
            double highest = targets.Where(t => t.Object is WoWGameObject).Max(t => t.Score);
            if (!double.IsFinite(highest) || !_eligible(subject) || !current() || !ReferenceEquals(_current, current)
                || subject.Guid != _guid || subject.Entry != _entry || !subject.Location.Equals(_destination)
                || !subject.IsValid || subject.IsDisabled) return;
            // This only reorders freshly admitted candidates. It never admits an
            // absent object, publishes a POI, restores a path or requests input.
            selected.Score = highest + 1;
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        { if (ReferenceEquals(_current, current)) Clear(); }
    }

    private void Clear() { _generation++; _current = null; _guid = 0; _entry = 0; _destination = WoWPoint.Empty; _suspended = false; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Clear();
        BotPoi.CurrentChanged -= ObserveTransition;
        BotEvents.Player.OnPlayerDied -= Clear;
        _targeting.WeighTargetsFilter -= Weigh;
    }
}
