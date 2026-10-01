// LevelBot.cs - Ported from HB 4.3.4 (Cata)
// Main grinding bot - handles combat, looting, vendor, roaming behaviors
// Uses 3.3.5a offsets only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using CommonBehaviors;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using Levelbot.Actions.Combat;
using Levelbot.Actions.Death;
using Levelbot.Decorators.Combat;
using Levelbot.Decorators.Death;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Common;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.LootFrame;
using Styx.Logic.Inventory.Frames.MailBox;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Inventory.Frames.Taxi;
using Styx.Logic.Inventory.Frames.Trainer;
using Styx.CommonBot;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Bots.Grind
{
    /// <summary>
    /// LevelBot - Main grinding bot ported from HB 4.3.4
    /// Handles: Death, Combat, Loot, Vendor, Roam behaviors
    /// </summary>
    public class LevelBot : BotBase
    {
        // LevelBot (Grind) requires a profile to function (HB 6.2.3 pattern)
        public override bool RequiresProfile => true;

        // Loot tracking
        private static PoiType _lastLootPoiType;
        private static ulong _lastLootGuid;
        private static bool _lootEventsAttached;
        private static int _lootAttemptCount;
        private static int _lootFailCount;

        // Death tracking  
        private static readonly CorpseRecoveryState _corpseRecovery = new();
        private static bool _deathEventsAttached;
        private static WoWPoint _graveyardPoint;
        private static WoWPoint _dangerousCorpse;
        private static uint? _recoveryMap;
        private static bool _wasGhost;
        private static bool _waitingForHealerRecovery;
        private static DateTime _healerStartedUtc;
        private static DateTime _healerResurrectedUtc;
        private static DateTime _nextSafePointSearchUtc;
        private static readonly List<Blackspot> _corpseBlackspots = new();
        private static Stopwatch _corpseWaitStopwatch = new Stopwatch();
        private static bool _diedIndoors;
        private static bool _diedInInstance;
        private static readonly WaitTimer _releaseTimer = WaitTimer.FiveSeconds;
        private static WaitTimer _repairCostTimer = new WaitTimer(TimeSpan.FromMinutes(3.0));
        private static readonly WaitTimer _trainerRouteLogTimer = new WaitTimer(TimeSpan.FromSeconds(30.0));
        private static ulong _lastRepairCost;

        // Root behavior cache
        private PrioritySelector _rootBehavior;

        // HB 4.3.4 exact: LootAllItems helper
        private static bool LootAllItems(Func<bool> current, ulong lootGuid)
        {
            bool Ready() => current() && lootGuid != 0 && LootFrame.Instance.LootingObjectGuid == lootGuid && current();
            if (!Ready()) return false;
            using (new FrameLock())
            {
                if (!Ready()) return false;
                List<WoWItem> carriedItems = StyxWoW.Me.CarriedItems;
                int slotCount = LootFrame.Instance.LootItems;
                if (!Ready()) return false;
                for (int slot = 0; slot < slotCount; ++slot)
                {
                    if (!Ready()) return false;
                    uint itemId = LootFrame.Instance.GetItemId(slot);
                    if (!Ready()) return false;
                    foreach (WoWItem item in carriedItems)
                    {
                        if (item.Entry == itemId)
                        {
                            ItemInfo itemInfo = item.ItemInfo;
                            if (itemInfo != null && (itemInfo.UniqueCount == 1 || itemInfo.BeginQuestId != 0))
                            {
                                if (!Ready()) return false;
                                Blacklist.Add(lootGuid, TimeSpan.FromHours(3.0));
                                break;
                            }
                        }
                    }
                    if (!Ready()) return false;
                    LootFrame.Instance.Loot(slot);
                }
                // A client can close the exact frame while processing our final
                // slot. All observed slots were submitted; there is no frame left
                // to close. A replacement nonzero GUID must never inherit authority.
                if (slotCount > 0 && current() && LootFrame.Instance.LootingObjectGuid == 0 && current())
                    return true;
                if (!Ready()) return false;
                Lua.DoString("CloseLoot();");
                // The frame may disappear because of our own close request.
                // Continue only for the same managed actor/object/work owner.
                return current();
            }
        }

        private static void OnLootEvent(object sender, LuaEventArgs e)
        {
            _lootAttemptCount = 0;
            _lootFailCount = 0;
        }

        #region BotBase Implementation

        public override string Name => "Grind";

        public override bool IsPrimaryType => true;

        public override bool RequirementsMet => true;

        public override Composite Root
        {
            get
            {
                if (_rootBehavior == null)
                {
                    _rootBehavior = new PrioritySelector(
                        CreateDeathBehavior(),
                        CreateCombatBehavior(),
                        CreateLootBehavior(),
                        CreateVendorBehavior(),
                        CreateRoamBehavior(),
                        new ActionIdle()
                    );
                }
                return _rootBehavior;
            }
        }

        public override PulseFlags PulseFlags => PulseFlags.All;

        public override void Start()
        {
            PullIsolationCoordinator.Reset();
            if (ProfileManager.CurrentOuterProfile == null)
                throw new HonorbuddyUnableToStartException("You haven't loaded a profile.");

            GrindArea currentGrindArea = StyxWoW.AreaManager?.CurrentGrindArea;
            if (currentGrindArea != null)
                currentGrindArea.CycleToNearest();

            Targeting.Instance.IncludeTargetsFilter += LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += LevelbotIncludeLootsFilter;

            // HB 6.2.3 AvoidanceNavigationProvider pattern: register world obstacle avoidance
            // so the bot routes around forges, mailboxes, and similar navmesh-absent objects.
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Initialize();
        }

        public override void Stop()
        {
            PullIsolationCoordinator.Reset();
            Targeting.Instance.IncludeTargetsFilter -= LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= LevelbotIncludeLootsFilter;
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Shutdown();
        }

        private static CombatRoutine Routine => RoutineManager.Current;

        private float GetPathPrecision()
        {
            float speed = StyxWoW.Me.MovementInfo.CurrentSpeed;
            return MathEx.Clamp(speed * 0.15f, 1.5f, 10f);
        }

        public override void Pulse()
        {
            Navigator.PathPrecision = GetPathPrecision();
        }

        #endregion

        #region Combat Behavior

        /// <summary>
        /// HB 4.3.4 CreateCombatBehavior - handles dismount, target validation, rest, pull, combat
        /// </summary>
        public static Composite CreateCombatBehavior()
        {
            return new PrioritySelector(
                // Dismount for combat if needed
                new Decorator(
                    ctx => Mount.ShouldDismount(BotPoi.Current.Location),
                    new TreeSharp.Action(ctx => Mount.Dismount("Combat"))
                ),
                new PrioritySelector(
                    // Cancel skinning if not skinning POI
                    new Decorator(
                        ctx => BotPoi.Current.Type != PoiType.Skin && StyxWoW.Me.HasPendingSpell("Skinning"),
                        new TreeSharp.Action(ctx => Lua.DoString("SpellStopTargeting()"))
                    ),
                    // POI Kill sanity checks
                    new DecoratorIsPoiType(PoiType.Kill, new PrioritySelector(
                        new Decorator(
                            // A filtered targeting gap does not end raw player/pet
                            // combat or revoke its still-live destination. Keep the
                            // self-heal/combat branch reachable while selection recovers.
                            ctx => Targeting.Instance.TargetList.Count == 0
                                && (!IsPlayerOrPetInCombat()
                                    || BotPoi.Current.AsObject is not WoWUnit { IsValid: true, IsAlive: true }),
                            new ActionClearPoi("No targets in target list - POI.Kill Sanity Checks")
                        ),
                        new Decorator(
                            ctx => BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.ToUnit().Dead,
                            new TreeSharp.Action(ctx => BotPoi.Clear("POI is dead from Combat"))
                        )
                    )),
                    // Not in combat: Rest, PreCombatBuff, Pull
                    new Decorator(
                        ctx => !IsPlayerOrPetInCombat(),
                        CreateOwnedPrePullBehavior()
                    ),
                    // A transient targeting gap does not end observed ground combat.
                    // Retain self-healing and ownership; only offensive leaves need a target.
                    new Decorator(
                        ctx => !StyxWoW.Me.Mounted && IsPlayerOrPetInCombat(),
                        CreateOwnedGroundCombatBehavior()
                    )
                )
            );
        }

        // The ordinary Decorator predicate runs once per activation, not before
        // every resumed tick. A yielded routine must not inherit a replacement
        // actor/POI or continue after a synchronous callback revokes admission.
        private sealed class RoutineAdmissionGuard : Decorator
        {
            private readonly Func<bool> current;
            internal RoutineAdmissionGuard(Func<bool> current, Composite child) : base(child) { this.current = current; }
            public override RunStatus Tick(object context)
            {
                if (current())
                {
                    RunStatus result = base.Tick(context);
                    if (current()) return result;
                }
                LastStatus = RunStatus.Failure;
                // Cleanup may start a new lifetime on this same guard. Publish
                // the old result before Stop, never over the replacement's status.
                Stop(context);
                return RunStatus.Failure;
            }
        }

        private static Composite CreateOwnedPrePullBehavior()
        {
            LocalPlayer actor = null;
            WoWUnit best = null, displayed = null;
            WoWObject subject = null;
            BotPoi poi = null;
            object targeting = null;
            ulong actorGuid = 0, bestGuid = 0, displayedGuid = 0, poiGuid = 0, subjectGuid = 0;
            uint map = 0, entry = 0;
            PoiType type = PoiType.None;
            bool ParticipantsCurrent() => actor != null && actorGuid != 0
                && ReferenceEquals(StyxWoW.Me, actor) && actor.IsValid && actor.IsAlive
                && actor.Guid == actorGuid && actor.MapId == map && !IsPlayerOrPetInCombat()
                && ReferenceEquals(Targeting.Instance, targeting) && ReferenceEquals(Targeting.Instance.FirstUnit, best)
                && (best == null || bestGuid != 0 && best.IsValid && best.IsAlive && best.Guid == bestGuid);
            bool PoiCurrent() => ParticipantsCurrent() && ReferenceEquals(BotPoi.Current, poi)
                && poi != null && poi.Type == type && poi.Guid == poiGuid && poi.Entry == entry
                && ReferenceEquals(poi.AsObject, subject) && (subject == null || subject.Guid == subjectGuid)
                && ParticipantsCurrent();
            bool Current() => PoiCurrent() && ReferenceEquals(actor.CurrentTarget, displayed)
                && actor.CurrentTargetGuid == displayedGuid && PoiCurrent();
            void CapturePoi(BotPoi value)
            {
                poi = value; type = poi?.Type ?? PoiType.None; poiGuid = poi?.Guid ?? 0; entry = poi?.Entry ?? 0;
                subject = poi?.AsObject; subjectGuid = subject?.Guid ?? 0;
            }
            Composite Guard(Composite child) => new RoutineAdmissionGuard(Current, child);

            return new Sequence(
                new TreeSharp.Action(ctx =>
                {
                    actor = StyxWoW.Me; actorGuid = actor?.Guid ?? 0; map = actor?.MapId ?? 0;
                    targeting = Targeting.Instance; best = Targeting.Instance.FirstUnit; bestGuid = best?.Guid ?? 0;
                    displayed = actor?.CurrentTarget; displayedGuid = displayed?.Guid ?? 0;
                    CapturePoi(BotPoi.Current);
                    return Current() ? RunStatus.Success : RunStatus.Failure;
                }),
                Guard(new PrioritySelector(
                    Guard(Routine.RestBehavior),
                    Guard(Routine.PreCombatBuffBehavior),
                    Guard(new DecoratorIsPoiType(PoiType.Kill, new PrioritySelector(
                        new Decorator(ctx => Current() && best != null && !ReferenceEquals(subject, best),
                            new Sequence(
                                Guard(new ActionDebugString("Current POI is not the best pull target. Changing.")),
                                new TreeSharp.Action(ctx =>
                                {
                                    if (!Current()) return RunStatus.Failure;
                                    var next = new BotPoi(best, PoiType.Kill);
                                    if (!Current()) return RunStatus.Failure;
                                    // Declare only our own publication before its callbacks.
                                    // Never reread a replacement global POI as the target.
                                    CapturePoi(next);
                                    BotPoi.Current = next;
                                    if (!Current()) return RunStatus.Failure;
                                    best.Target();
                                    if (!PoiCurrent() || !ReferenceEquals(actor.CurrentTarget, best)
                                        || actor.CurrentTargetGuid != bestGuid)
                                        return RunStatus.Failure;
                                    displayed = best; displayedGuid = bestGuid;
                                    return Current() ? RunStatus.Success : RunStatus.Failure;
                                }))),
                        Guard(PullIsolationCoordinator.CreatePreCombatBehavior()),
                        Guard(new Decorator(ctx => CanPull(), Guard(Routine.PullBehavior)))
                    )))
                )));
        }

        private static Composite CreateOwnedGroundCombatBehavior()
        {
            LocalPlayer actor = null;
            WoWUnit candidate = null;
            object targeting = null;
            ulong actorGuid = 0, candidateGuid = 0;
            uint map = 0;
            bool ActorCurrent() => actor != null && actorGuid != 0
                && ReferenceEquals(StyxWoW.Me, actor) && actor.IsValid && actor.IsAlive
                && actor.Guid == actorGuid && actor.MapId == map && !actor.Mounted
                && IsPlayerOrPetInCombat() && ReferenceEquals(StyxWoW.Me, actor)
                && actor.Guid == actorGuid;
            bool TargetCurrent() => ActorCurrent() && candidate != null && candidateGuid != 0
                && candidate.IsValid && candidate.IsAlive && candidate.Guid == candidateGuid
                && ReferenceEquals(Targeting.Instance, targeting)
                && ReferenceEquals(Targeting.Instance.FirstUnit, candidate) && ActorCurrent();
            Composite ActorGuard(Composite child) => new RoutineAdmissionGuard(ActorCurrent, child);
            Composite TargetGuard(Composite child) => new RoutineAdmissionGuard(TargetCurrent, child);

            return new Sequence(
                new TreeSharp.Action(ctx =>
                {
                    actor = StyxWoW.Me; actorGuid = actor?.Guid ?? 0; map = actor?.MapId ?? 0;
                    return ActorCurrent() ? RunStatus.Success : RunStatus.Failure;
                }),
                ActorGuard(new PrioritySelector(
                    // A missing enemy never suppresses this actor's self-heal.
                    ActorGuard(Routine.HealBehavior),
                    ActorGuard(PullIsolationCoordinator.CreateRetreatBehavior()),
                    ActorGuard(new Sequence(
                        new TreeSharp.Action(ctx =>
                        {
                            if (!ActorCurrent()) return RunStatus.Failure;
                            targeting = Targeting.Instance;
                            candidate = Targeting.Instance.FirstUnit; candidateGuid = candidate?.Guid ?? 0;
                            return TargetCurrent() ? RunStatus.Success : RunStatus.Failure;
                        }),
                        TargetGuard(new PrioritySelector(
                            TargetGuard(Routine.CombatBuffBehavior),
                            TargetGuard(Routine.CombatBehavior))))),
                    // Target revocation still belongs to ongoing player/pet combat;
                    // do not release it to gathering just because a routine failed.
                    new ActionAlwaysSucceed())));
        }

        private static bool IsPlayerOrPetInCombat()
        {
            var player = StyxWoW.Me;
            if (player == null)
                return false;
            var pet = player.GotAlivePet ? player.Pet : null;
            return player.Combat || (pet != null && pet.Combat);
        }

        private static bool CanPull()
        {
            LocalPlayer player = StyxWoW.Me;
            WoWUnit target = player?.CurrentTarget;
            BotPoi poi = BotPoi.Current;
            ulong actorGuid = player?.Guid ?? 0, targetGuid = target?.Guid ?? 0;
            uint map = player?.MapId ?? 0;
            bool Current() => player != null && actorGuid != 0 && ReferenceEquals(StyxWoW.Me, player)
                && player.IsValid && player.IsAlive && player.Guid == actorGuid && player.MapId == map
                && target != null && targetGuid != 0 && target.IsValid && target.IsAlive && target.Guid == targetGuid
                && ReferenceEquals(player.CurrentTarget, target) && player.CurrentTargetGuid == targetGuid
                && ReferenceEquals(BotPoi.Current, poi) && poi != null && poi.Type == PoiType.Kill
                && ReferenceEquals(poi.AsObject, target) && poi.Guid == targetGuid;
            if (!Current())
                return false;
            bool sight = target.InLineOfSpellSight;
            if (!Current() || !sight)
                return false;
            double distance = target.Distance, range = Targeting.PullDistance;
            return Current() && double.IsFinite(distance) && distance >= 0
                && double.IsFinite(range) && range >= 0 && distance <= range && Current();
        }

        #endregion

        #region Death Behavior

        /// <summary>
        /// HB 4.3.4 CreateDeathBehavior - handles release, ghost movement, corpse retrieval
        /// </summary>
        public static PrioritySelector CreateDeathBehavior()
        {
            if (!_deathEventsAttached)
            {
                BotEvents.OnBotStopped += ResetCorpseRecovery;
                _deathEventsAttached = true;
            }
            return new PrioritySelector(
                new TreeSharp.Action(ctx => ObserveCorpseRecovery()),
                new Decorator(
                    ctx => StyxWoW.Me.IsAlive && !StyxWoW.Me.IsGhost &&
                           !StyxWoW.Me.Combat &&
                           ((CharacterSettings.Instance.RessAtSpiritHealers && StyxWoW.Me.HasAura("Resurrection Sickness")) ||
                            (_waitingForHealerRecovery && DateTime.UtcNow - _healerResurrectedUtc < TimeSpan.FromSeconds(5))),
                    new TreeSharp.Action(ctx =>
                    {
                        TreeRoot.StatusText = "Waiting for Resurrection Sickness";
                        WoWMovement.MoveStop();
                    })
                ),
                // Dead - need to release
                new Decorator(
                    ctx => StyxWoW.Me.IsDead,
                    new Sequence(
                        new ActionSetActivity("Releasing from corpse"),
                        new TreeSharp.Action(ctx => ReleaseCorpse()),
                        new WaitContinue(5, ctx => StyxWoW.Me.IsGhost, 
                            new TreeSharp.Action(ctx => SleepForLag()))
                    )
                ),
                // Ghost - need to use spirit healer (if enabled and can't reach corpse)
                new Decorator(
                    ctx => ShouldUseSpiritHealer && StyxWoW.Me.IsGhost,
                    CreateSpiritHealerBehavior()
                ),
                new Decorator(
                    ctx => !Battlegrounds.IsInsideBattleground && GrindSafetyPolicy.ShouldUseInstancePortal(
                        _diedInInstance || StyxWoW.Me.IsInInstance, StyxWoW.Me.IsGhost,
                        StyxWoW.Me.InstanceCorpseLocation != WoWPoint.Empty),
                    new TreeSharp.Action(ctx => Navigator.MoveTo(StyxWoW.Me.InstanceCorpseLocation))
                ),
                // Ghost - can't navigate to corpse, use spirit healer
                new DecoratorIsNotPoiType(PoiType.Corpse, new Decorator(
                    ctx => CharacterSettings.Instance.RessAtSpiritHealers &&
                           !_diedInInstance && !StyxWoW.Me.IsInInstance && !Battlegrounds.IsInsideBattleground &&
                           StyxWoW.Me.IsGhost &&
                           StyxWoW.Me.CorpsePoint != WoWPoint.Empty &&
                           StyxWoW.Me.Location.DistanceSqr(StyxWoW.Me.CorpsePoint) > 40.0 &&
                           !Navigator.CanNavigateFully(StyxWoW.Me.Location, StyxWoW.Me.CorpsePoint),
                    new Sequence(
                        new TreeSharp.Action(ctx => Logging.Write("Corpse point has no mesh. MapId: {0} Location: {1}", StyxWoW.Me.MapId, StyxWoW.Me.CorpsePoint)),
                        new TreeSharp.Action(ctx => Logging.Write("Can't navigate to our corpse. Trying the spirit healer instead! DEBUG: {0}", StyxWoW.Me.CorpsePoint)),
                        new TreeSharp.Action(ctx => BeginSpiritHealerRecovery("corpse is unreachable"))
                    )
                )),
                // Ghost - far from corpse, need to move
                // HB 4.3.4 smethod_84: IsGhost && Distance > 40
                new DecoratorIsNotPoiType(PoiType.Corpse, new Decorator(
                    ctx => StyxWoW.Me.IsGhost && StyxWoW.Me.Location.Distance(StyxWoW.Me.CorpsePoint) > 40f,
                    new Sequence(
                        // HB 4.3.4 smethod_85: Wait up to 10 sec for server to send CorpsePoint
                        new Wait(10, ctx => StyxWoW.Me.CorpsePoint != WoWPoint.Empty, new ActionAlwaysSucceed()),
                        new ActionSetActivity("Moving to corpse"),
                        // HB 4.3.4 smethod_86/87/88: fly if !diedIndoors && (Mounted || CanFly), else walk
                        new PrioritySelector(
                            new Decorator(
                                ctx => !_diedIndoors && (StyxWoW.Me.Mounted || StyxWoW.Me.MovementInfo.CanFly),
                                new TreeSharp.Action(ctx => Flightor.MoveTo(StyxWoW.Me.CorpsePoint))
                            ),
                            new TreeSharp.Action(ctx => Navigator.MoveTo(StyxWoW.Me.CorpsePoint))
                        )
                    )
                )),
                // Ghost - near corpse, retrieve it.
                // HB 4.3.4 LevelBot.smethod_89: IsGhost && Distance(CorpsePoint) < 40f.
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost && StyxWoW.Me.Location.Distance(StyxWoW.Me.CorpsePoint) < 40f,
                    CreateCorpseRetrievalBehavior()
                ),
                // Succeed if dead or ghost (to prevent other behaviors from running)
                new ActionSuceedIfDeadOrGhost()
            );
        }

        public static bool ShouldUseSpiritHealer { get; set; }

        private static void ResetCorpseRecovery(EventArgs args)
        {
            if (_corpseBlackspots.Count > 0) BlackspotManager.RemoveBlackspots(_corpseBlackspots);
            _corpseBlackspots.Clear();
            _corpseRecovery.Reset();
            _recoveryMap = null;
            _wasGhost = false;
            _diedInInstance = false;
            _waitingForHealerRecovery = false;
            ShouldUseSpiritHealer = false;
            _graveyardPoint = _dangerousCorpse = WoWPoint.Empty;
            _healerStartedUtc = _healerResurrectedUtc = _nextSafePointSearchUtc = DateTime.MinValue;
            _corpseWaitStopwatch.Reset();
        }

        private static RunStatus ObserveCorpseRecovery()
        {
            var me = StyxWoW.Me;
            if (me == null) return RunStatus.Failure;
            bool alive = me.IsAlive && !me.IsGhost;
            if (_recoveryMap != me.MapId)
            {
                BlackspotManager.RemoveBlackspots(_corpseBlackspots);
                _corpseBlackspots.Clear();
                _graveyardPoint = WoWPoint.Empty;
                _dangerousCorpse = WoWPoint.Empty;
                _wasGhost = false;
                _waitingForHealerRecovery = false;
                ShouldUseSpiritHealer = false;
                _recoveryMap = me.MapId;
            }
            _corpseRecovery.Observe(alive, me.MapId, me.Location, DateTime.UtcNow);
            if (ShouldUseSpiritHealer && _healerStartedUtc == DateTime.MinValue)
                _healerStartedUtc = DateTime.UtcNow;
            if (me.IsGhost && !_wasGhost)
            {
                // On release we are at the graveyard. If starting mid-run, only
                // remember a location when the healer is actually visible.
                var healer = ObjectManager.CachedUnits.FirstOrDefault(u => u.IsValid && u.IsSpiritHealer);
                if (healer != null) _graveyardPoint = healer.Location;
                else if (me.Location.Distance(me.CorpsePoint) > 100f) _graveyardPoint = me.Location;
            }
            _wasGhost = me.IsGhost;
            if (alive)
            {
                _diedInInstance = false;
                _corpseWaitStopwatch.Reset();
                if (ShouldUseSpiritHealer)
                {
                    // Do not clear recovery until the server confirms resurrection.
                    ShouldUseSpiritHealer = false;
                    _waitingForHealerRecovery = true;
                    _healerResurrectedUtc = DateTime.UtcNow;
                    if (_dangerousCorpse != WoWPoint.Empty &&
                        !_corpseBlackspots.Any(b => b.Location.Distance(_dangerousCorpse) < 40f))
                    {
                        var spot = new Blackspot(_dangerousCorpse, 45f, 30f);
                        _corpseBlackspots.Add(spot);
                        BlackspotManager.AddBlackspots(new[] { spot });
                    }
                    BotPoi.Clear("Spirit healer resurrection confirmed; recalculate destination and route");
                    Flightor.Clear();
                    Logging.Write("[CorpseRecovery] Resurrected at spirit healer. Avoiding the death area and recalculating the next service route after recovery.");
                }
                if (_waitingForHealerRecovery && !me.HasAura("Resurrection Sickness") &&
                    DateTime.UtcNow - _healerResurrectedUtc >= TimeSpan.FromSeconds(5))
                    _waitingForHealerRecovery = false;
                if (BotPoi.Current.Type == PoiType.Corpse)
                    BotPoi.Clear("Corpse resurrection confirmed");
            }
            else if (!ShouldUseSpiritHealer && CorpseRecoveryState.ShouldUseHealer(
                CharacterSettings.Instance.RessAtSpiritHealers, _diedInInstance || me.IsInInstance,
                Battlegrounds.IsInsideBattleground, _corpseRecovery.RepeatedDeath, false))
            {
                BeginSpiritHealerRecovery("died again near the resurrection point within two minutes");
            }
            return RunStatus.Failure;
        }

        private static void BeginSpiritHealerRecovery(string reason)
        {
            if (ShouldUseSpiritHealer) return;
            ShouldUseSpiritHealer = true;
            _healerStartedUtc = DateTime.UtcNow;
            _dangerousCorpse = StyxWoW.Me.IsGhost ? StyxWoW.Me.CorpsePoint : StyxWoW.Me.Location;
            _corpseWaitStopwatch.Reset();
            BotPoi.Clear("Switching from unsafe corpse recovery to spirit healer");
            Flightor.Clear();
            Logging.Write("[CorpseRecovery] Using existing spirit healer recovery: {0}.", reason);
        }

        private static RunStatus ReturnToSpiritHealer()
        {
            if (_graveyardPoint == WoWPoint.Empty || StyxWoW.Me.Location.Distance(_graveyardPoint) < 10f)
            {
                var healer = Styx.Database.Query.GetNearestNpc(StyxWoW.Me.MapId,
                    StyxWoW.Me.Location, UnitNPCFlags.Spirithealer);
                if (healer != null) _graveyardPoint = healer.Location;
            }
            if (_graveyardPoint == WoWPoint.Empty ||
                DateTime.UtcNow - _healerStartedUtc > TimeSpan.FromMinutes(5))
            {
                TreeRoot.Stop("Cannot reach a spirit healer. Unsafe corpse resurrection remains blocked.");
                return RunStatus.Success;
            }
            TreeRoot.StatusText = "Returning to graveyard spirit healer";
            Navigator.MoveTo(_graveyardPoint);
            return RunStatus.Success;
        }

        private static Composite CreateSpiritHealerBehavior()
        {
            return new PrioritySelector(
                ctx => ObjectManager.CachedUnits
                    .Where(u => u.IsValid && u.IsSpiritHealer)
                    .OrderBy(u => u.DistanceSqr).FirstOrDefault(),
                new Decorator(ctx => DateTime.UtcNow - _healerStartedUtc > TimeSpan.FromMinutes(5),
                    new TreeSharp.Action(ctx => TreeRoot.Stop("Spirit healer recovery timed out; unsafe corpse resurrection remains blocked."))),
                new Decorator(ctx => ctx == null,
                    new TreeSharp.Action(ctx => ReturnToSpiritHealer())),
                // Move to spirit healer
                new Decorator(
                    ctx => ctx != null && !((WoWObject)ctx).WithinInteractRange,
                    new TreeSharp.Action(ctx => { Navigator.MoveTo(((WoWObject)ctx).Location); })
                ),
                // Interact with spirit healer
                new Decorator(
                    ctx => ctx != null && ((WoWObject)ctx).WithinInteractRange,
                    new Sequence(
                        new TreeSharp.Action(ctx => ((WoWObject)ctx).Interact()),
                        new Wait(5,
                            ctx => Lua.GetReturnVal<bool>("return StaticPopup1:IsVisible() or GossipFrame:IsVisible()", 0),
                            new Sequence(
                                // GossipFrame path: select Healer gossip option (decorator skips if frame absent)
                                new DecoratorContinue(ctx => GossipFrame.Instance.IsVisible,
                                    new TreeSharp.Action(ctx =>
                                    {
                                        var entry = GossipFrame.Instance.GossipOptionEntries
                                            .FirstOrDefault(e => e.Type == GossipEntry.GossipEntryType.Healer);
                                        if (entry.Type == GossipEntry.GossipEntryType.Healer)
                                            GossipFrame.Instance.SelectGossipOption(entry.Index);
                                    })
                                ),
                                new WaitContinue(1, ctx => false, new ActionAlwaysSucceed()),
                                new TreeSharp.Action(ctx => Lua.DoString("AcceptXPLoss()")),
                                new WaitContinue(5, ctx => StyxWoW.Me.IsAlive && !StyxWoW.Me.IsGhost,
                                    new ActionAlwaysSucceed())
                            )
                        )
                    )
                )
            );
        }

        private static Composite CreateCorpseRetrievalBehavior()
        {
            // Yield every pulse so a healer request or resurrection is observed
            // before any further corpse movement or retrieval attempt.
            return new TreeSharp.Action(ctx =>
            {
                var me = StyxWoW.Me;
                if (!me.IsGhost || ShouldUseSpiritHealer) return RunStatus.Success;
                if (!_corpseWaitStopwatch.IsRunning) _corpseWaitStopwatch.Start();
                if (BotPoi.Current.Type != PoiType.Corpse)
                {
                    if (DateTime.UtcNow < _nextSafePointSearchUtc) return RunStatus.Success;
                    _nextSafePointSearchUtc = DateTime.UtcNow.AddSeconds(5);
                    var safePoint = FindSafeResPoint();
                    if (safePoint == WoWPoint.Empty)
                    {
                        if (CorpseRecoveryState.ShouldUseHealer(CharacterSettings.Instance.RessAtSpiritHealers,
                            _diedInInstance || me.IsInInstance, Battlegrounds.IsInsideBattleground, false, true))
                            BeginSpiritHealerRecovery("no safe resurrection point around the corpse");
                        else
                            TreeRoot.StatusText = "No safe resurrection point; remaining a ghost";
                        return RunStatus.Success;
                    }
                    BotPoi.Current = new BotPoi(safePoint, PoiType.Corpse);
                }
                if (_corpseWaitStopwatch.Elapsed.TotalSeconds >= 40 || IsNearCurrentPoi())
                    return GrabCorpse();
                Navigator.MoveTo(BotPoi.Current.Location);
                return RunStatus.Success;
            });
        }

        private static bool IsNearCurrentPoi()
        {
            return BotPoi.Current != null && 
                   BotPoi.Current.Location != WoWPoint.Empty &&
                   StyxWoW.Me.Location.Distance2DSqr(BotPoi.Current.Location) < 25.0;
        }

        /// <summary>
        /// HB 4.3.4 smethod_6 — Attempt to retrieve corpse.
        /// Yields each pulse so recovery decisions and server state are rechecked.
        /// </summary>
        private static RunStatus GrabCorpse()
        {
            var me = StyxWoW.Me;
            bool safe = IsResPointSafe(me.Location, ObjectManager.CachedUnits);
            if (!safe)
            {
                if (_corpseWaitStopwatch.Elapsed.TotalSeconds >= 40 &&
                    CorpseRecoveryState.ShouldUseHealer(CharacterSettings.Instance.RessAtSpiritHealers,
                        _diedInInstance || me.IsInInstance, Battlegrounds.IsInsideBattleground, false, true))
                    BeginSpiritHealerRecovery("safe-resurrection wait expired with hostiles still nearby");
                else
                    BotPoi.Clear("Hostiles moved near the resurrection point; search again");
                return RunStatus.Success;
            }
            if (Lua.GetReturnVal<int>("return GetCorpseRecoveryDelay()", 0) != 0)
            {
                TreeRoot.StatusText = "Waiting for corpse recovery delay to expire";
                return RunStatus.Success;
            }
            if (!CorpseRecoveryState.CanRetrieve(me.IsGhost, safe, ShouldUseSpiritHealer, true))
                return RunStatus.Success;
            Logging.Write("Clicking corpse popup...");
            Lua.DoString("RetrieveCorpse()");
            return RunStatus.Success;
        }

        private static void ReleaseCorpse()
        {
            if (!_releaseTimer.IsFinished)
                return;

            _releaseTimer.Reset();
            _corpseWaitStopwatch.Reset();
            _nextSafePointSearchUtc = DateTime.MinValue;
            GameStats.Died();
            Navigator.Clear();
            Logging.Write("I died.");
            _diedIndoors = StyxWoW.Me.IsIndoors;
            _diedInInstance = StyxWoW.Me.IsInInstance;
            Lua.DoString("RepopMe()");
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_0 — Simple safe res point: tries direct nav, then raycasts
        /// around corpse with FindHeight validation.
        /// </summary>
        private static WoWPoint FindCorpsePoint()
        {
            WoWPoint corpsePoint = StyxWoW.Me.CorpsePoint;
            WoWPoint myLocation = StyxWoW.Me.Location;

            if (Navigator.CanNavigateFully(myLocation, corpsePoint))
                return corpsePoint;

            for (float degrees = 0.0f; degrees < 360.0f; degrees += 15f)
            {
                for (float distance = 0.0f; distance <= 35.0f; distance += 5f)
                {
                    var vector = corpsePoint.RayCast(WoWMathHelper.DegreesToRadians(degrees), distance);
                    float originalZ = vector.Z;
                    if (Navigator.FindHeight(vector.X, vector.Y, out float newZ) &&
                        Math.Abs(originalZ - newZ) <= 15f &&
                        Navigator.CanNavigateFully(myLocation, new WoWPoint(vector.X, vector.Y, newZ)))
                    {
                        return new WoWPoint(vector.X, vector.Y, newZ);
                    }
                }
            }

            return corpsePoint;
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_1 — Full safe res point algorithm with hostile mob avoidance.
        /// Uses MassTraceLine LOS checks and scores points by distance from nearest hostile.
        /// </summary>
        private static WoWPoint FindSafeResPoint()
        {
            // HB 4.3.4: woWPoint_0 = first read of CorpsePoint (used for safety check & fallback)
            WoWPoint originalCorpse = StyxWoW.Me.CorpsePoint;

            // Gather hostile NPC positions
            var hostiles = ObjectManager.GetObjectsOfType<WoWUnit>(true, false)
                .Where(u => u.IsValid && !u.Dead && u.IsHostile)
                .ToList();
            var hostilePositions = hostiles.Select(u => u.Location).ToList();

            Logging.Write("There are {0} hostile mobs near our corpse.", hostilePositions.Count);

            // HB 4.3.4: second read of CorpsePoint (used for raycasting, raised by 2.132)
            WoWPoint corpsePoint = StyxWoW.Me.CorpsePoint;
            WoWPoint myLocation = StyxWoW.Me.Location;

            // If corpse hasn't moved significantly and no hostiles within 25yd, use original point directly
            if (corpsePoint.Distance2D(originalCorpse) < 39f && IsResPointSafe(originalCorpse, hostiles) &&
                Navigator.CanNavigateFully(myLocation, originalCorpse))
                return originalCorpse;

            // Build raycast lines from corpse outward
            WoWPoint raisedCorpse = corpsePoint;
            raisedCorpse.Z += 2.132f;

            var traceLines = new List<WorldLine>();
            for (float degrees = 0.0f; degrees < 360.0f; degrees += 15f)
            {
                for (float distance = 0.0f; distance <= 35.0f; distance += 5f)
                {
                    WoWPoint endPoint = raisedCorpse.RayCast((float)(degrees * Math.PI / 180.0), distance);
                    traceLines.Add(new WorldLine(raisedCorpse, endPoint));
                }
            }

            // MassTraceLine for LOS — points that hit geometry are blocked
            GameWorld.MassTraceLine(traceLines.ToArray(), GameWorld.CGWorldFrameHitFlags.HitTestLOS, out bool[] hitResults);

            WoWPoint bestPoint = WoWPoint.Empty;
            float bestDistance = 0f;

            for (int i = 0; i < traceLines.Count; i++)
            {
                // Skip points blocked by LOS
                if (hitResults != null && hitResults[i])
                    continue;

                WoWPoint candidate = traceLines[i].End;
                // A point must actually be safe, not merely less dangerous than the corpse.
                if (!IsResPointSafe(candidate, hostiles)) continue;

                // Validate path to candidate
                WoWPoint[]? path = Navigator.GeneratePath(myLocation, candidate);
                if (path == null || path.Length == 0)
                    continue;

                // Check path endpoint is close to candidate (within PathPrecision + Z tolerance)
                WoWPoint pathEnd = path[path.Length - 1];
                if (pathEnd.Distance2DSqr(candidate) > Navigator.PathPrecision * Navigator.PathPrecision ||
                    Math.Abs(pathEnd.Z - candidate.Z) >= 3f ||
                    pathEnd.Distance(corpsePoint) >= 39f || !IsResPointSafe(pathEnd, hostiles))
                    continue;

                // Score: distance from nearest hostile (higher = safer)
                float distFromHostile = GetDistanceToNearestHostile(candidate, hostilePositions);
                if (distFromHostile > bestDistance)
                {
                    bestPoint = pathEnd;
                    bestDistance = distFromHostile;
                }
            }

            return bestPoint;
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_3 — Check if no hostile is within 25 yards of a point.
        /// </summary>
        private static bool IsResPointSafe(WoWPoint point, IEnumerable<WoWUnit> units)
        {
            return units.Where(u => u.IsValid && !u.Dead && u.IsHostile).All(u =>
                GrindSafetyPolicy.IsHostileSafeForResurrection(point.Distance(u.Location), u.MyAggroRange));
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_4 — Get distance to the nearest hostile point.
        /// Returns float.MaxValue if no hostiles exist.
        /// </summary>
        private static float GetDistanceToNearestHostile(WoWPoint point, IEnumerable<WoWPoint> hostilePositions)
        {
            WoWPoint nearest = hostilePositions
                .OrderBy(h => h.Distance(point))
                .FirstOrDefault();

            if (nearest == default)
                return float.MaxValue;

            return nearest.Distance2D(point);
        }

        #endregion

        #region Loot Behavior

        /// <summary>
        /// HB 4.3.4 CreateLootBehavior - handles looting, skinning, harvesting
        /// </summary>
        public static Composite CreateLootBehavior()
        {
            LootWorkObservation movementOwner = null;
            // Attach loot events once
            if (!_lootEventsAttached)
            {
                Lua.Events.AttachEvent("CHAT_MSG_LOOT", OnLootEvent);
                _lootEventsAttached = true;
            }

            return new Decorator(
                ctx => CanBeginLoot() && CanLoot(),
                new PrioritySelector(
                    // Handle loot/skin/harvest POI
                    new DecoratorIsPoiType(new[] { PoiType.Loot, PoiType.Skin, PoiType.Harvest },
                        new PrioritySelector(
                            // Check for enemies while looting
                            new DecoratorIsNotPoiType(PoiType.Kill,
                                new DecoratorNeedToFindTarget(new PrioritySelector(
                                    new ActionDebugString("[LB] DNTFT -> S"),
                                    new Decorator(
                                        ctx => Targeting.Instance.FirstUnit != null &&
                                               Targeting.Instance.FirstUnit.IsHostile &&
                                               Targeting.Instance.FirstUnit.Distance < 
                                               Targeting.Instance.FirstUnit.MyAggroRange + 2.0,
                                        new OwnedTargetHandoff(() =>
                                            (BotPoi.Current.Type == PoiType.Loot || BotPoi.Current.Type == PoiType.Skin || BotPoi.Current.Type == PoiType.Harvest)
                                            && Targeting.Instance.FirstUnit is { } threat && threat.IsHostile
                                            && threat.Distance < threat.MyAggroRange + 2.0)
                                    )
                                ))
                            ),
                            // Already looted check
                            new Decorator(
                                ctx => _lastLootPoiType == BotPoi.Current.Type && _lastLootGuid == BotPoi.Current.Guid,
                                new TreeSharp.Action(ctx =>
                                {
                                    var owner = new LootWorkObservation();
                                    if (!owner.Current || owner.Type != _lastLootPoiType || owner.Guid != _lastLootGuid)
                                        return RunStatus.Success; // End this revoked branch without more loot work.
                                    if (++_lootAttemptCount >= 5)
                                    {
                                        if (++_lootFailCount >= 2)
                                        {
                                            Logging.Write("Blacklisting lootable to avoid useless POI spam, tried looting twice but we still can't loot.");
                                            if (!owner.Current) return RunStatus.Success;
                                            Blacklist.Add(owner.Guid, TimeSpan.FromMinutes(15.0));
                                            if (!owner.Current) return RunStatus.Success;
                                            _lootFailCount = 0;
                                            BotPoi.Clear("Tried to loot more than 2 times");
                                        }
                                        else
                                        {
                                            _lastLootGuid = 0;
                                            _lootAttemptCount = 0;
                                        }
                                    }
                                    else
                                    {
                                        BotPoi.Clear("Already looted");
                                    }
                                    return RunStatus.Success;
                                })
                            ),
                            // HB 4.3.4 smethod_25/26: "Can't generate a path to lootable" blacklist.
                            // REMOVED: In HB 4.3.4 + Tripper navmesh, CanNavigateFully() never returned false
                            // for reachable WotLK terrain, so this check never fired in practice.
                            // Our Detour navmesh returns DT_PARTIAL_RESULT for corpses slightly off-mesh
                            // (slopes, geometry edges) — false positives that incorrectly blacklist real loot.
                            // HB-parity fallback: loot sequence tries Interact, WaitLuaEvent("LOOT_OPENED")
                            // times out after 3s, fallback action checks CanLoot and handles the blacklist.
                            // Stale loot POI: object despawned and no longer in ObjectManager
                            new Decorator(
                                ctx => BotPoi.Current.AsObject == null,
                                new TreeSharp.Action(ctx =>
                                {
                                    var owner = new LootWorkObservation();
                                    if (!owner.Current || owner.Subject != null) return RunStatus.Success;
                                    Logging.Write("[LB] Loot object 0x{0:X016} no longer in world (despawned), clearing stale POI.", owner.Guid);
                                    if (!owner.Current) return RunStatus.Success;
                                    Blacklist.Add(owner.Guid, TimeSpan.FromMinutes(5.0));
                                    if (!owner.Current) return RunStatus.Success;
                                    BotPoi.Clear("Loot object despawned");
                                    return RunStatus.Success;
                                })
                            ),
                            // GameObjects require a ground approach, confirmed
                            // landing and dismount before ordinary loot can run.
                            // Keep this ahead of the generic out-of-range branch
                            // so aerial travel cannot starve the descent state.
                            new GroundLootApproach(() => CanBeginLoot() && CanLoot()),
                            // Move to lootable
                            new Decorator(
                                ctx =>
                                {
                                    movementOwner = new LootWorkObservation();
                                    WoWObject target = movementOwner.Subject;
                                    return target != null && (target is WoWUnit unit
                                        ? !unit.WithinLootRange : !target.WithinInteractRange);
                                },
                                new ActionMoveToPoi(() => movementOwner != null && movementOwner.Current)
                            ),
                            // Stop descending if flying
                            new Decorator(
                                ctx => StyxWoW.Me.IsFlying,
                                new TreeSharp.Action(ctx =>
                                {
                                    if (movementOwner != null && movementOwner.Current)
                                        WoWMovement.Move(WoWMovement.MovementDirection.Descend);
                                    return RunStatus.Success;
                                })
                            ),
                            new Decorator(
                                ctx => StyxWoW.Me.MovementInfo.IsDescending,
                                new TreeSharp.Action(ctx =>
                                {
                                    if (movementOwner != null && movementOwner.Current)
                                        WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend);
                                    return RunStatus.Success;
                                })
                            ),
                            CreateOwnedLootInteraction(() => movementOwner != null && movementOwner.Current)
                        )
                    ),
                    // Not currently looting - find something to loot
                    new DecoratorIsPoiType(new[] { PoiType.None, PoiType.Hotspot, PoiType.Quest },
                        CreateOwnedLootSelection()
                    )
                )
            );
        }

        private static Composite CreateOwnedLootSelection() => new TreeSharp.Action(ctx =>
        {
            var actor = StyxWoW.Me; ulong actorGuid = actor?.Guid ?? 0; uint map = actor?.MapId ?? 0;
            var mover = WoWMovement.ActiveMover; ulong moverGuid = mover?.Guid ?? 0;
            var provider = Navigator.NavigationProvider;
            var poi = BotPoi.Current; var priorType = poi?.Type ?? PoiType.None;
            ulong priorGuid = poi?.Guid ?? 0; uint priorEntry = poi?.Entry ?? 0;
            var targeting = LootTargeting.Instance; var candidate = targeting?.FirstObject;
            ulong guid = candidate?.Guid ?? 0; bool? alive = candidate?.ToUnit()?.IsAlive;
            PoiType Kind() => candidate is WoWUnit unit && LootTargeting.SkinMobs && unit.SkinType == WoWCreatureSkinType.Leather && unit.CanSkin ? PoiType.Skin
                : candidate is WoWGameObject obj && ((obj.IsHerb && LootTargeting.HarvestHerbs) || (obj.IsMineral && LootTargeting.HarvestMinerals))
                    ? PoiType.Harvest : PoiType.Loot;
            PoiType type = Kind();
            bool InputsCurrent() => actor != null && actorGuid != 0 && ReferenceEquals(StyxWoW.Me, actor)
                && actor.Guid == actorGuid && actor.MapId == map && CanBeginLoot() && CanLoot()
                && mover != null && moverGuid != 0 && mover.IsValid && mover.Guid == moverGuid
                && ReferenceEquals(WoWMovement.ActiveMover, mover) && ReferenceEquals(Navigator.NavigationProvider, provider)
                && candidate != null && guid != 0 && candidate.IsValid && candidate.Guid == guid && candidate.ToUnit()?.IsAlive == alive
                && ReferenceEquals(LootTargeting.Instance, targeting) && ReferenceEquals(targeting.FirstObject, candidate)
                && Kind() == type && ReferenceEquals(StyxWoW.Me, actor) && actor.Guid == actorGuid;
            bool Current() => InputsCurrent() && poi != null && ReferenceEquals(BotPoi.Current, poi)
                && poi.Type == priorType && poi.Guid == priorGuid && poi.Entry == priorEntry
                && (priorType == PoiType.None || priorType == PoiType.Hotspot || priorType == PoiType.Quest);
            if (!Current()) return RunStatus.Failure;
            var next = new BotPoi(candidate, type);
            if (!Current()) return RunStatus.Failure;
            BotPoi.Current = next;
            return InputsCurrent() && ReferenceEquals(BotPoi.Current, next) && next.Type == type && next.Guid == guid
                && ReferenceEquals(next.AsObject, candidate) ? RunStatus.Success : RunStatus.Failure;
        });

        private static bool CanBeginLoot()
        {
            var actor = StyxWoW.Me;
            // A missing cached aggro unit is not proof that combat has ended.
            return actor != null && actor.IsValid && actor.IsAlive && !IsPlayerOrPetInCombat()
                && !actor.OnTaxi && !actor.IsOnTransport && !actor.IsCasting && actor.ChanneledCastingSpellId == 0;
        }

        private sealed class LootWorkObservation
        {
            internal readonly LocalPlayer Actor = StyxWoW.Me;
            internal readonly BotPoi Poi = BotPoi.Current;
            internal readonly WoWObject Subject;
            internal readonly ulong Guid;
            internal readonly PoiType Type;
            private readonly ulong actorGuid, moverGuid;
            private readonly uint map, entry;
            private readonly WoWUnit mover = WoWMovement.ActiveMover;
            private readonly object provider = Navigator.NavigationProvider;
            private readonly bool? alive;

            internal LootWorkObservation()
            {
                actorGuid = Actor?.Guid ?? 0; map = Actor?.MapId ?? 0; moverGuid = mover?.Guid ?? 0;
                Guid = Poi?.Guid ?? 0; Type = Poi?.Type ?? PoiType.None; entry = Poi?.Entry ?? 0;
                Subject = Poi?.AsObject; alive = Subject?.ToUnit()?.IsAlive;
            }

            internal bool Current => Actor != null && actorGuid != 0 && Actor.IsValid && Actor.IsAlive
                && ReferenceEquals(StyxWoW.Me, Actor) && Actor.Guid == actorGuid && Actor.MapId == map
                && !IsPlayerOrPetInCombat() && !Actor.OnTaxi && !Actor.IsOnTransport
                && mover != null && moverGuid != 0 && mover.IsValid && mover.Guid == moverGuid
                && ReferenceEquals(WoWMovement.ActiveMover, mover) && ReferenceEquals(Navigator.NavigationProvider, provider)
                && Poi != null && ReferenceEquals(BotPoi.Current, Poi) && Poi.Type == Type && Poi.Guid == Guid && Poi.Entry == entry
                && (Type == PoiType.Loot || Type == PoiType.Skin || Type == PoiType.Harvest)
                && ReferenceEquals(Poi.AsObject, Subject)
                && (Subject == null || Subject.IsValid && Subject.Guid == Guid && Subject.ToUnit()?.IsAlive == alive);

            internal bool InRange => Current && Subject != null && (Subject is WoWUnit unit
                ? unit.WithinLootRange : Subject.WithinInteractRange) && Current;
        }

        private static Composite CreateOwnedLootInteraction(Func<bool> admitted)
        {
            LootWorkObservation owner = null;
            bool attempted = false, observedEvent = false, dispatched = false;
            bool Current() => owner != null && owner.Current && admitted() && owner.Current;
            Composite Guard(Composite child) => new RoutineAdmissionGuard(Current, child);
            return new Sequence(
                new TreeSharp.Action(ctx =>
                {
                    owner = new LootWorkObservation(); attempted = observedEvent = dispatched = false;
                    return Current() && owner.Subject != null ? RunStatus.Success : RunStatus.Failure;
                }),
                new PrioritySelector(
                    new Sequence(
                        Guard(new DecoratorContinue(ctx => owner.Actor.IsMoving, new Sequence(
                            Guard(new TreeSharp.Action(ctx => WoWMovement.MoveStop())),
                            Guard(new TreeSharp.Action(ctx => SleepForLag()))))),
                        // A ground object's opening action can outlast the
                        // ordinary corpse wait. Keep the existing finite
                        // harvesting budget and subscribe before native dispatch.
                        Guard(new WaitLuaEvent("LOOT_OPENED", () => owner.Subject is WoWGameObject
                            || owner.Type == PoiType.Harvest ? 10 : 3, () =>
                        {
                            if (!Current() || !owner.InRange || owner.Actor.IsFlying || owner.Actor.MovementInfo.IsDescending
                                || owner.Actor.IsCasting || owner.Actor.ChanneledCastingSpellId != 0
                                || LootFrame.Instance.IsVisible || !CanLoot()
                                || !GroundLootApproach.CanInteractNow(owner.Subject, Current) || !Current()) return false;
                            attempted = true;
                            GroundLootApproach.ObserveInteraction(owner.Subject, "interaction-issued", "native-request-not-acknowledged");
                            owner.Subject.Interact(true);
                            return Current();
                        },
                            new TreeSharp.Action(ctx =>
                            {
                                if (!Current()) return RunStatus.Failure;
                                observedEvent = true;
                                // This is a necessary mismatch veto, not causal
                                // proof that the native menu belongs to this request.
                                if (owner.Guid == 0 || LootFrame.Instance.LootingObjectGuid != owner.Guid || !Current())
                                    return RunStatus.Failure;
                                Logging.Write("Looting {0} Guid 0x{1:X016}", owner.Subject.Name, owner.Guid);
                                if (!Current()) return RunStatus.Failure;
                                dispatched = LootAllItems(Current, owner.Guid);
                                if (dispatched && Current())
                                    GroundLootApproach.ObserveInteraction(owner.Subject, "loot-slots-dispatched", "awaiting-authoritative-item-or-objective-increment");
                                return dispatched && Current() ? RunStatus.Success : RunStatus.Failure;
                            }))),
                        // WaitLuaEvent inherits WaitContinue: timeout is success,
                        // but it did not run the callback and is not looted progress.
                        Guard(new TreeSharp.Action(ctx => observedEvent && dispatched ? RunStatus.Success : RunStatus.Failure)),
                        Guard(new DecoratorContinue(
                            ctx => owner.Type == PoiType.Loot
                                && (CharacterSettings.Instance.SkinMobs || CharacterSettings.Instance.NinjaSkin)
                                && owner.Subject is WoWUnit unit && unit.SkinType == WoWCreatureSkinType.Leather
                                && unit.Level <= owner.Actor.CanSkinLevel,
                            Guard(new WaitContinue(2, ctx => owner.Subject.ToUnit().CanSkin
                                && LootTargeting.Instance.FirstObject != null && LootTargeting.Instance.FirstObject.Guid == owner.Guid,
                                new ActionAlwaysSucceed())))),
                        Guard(new DecoratorContinue(ctx => owner.Type == PoiType.Loot && owner.Subject is WoWUnit,
                            Guard(new TreeSharp.Action(ctx => GameStats.LootedMob())))),
                        Guard(new TreeSharp.Action(ctx => { _lastLootPoiType = owner.Type; _lastLootGuid = owner.Guid; })),
                        // Keep cleanup last; no old work may run after its callbacks.
                        new Decorator(ctx => Current(), new ActionClearPoi("Waiting for loot flag"))),
                    new TreeSharp.Action(ctx =>
                    {
                        if (!attempted || !Current()) return RunStatus.Failure;
                        GroundLootApproach.ObserveInteraction(owner.Subject, "interaction-not-acknowledged", observedEvent
                            ? "loot-window-or-slot-ownership-changed" : "loot-window-timeout");
                        Logging.Write(observedEvent
                            ? "Loot frame or slot processing changed after LOOT_OPENED; deferring this lootable."
                            : "Loot window did not open before the bounded wait; deferring this lootable.");
                        if (!Current()) return RunStatus.Failure;
                        SleepForLag();
                        if (!Current()) return RunStatus.Failure;
                        bool canStillLoot = owner.Type switch
                        {
                            PoiType.Harvest => owner.Subject.ToGameObject()?.CanLoot == true,
                            PoiType.Skin => owner.Subject.ToUnit()?.CanSkin == true,
                            _ => owner.Subject is WoWGameObject gameObject ? gameObject.CanLoot : owner.Subject.ToUnit()?.CanLoot == true
                        };
                        if (!Current()) return RunStatus.Failure;
                        Logging.Write(canStillLoot ? "I can't tell if we looted, blacklisting it just to be safe." : "Lootable isn't lootable, blacklisting.");
                        if (!Current()) return RunStatus.Failure;
                        Blacklist.Add(owner.Guid, owner.Subject is WoWGameObject
                            ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(canStillLoot ? 10 : 5));
                        if (!Current()) return RunStatus.Failure;
                        BotPoi.Clear("Done looting");
                        return RunStatus.Success;
                    })));
        }

        private static bool IsPathBlocked(WoWObject target)
        {
            WoWPoint myLocation = ObjectManager.Me.Location;
            var path = Navigator.GeneratePath(myLocation, target.Location);

            if (path != null && path.Length > 0)
            {
                // Check if any path point is too far from target (blocked)
                if (path.Any(p => p.Distance(myLocation) > 80f))
                    return true;

                // Check if path end is too far from target
                if (path[path.Length - 1].Distance(target.Location) > 5.0)
                {
                    Blacklist.Add(target.Guid, new TimeSpan(1, 1, 1));
                    return true;
                }
            }

            return false;
        }

        private static bool CanLoot()
        {
            if (ProfileManager.CurrentProfile == null)
                return true;

            uint freeSlots = LevelbotSettings.Instance.GroundMountFarmingMode
                ? StyxWoW.Me.FreeBagSlots
                : StyxWoW.Me.FreeNormalBagSlots;

            if (freeSlots <= 1 || freeSlots < ProfileManager.CurrentProfile.MinFreeBagSlots)
            {
                // HB 4.3.4 uses Trace.WriteLine here (invisible in bot log), not Logging.WriteDebug.
                return false;
            }

            return true;
        }

        #endregion

        #region Vendor Behavior

        /// <summary>
        /// HB 4.3.4 CreateVendorBehavior - handles selling, repairing, mailing, training
        /// </summary>
        public static PrioritySelector CreateVendorBehavior()
        {
            return new PrioritySelector(
                // Handle vendor POI types
                new DecoratorIsPoiType(new[] { PoiType.Sell, PoiType.Repair, PoiType.Mail, PoiType.Buy, PoiType.Train, PoiType.Fly },
                    new PrioritySelector(
                        new Decorator(
                            ctx => VendorManager.RejectInvalidCurrentVendor(),
                            new ActionClearPoi("Unsafe or invalid service NPC; selecting an alternative")
                        ),
                        // Move to vendor
                        new Decorator(
                            ctx => BotPoi.Current.Location.Distance(StyxWoW.Me.Location) > 5.0,
                            new ActionMoveToPoi()
                        ),
                        // At vendor
                        new Decorator(
                            ctx => BotPoi.Current.Location.Distance(StyxWoW.Me.Location) <= 5.0,
                            new PrioritySelector(
                            // Vendor/mailbox not found
                                new Decorator(
                                    ctx => BotPoi.Current.AsObject == null,
                                    new Sequence(
                                        new TreeSharp.Action(ctx => Logging.Write(System.Drawing.Color.Red, 
                                            "Could not find {0} {1}[{2}], blacklisting.",
                                            BotPoi.Current.Type == PoiType.Mail ? "mailbox" : "vendor",
                                            BotPoi.Current.Name, BotPoi.Current.Entry)),
                                        new DecoratorContinue(
                                            ctx => BotPoi.Current.AsVendor != null,
                                            new TreeSharp.Action(ctx => 
                                                VendorManager.RejectVendor((int)BotPoi.Current.Entry, "service NPC was not found"))
                                        ),
                                        new ActionClearPoi("Vendor/mailbox was blacklisted")
                                    )
                                ),
                                // Interact with vendor
                                new Decorator(
                                    ctx => !IsVendorFrameOpen() && BotPoi.Current.AsObject != null,
                                    new Sequence(
                                        new TreeSharp.Action(ctx => Navigator.PlayerMover.MoveStop()),
                                        new TreeSharp.Action(ctx => SleepForLag()),
                                        new TreeSharp.Action(ctx => BotPoi.Current.AsObject.Interact()),
                                        new WaitContinue(5, ctx => IsVendorFrameOpen(),
                                            new PrioritySelector(
                                                new DecoratorFrameIsVisible<GossipFrame>(new Sequence(
                                                    new TreeSharp.Action(ctx =>
                                                    {
                                                        var entry = GossipFrame.Instance.GossipOptionEntries
                                                            .FirstOrDefault(e => e.Type == BotPoi.Current.Type.GetGossipType());
                                                        if (entry.Index >= 0)
                                                            GossipFrame.Instance.SelectGossipOption(entry.Index);
                                                    }),
                                                    // HB 6.2.3 fix: delay after gossip selection to let the game
                                                    // process the request and open the correct frame
                                                    new ActionSleep(500),
                                                    new Wait(5, ctx => !GossipFrame.Instance.IsVisible, new ActionIdle())
                                                )),
                                                new ActionIdle()
                                            )
                                        ),
                                        // Fly POI: if TaxiFrame never opened after 5 seconds, blacklist the flight master
                                        new DecoratorContinue(
                                            ctx => BotPoi.Current.Type == PoiType.Fly && !TaxiFrame.Instance.IsVisible,
                                            new Sequence(
                                                new TreeSharp.Action(ctx => Logging.Write("Taximap failed to open. Blacklisting the flight master.")),
                                                new TreeSharp.Action(ctx =>
                                                {
                                                    if (BotPoi.Current.AsObject != null)
                                                        Blacklist.Add(BotPoi.Current.AsObject.Guid, TimeSpan.FromMinutes(30));
                                                }),
                                                new ActionClearPoi("Flight master blacklisted")
                                            )
                                        )
                                    )
                                ),
                                // Vendor frame is open - do actions
                                new Decorator(
                                    ctx => IsVendorFrameOpen(),
                                    new PrioritySelector(
                                        // Sell/Repair — HB 6.2.3 pattern: require MerchantFrame visible
                                        new DecoratorIsPoiType(new[] { PoiType.Sell, PoiType.Repair },
                                            new Decorator(ctx => MerchantFrame.Instance.IsVisible, new Sequence(
                                            new DecoratorContinue(
                                                ctx => BotPoi.Current.AsObject?.ToUnit()?.IsVendor == true,
                                                new Sequence(
                                                    new ActionDebugString("Selling items"),
                                                    new ActionSetActivity("Selling Items"),
                                                    new TreeSharp.Action(ctx =>
                                                        Vendors.SellAllItemsStep() ? RunStatus.Success : RunStatus.Running),
                                                    new DecoratorContinue(
                                                        ctx => StyxWoW.Me.FreeBagSlots < 2,
                                                        new Sequence(
                                                            new TreeSharp.Action(ctx => Logging.Write(System.Drawing.Color.Red,
                                                                "We have just done a sell run and bags are still full. Stopping the bot.")),
                                                            new TreeSharp.Action(ctx => TreeRoot.Stop())
                                                        )
                                                    )
                                                )
                                            ),
                                            new DecoratorContinue(
                                                ctx => BotPoi.Current.AsObject?.ToUnit()?.IsRepairMerchant == true,
                                                new Sequence(
                                                    new ActionDebugString("Repairing items"),
                                                    new ActionSetActivity("Repairing Items"),
                                                    new TreeSharp.Action(ctx => Vendors.RepairAllItems()),
                                                    new ActionSleep(2000)
                                                )
                                            ),
                                            // Check if need to mail
                                            new DecoratorContinue(
                                                ctx =>
                                                {
                                                    if (!NeedToMail())
                                                        return false;
                                                    var mailbox = ProfileManager.CurrentProfile?.MailboxManager?.GetClosestMailbox();
                                                    if (mailbox == null)
                                                        return false;
                                                    // Store mailbox in context for ActionSetPoi
                                                    if (ctx is Dictionary<string, object> dict)
                                                        dict["_mailbox"] = mailbox;
                                                    return true;
                                                },
                                                new Sequence(
                                                    new ActionSetPoi(ctx =>
                                                    {
                                                        if (ctx is Dictionary<string, object> dict && dict.TryGetValue("_mailbox", out var mb) && mb is Mailbox mailbox)
                                                            return new BotPoi(mailbox.Location, PoiType.Mail);
                                                        return BotPoi.Current;
                                                    })
                                                )
                                            ),
                                            new DecoratorContinue(
                                                ctx => BotPoi.Current.Type != PoiType.Mail,
                                                new ActionClearPoi("POI is not Mail")
                                            ),
                                            new TreeSharp.Action(ctx => MerchantFrame.Instance.Close()),
                                            new TreeSharp.Action(ctx => StyxWoW.Me.ClearTarget()),
                                            new ActionAlwaysFail()
                                        ))),
                                        // Mail
                                        new DecoratorIsPoiType(PoiType.Mail, new Sequence(
                                            new ActionDebugString("Mailing items"),
                                            new ActionSetActivity("Mailing Items"),
                                            new TreeSharp.Action(ctx => Vendors.MailAllItems()),
                                            new DecoratorContinue(
                                                ctx => StyxWoW.Me.FreeBagSlots < 2,
                                                new Sequence(
                                                    new TreeSharp.Action(ctx => Logging.Write(System.Drawing.Color.Red,
                                                        "We have just done a mail run and bags are still full. Stopping the bot.")),
                                                    new TreeSharp.Action(ctx => TreeRoot.Stop())
                                                )
                                            ),
                                            new ActionClearPoi("Done mailing")
                                        )),
                                        // Buy
                                        new DecoratorIsPoiType(PoiType.Buy, new Sequence(
                                            // The merchant frame can become visible before its item list is
                                            // populated. Give the WotLK client time to receive the catalog.
                                            new Wait(3,
                                                ctx => MerchantFrame.Instance.IsVisible &&
                                                       MerchantFrame.Instance.LuaMerchantNumItems > 0,
                                                new ActionIdle()),
                                            new ActionDebugString("Buying items"),
                                            new ActionSetActivity("Buying Items"),
                                            new TreeSharp.Action(ctx => Vendors.BuyItems()),
                                            new ActionClearPoi("Done buying")
                                        )),
                                        // Train
                                        new DecoratorIsPoiType(PoiType.Train, new Sequence(
                                            new Wait(3, ctx => TrainerFrame.Instance.IsVisible, null),
                                            new ActionDebugString("Training Skills"),
                                            new ActionSetActivity("Training Skills"),
                                            new TreeSharp.Action(ctx => Vendors.TrainSkills()),
                                            new TreeSharp.Action(ctx => Lua.DoString("CloseTrainer()")),
                                            new ActionClearPoi("Done training")
                                        ))
                                    )
                                )
                            )
                        )
                    )
                ),
                // Check if need to sell
                new Decorator(
                    ctx => NeedToSell(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Sell), 
                        PoiType.Sell))
                ),
                // Check if need to repair
                new Decorator(
                    ctx => NeedToRepair(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Repair), 
                        PoiType.Repair))
                ),
                // Check if need to train
                new Decorator(
                    ctx => NeedToTrain(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Train), 
                        PoiType.Train))
                ),
                // Check if need to buy
                new Decorator(
                    ctx => NeedToBuy(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Food), 
                        PoiType.Buy))
                ),
                // Check flight paths
                new Decorator(
                    ctx => FlightPaths.Reason != FlightPathReason.None || 
                           FlightPaths.NeedFlightPath || 
                           FlightPaths.NeedNearbyUpdate(),
                    new TreeSharp.Action(ctx => FlightPaths.SetPoi())
                )
            );
        }

        private static bool NeedToSell()
        {
            if (StyxWoW.Me == null) return false;
            bool needsSellRun = Vendors.ForceSell ||
                                StyxWoW.Me.FreeNormalBagSlots <= ProfileManager.CurrentProfile.MinFreeBagSlots;
            if (!needsSellRun)
                return false;

            // HB 4.3.4 smethod_11 — no FindVendorsAutomatically check
            if (ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(Vendor.VendorType.Sell) == null)
                return false;
            return true;
        }

        private static bool NeedToTrain()
        {
            // HB 4.3.4 smethod_12
            if (!CharacterSettings.Instance.TrainNewSkills && !Vendors.ForceTrainer)
                return false;
            if (!Vendors.ForceTrainer && !Vendors.NeedClassTraining)
                return false;
            var trainer = ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(Vendor.VendorType.Train);
            if (trainer == null || StyxWoW.Me == null)
                return false;

            float distance = StyxWoW.Me.Location.Distance(trainer.Location);
            bool hasKnownFlightConnection = FlightPaths.HasKnownConnection(StyxWoW.Me.Location, trainer.Location);
            if (!ShouldVisitTrainer(distance, hasKnownFlightConnection))
            {
                if (_trainerRouteLogTimer.IsFinished)
                {
                    Logging.WriteDebug(
                        "Deferring trainer '{0}' ({1:F0} yards): no efficient flight route is known.",
                        trainer.Name,
                        distance);
                    _trainerRouteLogTimer.Reset();
                }
                return false;
            }
            return true;
        }

        internal static bool ShouldVisitTrainer(float distance, bool hasKnownFlightConnection)
        {
            const float MaximumAutomaticGroundTrainerDistance = 1200f;
            return distance <= MaximumAutomaticGroundTrainerDistance || hasKnownFlightConnection;
        }

        private static bool NeedToRepair()
        {
            if (StyxWoW.Me == null) return false;
            // HB 4.3.4 smethod_13 — no FindVendorsAutomatically check
            if (Vendors.RepairDisabled)
                return false;

            // HB 4.3.4: update repair cost periodically
            if (_repairCostTimer.IsFinished)
            {
                var cost = StyxWoW.Me.GetEstimatedRepairCost();
                if (cost.TotalCoppers != 0L)
                {
                    Logging.WriteDebug("Updating repair cost for current equipped items. New value: [{0}]", cost);
                    _lastRepairCost = (ulong)cost.TotalCoppers;
                }
                _repairCostTimer.Reset();
            }

            if (StyxWoW.Me.Coinage <= _lastRepairCost)
            {
                if (Vendors.ForceRepair)
                {
                    Logging.Write(System.Drawing.Color.Red, "WARNING! You have no money to repair! Cancelling forced repair run.");
                    Vendors.ForceRepair = false;
                }
                return false;
            }

            bool needsRepairRun = Vendors.ForceRepair ||
                                  StyxWoW.Me.LowestDurabilityPercent <= ProfileManager.CurrentProfile.MinDurability;
            if (!needsRepairRun)
                return false;

            return ProfileManager.CurrentProfile?.VendorManager?
                .GetClosestVendor(Vendor.VendorType.Repair) != null;
        }

        /// <summary>
        /// HB 4.3.4 smethod_14 - Check if need to buy food/drink.
        /// Note: Unlike Sell/Repair, HB 4.3.4 does NOT check FindVendorsAutomatically for buying.
        /// The FoodAmount/DrinkAmount sliders are the explicit opt-in.
        /// </summary>
        private static bool NeedToBuy()
        {
            // HB 4.3.4: Minimum 1 gold required to buy
            if (StyxWoW.Me.Coinage < 10000)
            {
                if (Vendors.ForceBuy)
                {
                    Logging.Write(System.Drawing.Color.Red, "WARNING! You have no money to restock! Cancelling forced restock run.");
                    Vendors.ForceBuy = false;
                }
                return false;
            }

            if (Vendors.ForceBuy)
                return true;

            // Check inventory need before automatic vendor discovery. Database discovery can
            // path-test many NPCs, so it must not run on every ordinary combat/movement pulse.
            bool usesMana = StyxWoW.Me.PowerType == WoWPowerType.Mana || StyxWoW.Me.Class == WoWClass.Druid;
            bool needsDrink = usesMana && Consumable.GetBestDrink(false) == null &&
                              CharacterSettings.Instance.DrinkAmount > 0;
            bool needsFood = Consumable.GetBestFood(false) == null && CharacterSettings.Instance.FoodAmount > 0;
            if (!needsDrink && !needsFood)
                return false;

            // HB 4.3.4: Check if food vendor exists (from profile or NPC database)
            var foodVendor = ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(Vendor.VendorType.Food);
            if (foodVendor == null)
                return false;

            if (needsDrink)
            {
                Logging.WriteDebug("[NeedToBuy] Need drink: DrinkAmount={0}, Vendor={1}",
                    CharacterSettings.Instance.DrinkAmount, foodVendor.Name);
                return true;
            }

            if (needsFood)
            {
                Logging.WriteDebug("[NeedToBuy] Need food: FoodAmount={0}, Vendor={1}",
                    CharacterSettings.Instance.FoodAmount, foodVendor.Name);
                return true;
            }

            return false;
        }

        private static bool NeedToMail()
        {
            LocalPlayer me = StyxWoW.Me;
            Profile currentProfile = ProfileManager.CurrentProfile;

            if (string.IsNullOrEmpty(CharacterSettings.Instance.MailRecipient) ||
                currentProfile == null ||
                ProfileManager.CurrentProfile?.MailboxManager == null)
                return false;

            Mailbox closestMailbox = ProfileManager.CurrentProfile.MailboxManager.GetClosestMailbox();
            if (closestMailbox == null)
                return false;

            return me.Level >= currentProfile.MinMailLevel &&
                   (closestMailbox.Location.Distance(me.Location) < 200.0 || StyxWoW.Me.FreeBagSlots < 30);
        }

        private static bool IsVendorFrameOpen()
        {
            return MerchantFrame.Instance.IsVisible ||
                   GossipFrame.Instance.IsVisible ||
                   MailFrame.Instance.IsVisible ||
                   TrainerFrame.Instance.IsVisible ||
                   TaxiFrame.Instance.IsVisible;
        }

        #endregion

        #region Roam Behavior

        /// <summary>
        /// HB 4.3.4 CreateRoamBehavior - handles movement between hotspots
        /// </summary>
        public static PrioritySelector CreateRoamBehavior()
        {
            LocalPlayer selectingActor = null;
            WoWUnit selected = null, displayed = null;
            ulong actorGuid = 0, selectedGuid = 0, displayedGuid = 0;
            uint selectingMap = 0;
            BotPoi selectingPoi = null;
            PoiType selectingPoiType = PoiType.None;
            object selectingTargeting = null, selectingProfile = null;
            bool ParticipantsCurrent() => selectingActor != null && actorGuid != 0
                && ReferenceEquals(StyxWoW.Me, selectingActor) && selectingActor.IsValid && selectingActor.IsAlive
                && selectingActor.Guid == actorGuid && selectingActor.MapId == selectingMap
                && selected != null && selectedGuid != 0 && selected.IsValid && selected.IsAlive && selected.Guid == selectedGuid
                && ReferenceEquals(Targeting.Instance, selectingTargeting) && ReferenceEquals(Targeting.Instance.FirstUnit, selected)
                && ReferenceEquals(ProfileManager.CurrentProfile, selectingProfile);
            bool SelectionCurrent() => ParticipantsCurrent() && ReferenceEquals(BotPoi.Current, selectingPoi)
                && selectingPoi != null && selectingPoi.Type == selectingPoiType && ParticipantsCurrent();
            bool DisplayUnchanged() => SelectionCurrent() && ReferenceEquals(selectingActor.CurrentTarget, displayed)
                && selectingActor.CurrentTargetGuid == displayedGuid && SelectionCurrent();
            bool Acknowledged() => SelectionCurrent() && ReferenceEquals(selectingActor.CurrentTarget, selected)
                && selectingActor.CurrentTargetGuid == selectedGuid && SelectionCurrent();

            return new PrioritySelector(
                // Find target if not looting/killing/vendoring
                // HB 6.2.3 fix: also exclude Sell/Repair/Train/Buy/Mail to prevent
                // pulling mobs during vendor runs (overwrites Sell POI with Kill)
                    new DecoratorIsNotPoiType(new[] { PoiType.Kill, PoiType.Loot, PoiType.Skin, PoiType.Harvest,
                        PoiType.Sell, PoiType.Repair, PoiType.Train, PoiType.Buy, PoiType.Mail, PoiType.Fly },
                    new Sequence(
                        new TreeSharp.Action(ctx =>
                        {
                            selectingActor = StyxWoW.Me; actorGuid = selectingActor?.Guid ?? 0;
                            selectingMap = selectingActor?.MapId ?? 0;
                            selectingTargeting = Targeting.Instance; selected = Targeting.Instance.FirstUnit;
                            selectedGuid = selected?.Guid ?? 0;
                            displayed = selectingActor?.CurrentTarget; displayedGuid = displayed?.Guid ?? 0;
                            selectingPoi = BotPoi.Current; selectingPoiType = selectingPoi?.Type ?? PoiType.None;
                            selectingProfile = ProfileManager.CurrentProfile;
                            return DisplayUnchanged() ? RunStatus.Success : RunStatus.Failure;
                        }),
                        new DecoratorNeedToFindTarget(new Sequence(
                            new TreeSharp.Action(ctx =>
                            {
                                if (!DisplayUnchanged()) return RunStatus.Failure;
                                selected.Target();
                                return SelectionCurrent() ? RunStatus.Success : RunStatus.Failure;
                            }),
                            new Wait(5, ctx => !SelectionCurrent() || Acknowledged() || !DisplayUnchanged(),
                                new Decorator(ctx => Acknowledged(), new ActionIdle())),
                            new TreeSharp.Action(ctx =>
                            {
                                // A displayed replacement is not this selection's acknowledgement.
                                // POI construction and publication may also deliver callbacks.
                                if (!Acknowledged()) return RunStatus.Failure;
                                var next = new BotPoi(selected, PoiType.Kill);
                                if (!Acknowledged()) return RunStatus.Failure;
                                BotPoi.Current = next;
                                return ParticipantsCurrent() && ReferenceEquals(BotPoi.Current, next)
                                    ? RunStatus.Success : RunStatus.Failure;
                            }))))
                ),
                // Move to hotspot if needed
                new DecoratorIsNotPoiType(new[] { PoiType.Kill, PoiType.Sell, PoiType.Repair,
                    PoiType.Train, PoiType.Buy, PoiType.Mail, PoiType.Fly }, new Decorator(
                    ctx => ShouldMoveToHotspot(),
                    new TreeSharp.Action(ctx =>
                    {
                        var actor = StyxWoW.Me;
                        ulong guid = actor?.Guid ?? 0;
                        uint map = actor?.MapId ?? 0;
                        var areaManager = StyxWoW.AreaManager;
                        GrindArea grindArea = areaManager?.CurrentGrindArea;
                        var poi = BotPoi.Current;
                        PoiType poiType = poi?.Type ?? PoiType.None;
                        var profile = ProfileManager.CurrentProfile;
                        object provider = Navigator.NavigationProvider;
                        bool ActorCurrent() => actor != null && guid != 0 && ReferenceEquals(StyxWoW.Me, actor)
                            && actor.IsValid && actor.IsAlive && actor.Guid == guid && actor.MapId == map
                            && (!actor.Combat || actor.Mounted) && (!actor.GotAlivePet || actor.Pet?.Combat != true || actor.Mounted)
                            && !actor.IsCasting && actor.ChanneledCastingSpellId == 0 && !actor.OnTaxi && !actor.IsOnTransport;
                        if (!ActorCurrent() || grindArea == null || poi == null)
                            return RunStatus.Failure;

                        Hotspot currentHotSpot = grindArea.CurrentHotSpot;
                        if (currentHotSpot == null) return RunStatus.Failure;
                        WoWPoint hotspot = currentHotSpot.Position;
                        bool Current() => ActorCurrent() && ReferenceEquals(BotPoi.Current, poi) && poi.Type == poiType
                            && ReferenceEquals(Navigator.NavigationProvider, provider) && ReferenceEquals(ProfileManager.CurrentProfile, profile)
                            && ReferenceEquals(StyxWoW.AreaManager, areaManager) && ReferenceEquals(areaManager.CurrentGrindArea, grindArea)
                            && ReferenceEquals(grindArea.CurrentHotSpot, currentHotSpot) && currentHotSpot.Position == hotspot && ActorCurrent();
                        if (!float.IsFinite(hotspot.X) || !float.IsFinite(hotspot.Y) || !float.IsFinite(hotspot.Z) || !Current())
                            return RunStatus.Failure;

                        bool canFly = Flightor.CanFly;
                        if (!Current()) return RunStatus.Failure;
                        if (canFly)
                        {
                            TreeRoot.StatusText = "Flying to hotspot";
                            if (!Current()) return RunStatus.Failure;
                            Flightor.MoveTo(hotspot);
                            return Current() ? RunStatus.Success : RunStatus.Failure;
                        }
                        bool shouldMount = Mount.ShouldMount(hotspot);
                        if (!Current()) return RunStatus.Failure;
                        if (shouldMount)
                        {
                            Mount.MountUp(() => Current() ? hotspot : WoWPoint.Empty);
                            if (!Current()) return RunStatus.Failure;
                        }

                        TreeRoot.StatusText = "Moving to hotspot";
                        if (!Current()) return RunStatus.Failure;
                        MoveResult movement = Navigator.MoveTo(hotspot);
                        return Current() ? Navigator.GetRunStatusFromMoveResult(movement) : RunStatus.Failure;
                    })
                )),
                // Move closer to target or clear POI if better target
                CreateOwnedRoamChaseBehavior()
            );
        }

        private static Composite CreateOwnedRoamChaseBehavior()
        {
            LocalPlayer actor = null;
            WoWUnit target = null, displayed = null;
            BotPoi poi = null;
            ulong actorGuid = 0, targetGuid = 0, displayedGuid = 0, poiGuid = 0;
            uint map = 0, entry = 0;
            PoiType type = PoiType.None;
            object targeting = null, provider = null, profile = null;
            var routine = RoutineManager.Current;
            var customMove = routine?.MoveToTargetBehavior;
            bool ParticipantsCurrent() => actor != null && actorGuid != 0
                && ReferenceEquals(StyxWoW.Me, actor) && actor.IsValid && actor.IsAlive
                && actor.Guid == actorGuid && actor.MapId == map
                && !actor.Combat && (!actor.GotAlivePet || actor.Pet?.Combat != true)
                && !actor.IsCasting && actor.ChanneledCastingSpellId == 0 && !actor.OnTaxi && !actor.IsOnTransport
                && target != null && targetGuid != 0 && target.IsValid && target.IsAlive && target.Guid == targetGuid
                && ReferenceEquals(Targeting.Instance, targeting) && ReferenceEquals(Targeting.Instance.FirstUnit, target);
            bool Current() => ParticipantsCurrent() && ReferenceEquals(BotPoi.Current, poi)
                && poi != null && poi.Type == type && poi.Guid == poiGuid && poi.Entry == entry
                && (type == PoiType.None || type == PoiType.Kill)
                && ReferenceEquals(actor.CurrentTarget, displayed) && actor.CurrentTargetGuid == displayedGuid
                && ReferenceEquals(Navigator.NavigationProvider, provider)
                && ReferenceEquals(ProfileManager.CurrentProfile, profile)
                && ReferenceEquals(RoutineManager.Current, routine) && ReferenceEquals(routine?.MoveToTargetBehavior, customMove)
                && ParticipantsCurrent();
            Composite Guard(Composite child) => new RoutineAdmissionGuard(Current, child);

            return new Sequence(
                new TreeSharp.Action(ctx =>
                {
                    actor = StyxWoW.Me; actorGuid = actor?.Guid ?? 0; map = actor?.MapId ?? 0;
                    targeting = Targeting.Instance; target = Targeting.Instance.FirstUnit; targetGuid = target?.Guid ?? 0;
                    displayed = actor?.CurrentTarget; displayedGuid = displayed?.Guid ?? 0;
                    poi = BotPoi.Current; type = poi?.Type ?? PoiType.None; poiGuid = poi?.Guid ?? 0; entry = poi?.Entry ?? 0;
                    provider = Navigator.NavigationProvider; profile = ProfileManager.CurrentProfile;
                    return Current() ? RunStatus.Success : RunStatus.Failure;
                }),
                new PrioritySelector(
                    Guard(customMove),
                    Guard(new Decorator(ctx => ShouldMoveCloserToTarget(), Guard(new ActionMoveToTarget()))),
                    new Decorator(ctx => Current() && ShouldClearPoiForBetterTarget() && Current(), new TreeSharp.Action(ctx =>
                    {
                        if (!Current()) return RunStatus.Failure;
                        // This action ends its admitted POI. Parent revalidation
                        // must not clear or chase any replacement created by cleanup.
                        BotPoi.Clear("NeedToClearPOI is true #2");
                        return RunStatus.Success;
                    }))));
        }

        private static bool ShouldClearPoiForBetterTarget()
        {
            // HB 4.3.4 smethod_8 — no dead check
            WoWUnit firstUnit = Targeting.Instance.FirstUnit;
            WoWUnit currentTarget = StyxWoW.Me.CurrentTarget;

            if (BotPoi.Current.Type == PoiType.Kill &&
                firstUnit != null &&
                firstUnit.Distance < Targeting.PullDistance &&
                currentTarget == null)
                return true;

            if (currentTarget != null && firstUnit != null)
                return currentTarget.Guid != firstUnit.Guid;

            return false;
        }

        private static bool ShouldMoveToHotspot()
        {
            GrindArea grindArea = StyxWoW.AreaManager?.CurrentGrindArea;
            if (grindArea == null)
            {
                Logging.WriteDebug("StyxWoW.AreaManager.CurrentGrindArea is null");
                return false;
            }
            return grindArea.HotspotChanged;
        }

        private static bool ShouldMoveCloserToTarget()
        {
            WoWUnit firstUnit = Targeting.Instance.FirstUnit;
            if (firstUnit == null)
                return false;

            return firstUnit.DistanceSqr >= Targeting.PullDistance * Targeting.PullDistance ||
                   !firstUnit.InLineOfSpellSight;
        }

        #endregion

        #region Target Filters

        /// <summary>
        /// HB 4.3.4 LevelbotIncludeLootsFilter - filters loot targets
        /// </summary>
        public static void LevelbotIncludeLootsFilter(List<WoWObject> incomingObjects, HashSet<WoWObject> outgoingObjects)
        {
            for (int i = 0; i < incomingObjects.Count; i++)
            {
                if (incomingObjects[i] is WoWUnit unit)
                {
                    if (LootTargeting.LootMobs &&
                        unit.Distance <= LootTargeting.LootRadius &&
                        unit.Dead &&
                        !Blacklist.Contains(unit.Guid) &&
                        (unit.KilledByMe && unit.CanLoot ||
                         unit.CanSkin && LootTargeting.SkinMobs && (CharacterSettings.Instance.NinjaSkin || unit.KilledByMe)))
                    {
                        outgoingObjects.Add(unit);
                    }
                }
                else if (incomingObjects[i] is WoWGameObject gameObj)
                {
                    WoWPoint location = StyxWoW.Me.Location;
                    if (gameObj.Distance <= LootTargeting.LootRadius &&
                        (gameObj.IsHerb && LootTargeting.HarvestHerbs ||
                         gameObj.IsMineral && LootTargeting.HarvestMinerals ||
                         gameObj.IsChest && LootTargeting.LootChests) &&
                        gameObj.CanLoot &&
                        !Blacklist.Contains(gameObj.Guid))
                    {
                        if (IsTooNearBlackspot(ProfileManager.CurrentProfile?.Blackspots, gameObj.Location))
                            Blacklist.Add(gameObj.Guid, TimeSpan.FromDays(3.0));
                        else
                            outgoingObjects.Add(gameObj);
                    }
                }
            }
        }

        /// <summary>
        /// HB 6.2.3 LevelBotIncludeTargetsFilter — filters combat targets by faction,
        /// MobIDs, level range, and an aggro-based fallback when traveling mounted.
        /// Excludes critters, player-owned NPCs (except flight masters), mobs tagged
        /// by other players, and any unit whose MyReaction >= Neutral (i.e. friendly
        /// or neutral NPCs like quest givers, vendors, and goblin faction NPCs such
        /// as Marvon Rivetseeker — without this check, the bot would happily target
        /// them because their FactionId is listed in the grind area / profile).
        /// </summary>
        public static void LevelBotIncludeTargetsFilter(List<WoWObject> incomingUnits, HashSet<WoWObject> outgoingUnits)
        {
            Profile currentProfile = ProfileManager.CurrentProfile;
            if (currentProfile == null || StyxWoW.Me.Combat)
                return;

            GrindArea grindArea = StyxWoW.AreaManager?.CurrentGrindArea;

            HashSet<uint> validFactions = new HashSet<uint>();
            List<int> validMobIds = new List<int>();

            if (grindArea != null)
            {
                validMobIds = grindArea.MobIDs;
                if (grindArea.Factions.Count > 0)
                {
                    foreach (int faction in grindArea.Factions)
                        validFactions.Add((uint)faction);
                }
            }
            if (validFactions.Count == 0 && currentProfile.Factions != null)
            {
                foreach (uint faction in currentProfile.Factions)
                    validFactions.Add(faction);
            }

            int minLevel = grindArea != null ? grindArea.TargetMinLevel
                          : currentProfile != null ? currentProfile.TargetMinLevel
                          : int.MinValue;
            int maxLevel = grindArea != null ? grindArea.TargetMaxLevel
                          : currentProfile != null ? currentProfile.TargetMaxLevel
                          : int.MaxValue;

            WoWPoint meLocation = StyxWoW.Me.Location;
            bool mounted = StyxWoW.Me.Mounted;
            PoiType poiType = BotPoi.Current.Type;
            bool isVendorRun = poiType == PoiType.Buy || poiType == PoiType.Mail ||
                               poiType == PoiType.Repair || poiType == PoiType.Sell;

            // Snapshot the navigator's current path so the aggro-based fallback can
            // pull in mobs that are on-route while we're mounted (HB 6.2.3).
            WoWPoint pathDestination = meLocation;
            if (Navigator.NavigationProvider is MeshNavigator meshNav && meshNav.HasActivePath)
            {
                pathDestination = meshNav.CurrentPath[meshNav.CurrentPath.Count - 1];
            }

            foreach (WoWObject obj in incomingUnits)
            {
                WoWUnit unit = obj as WoWUnit;
                if (unit == null || unit.IsPlayer)
                    continue;

                // HB 6.2.3: exclude player-owned NPCs (companions, hunter pets),
                // unless the owner is a flight master (so taxi NPCs are kept
                // accessible to FlightPaths).
                WoWUnit ownedByRoot = unit.OwnedByRoot;
                if (ownedByRoot != null && (ownedByRoot.IsPlayer || ownedByRoot.IsFlightMaster))
                {
                    if (ownedByRoot.IsPlayer && !ownedByRoot.IsFlightMaster)
                        continue;
                }

                // HB 6.2.3: skip critters (non-combat critters like rats, rabbits).
                if (unit.IsCritter)
                    continue;

                // HB 6.2.3: skip mobs tagged by another player (loot rights)
                // unless they already have a valid target.
                if (unit.TaggedByOther && unit.CurrentTargetGuid == 0)
                    continue;

                if (currentProfile != null && IsTooNearBlackspot(currentProfile.Blackspots, unit.Location))
                    continue;

                bool inLevelRange = unit.Level >= minLevel && unit.Level <= maxLevel;

                // Primary faction / MobIDs match — this is the "profile wants this mob" path.
                if (inLevelRange && !isVendorRun &&
                    (validFactions.Contains(unit.FactionId) || validMobIds.Contains((int)unit.Entry)) &&
                    !currentProfile.AvoidMobs.Contains(unit.Entry))
                {
                    outgoingUnits.Add(obj);
                    continue;
                }

                // HB 6.2.3: mounted-travel fallback. While we're mounted and moving
                // toward a hotspot, pull any aggressive mob that's on our path
                // (regardless of faction list). Skip when mounted and the kill-between
                // option is off, or when the target is on a transport/has no target.
                bool mountedPull = mounted
                    ? (Targeting.Instance.KillBetweenHotspots && unit.Difficulty > DifficultyColor.Gray)
                    : true;
                if (mountedPull
                    && unit.CurrentTargetGuid == 0
                    && unit.MyReaction < WoWUnitReaction.Neutral
                    && WoWMathHelper.IsInPath(unit, meLocation, pathDestination)
                    && (Math.Abs(meLocation.Z - unit.Location.Z) <= 10f || unit.InLineOfSpellSight))
                {
                    outgoingUnits.Add(obj);
                }
            }
        }

        /// <summary>
        /// HB 4.3.4 IsTooNearBlackspot - checks if point is within any blackspot
        /// </summary>
        public static bool IsTooNearBlackspot(IEnumerable<Blackspot> blackspots, WoWPoint point)
        {
            if (blackspots == null)
                return false;
            return blackspots.Any(b => point.Distance2D(b.Location) < b.Radius);
        }

        #endregion

        #region Helpers

        private static void SleepForLag()
        {
            // Sleep for estimated latency
            StyxWoW.Sleep(100 + (int)(StyxWoW.WoWClient?.Latency ?? 100));
        }

        /// <summary>
        /// HB 4.3.4 SetDefaultQueryFilter — Resets the mesh navigator query filter.
        /// Called when navigation parameters need to be restored to defaults.
        /// </summary>
        public static void SetDefaultQueryFilter()
        {
            if (Navigator.IsNavigatorLoaded)
            {
                Navigator.TripperNavigator.ResetQueryFilter();
            }
        }

        #endregion
    }
}
