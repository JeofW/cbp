using System;
using System.Globalization;
using System.Linq;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.World;

namespace Styx.Logic.Pathing;

/// <summary>Ground companion selection and fenced, nonblocking mount submission.</summary>
internal sealed class GroundTravelMount
{
    private readonly GroundMountRequest _request = new();
    private WoWSpell? _selected;
    private double _nextDiagnostic;

    private void Explain(double now, string configured, string reason)
    {
        if (!double.IsFinite(now) || now < _nextDiagnostic) return;
        _nextDiagnostic = now + 5;
        Logging.WriteDiagnostic("[GroundTravel] configured={0}; reason={1}; decision-only, not mount acknowledgement",
            configured, reason);
    }

    internal bool Wait(GroundTransitionContext context, double now, Func<bool> owner, Action stop)
    {
        var actor = context.Actor;
        var settings = CharacterSettings.Instance;
        string configured = settings.MountName;
        bool Current() => owner() && context.Current && ReferenceEquals(CharacterSettings.Instance, settings)
            && settings.UseMount && settings.MountName == configured && !actor.Combat && !actor.IsSwimming
            && !actor.OnTaxi && !actor.IsOnTransport && context.Current && owner();
        if (!Current()) return false;
        try
        {
            bool mounted = WorldQueryObservation.ReadGroundUnitState(actor).Mounted;
            return _request.Waiting(now, mounted, actor.IsCasting || actor.ChanneledCastingSpellId != 0,
                actor.IsMoving, Current, Select, Submit, stop);
        }
        catch (ObservationUnavailableException error)
        {
            RecoveryActions.ReportDeferral(error, "Ground travel mount");
            return false;
        }

        int Select()
        {
            double journey = context.RemainingGroundTravelDistance ?? actor.Location.Distance(context.Destination);
            int No(string reason) { Explain(now, configured, reason); return 0; }
            if (!Current()) return 0;
            if (!Mount.AreMountTimersReady) return No("mount-retry-or-post-combat-timer");
            if (actor.IsCasting) return No("actor-casting");
            if (!actor.IsOutdoors) return No("indoors");
            if (!double.IsFinite(journey)) return No("journey-distance-unavailable");
            if (journey < Math.Max(12, settings.MountDistance)) return No("journey-below-mount-distance");
            var mounts = Styx.Logic.MountHelper.GroundMounts;
            var chosen = mounts.FirstOrDefault(m => string.Equals(m.Name, configured, StringComparison.OrdinalIgnoreCase)
                || m.CreatureSpellId.ToString(CultureInfo.InvariantCulture) == configured);
            if (chosen == null && settings.FindMountAutomatically
                && (string.IsNullOrWhiteSpace(configured) || configured == "Mount Name Here" || configured.Contains("Automatically detected")))
                chosen = mounts.FirstOrDefault();
            var spell = chosen?.CreatureSpell;
            if (spell == null || !spell.IsValid) return No("configured-ground-companion-unavailable");
            if (!Current()) return 0;
            if (!spell.TryGetCurrentSpellInfo(out uint cast, out _, out _, out _) || cast == 0)
                return No("mount-cast-metadata-unavailable");
            double speed = actor.MovementInfo.RunSpeed;
            double mountedSpeed = TravelTimeEstimator.MountedSpeed(spell, flight: false);
            double distance = Math.Max(0, journey - 12);
            // Straight distance is a LOWER bound on a ground journey. When the
            // faster mount already saves time at that bound, a detour increases
            // the saving. This estimate grants no route or arrival authority.
            if (!double.IsFinite(speed) || speed <= 0 || !double.IsFinite(mountedSpeed) || mountedSpeed <= 0)
                return No("mount-speed-metadata-unavailable");
            if (mountedSpeed <= speed || !double.IsFinite(distance)
                || distance / speed <= distance / mountedSpeed + cast / 1000.0 + 2)
                return No("journey-cost-prefers-walking");
            if (!Current()) return 0;
            _selected = spell;
            return spell.Id;
        }

        bool Submit(int spellId)
        {
            var spell = _selected;
            if (spell == null || spell.Id != spellId || !Current() || !Mount.CanMount()
                || actor.IsMoving || actor.IsCasting || actor.ChanneledCastingSpellId != 0
                || WorldQueryObservation.ReadLocalVehicle(actor) || !Current())
            { Explain(now, configured, "mount-admission-unavailable"); return false; }
            if (!Mount.AllowMountAttempt(false, spell.Name, context.Destination) || !Current())
            { Explain(now, configured, "mount-veto-or-owner-revocation"); return false; }
            var position = actor.Location;
            var state = WorldQueryObservation.ReadGroundUnitState(actor);
            bool Entry() => Current() && !state.Mounted && !state.OnTaxi && !state.Rooted && !state.Stunned
                && actor.TryGetMovementState(out uint flags, out ulong transport) && transport == 0
                && (flags & 0x02003000u) == 0 && !actor.IsMoving && !actor.IsCasting && actor.ChanneledCastingSpellId == 0
                && WorldQueryObservation.ReadGroundUnitState(actor).Equals(state) && actor.Location.Equals(position)
                && Current();
            if (!Entry()) return false;
            // Reserve retry throttling before the native call: an ambiguous
            // post-entry response must not duplicate a possibly-started mount.
            Mount.ResetMountTimer();
            try
            {
                var reply = Lua.GetObservedReturnValues(BuildScript(spellId, context.ActorGuid), Entry);
                bool pending = reply.Count == 1 && (reply[0] == "cb-ground-mount-submitted" || reply[0] == "cb-ground-mount-pending");
                if (pending) Logging.Write("[GroundTravel] Mount request: {0}; awaiting observed mount.", spell.Name);
                else Explain(now, configured, "client-companion-request-rejected");
                return pending && Current();
            }
            catch (ObservationUnavailableException error)
            {
                RecoveryActions.ReportDeferral(error, "Ground mount submission outcome");
                return Current(); // bounded UNKNOWN wait; not mounted acknowledgement
            }
        }
    }

