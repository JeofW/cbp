// Flightor.cs - Ported from HB 4.3.4 and adapted for WoW 3.3.5a
// Flying pathfinding and movement - supports WotLK flying mounts
// Trinity mmaps support flying everywhere

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing.FlightorNavigation;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Vector2 = Tripper.XNAMath.Vector2;

namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Flightor - Flying movement and pathfinding
    /// Ported from HB 4.3.4, adapted for WotLK with Trinity mmap support
    /// </summary>
    public static class Flightor
    {
        private static int _pulseCount;
        private static WoWPoint _lastDestination = WoWPoint.Zero;
        private static WoWPoint _prevDestination = WoWPoint.Zero;

        // Anti-stuck state (WoD smethod_14 port)
        private static WoWPoint _antiStuckCheckPos = WoWPoint.Empty;
        private static DateTime _antiStuckLastCheck = DateTime.MinValue;
        private static readonly WaitTimer _antiStuckTimer = new WaitTimer(TimeSpan.FromMilliseconds(500));
        private static bool _asAscended;
        private static bool _asStrafedLeft;
        private static bool _asStrafedRight;
        private static WoWPoint _antiStuckStartPos = WoWPoint.Empty;
        private static LocalPlayer _antiStuckPlayer;
        private static WoWUnit _antiStuckOwner;
        private static ulong _antiStuckPlayerGuid, _antiStuckOwnerGuid;
        private static uint _antiStuckMap, _antiStuckPlayerAddress, _antiStuckOwnerAddress;
        private static bool _antiStuckAlive, _antiStuckGhost;
        private static BotPoi _antiStuckPoi;
        private static long _antiStuckPoiGeneration;
        private static Func<bool>? _antiStuckRouteLease;
        private static object _antiStuckProfile, _antiStuckProvider, _antiStuckInput, _antiStuckRequestOwner, _antiStuckMemory, _antiStuckExecutor;

        // PolyNav path state
        private static FlightPath _flightPath;
        private static LocalPlayer _pathPlayer;
        private static WoWUnit _pathMover;
        private static ulong _pathPlayerGuid, _pathMoverGuid;
        private static uint _pathMap, _pathPlayerAddress, _pathMoverAddress;
        private static bool _pathAlive, _pathGhost;
        private static BotPoi _pathPoi;
        private static long _pathPoiGeneration;
        private static Func<bool>? _pathRouteLease;
        private static object _pathProfile, _pathProvider, _pathInput, _flightRequestOwner, _pathMemory, _pathExecutor;
        private static string? _pathAerialContext;
        private static PolyNav _polyNav;
        private static uint? _polyNavMapId;
        private static WoWFactionGroup? _polyNavFaction;
        private static long _polyNavRevision;
        private static WoWPoint _lastFlightWaypoint = WoWPoint.Empty;
        internal static object RequestIdentity => _flightRequestOwner;
        internal static WoWPoint LastFlightWaypoint => _lastFlightWaypoint;

        // HB 6.2.3 woWPoint_4/5: cached outdoor takeoff spot and its associated destination.
        // When the bot can't fly from its current location, it navigates to _takeoffSpot first.
        // Cache is invalidated when destination moves >30y (DistanceSqr > 900f).
        // Ported from Flightor.smethod_10 / smethod_4 / smethod_5.
        private static WoWPoint _takeoffSpot        = WoWPoint.Empty;
        private static WoWPoint _takeoffDestination = WoWPoint.Empty;

        static Flightor()
        {
            BotEvents.OnBotStop += args => Clear();
            BotEvents.OnPulse += (sender, args) => InvalidateRouteContext();
        }

        // Detach managed state before stopping input. Only the still-current
        // actor/controller/provider may receive cleanup for the obsolete route.
        internal static void InvalidateRouteContext()
        {
            bool ContextChanged(BotPoi poi, long generation, Func<bool>? routeLease, object profile, object provider) =>
                !ReferenceEquals(BotPoi.Current, poi) || (routeLease != null ? !routeLease() : BotPoi.CurrentGeneration != generation)
                || !ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile)
                || !ReferenceEquals(Navigator.NavigationProvider, provider) || poi.IsWorldSubjectBlacklisted;
            var input = Navigator.PlayerMover;
            bool stopFlight = false, stopRecovery = false;
            if (_pathPlayer != null && (!ReferenceEquals(ObjectManager.Wow, _pathMemory) || !ReferenceEquals(ObjectManager.Executor, _pathExecutor)
                || _pathPlayer.BaseAddress != _pathPlayerAddress || _pathMover.BaseAddress != _pathMoverAddress
                || ContextChanged(_pathPoi, _pathPoiGeneration, _pathRouteLease, _pathProfile, _pathProvider)
                || !ReferenceEquals(StyxWoW.Me, _pathPlayer) || !ReferenceEquals(WoWMovement.ActiveMover, _pathMover)
                || !ReferenceEquals(input, _pathInput) || _pathPlayer.Guid != _pathPlayerGuid || _pathPlayer.MapId != _pathMap
                || _pathPlayer.IsAlive != _pathAlive || _pathPlayer.IsGhost != _pathGhost))
            {
                stopFlight = _flightPath != null && ReferenceEquals(ObjectManager.Wow, _pathMemory) && ReferenceEquals(ObjectManager.Executor, _pathExecutor)
                    && _pathPlayer.BaseAddress == _pathPlayerAddress && _pathMover.BaseAddress == _pathMoverAddress && ReferenceEquals(StyxWoW.Me, _pathPlayer)
                    && _pathPlayer.Guid == _pathPlayerGuid && _pathPlayer.MapId == _pathMap
                    && ReferenceEquals(WoWMovement.ActiveMover, _pathMover) && _pathMover.Guid == _pathMoverGuid
                    && ReferenceEquals(input, _pathInput) && ReferenceEquals(Navigator.NavigationProvider, _pathProvider);
                _flightRequestOwner = new object();
                _pathPlayer = null;
                _flightPath = null;
                _pathRouteLease = null;
                _lastDestination = WoWPoint.Empty;
                _takeoffSpot = _takeoffDestination = WoWPoint.Empty;
            }
            if (_antiStuckPlayer != null && (!ReferenceEquals(ObjectManager.Wow, _antiStuckMemory) || !ReferenceEquals(ObjectManager.Executor, _antiStuckExecutor)
                || _antiStuckPlayer.BaseAddress != _antiStuckPlayerAddress || _antiStuckOwner.BaseAddress != _antiStuckOwnerAddress
                || ContextChanged(_antiStuckPoi, _antiStuckPoiGeneration, _antiStuckRouteLease, _antiStuckProfile, _antiStuckProvider)
                || !ReferenceEquals(StyxWoW.Me, _antiStuckPlayer) || !ReferenceEquals(WoWMovement.ActiveMover, _antiStuckOwner)
                || !ReferenceEquals(input, _antiStuckInput) || _antiStuckPlayer.Guid != _antiStuckPlayerGuid
                || _antiStuckPlayer.MapId != _antiStuckMap || _antiStuckPlayer.IsAlive != _antiStuckAlive || _antiStuckPlayer.IsGhost != _antiStuckGhost))
            {
                stopRecovery = ReferenceEquals(ObjectManager.Wow, _antiStuckMemory) && ReferenceEquals(ObjectManager.Executor, _antiStuckExecutor)
                    && _antiStuckPlayer.BaseAddress == _antiStuckPlayerAddress && _antiStuckOwner.BaseAddress == _antiStuckOwnerAddress
                    && ReferenceEquals(StyxWoW.Me, _antiStuckPlayer) && _antiStuckPlayer.Guid == _antiStuckPlayerGuid
                    && _antiStuckPlayer.MapId == _antiStuckMap && ReferenceEquals(WoWMovement.ActiveMover, _antiStuckOwner)
                    && _antiStuckOwner.Guid == _antiStuckOwnerGuid && ReferenceEquals(input, _antiStuckInput)
                    && ReferenceEquals(Navigator.NavigationProvider, _antiStuckProvider);
                _antiStuckRequestOwner = new object();
                _antiStuckPlayer = null;
                _antiStuckRouteLease = null;
                _asAscended = _asStrafedLeft = _asStrafedRight = false;
                _antiStuckStartPos = WoWPoint.Empty;
            }
            if (stopFlight || stopRecovery) input.MoveStop();
        }

        /// <summary>
        /// True if the local player is currently able to fly.
        /// Ported from HB 6.2.3 Flightor.CanFly, adapted for WotLK (no WoD zone-map infrastructure).
        /// </summary>
        public static bool CanFly
        {
            get
            {
                LocalPlayer player = StyxWoW.Me;
                WoWUnit activeMover = WoWMovement.ActiveMover;
                if (player == null || activeMover == null) return false;
                ulong guid = player.Guid;
                uint mapId = player.MapId;
                object memory = ObjectManager.Wow;
                bool Current() => guid != 0 && ReferenceEquals(StyxWoW.Me, player)
                    && player.Guid == guid && player.MapId == mapId && player.IsValid
                    && ReferenceEquals(WoWMovement.ActiveMover, activeMover) && activeMover.IsMe
                    && !player.InVehicle && !player.OnTaxi && !player.IsOnTransport
                    && ReferenceEquals(ObjectManager.Wow, memory);
                if (!Current()) return false;

                // If the player is already airborne, CanFly is true by definition.
                // This prevents RemoveLootFilter from calling CanNavigateWithin (→ CalculatePathEx)
                // from mid-air when mount classification fails to populate FlyingMounts.
                if (player.MovementInfo.IsFlying) return Current();

                // WotLK 3.3.5a: flying is ONLY valid in Outland (530) or Northrend (571).
                // Explicit map guard prevents aerial path attempts in old world zones even when
                // the private server's IsFlyableArea() incorrectly returns true outside those maps.
                if (mapId != 530U && mapId != 571U)
                    return false;
                if (!player.IsAlive || player.IsGhost || !player.IsOutdoors || player.IsSwimming)
                    return false;

                // NOTE: do NOT add MovementInfo.CanFly fast-path here.
                // That bypasses IsFlyableArea() and causes Navigator→Flightor→Navigator recursion
                // when the player is already airborne in a no-fly zone (Dalaran etc.).
                // Original-client riding spells teach SKILL_RIDING; they need not
                // remain in the castable spellbook. TC 3.3.5 uses base skill225/300.
                bool hasFlyingRiding = (player.GetSkill(SkillLine.Riding)?.CurrentValue ?? 0) >= 225;
                bool hasDruidFlightForm = player.Class == WoWClass.Druid &&
                                          (SpellManager.HasSpell("Swift Flight Form") ||
                                           SpellManager.HasSpell("Flight Form"));

                return (hasFlyingRiding || hasDruidFlightForm)
                    && Current()
                    && Lua.GetReturnVal<bool>("return IsFlyableArea()", 0U)
                    && Current()
                    && MountHelper.FlyingMount != null
                    && (player.Level >= 60 || player.Class == WoWClass.Druid)
                    && (player.Level >= 58 || player.Class != WoWClass.Druid)
                    && (mapId != 571U || SpellManager.HasSpell("Cold Weather Flying"))
                    && Current();
            }
        }

        /// <summary>
        /// Flying speed multiplier used for walk-vs-fly time comparison.
        /// Ported from HB 6.2.3 Flightor.Single_0.
        /// </summary>
        private static float FlySpeedMultiplier
        {
            get
            {
                var player = StyxWoW.Me;
                int riding = player?.GetSkill(SkillLine.Riding)?.CurrentValue ?? 0;
                if (!ReferenceEquals(StyxWoW.Me, player)) return 0f;
                // Conservative travel estimate: individual 310-percent mounts
                // do not imply a later-expansion Master Riding skill.
                if (riding >= 300) return 3.8f;
                if (riding >= 225) return 2.5f;
                return 0f;
            }
        }

        /// <summary>
        /// True if ground navigation is faster than mounting and flying to <paramref name="destination"/>.
        /// Ported from HB 6.2.3 Flightor.smethod_9.
        /// </summary>
        internal static bool ShouldAttemptGroundMount(
            bool canFly,
            bool mounted,
            bool mountPolicyAllows)
        {
            return !canFly && !mounted && mountPolicyAllows;
        }

        private static bool ShouldWalk(WoWPoint destination)
        {
            if (HasSeaLegs(StyxWoW.Me)) return false;
            if (MountHelper.Mounted)             return false;
            // Raw WoW mount flag covers ground mounts and the CanFly-flag timing window
            // right after casting a flying mount (SMSG_MOVE_SET_CAN_FLY may not yet be
            // processed, so MountHelper.Mounted can return false while StyxWoW.Me.Mounted
            // is already true). Mirrors the !StyxWoW.Me.Mounted guard in RemoveLootFilter.
            if (StyxWoW.Me.Mounted)              return false;
            // HB WoD: never walk if already airborne — CalculatePathEx from mid-air always fails.
            if (StyxWoW.Me.MovementInfo.IsFlying) return false;
            if (StyxWoW.Me.IsSwimming)           return false;
            if (!CanFly)                         return true;

            double stopRange = BotPoi.Current.Type == PoiType.Kill ? Math.Max(0, Targeting.PullDistance) : 0;
            return !TravelTimeEstimator.PreferFlight(destination, stopRange, MountHelper.FlyingMount, alreadyMounted: false);
        }

        internal static bool PreferFlightForGroundInteraction(WoWPoint destination, float interactionRange, bool retainDeparture = false)
        {
            return CanFly && !Navigator.IsInNoFlyZone && !Navigator.IsRidingElevator
                && (retainDeparture || TravelTimeEstimator.PreferFlight(destination, interactionRange,
                    MountHelper.FlyingMount, MountHelper.Mounted));
        }

        internal static bool IsFlightTravelCheaper(double distance, double groundDistance,
            double groundSpeed, double flightSpeed, double mountCastSeconds)
        {
            if (!double.IsFinite(distance) || !double.IsFinite(groundDistance) || !double.IsFinite(groundSpeed)
                || !double.IsFinite(flightSpeed) || !double.IsFinite(mountCastSeconds)
                || distance < 0 || groundDistance < distance || groundSpeed <= 0 || flightSpeed <= 0 || mountCastSeconds < 0) return false;
            // Estimates, not arrival receipts: budget the normal 40-yard flight
            // height in both directions, takeoff, landing and dismount latency.
            double flightSeconds = (distance + 80.0) / flightSpeed + mountCastSeconds + 6.0;
            return flightSeconds + 1.0 < groundDistance / groundSpeed;
        }

        // Sea Legs is Vashj'ir scaffolding from the later HB port. The shipped
        // original12340 Spell.dbc has no such row/mechanic. Do not make an
        // unrelated unresolved aura's name a prerequisite for ordinary travel.
        private static bool HasSeaLegs(LocalPlayer player) => false;

        // Complete exact-name families from the same original Spell.dbc. Raw
        // IDs require complete readable aura records, but no unrelated metadata.
        private static bool HasTravelFormAura(LocalPlayer player)
            => player.GetRawAuras().Any(a => a.SpellId == 33943 || a.SpellId == 33950
                || a.SpellId == 40120 || a.SpellId == 40123 || a.SpellId == 1066 || a.SpellId == 1446);

        /// <summary>
        /// Move to destination using flying mount
        /// </summary>
        public static void MoveTo(WoWPoint destination) => MoveTo(destination, 40f);

        /// <summary>
        /// Move to destination with minimum height
        /// </summary>
        public static void MoveTo(WoWPoint destination, float minHeight)
            => MoveToCore(destination, minHeight, false, null);

        // Ground interaction already compared route/mount/landing costs. Keep
        // that decision through this request while retaining the caller's owner
        // across waits; all flight capability/collision/takeoff gates still run.
        internal static void MoveToGroundInteraction(WoWPoint destination, Func<bool> admitted)
            => MoveToCore(destination, 40f, true, admitted);

        internal static void MoveToOwnedExterior(WoWPoint destination, Func<bool> admitted, System.Action<object> registered,
            Func<bool>? routeLease = null)
        {
            if (admitted()) MoveToCore(destination, 4f, true, admitted, registered, true, routeLease);
        }

        private static void MoveToCore(WoWPoint destination, float minHeight, bool preferFlight, Func<bool> admitted,
            System.Action<object> registered = null, bool flightOnly = false, Func<bool>? routeLease = null)
        {
            object requestOwner = _flightRequestOwner = new object();
            registered?.Invoke(requestOwner);
            var memory = ObjectManager.Wow;
            var executor = ObjectManager.Executor;
            var poi = BotPoi.Current;
            long poiGeneration = BotPoi.CurrentGeneration;
            var profile = ProfileManager.CurrentProfileSnapshot;
            var provider = Navigator.NavigationProvider;
            var playerMover = Navigator.PlayerMover;
            string? aerialContext = null;
            LocalPlayer me = StyxWoW.Me;
            if (me == null)
            {
                _pathPlayer = null;
                return;
            }
            ulong playerGuid = me.Guid;
            uint map = me.MapId;
            bool alive = me.IsAlive, ghost = me.IsGhost;
            WoWUnit inputOwner = WoWMovement.ActiveMover;
            ulong inputGuid = inputOwner?.Guid ?? 0;
            uint playerAddress = me.BaseAddress, inputAddress = inputOwner?.BaseAddress ?? 0;
            bool PoiCurrent() => ReferenceEquals(BotPoi.Current, poi) && !poi.IsWorldSubjectBlacklisted
                && (routeLease != null ? routeLease() : BotPoi.CurrentGeneration == poiGeneration)
                && ReferenceEquals(BotPoi.Current, poi);
            bool CanContinue()
            {
                bool OwnsContext() => memory != null && ReferenceEquals(ObjectManager.Wow, memory) && ReferenceEquals(ObjectManager.Executor, executor)
                    && playerAddress != 0 && inputAddress != 0 && me.BaseAddress == playerAddress && inputOwner.BaseAddress == inputAddress
                    && playerGuid != 0 && inputGuid != 0 && ReferenceEquals(StyxWoW.Me, me)
                    && me.Guid == playerGuid && me.IsValid && me.MapId == map
                    && me.IsAlive == alive && me.IsGhost == ghost && inputOwner.IsValid && inputOwner.Guid == inputGuid
                    && ReferenceEquals(WoWMovement.ActiveMover, inputOwner)
                    && ReferenceEquals(_flightRequestOwner, requestOwner)
                    && PoiCurrent()
                    && ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile)
                    && ReferenceEquals(Navigator.NavigationProvider, provider) && ReferenceEquals(Navigator.PlayerMover, playerMover)
                    && (aerialContext == null || aerialContext == Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.ContextKey);
                bool valid = OwnsContext() && (admitted == null || admitted()) && OwnsContext();
                // Even if the same wrapper returns later, an observed ownership
                // gap invalidates the earlier route and takeoff observations.
                if (!valid && ReferenceEquals(_flightRequestOwner, requestOwner)) _pathPlayer = null;
                return valid;
            }
            if (!CanContinue()) return;

            // A destination alone cannot identify a route across actor, control,
            // map or life-state changes. Keep the map-keyed PolyNav geometry,
            // but discard the old traversal and takeoff observations.
            bool pathPoiChanged = !ReferenceEquals(_pathPoi, poi)
                || (routeLease == null
                    ? _pathRouteLease != null || _pathPoiGeneration != poiGeneration
                    : !ReferenceEquals(_pathRouteLease, routeLease));
            if (!ReferenceEquals(_pathMemory, memory) || !ReferenceEquals(_pathExecutor, executor)
                || _pathPlayerAddress != playerAddress || _pathMoverAddress != inputAddress
                || !ReferenceEquals(_pathPlayer, me) || _pathPlayerGuid != playerGuid
                || !ReferenceEquals(_pathMover, inputOwner) || _pathMoverGuid != inputGuid
                || _pathMap != map || _pathAlive != alive || _pathGhost != ghost
                || pathPoiChanged
                || !ReferenceEquals(_pathProfile, profile) || !ReferenceEquals(_pathProvider, provider) || !ReferenceEquals(_pathInput, playerMover))
            {
                _flightPath = null;
                _lastDestination = WoWPoint.Empty;
                _takeoffSpot = _takeoffDestination = WoWPoint.Empty;
                _pathMemory = memory; _pathExecutor = executor;
                _pathPlayerAddress = playerAddress; _pathMoverAddress = inputAddress;
                _pathPlayer = me;
                _pathPlayerGuid = playerGuid;
                _pathMover = inputOwner;
                _pathMoverGuid = inputGuid;
                _pathMap = map;
                _pathAlive = alive;
                _pathGhost = ghost;
                _pathPoi = poi;
                _pathPoiGeneration = poiGeneration;
                _pathRouteLease = routeLease;
                _pathProfile = profile;
                _pathProvider = provider;
                _pathInput = playerMover;
            }

            // P6.10: Refuse to fly in no-fly zones (Dalaran, indoor dungeons)
            // Force ground navigation instead of trying to mount a flying mount
            if (Navigator.IsInNoFlyZone && (!flightOnly || !me.IsFlying))
            {
                if (!flightOnly && CanContinue()) Navigator.MoveTo(destination);
                return;
            }

            // Don't attempt flying while riding an elevator
            if (Navigator.IsRidingElevator)
            {
                if (!flightOnly && CanContinue()) Navigator.MoveTo(destination);
                return;
            }

            // HB 6.2.3 smethod_10: NO combat early-return here.
            // When in combat and not mounted, CanMount returns false (checks !me.Combat),
            // which falls through to Navigator.MoveTo() — the bot walks on foot to the
            // destination instead of freezing. This matches HB behavior exactly.

            WoWPoint myLocation = me.Location;
            bool hasSeaLegs = HasSeaLegs(me);

            // Ground nav is faster than mounting and flying: prefer walking (HB smethod_9)
            if (!preferFlight && ShouldWalk(destination))
            {
                if (!CanContinue()) return;
                // In no-fly zones (e.g. Eastern Kingdoms in WotLK), attempt a ground mount
                // for faster patrol before falling back to on-foot navigation.
                // Reuse the shared distance policy so nearby loot and quest targets stay on foot.
                if (ShouldAttemptGroundMount(
                        CanFly,
                        StyxWoW.Me.Mounted,
                        Mount.ShouldMount(destination)))
                    if (CanContinue()) Mount.MountUp(
                        () => CanContinue() && (StyxWoW.Me.Mounted || Mount.ShouldMount(destination)),
                        () => destination);
                if (CanContinue()) Navigator.MoveTo(destination);
                return;
            }

            WoWPoint traceLinePos = me.GetTraceLinePos();
            if (!CanContinue()) return;

            // Not mounted - need to mount up
            if (!MountHelper.Mounted)
            {
                // No-fly zone (e.g. Eastern Kingdoms in WotLK): ShouldWalk() returned false
                // because me.Mounted=true (ground mount), skipping its !CanFly early-return.
                // Don't attempt flying-mount logic here — just do ground navigation.
                if (!CanFly)
                {
                    if (!flightOnly && CanContinue()) Navigator.MoveTo(destination);
                    return;
                }

                // HB 6.2.3 smethod_10 lines 581/604: can we take off from here?
                // HB 6.2.3 reduced the LOS probe from 30f (4.3.4) → 10f for normal mode
                // (flag2=false → not WoD Maelstrom map). 30f caused false negatives in canyons:
                // wall blocked the 30f ray → canFlyFromHere=false → FindTakeoffCandidate spam.
                bool canFlyFromHere = me.IsOutdoors
                    && !Mount.IsInCantMountSpot(myLocation)
                    && (hasSeaLegs || GameWorld.IsInLineOfSight(traceLinePos, myLocation.Add(0f, 0f, 10f)));
                if (!CanContinue()) return;

                // F3: Invalidate takeoff cache if destination moved >30y (HB woWPoint_5 guard).
                if (_takeoffDestination != WoWPoint.Empty && _takeoffDestination.DistanceSqr(destination) > 900f)
                {
                    _takeoffSpot        = WoWPoint.Empty;
                    _takeoffDestination = WoWPoint.Empty;
                }

                // F3: Clear takeoff cache when we're already in a good takeoff position.
                if (canFlyFromHere)
                {
                    _takeoffSpot        = WoWPoint.Empty;
                    _takeoffDestination = WoWPoint.Empty;
                }

                // F3/F4: Navigate to cached takeoff spot (with mount-up suppression).
                if (_takeoffSpot != WoWPoint.Empty)
                {
                    if (_takeoffSpot.DistanceSqr(myLocation) < 16f)            // arrived (<4y)
                    {
                        _takeoffSpot        = WoWPoint.Empty;
                        _takeoffDestination = WoWPoint.Empty;
                    }
                    else if (Navigator.CanNavigateWithin(myLocation, _takeoffSpot, Navigator.PathPrecision))
                    {
                        if (CanContinue()) NavigateToTakeoffSpot();             // suppresses OnMountUp
                        return;
                    }
                    else
                    {
                        _takeoffSpot        = WoWPoint.Empty;
                        _takeoffDestination = WoWPoint.Empty;
                    }
                }

                // HB 6.2.3 smethod_10 line 678: in WotLK 3.3.5a CanMount=false while IsSwimming=true.
                // Without this guard the bot keeps retrying the CanMount=false→Navigator.MoveTo
                // path while still in the water and never mounts → infinite mount loop.
                // Fix: ascend to surface via JumpAscend every pulse until !IsSwimming, then mount normally.
                if (me.IsSwimming && !hasSeaLegs)
                {
                    if (!CanContinue()) return;
                    WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
                    StyxWoW.Sleep(100);
                    if (!CanContinue()) return;
                    WoWMovement.MoveStop();
                    return;
                }

                // F2/F3: If blocked from flying, find an outdoor spot and cache it (HB smethod_5).
                // Guard: only search while stationary on the ground — mirrors HB's exact condition.
                if (!me.IsMoving && !me.MovementInfo.IsFlying && !canFlyFromHere)
                {
                    WoWObject candidate = FindTakeoffCandidate(myLocation, 10f);
                    if (!CanContinue()) return;
                    if (candidate != null)
                    {
                        Logging.WriteDiagnostic("[Flightor] Can't take off here. Moving to: {0}", candidate.Location);
                        if (!CanContinue()) return;
                        _takeoffSpot        = candidate.Location;
                        _takeoffDestination = destination;
                        NavigateToTakeoffSpot();
                        return;
                    }
                }

                // Try to mount
                if (MountHelper.CanMount)
                {
                    if (!CanContinue()) return;
                    // Swimming - move up first
                    if (me.IsSwimming && !HasSeaLegs(me) &&
                        !GameWorld.TraceLine(traceLinePos, myLocation, GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                    {
                        float neededFacing = WoWMathHelper.CalculateNeededFacing(myLocation, destination);
                        WoWPoint p = GetPointInDirection(myLocation, 10f, neededFacing, WoWMathHelper.DegreesToRadians(60f));
                        if (CanContinue()) Navigator.PlayerMover.MoveTowards(p);
                    }
                    // Druid flight form while swimming — HB 4.3.4 exact port.
                    else if (!HasSeaLegs(me) &&
                             me.IsSwimming &&
                             me.Class == WoWClass.Druid &&
                             (SpellManager.HasSpell("Flight Form") || SpellManager.HasSpell("Swift Flight Form")))
                    {
                        if (!CanContinue()) return;
                        WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
                        StyxWoW.Sleep(50);
                        if (!CanContinue()) return;
                        MountHelper.MountUpInternal(true);
                        if (!CanContinue()) return;
                        StyxWoW.Sleep(50);
                        if (!CanContinue()) return;
                        MountHelper.MountUpInternal(true);
                        if (!CanContinue()) return;
                        StyxWoW.Sleep(50);
                        if (!CanContinue()) return;
                        MountHelper.MountUpInternal(true);
                        if (!CanContinue()) return;
                        WoWMovement.MoveStop();
                    }
                    else
                    {
                        if (CanContinue()) MountHelper.MountUp();
                    }
                }
                else
                {
                    // CanMount=false. Two sub-cases:
                    // 1) CanFly=true, not in combat: the post-mount/post-combat timer isn't
                    //    ready yet (e.g. mount fired <10s ago for a quick gather). Calling
                    //    Navigator.MoveTo with an aerial waypoint spams CalculatePathEx:FAILED.
                    //    Just return — next tick retries CanMount until the timer expires.
                    // 2) Ground-only map (!CanFly) and not on any mount, or currently in combat:
                    //    fall back to ground navigation as HB originally intended.
                    //    !StyxWoW.Me.Mounted guard: prevents ground nav during the CanFly-flag
                    //    timing window right after mounting (same pattern as RemoveLootFilter).
                    if (!flightOnly && CanContinue() && ((!CanFly && !me.Mounted) || me.Combat) && CanContinue())
                        Navigator.MoveTo(destination);
                }
            }
            else
            {
                // Already mounted — process flight using PolyNav path queue.
                // Ported from WoD smethod_10 (Flightor.cs, HB 6.2.3).
                aerialContext = Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.ContextKey;
                if (!CanContinue()) return;
                if (_pathAerialContext != aerialContext)
                {
                    _flightPath = null;
                    _lastDestination = WoWPoint.Empty;
                    _pathAerialContext = aerialContext;
                }

                // Travel support is optional. It must not prevent an already
                // mounted route from taking off when aura/readiness data is
                // unavailable. Required flight and ownership checks stay below.
                if (myLocation.Distance(destination) > 100.0 && me.IsAlive)
                {
                    try
                    {
                        if (!me.HasAura(32223) && CanContinue()
                            && SpellManager.CanCast("Crusader Aura", me) && CanContinue())
                            SpellManager.Cast("Crusader Aura", me);
                    }
                    catch (ObservationUnavailableException error)
                    {
                        if (CanContinue()) RecoveryActions.ReportDeferral(error, "Flightor optional Crusader Aura");
                    }
                }

                if (!CanContinue()) return;
                WoWUnit activeMover = inputOwner;

                // WoD: increment pulse counter, check anti-stuck (resets counter), skip odd pulses
                ++_pulseCount;
                if (AntiStuck)
                    _pulseCount = 0;
                if (!CanContinue()) return;
                if (_pulseCount % 2 != 0)
                    return;
                _pulseCount = 0;

                // Step 1: Force ascent BEFORE path computation (WoD: mounted but not yet flying)
                if (MountHelper.Mounted && ((!hasSeaLegs && !activeMover.IsFlying) || (hasSeaLegs && !activeMover.IsSwimming)))
                {
                    WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                    StyxWoW.Sleep(100);
                    if (!CanContinue()) return;
                    WoWMovement.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                }
                if (!CanContinue()) return;

                // Step 2: CTM early return — already making progress toward same destination
                WoWMovement.ClickToMoveInfoStruct ctm = WoWMovement.ClickToMoveInfo;
                if (activeMover.IsMoving && _lastDestination == destination &&
                    ctm.IsClickMoving && ctm.ClickPos.DistanceSqr(activeMover.Location) > 900f
                    && CanFollowFlightSegment(activeMover.Location, ctm.ClickPos) && CanContinue())
                    return;

                // Step 3: Destination change → discard cached path
                if (_lastDestination != destination)
                    _flightPath = null;
                _lastDestination = destination;

                // Step 4: Build path if we don't have one
                var myPos2D = new Vector2(myLocation.X, myLocation.Y);
                var dest2D  = new Vector2(destination.X, destination.Y);
                if (_flightPath == null)
                {
                    var generatedPath = BuildPath(myPos2D, dest2D);
                    if (!CanContinue()) return;
                    _flightPath = generatedPath;
                }
                if (!CanContinue()) return;

                if (_flightPath.Waypoints.Count == 0)
                {
                    // Stop this route's old input rather than following an
                    // empty avoidance plan or dereferencing its missing head.
                    _flightPath = null;
                    if (CanContinue()) playerMover.MoveStop();
                    return;
                }

                // Consumed vertices are not new movement destinations. Advance
                // the finite queue in this dispatch and select its next point;
                // retain the final vertex for the exact destination below.
                while (_flightPath.Waypoints.Count > 1)
                {
                    Vector2 reached = _flightPath.Waypoints.Peek();
                    if (myLocation.Distance2DSqr(new WoWPoint(reached.X, reached.Y, 0)) > 900f)
                        break;
                    Vector2 following = _flightPath.Waypoints.ElementAt(1);
                    if (!CanFollowFlightSegment(myLocation, new WoWPoint(following.X, following.Y, myLocation.Z)))
                        break;
                    _flightPath.Waypoints.Dequeue();
                }
                if (!CanContinue()) return;
                Vector2 waypointVec = _flightPath.Waypoints.Peek();

                // Step 6: Smart Z + dispatch by remaining queue depth
                WoWPoint flightPoint;
                if (_flightPath.Waypoints.Count == 1)
                {
                    // Final stretch — aim directly at the actual destination
                    flightPoint = CalculateFlightPoint(destination, minHeight);
                }
                else
                {
                    // Intermediate waypoint — use dest.Z only within 200m, else maintain current altitude
                    float smartZ = destination.DistanceSqr(myLocation) < 40000f ? destination.Z : myLocation.Z;
                    flightPoint = CalculateFlightPoint(new WoWPoint(waypointVec.X, waypointVec.Y, smartZ), minHeight);
                }

                // Step 7: Apply movement or trigger anti-stuck
                if (!CanContinue()) return;
                if (flightPoint != WoWPoint.Empty)
                {
                    bool safeSegment = CanFollowFlightSegment(me.Location, flightPoint);
                    if (!CanContinue()) return;
                    if (!safeSegment)
                    {
                        _flightPath = null;
                        playerMover.MoveStop();
                        return;
                    }
                    // Only re-issue CTM if not already moving to the exact same point
                    if (!activeMover.IsMoving || ctm.ClickPos != flightPoint || !ctm.IsClickMoving)
                    {
                        _lastFlightWaypoint = flightPoint;
                        Navigator.PlayerMover.MoveTowards(flightPoint);
                    }
                    if (!CanContinue()) return;

                    // Second ascent check after issuing movement command
                    if (MountHelper.Mounted && ((!hasSeaLegs && !activeMover.IsFlying) || (hasSeaLegs && !activeMover.IsSwimming)))
                    {
                        StyxWoW.Sleep(100);
                        if (!CanContinue()) return;
                        WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                        StyxWoW.Sleep(100);
                        if (!CanContinue()) return;
                        WoWMovement.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                        if (!CanFollowFlightSegment(me.Location, flightPoint) || !CanContinue()) return;
                        Navigator.PlayerMover.MoveTowards(flightPoint);
                        return;
                    }
                }
                else
                {
                    DoAntiStuck(routeLease);
                }
                return;
            }
        }

        // ── Inner types ───────────────────────────────────────────────────────

        /// <summary>
        /// Represents a pre-computed 2D flight path with an ordered waypoint queue.
        /// Ported from WoD Class1052.
        /// </summary>
        private class FlightPath
        {
            public Vector2 StartPoint;
            public Vector2 EndPoint;
            public Queue<Vector2> Waypoints = new Queue<Vector2>();
        }

        /// <summary>
        /// Build the candidate ray list for CalculateFlightPoint.
        /// Ported from WoD smethod_12 (Flightor.cs, HB 6.2.3).
        /// </summary>
        private static List<WorldLine> BuildRayList(WoWPoint origin, float rayLength, float heading, float pitch)
        {
            const int angleStep = 15;
            var lines = new List<WorldLine>();

            // Pitch up first (most likely to clear terrain)
            for (int i = 1; i <= 3; ++i)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(i * angleStep))));

            // Turn left / right
            for (int i = 1; i <= 3; ++i)
            {
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i * -angleStep), pitch)));
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i *  angleStep), pitch)));
            }

            // More aggressive pitch up
            for (int i = 4; i <= 6; ++i)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(i * angleStep))));

            // Wider turns
            for (int i = 4; i <= 8; ++i)
            {
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i * -angleStep), pitch)));
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i *  angleStep), pitch)));
            }

            // Extreme pitch up (WoD: 7..9 inclusive)
            for (int i = 7; i <= 9; ++i)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(i * angleStep))));

            // Pitch down — descend angles (WoD: 4 downward rays, n=1..4)
            for (int n = 1; n <= 4; ++n)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(n * -angleStep))));

            return lines;
        }

        /// <summary>
        /// Calculate the next waypoint for flight, routed through the PolyNav
        /// visibility graph.  Ported from WoD smethod_11 (Flightor.cs, HB 6.2.3).
        /// Returns WoWPoint.Empty when all raycasts are blocked — caller must invoke DoAntiStuck().
        /// </summary>
        private static WoWPoint CalculateFlightPoint(WoWPoint destination, float minHeight)
        {
            LocalPlayer me = StyxWoW.Me;
            WoWPoint traceLinePos = me.GetTraceLinePos();
            WoWPoint myLocation   = me.Location;

            // Direct LOS to target — go straight.
            // IMPORTANT: must use HitTestGroundAndStructures (0x100111), NOT IsInLineOfSight
            // (which uses HitTestLOS = 0x100011, missing HitTestGround = 0x100).
            // Without the ground flag, mountains are invisible to this check and the bot
            // flies straight through solid terrain to reach nodes on the other side.
            // HB 4.3.4 used HitTestLOS = 0x100121 which includes HitTestGround — same intent.
            if (destination.Z != 0.0 &&
                traceLinePos.DistanceSqr(destination) < 40000.0 &&
                CanFollowFlightSegment(myLocation, destination) &&
                !GameWorld.TraceLine(traceLinePos, destination.Add(0.0f, 0.0f, 2f),
                    GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures))
            {
                return destination;
            }

            // WoD: heading and distance checks use player location (not eye-level traceLinePos)
            float neededFacing = WoWMathHelper.CalculateNeededFacing(myLocation, destination);
            float rayLength = 60f;
            float heightNum = 200f;
            float pitch     = 0.0f;

            // Close approach: match target altitude
            if (myLocation.Distance2D(destination) < 100.0 && destination.Z != 0.0)
            {
                float distance   = myLocation.Distance(destination); // WoD smethod_11: uses location, not traceLinePos
                rayLength        = distance - 1.5f;
                float heightDiff = Math.Abs(destination.Z - myLocation.Z);
                // Math.Min(1f, ...) prevents NaN when heightDiff > distance (WoD fix)
                float angle = (float)Math.Asin(Math.Min(1f, heightDiff / distance));
                pitch = traceLinePos.Z > destination.Z ? -angle : angle;
            }
            else if (!HasSeaLegs(me))
            {
                if (GameWorld.TraceLine(traceLinePos, traceLinePos.Add(0.0f, 0.0f, -minHeight),
                    GameWorld.CGWorldFrameHitFlags.HitTestGround | GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                {
                    // Below minimum height — climb
                    pitch = WoWMathHelper.DegreesToRadians(20f);
                }
                else if (!GameWorld.TraceLine(traceLinePos, traceLinePos.Add(0.0f, 0.0f, -heightNum),
                    GameWorld.CGWorldFrameHitFlags.HitTestWMO | GameWorld.CGWorldFrameHitFlags.HitTestGround | GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                {
                    // Very high — descend if far from destination, not Dalaran, not blocked by Outland terrain
                        // WoD: uses StyxWoW.Me.Rotation (current facing) for the Outland forward trace,
                        // not the heading to destination. Tests if path ahead is blocked.
                        if (!HasSeaLegs(me) && me.ZoneId != 3540U && myLocation.Distance2D(destination) > 300f &&
                            (me.MapId != 530U || !GameWorld.TraceLine(traceLinePos,
                                GetPointInDirection(traceLinePos, 300f, me.Rotation, 0f),
                            GameWorld.CGWorldFrameHitFlags.HitTestWMO | GameWorld.CGWorldFrameHitFlags.HitTestGround)))
                        pitch = WoWMathHelper.DegreesToRadians(-60f);
                }
            }

            // Dalaran (ZoneId 3540): force gentle ascent to clear the crater rim
            if (me.ZoneId == 3540U)
                pitch = WoWMathHelper.DegreesToRadians(30f);

            WoWPoint targetPoint = GetPointInDirection(traceLinePos, rayLength, neededFacing, pitch);

            // Check if direct path is clear
            if (CanFollowFlightSegment(myLocation, targetPoint)
                && !GameWorld.TraceLine(traceLinePos, targetPoint, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures))
                return targetPoint;

            // First pass: standard-length rays
            List<WorldLine> testLines = BuildRayList(traceLinePos, rayLength, neededFacing, pitch);
            WorldLine[] linesArray = testLines.ToArray();
            GameWorld.MassTraceLine(linesArray, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures, out bool[] hitResults);
            for (int i = 0; i < hitResults.Length; ++i)
            {
                if (!hitResults[i] && CanFollowFlightSegment(myLocation, linesArray[i].End))
                    return linesArray[i].End;
            }

            // Second pass: shorter rays (WoD fallback — rayLength/3f, last resort before DoAntiStuck)
            List<WorldLine> shortLines = BuildRayList(traceLinePos, rayLength / 3f, neededFacing, pitch);
            WorldLine[] shortArray = shortLines.ToArray();
            GameWorld.MassTraceLine(shortArray, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures, out bool[] shortHits);
            for (int j = 0; j < shortHits.Length; j++)
            {
                if (!shortHits[j] && CanFollowFlightSegment(myLocation, shortArray[j].End))
                    return shortArray[j].End;
            }

            // All rays blocked — caller must invoke DoAntiStuck()
            return WoWPoint.Empty;
        }

        /// <summary>
        /// Build a 2D PolyNav path from current position to destination.
        /// Reuses the cached PolyNav instance when the map has not changed.
        /// (WoD smethod_14 port)
        /// </summary>
        private static FlightPath BuildPath(Vector2 from, Vector2 to)
        {
            uint mapId = StyxWoW.Me.MapId;
            var faction = Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.CurrentFaction;
            long revision = Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.Revision;

            if (_polyNav == null || _polyNavMapId != mapId || _polyNavFaction != faction || _polyNavRevision != revision)
            {
                if (!Areas.ContinentAreas.TryGetValue(mapId, out Vector2[] area))
                {
                    // Unknown map — use a huge square so PolyNav still works
                    area = new Vector2[]
                    {
                        new Vector2( 20000f,  20000f),
                        new Vector2(-20000f,  20000f),
                        new Vector2(-20000f, -20000f),
                        new Vector2( 20000f, -20000f)
                    };
                }
                _polyNavMapId = mapId;
                _polyNavFaction = faction;
                _polyNavRevision = revision;
                _polyNav = new PolyNav(area, Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.RoutingBlackspots);
            }

            Vector2 routedFrom = from;
            if (!_polyNav.ContainsPoint(from)
                && !Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.TryGetFlightExit(from, out routedFrom))
                return new FlightPath { StartPoint = from, EndPoint = to, Waypoints = new Queue<Vector2>() };
            Vector2[] rawPath = _polyNav.FindPath(routedFrom, to);
            if (routedFrom != from && rawPath.Length > 0) rawPath = new[] { from }.Concat(rawPath).ToArray();
            // Failed/partial avoidance never grants direct travel through the
            // very region the route was unable to avoid.
            bool complete = rawPath.Length > 0 && rawPath[^1] == to;
            for (int i = 1; complete && i < rawPath.Length; i++)
                complete = CanFollowFlightSegment(new WoWPoint(rawPath[i - 1].X, rawPath[i - 1].Y, 0),
                    new WoWPoint(rawPath[i].X, rawPath[i].Y, 0));
            var queue = new Queue<Vector2>(complete ? rawPath : Array.Empty<Vector2>());

            // Skip the start point — bot is already there (WoD smethod_14 port)
            if (queue.Count > 1)
                queue.Dequeue();

            return new FlightPath { StartPoint = from, EndPoint = to, Waypoints = queue };
        }

        internal static bool CanFollowFlightSegment(WoWPoint from, WoWPoint to)
            => Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsSegmentAllowed(from, to);

        internal static WoWPoint GetFlightRouteWaypoint(WoWPoint from, WoWPoint destination)
        {
            if (!Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsInBlackspot(from)
                && CanFollowFlightSegment(from, destination)) return destination;
            var path = BuildPath(new Vector2(from.X, from.Y), new Vector2(destination.X, destination.Y));
            if (path.Waypoints.Count == 0)
                throw new ObservationUnavailableException("flight-route", "No complete route avoids the current aerial exclusions.");
            var next = path.Waypoints.Peek();
            return new WoWPoint(next.X, next.Y, from.Z);
        }

        /// <summary>
        /// Calculate a point in 3D space given direction
        /// </summary>
        private static WoWPoint GetPointInDirection(WoWPoint origin, float distance, float heading, float pitch)
        {
            float x = (float)(Math.Cos(pitch) * Math.Cos(heading)) * distance;
            float y = (float)(Math.Cos(pitch) * Math.Sin(heading)) * distance;
            float z = (float)Math.Sin(pitch) * distance;
            return origin + new WoWPoint(x, y, z);
        }

        /// <summary>
        /// Find a nearby WoW object from which the bot can safely take off.
        /// Ported from HB 6.2.3 Flightor.smethod_5.
        /// </summary>
        /// <param name="from">Current player location.</param>
        /// <param name="losHeight">Check: IsInLineOfSight(object+2z, object+losHeight) — 10f normal, 200f checkIndoors.</param>
        private static WoWObject FindTakeoffCandidate(WoWPoint from, float losHeight)
        {
            float minDistSq = Navigator.PathPrecision * Navigator.PathPrecision;
            return ObjectManager.GetObjectsOfType<WoWObject>(true, false)
                .Where(o => o is WoWGameObject || o is WoWUnit)
                .OrderBy(o => o.DistanceSqr)
                .FirstOrDefault(o =>
                    o.DistanceSqr >= minDistSq
                    && !Blacklist.Contains(o)
                    && !Mount.IsInCantMountSpot(o.Location)
                    && o.IsOutdoors
                    && Navigator.CanNavigateWithin(from, o.Location, Navigator.PathPrecision)
                    && GameWorld.IsInLineOfSight(o.Location.Add(0f, 0f, 2f), o.Location.Add(0f, 0f, losHeight)));
        }

        /// <summary>
        /// Navigate to <see cref="_takeoffSpot"/> while suppressing any OnMountUp event.
        /// Ported from HB 6.2.3 Flightor.smethod_4.
        /// </summary>
        private static void NavigateToTakeoffSpot()
        {
            void CancelMount(object sender, MountUpEventArgs e) => e.Cancel = true;
            try
            {
                Mount.OnMountUp += CancelMount;
                Navigator.MoveTo(_takeoffSpot);
            }
            finally
            {
                Mount.OnMountUp -= CancelMount;
            }
        }

        /// <summary>
        /// Clear all cached path and anti-stuck state (WoD Flightor.Clear port).
        /// Called when the bot stops, or when blackspots/areas change.
        /// </summary>
        public static void Clear()
        {
            DetachRouteState(true);
            // Preserve explicit public/bot-stop cleanup semantics.
            WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend);
            WoWMovement.MoveStop();
        }

        internal static bool ReleaseOwned(object expected, Func<bool> ownsInput, System.Action<object> registered)
        {
            if (!ReferenceEquals(_flightRequestOwner, expected) || !ownsInput()
                || !ReferenceEquals(_flightRequestOwner, expected)) return false;
            DetachRouteState(false);
            var released = _flightRequestOwner;
            registered(released);
            // Every input callback can publish a replacement route. Managed
            // state is detached first and no later cleanup touches a successor.
            bool Current() => ReferenceEquals(_flightRequestOwner, released) && ownsInput()
                && ReferenceEquals(_flightRequestOwner, released);
            if (Current()) WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend);
            if (Current()) WoWMovement.MoveStop();
            return Current();
        }

        private static void DetachRouteState(bool discardMapGeometry)
        {
            _flightRequestOwner = new object();
            _antiStuckRequestOwner = new object();
            _prevDestination    = WoWPoint.Empty;
            _antiStuckStartPos  = WoWPoint.Empty;
            _antiStuckCheckPos  = WoWPoint.Empty;
            _asAscended = _asStrafedLeft = _asStrafedRight = false;
            _antiStuckPlayer = null;
            _antiStuckOwner = null;
            _antiStuckRouteLease = null;
            _antiStuckPlayerGuid = _antiStuckOwnerGuid = 0;
            _flightPath   = null;
            _pathPlayer = null;
            _pathMover = null;
            _pathRouteLease = null;
            _pathPlayerGuid = _pathMoverGuid = 0;
            if (discardMapGeometry) { _polyNav = null; _polyNavMapId = null; }
            _lastDestination = _prevDestination = WoWPoint.Zero;
            _lastFlightWaypoint = WoWPoint.Empty;
            _takeoffSpot        = WoWPoint.Empty;
            _takeoffDestination = WoWPoint.Empty;
        }

        /// <summary>
        /// Calculate total path distance
        /// </summary>
        private static float GetPathDistance(WoWPoint destination)
        {
            WoWPoint[] path = Navigator.GeneratePath(StyxWoW.Me.Location, destination);
            if (path == null || path.Length == 0)
                return float.MaxValue;

            float total = StyxWoW.Me.Location.Distance(path[0]);
            for (int i = 1; i < path.Length; ++i)
                total += path[i].Distance(path[i - 1]);

            return total;
        }

        /// <summary>
        /// Anti-stuck detection based on WaitTimer + displacement check (WoD port).
        /// Returns true and calls DoAntiStuck() when the bot has been stationary too long.
        /// </summary>
        private static bool AntiStuck
        {
            get
            {
                WoWUnit mover = WoWMovement.ActiveMover;
                if (mover == null || mover.Stunned || mover.Fleeing) return false;

                var now = DateTime.Now;
                if (now.Subtract(_antiStuckLastCheck).TotalMilliseconds > 500.0)
                {
                    // New check window — reset and start fresh
                    _antiStuckCheckPos  = WoWPoint.Empty;
                    _antiStuckLastCheck = now;
                    return false;
                }
                _antiStuckLastCheck = now;

                if (!_antiStuckTimer.IsFinished) return false;

                WoWPoint loc = mover.Location;
                if (_antiStuckCheckPos != WoWPoint.Empty &&
                    _antiStuckCheckPos.DistanceSqr(loc) < 9f)
                {
                    // Less than 3m moved over 500ms — stuck
                    Logging.Write(Colors.Red, "[Flightor] We are stuck! ({0})", loc);
                    DoAntiStuck(_pathRouteLease);
                    return true;
                }

                if (mover.IsMoving && mover.MovementInfo.TimeMoved == 0U)
                {
                    // Bot is issuing movement commands but WoW hasn't advanced TimeMoved —
                    // geometry/physics stuck (HB 4.3.4 exact condition: IsMoving && TimeMoved==0).
                    // Do NOT prime during mount cast: player is !IsMoving and TimeMoved==0 which
                    // falsely fires stuck ~600ms after every MountUp() call.
                    _antiStuckCheckPos = loc;
                    _antiStuckTimer.Reset();
                    return false;
                }

                _antiStuckCheckPos = WoWPoint.Empty;
                return false;
            }
        }

        /// <summary>
        /// Stateful 3-step anti-stuck maneuver (WoD port).
        /// Steps: JumpAscend → StrafeLeft → StrafeRight → Backwards → reset.
        /// Each call advances one step; state resets when the bot moves > 10m.
        /// </summary>
        public static void DoAntiStuck() => DoAntiStuck(null);

        private static void DoAntiStuck(Func<bool>? routeLease)
        {
            object requestOwner = _antiStuckRequestOwner = new object();
            var memory = ObjectManager.Wow;
            var executor = ObjectManager.Executor;
            var poi = BotPoi.Current;
            long poiGeneration = BotPoi.CurrentGeneration;
            var profile = ProfileManager.CurrentProfileSnapshot;
            var provider = Navigator.NavigationProvider;
            LocalPlayer player = StyxWoW.Me;
            var playerMover = Navigator.PlayerMover;
            WoWUnit mover = WoWMovement.ActiveMover;
            ulong playerGuid = player?.Guid ?? 0, moverGuid = mover?.Guid ?? 0;
            uint map = player?.MapId ?? 0;
            uint playerAddress = player?.BaseAddress ?? 0, moverAddress = mover?.BaseAddress ?? 0;
            bool alive = player?.IsAlive ?? false, ghost = player?.IsGhost ?? false;
            bool PoiCurrent() => ReferenceEquals(BotPoi.Current, poi) && !poi.IsWorldSubjectBlacklisted
                && (routeLease != null ? routeLease() : BotPoi.CurrentGeneration == poiGeneration)
                && ReferenceEquals(BotPoi.Current, poi);
            bool CanContinue()
            {
                bool valid = memory != null && ReferenceEquals(ObjectManager.Wow, memory) && ReferenceEquals(ObjectManager.Executor, executor)
                    && playerAddress != 0 && moverAddress != 0 && player.BaseAddress == playerAddress && mover.BaseAddress == moverAddress
                    && playerGuid != 0 && moverGuid != 0 && ReferenceEquals(StyxWoW.Me, player)
                    && player.Guid == playerGuid && player.IsValid && player.MapId == map
                    && player.IsAlive == alive && player.IsGhost == ghost && mover.IsValid && mover.Guid == moverGuid
                    && ReferenceEquals(WoWMovement.ActiveMover, mover)
                    && ReferenceEquals(_antiStuckRequestOwner, requestOwner)
                    && PoiCurrent()
                    && ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile)
                    && ReferenceEquals(Navigator.NavigationProvider, provider) && ReferenceEquals(Navigator.PlayerMover, playerMover);
                if (!valid && ReferenceEquals(_antiStuckRequestOwner, requestOwner)) _antiStuckPlayer = null;
                return valid;
            }
            if (!CanContinue()) return;
            bool antiStuckPoiChanged = !ReferenceEquals(_antiStuckPoi, poi)
                || (routeLease == null
                    ? _antiStuckRouteLease != null || _antiStuckPoiGeneration != poiGeneration
                    : !ReferenceEquals(_antiStuckRouteLease, routeLease));
            if (!ReferenceEquals(_antiStuckMemory, memory) || !ReferenceEquals(_antiStuckExecutor, executor)
                || _antiStuckPlayerAddress != playerAddress || _antiStuckOwnerAddress != moverAddress
                || !ReferenceEquals(_antiStuckPlayer, player) || _antiStuckPlayerGuid != playerGuid
                || !ReferenceEquals(_antiStuckOwner, mover) || _antiStuckOwnerGuid != moverGuid || _antiStuckMap != map
                || _antiStuckAlive != alive || _antiStuckGhost != ghost
                || antiStuckPoiChanged
                || !ReferenceEquals(_antiStuckProfile, profile) || !ReferenceEquals(_antiStuckProvider, provider) || !ReferenceEquals(_antiStuckInput, playerMover))
            {
                _asAscended = _asStrafedLeft = _asStrafedRight = false;
                _antiStuckStartPos = WoWPoint.Empty;
            }
            _antiStuckMemory = memory; _antiStuckExecutor = executor;
            _antiStuckPlayerAddress = playerAddress; _antiStuckOwnerAddress = moverAddress;
            _antiStuckPlayer = player;
            _antiStuckPlayerGuid = playerGuid;
            _antiStuckOwner = mover;
            _antiStuckOwnerGuid = moverGuid;
            _antiStuckMap = map;
            _antiStuckAlive = alive;
            _antiStuckGhost = ghost;
            _antiStuckPoi = poi;
            _antiStuckPoiGeneration = poiGeneration;
            _antiStuckRouteLease = routeLease;
            _antiStuckProfile = profile;
            _antiStuckProvider = provider;
            _antiStuckInput = playerMover;

            WoWPoint loc = mover.Location;
            if (!CanContinue()) return;

            // Reset if we've moved far enough since the last stuck event
            if (_antiStuckStartPos != WoWPoint.Empty &&
                _antiStuckStartPos.Distance2DSqr(loc) > 100f)
            {
                _asAscended = _asStrafedLeft = _asStrafedRight = false;
            }
            _antiStuckStartPos = loc;

            if (mover.IsMoving)
            {
                WoWMovement.MoveStop();
                StyxWoW.Sleep(100);
                if (!CanContinue()) return;
            }

            if (!_asAscended)
            {
                Logging.WriteDiagnostic("[Stuck] Trying to ascend.");
                if (!Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsRecoveryRegionClear(player.Location, 40)
                    || !CanContinue()) return;
                WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
                StyxWoW.Sleep(200);
                if (!CanContinue()) return;
                WoWMovement.MoveStop(WoWMovement.MovementDirection.JumpAscend);
                StyxWoW.Sleep(100);
                if (!CanContinue()) return;
                _asAscended = true;
                return;
            }
            if (!_asStrafedLeft)
            {
                Logging.WriteDiagnostic("[Stuck] Trying strafing left.");
                if (!Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsRecoveryRegionClear(player.Location, 40)
                    || !CanContinue()) return;
                WoWMovement.Move(WoWMovement.MovementDirection.StrafeLeft);
                StyxWoW.Sleep(300);
                if (!CanContinue()) return;
                WoWMovement.MoveStop(WoWMovement.MovementDirection.StrafeLeft);
                StyxWoW.Sleep(100);
                if (!CanContinue()) return;
                _asStrafedLeft = true;
                return;
            }
            if (!_asStrafedRight)
            {
                Logging.WriteDiagnostic("[Stuck] Trying strafing right.");
                if (!Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsRecoveryRegionClear(player.Location, 40)
                    || !CanContinue()) return;
                WoWMovement.Move(WoWMovement.MovementDirection.StrafeRight);
                StyxWoW.Sleep(300);
                if (!CanContinue()) return;
                WoWMovement.MoveStop(WoWMovement.MovementDirection.StrafeRight);
                StyxWoW.Sleep(100);
                if (!CanContinue()) return;
                _asStrafedRight = true;
                return;
            }

            // Final step: reverse
            Logging.WriteDiagnostic("[Stuck] Trying to backup.");
            if (!Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsRecoveryRegionClear(player.Location, 40)
                || !CanContinue()) return;
            WoWMovement.Move(WoWMovement.MovementDirection.Backwards);
            StyxWoW.Sleep(500);
            if (!CanContinue()) return;
            WoWMovement.MoveStop(WoWMovement.MovementDirection.Backwards);
            StyxWoW.Sleep(100);
            if (!CanContinue()) return;
            _asAscended = _asStrafedLeft = _asStrafedRight = false;
        }

        /// <summary>
        /// Flying mount helper - manages mounting/dismounting
        /// </summary>
        public static class MountHelper
        {
            private static readonly Random _random = new Random();

            /// <summary>
            /// Get best available flying mount spell. Internal to match WoD WoWSpell_0 visibility.
            /// </summary>
            internal static WoWSpell FlyingMount
            {
                get
                {
                    LocalPlayer me = StyxWoW.Me;

                    // Sea Legs handling (Vashj'ir) - not in WotLK but keep for future
                    if (HasSeaLegs(me))
                    {
                        if (!me.IsOutdoors)
                            return null;

                        WoWPoint location = me.Location;
                        if (!GameWorld.TraceLine(location.Add(0.0f, 0.0f, me.BoundingHeight), location,
                            GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                        {
                            if (SpellManager.HasSpell("Aquatic Form"))
                                return SpellManager.Spells["Aquatic Form"];
                        }
                    }

                    // Configured flying mount (HB 6.2.3: name takes priority over Druid form).
                    if (!string.IsNullOrEmpty(CharacterSettings.Instance.FlyingMountName))
                    {
                        string mountName = CharacterSettings.Instance.FlyingMountName;

                        // Try FlyingMounts list first (correctly classified)
                        var mount = Styx.Logic.MountHelper.FlyingMounts.FirstOrDefault(m =>
                            m.Name == mountName ||
                            m.CreatureSpellId.ToString(System.Globalization.CultureInfo.InvariantCulture) == mountName ||
                            m.Name.ToLowerInvariant().Contains(mountName.ToLowerInvariant()));

                        if (mount != null)
                            return mount.CreatureSpell;

                        // Fallback: search ALL mounts by name — WotLK classification via aura types
                        // may still leave some mounts as MountType.Ground if the spell data differs.
                        mount = Styx.Logic.MountHelper.Mounts.FirstOrDefault(m =>
                            m.Name == mountName ||
                            m.CreatureSpellId.ToString(System.Globalization.CultureInfo.InvariantCulture) == mountName ||
                            m.Name.ToLowerInvariant().Contains(mountName.ToLowerInvariant()));

                        if (mount != null)
                            return mount.CreatureSpell;

                        // Last resort for Druid: flight form keeps the bot airborne
                        // (configured mount not found, avoid returning null and blocking CanMount)
                        if (me.Class == WoWClass.Druid)
                        {
                            if (SpellManager.HasSpell("Swift Flight Form"))
                                return SpellManager.Spells["Swift Flight Form"];
                            if (SpellManager.HasSpell("Flight Form"))
                                return SpellManager.Spells["Flight Form"];
                        }

                        return null;
                    }

                    // No mount configured: Druid uses flight form (HB 4.3.4 lines 622-628)
                    if (me.Class == WoWClass.Druid)
                    {
                        if (SpellManager.HasSpell("Swift Flight Form"))
                            return SpellManager.Spells["Swift Flight Form"];
                        if (SpellManager.HasSpell("Flight Form"))
                            return SpellManager.Spells["Flight Form"];
                    }

                    // Random flying mount
                    if (Styx.Logic.MountHelper.FlyingMounts.Count > 0)
                    {
                        int index = _random.Next(0, Styx.Logic.MountHelper.FlyingMounts.Count);
                        return Styx.Logic.MountHelper.FlyingMounts[index].CreatureSpell;
                    }

                    return null;
                }
            }

            /// <summary>
            /// Check if we can mount a flying mount
            /// WotLK: Cold Weather Flying required for Northrend
            /// </summary>
            public static bool CanMount
            {
                get
                {
                    LocalPlayer me = StyxWoW.Me;
                    ulong guid = me?.Guid ?? 0;
                    uint mapId = me?.MapId ?? 0;
                    bool SameActor() => me != null && guid != 0 && ReferenceEquals(StyxWoW.Me, me)
                        && me.Guid == guid && me.IsValid && me.IsAlive && !me.IsGhost
                        && me.MapId == mapId && me.IsOutdoors && !me.Combat;
                    if (!SameActor()) return false;

                    // WotLK 3.3.5a: flying only valid in Outland (530) or Northrend (571)
                    if (me.MapId != 530U && me.MapId != 571U)
                        return false;

                    // WotLK: Cold Weather Flying required for Northrend
                    if (!SpellManager.HasSpell("Cold Weather Flying") && me.MapId == 571U)
                        return false;

                    // Must be outdoors
                    if (!me.IsOutdoors)
                        return false;

                    // Must have a flying mount
                    if (FlyingMount == null)
                        return false;

                    // Respect post-combat and post-mount cooldowns from Mount.cs.
                    // Bypass timer when near liquid so swim-ascent handlers fire every tick
                    // until the player clears the water (WotLK: IsSwimming=false on riverbed,
                    // TraceLine(eye→feet, Liquid) catches that shallow-water case).
                    bool nearLiquid = me.IsSwimming ||
                        GameWorld.TraceLine(me.GetTraceLinePos(), me.Location, GameWorld.CGWorldFrameHitFlags.HitTestLiquid);
                    if (!Mount.AreMountTimersReady && !nearLiquid)
                        return false;

                    // Check for overhead clearance
                    float boundingHeight = me.BoundingHeight;
                    WoWPoint from = me.Location + new WoWPoint(0.0f, 0.0f, boundingHeight);
                    WoWPoint to = from + new WoWPoint(0.0f, 0.0f, boundingHeight / 2f);
                    bool blocked = GameWorld.TraceLine(from, to, GameWorld.CGWorldFrameHitFlags.HitTestLOS);

                    // Not in combat and not blocked above
                    return !blocked && SameActor();
                }
            }

            /// <summary>
            /// Check if currently on a flying mount.
            /// WotLK-specific: checks druid shapeshift field first because CMovementData.CanFly
            /// (0x800000) lags behind the shapeshift state by several ticks after form invocation.
            /// Using CanFly alone causes an infinite mount-spam loop for druids.
            /// </summary>
            public static bool Mounted
            {
                get
                {
                    LocalPlayer me = StyxWoW.Me;
                    if (me == null) return false;

                    // Sea Legs: aquatic mounts, aquatic form, ghost — all count as "mounted".
                    if (HasSeaLegs(me))
                    {
                        if ((me.HasAura(1066) || me.HasAura(1446)) || me.IsGhost)
                            return true;

                        foreach (var mount in Styx.Logic.MountHelper.UnderwaterMounts)
                        {
                            if (me.HasAura(unchecked((int)mount.CreatureSpellId)))
                                return true;
                        }
                    }

                    // Druid flight form: shapeshift field updates faster than CMovementData.CanFly.
                    // Must check this before the CanFly flag to avoid mount-spam on every tick.
                    if (me.Class == WoWClass.Druid &&
                        (me.Shapeshift == ShapeshiftForm.EpicFlightForm ||
                         me.Shapeshift == ShapeshiftForm.FlightForm))
                        return true;

                    // Primary check: CanFly movement flag (set via SMSG_MOVE_SET_CAN_FLY).
                    // ActiveMover handles vehicle possession edge cases.
                    WoWUnit activeMover = WoWMovement.ActiveMover;
                    if ((activeMover != null && activeMover.MovementInfo.CanFly) || me.IsOnTransport)
                        return true;

                    // Fallback for regular flying mounts: the aura is applied before CanFly is set.
                    WoWSpell flyingMount = FlyingMount;
                    return me.Mounted &&
                           flyingMount != null &&
                           me.HasAura(flyingMount.Id);
                }
            }

            /// <summary>
            /// Mount up on flying mount
            /// </summary>
            public static void MountUp() => MountUpInternal(false);

            /// <summary>
            /// Internal mount up implementation
            /// </summary>
            internal static void MountUpInternal(bool quick)
            {
                LocalPlayer me = StyxWoW.Me;
                ulong guid = me?.Guid ?? 0;
                uint mapId = me?.MapId ?? 0;
                string configuredMount = CharacterSettings.Instance.FlyingMountName;
                bool SameActor() => me != null && guid != 0 && ReferenceEquals(StyxWoW.Me, me)
                    && me.Guid == guid && me.IsValid && me.IsAlive && !me.IsGhost && me.MapId == mapId;
                bool CanContinue() => SameActor() && !Mounted && !Navigator.IsRidingElevator && CanMount
                    && string.Equals(CharacterSettings.Instance.FlyingMountName, configuredMount, StringComparison.Ordinal)
                    && SameActor();
                if (!CanContinue()) return;

                WoWSpell flyingMount = FlyingMount;
                if (flyingMount == null || !CanContinue())
                    return;

                // Druid flight form transitions directly from any shapeshift form.
                // Cancelling the current form first is unnecessary and risks a tick in
                // caster form. HB 4.3.4: Druid path skips ClearShapeshift before flight form.
                bool isDruidFlightForm = me.Class == WoWClass.Druid
                    && (flyingMount.Name == "Swift Flight Form" || flyingMount.Name == "Flight Form");
                if (!isDruidFlightForm)
                    Mount.ClearShapeshift();
                if (!CanContinue()) return;

                // Stop moving
                if (me.IsMoving)
                {
                    Navigator.PlayerMover.MoveStop();
                    if (!CanContinue() || me.IsMoving) return;
                    if (!quick)
                        StyxWoW.SleepForLagDuration();
                }
                if (!CanContinue() || me.IsMoving) return;

                Logging.Write("Mounting: {0}", flyingMount.Name);
                if (!CanContinue() || me.IsMoving || !SpellManager.Cast(flyingMount) || !SameActor()) return;
                // Reset the mount timer so CanMount returns false for the next ~10s,
                // preventing spam if the cast is cancelled (e.g. by water or GCD).
                Mount.ResetMountTimer();

                if (!quick)
                {
                    StyxWoW.SleepForLagDuration();
                    if (!SameActor()) return;
                    StyxWoW.Sleep((int)flyingMount.CastTime + 100);
                    if (!SameActor()) return;
                    StyxWoW.SleepForLagDuration();
                }
            }

            /// <summary>
            /// Dismount from flying mount
            /// </summary>
            public static void Dismount() => TryDismount(true);

            // Both entry points require the same actor and complete landing
            // observation after setup. Dispatch is not a server acknowledgement.
            private static bool TryDismount(bool waitForLag)
            {
                LocalPlayer me = StyxWoW.Me;
                ulong guid = me?.Guid ?? 0;
                bool CanRemoveFlight() => me != null && guid != 0 && ReferenceEquals(StyxWoW.Me, me)
                    && me.Guid == guid && me.IsValid && me.IsAlive
                    && me.TryGetMovementState(out uint flags, out ulong transportGuid)
                    && transportGuid == 0 && (flags & 0x02003000U) == 0
                    && ReferenceEquals(StyxWoW.Me, me) && me.Guid == guid;

                if (!Mounted || !CanRemoveFlight()) return false;
                if (me.IsMoving)
                {
                    WoWMovement.MoveStop();
                    StyxWoW.SleepForLagDuration();
                }
                if (!Mounted || !CanRemoveFlight()) return false;
                bool inForm = HasTravelFormAura(me);
                if (!Mounted || !CanRemoveFlight()) return false;
                Lua.DoString(inForm ? "CancelShapeshiftForm()" : "Dismount()");
                if (inForm)
                {
                    if (waitForLag) StyxWoW.SleepForLagDuration();
                    else StyxWoW.Sleep(250);
                }
                return true;
            }

            /// <summary>
            /// TreeSharp action for dismounting
            /// </summary>
            public class DisMount : TreeSharp.Action
            {
                protected override RunStatus Run(object context)
                {
                    return TryDismount(false) ? RunStatus.Success : RunStatus.Failure;
                }
            }
        }
    }
}
