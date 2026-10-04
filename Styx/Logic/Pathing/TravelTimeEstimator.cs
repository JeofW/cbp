using System;
using System.Globalization;
using System.Linq;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

namespace Styx.Logic.Pathing;

/// <summary>
/// Optional travel economics, not movement/interaction authority. Walking uses
/// current speed and complete mesh length; mounting pays the selected spell's
/// cast time and explicit transition allowances. Unknown is not free mounting.
/// </summary>
internal static class TravelTimeEstimator
{
    private const double BaseTravelSpeed = 7.0;
    private const double GroundTransitionSeconds = 1.0;
    private const double MinimumSavingSeconds = 1.0;
    private const long GeometryLifetimeMs = 1000;
    private static Geometry? _geometry;
    private static long _nextDiagnostic;

    private sealed record Geometry(LocalPlayer Actor, object Memory, object Executor,
        object? Profile, object Provider, BotPoi Poi, long Work, WoWPoint Origin,
        WoWPoint Destination, long Sampled, double? Length, Func<bool> Current);

    internal static bool ShouldMount(WoWPoint destination, float minimumDistance)
    {
        var actor = StyxWoW.Me;
        if (actor == null || actor.Mounted || !CharacterSettings.Instance.UseMount
            || !float.IsFinite(minimumDistance) || minimumDistance < 0
            || !Finite(destination) || destination.Equals(WoWPoint.Empty)
            || actor.Location.Distance(destination) < minimumDistance) return false;
        try
        {
            // Match the ordinary MountUp owner's selected companion: with no
            // configured flying name it casts the ground choice, even in Outland.
            bool flight = Flightor.CanFly && !string.IsNullOrEmpty(CharacterSettings.Instance.FlyingMountName);
            WoWSpell? spell;
            if (flight) spell = Flightor.MountHelper.FlyingMount;
            else
            {
                string name = LevelbotSettings.Instance.MountName;
                var choices = MountHelper.GroundMounts;
                var selected = choices.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)
                    || m.CreatureSpellId.ToString(CultureInfo.InvariantCulture) == name);
                if (selected == null && CharacterSettings.Instance.FindMountAutomatically
                    && (string.IsNullOrEmpty(name) || name == "Mount Name Here" || name.Contains("Automatically detected")))
                    selected = choices.FirstOrDefault();
                spell = selected?.CreatureSpell;
            }
            double stopRange = BotPoi.Current.Type == PoiType.Kill ? Math.Max(0, Targeting.PullDistance) : 0;
            return ReferenceEquals(actor, StyxWoW.Me)
                && PreferMount(actor, destination, stopRange, spell, flight, alreadyMounted: false);
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is not ObservationUnavailableException) throw;
            return false;
        }
    }

    internal static bool PreferFlight(WoWPoint destination, double stopRange, WoWSpell? spell, bool alreadyMounted)
    {
        var actor = StyxWoW.Me;
        if (actor == null || Navigator.IsInNoFlyZone || Navigator.IsRidingElevator
            || !Finite(destination) || !double.IsFinite(stopRange) || stopRange < 0
            || actor.Location.Distance(destination) <= Math.Max(20.0, stopRange * 3.0)) return false;
        return PreferMount(actor, destination, stopRange, spell, flight: true, alreadyMounted);
    }

    private static bool PreferMount(LocalPlayer actor, WoWPoint destination, double stopRange,
        WoWSpell? spell, bool flight, bool alreadyMounted)
    {
        if (spell == null || !CharacterSettings.Instance.UseMount || !Finite(destination)
            || !actor.IsValid || !actor.IsAlive || actor.IsGhost || actor.Combat
            || actor.IsOnTransport || actor.OnTaxi || actor.IsSwimming) return false;
        try
        {
            var nativeOwner = WorldQueryObservation.CaptureLocalOwner(actor);
            var memory = ObjectManager.Wow; var executor = ObjectManager.Executor;
            var provider = Navigator.NavigationProvider; var profile = ProfileManager.CurrentProfileSnapshot;
            var poi = BotPoi.Current; long work = BotPoi.CurrentWorkGeneration;
            var settings = CharacterSettings.Instance;
            string groundName = settings.MountName, flightName = settings.FlyingMountName;
            WoWPoint origin = actor.Location;
            double speed = actor.MovementInfo.RunSpeed;
            bool Current() => nativeOwner() && ReferenceEquals(StyxWoW.Me, actor)
                && ReferenceEquals(Navigator.NavigationProvider, provider)
                && ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile)
                && ReferenceEquals(BotPoi.Current, poi) && BotPoi.CurrentWorkGeneration == work
                && ReferenceEquals(CharacterSettings.Instance, settings) && settings.UseMount
                && settings.MountName == groundName && settings.FlyingMountName == flightName
                && actor.IsAlive && !actor.IsGhost && !actor.Combat && !actor.IsOnTransport && !actor.OnTaxi
                && (flight || actor.Location.Equals(origin)) && actor.MovementInfo.RunSpeed == speed && nativeOwner();
            if (!Current() || !Finite(origin) || !double.IsFinite(speed) || speed <= 0) return false;
            double mountedSpeed = MountedSpeed(spell, flight);
            double cast = alreadyMounted ? 0 : spell.CastTime / 1000.0;
            // Failed legacy CastTime reads return zero. Only the original
            // instant flight forms have known zero cast time in this path.
            if (!double.IsFinite(mountedSpeed) || mountedSpeed <= 0 || !double.IsFinite(cast)
                || cast < 0 || cast == 0 && !alreadyMounted && spell.Id is not (33943 or 40120)
                || !Current()) return false;
            double? meshLength = GroundLength(actor, origin, destination, memory, executor, profile, provider, poi, work, nativeOwner);
            if (!Current()) return false;
            WoWPoint currentOrigin = actor.Location;
            if (!Finite(currentOrigin)) return false;
            // This is an optional cost estimate, not a movement admission. When
            // the same actor moves during a flight comparison, discard the old
            // ground path length and use the current straight-line lower bound.
            // The actual mount/takeoff owner still observes its stationary state
            // and complete geometry before issuing an effect.
            if (!origin.Equals(currentOrigin))
            {
                if (!flight) return false;
                meshLength = null;
                origin = currentOrigin;
            }
            double direct = origin.Distance(destination);
            // Missing ground geometry supplies only a straight-line lower bound
            // for flight. It never establishes a complete ground mount route.
            if (!flight && !meshLength.HasValue) return false;
            double walkDistance = Math.Max(0, (meshLength ?? direct) - stopRange);
            double mountedDistance = flight ? Math.Max(0, direct - stopRange) : walkDistance;
            double walking = walkDistance / speed;
            double riding = flight
                ? (mountedDistance + 80.0) / mountedSpeed + cast + 6.0
                : mountedDistance / mountedSpeed + cast + GroundTransitionSeconds;
            bool prefer = double.IsFinite(walking) && double.IsFinite(riding)
                && riding + MinimumSavingSeconds < walking;
            long now = Environment.TickCount64;
            if (now >= _nextDiagnostic)
            {
                _nextDiagnostic = now + 5000;
                Logging.WriteDiagnostic("[TravelCost] choice={0} mode={1} spell={2} walkSeconds={3:F2} mountedSeconds={4:F2} castSeconds={5:F2} route={6} distance={7:F1}; estimates-only",
                    prefer ? "mount" : "walk", flight ? "flight" : "ground", spell.Id, walking, riding, cast,
                    meshLength.HasValue ? "complete-mesh" : "straight-lower-bound", meshLength ?? direct);
            }
            return prefer && Current();
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is not ObservationUnavailableException) throw;
            return false;
        }
    }

    internal static double MountedSpeed(WoWSpell spell, bool flight)
    {
        if (spell == null) return 0;
        double result = 0;
        foreach (var effect in spell.SpellEffects)
        {
            if (effect == null || (int)effect.EffectType != 6) continue;
            int aura = (int)effect.AuraType;
            if (flight ? aura is not (207 or 208 or 209 or 211) : aura != 32) continue;
            // Original335 CalcValue: die-sides0 adds nothing;1 adds exactly1.
            // Random or level-scaled modifiers are not a known fixed speed.
            if (effect.RealPointsPerLevel != 0 || effect.DieSides is not (0 or 1)) return 0;
            double increase = (double)effect.BasePoints + effect.DieSides;
            if (increase <= 0 || increase > 1000) return 0;
            double candidate = BaseTravelSpeed * (1 + increase / 100);
            if (result != 0 && Math.Abs(result - candidate) > .001) return 0;
            result = candidate;
        }
        return result;
    }

    private static double? GroundLength(LocalPlayer actor, WoWPoint origin, WoWPoint destination,
        object memory, object executor, object? profile, object provider, BotPoi poi, long work, Func<bool> nativeOwner)
    {
        long now = Environment.TickCount64;
        var cached = _geometry;
        if (cached != null && ReferenceEquals(cached.Actor, actor) && ReferenceEquals(cached.Memory, memory)
            && ReferenceEquals(cached.Executor, executor) && ReferenceEquals(cached.Profile, profile)
            && ReferenceEquals(cached.Provider, provider) && ReferenceEquals(cached.Poi, poi) && cached.Work == work
            && cached.Destination.Equals(destination) && now >= cached.Sampled && now - cached.Sampled < GeometryLifetimeMs)
        {
            // No old route length after motion. Rate-limit optional cost queries
            // while normal on-foot navigation continues, rather than stalling it.
            if (!cached.Origin.Equals(origin)) return null;
            if (cached.Current()) return cached.Length;
        }
        _geometry = null;
        var route = Navigator.GeneratePath(origin, destination);
        if (!nativeOwner() || !ReferenceEquals(Navigator.NavigationProvider, provider)
            || !ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile)
            || !ReferenceEquals(BotPoi.Current, poi) || BotPoi.CurrentWorkGeneration != work) return null;
        double? length = null;
        if (route != null && route.Length >= 2 && route.Length <= 100000 && route.All(Finite)
            && route[0].Distance(origin) <= 5 && route[^1].Distance(destination) <= 5)
        {
            double sum = origin.Distance(route[0]);
            for (int index = 1; index < route.Length; index++) sum += route[index - 1].Distance(route[index]);
            sum += route[^1].Distance(destination);
            if (double.IsFinite(sum) && sum >= origin.Distance(destination) - .01) length = sum;
        }
        _geometry = new(actor, memory, executor, profile, provider, poi, work, origin, destination, now, length, nativeOwner);
        return length;
    }

    private static bool Finite(WoWPoint point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