    internal static string BuildScript(int spellId, ulong actorGuid)
    {
        if (spellId <= 0 || actorGuid == 0) throw new ArgumentOutOfRangeException(nameof(spellId));
        string id = spellId.ToString(CultureInfo.InvariantCulture);
        string guid = "0X" + actorGuid.ToString("X16", CultureInfo.InvariantCulture);
        return "local function finite(x) return type(x)=='number' and x==x and x~=math.huge and x~=-math.huge end " +
            "local function yes(x) return x==true or x==1 end " +
            "local function no() return 'cb-ground-mount-rejected' end " +
            "for _,n in ipairs({'UnitGUID','IsMounted','UnitAffectingCombat','IsFlying','IsFalling','IsSwimming','IsOutdoors','UnitOnTaxi','UnitInVehicle','UnitCastingInfo','UnitChannelInfo','GetUnitSpeed','GetCompanionCooldown','GetTime','GetNumCompanions','GetCompanionInfo','CallCompanion'}) do if type(_G[n])~='function' then return no() end end " +
            "local actor=UnitGUID('player'); if type(actor)~='string' or string.upper(actor)~='" + guid + "' then return no() end " +
            "if IsMounted() or UnitAffectingCombat('player') or IsFlying() or IsFalling() or IsSwimming() or not yes(IsOutdoors()) or UnitOnTaxi('player') or UnitInVehicle('player') or UnitCastingInfo('player') or UnitChannelInfo('player') then return no() end " +
            "local speed=GetUnitSpeed('player'); local now=GetTime(); local count=GetNumCompanions('MOUNT'); " +
            "if not finite(speed) or speed~=0 or not finite(now) or now<0 or not finite(count) or count<0 or count>5000 or count%1~=0 then return no() end " +
            "local lease=_G.CopilotBuddy_GroundMountLease; if lease~=nil then " +
            "if type(lease)~='table' or lease.schema~='cb-ground-mount-v1' or type(lease.actor)~='string' or #lease.actor~=18 or not finite(lease.spell) or lease.spell<=0 or lease.spell%1~=0 or not finite(lease.started) or not finite(lease.untilAt) or lease.untilAt-lease.started~=10 or now<lease.started then return no() end " +
            "if lease.actor==actor and now<lease.untilAt then return 'cb-ground-mount-pending' end end " +
            "for slot=1,count do local _,name,id=GetCompanionInfo('MOUNT',slot); if id==" + id + " then " +
            // Build12340 IsUsableSpell/540670 resolves the ordinary spellbook.
            // Owned companions are a separate collection; GetCompanionCooldown
            // (53E490) uses the same exact type/slot as CallCompanion (53E5E0).
            "if type(name)~='string' or name=='' then return no() end " +
            "local s,d,e=GetCompanionCooldown('MOUNT',slot); if not finite(s) or not finite(d) or s<0 or d<0 or e~=1 or s+d>now then return no() end " +
            "_G.CopilotBuddy_GroundMountLease={schema='cb-ground-mount-v1',actor=actor,spell=id,started=now,untilAt=now+10}; " +
            "CallCompanion('MOUNT',slot); return 'cb-ground-mount-submitted' end end return no()";
    }
}
