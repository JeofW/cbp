# CopilotBuddy evidence-led audit, redesign, and staged PR implementation

You are the senior engineer responsible for a wide, evidence-led audit of the private `CopilotBuddy` repository for World of Warcraft 3.3.5a. Work directly in the checked-out repository using Codex. Treat the source, installed runtime snapshot, tests, and captured runtime logs as one system. Investigate root causes before changing behavior, then implement the justified fixes as small, reviewable pull requests.

Do not assume every movement symptom is a bad mesh. Separate navmesh data defects from navigator policy, transport/lift handling, live-collision logic, stuck recovery, quest/profile routing, dynamic blackspots, plugin latency, combat-routine interference, and lifecycle/threading faults. Label every conclusion as **confirmed**, **strongly inferred**, or **unverified**, and cite the exact file/line or log timestamp supporting it.

## Repository evidence

Start by reading:

- `AUDIT_CONTEXT.md`
- `Styx/Logic/Pathing/MeshNavigator.cs`
- `Styx/Logic/Pathing/ElevatorTransitController.cs`
- `Styx/Logic/Pathing/StuckHandler.cs`
- `Styx/Logic/Pathing/Navigator.cs`
- `Styx/Logic/BehaviorTree/TreeRoot.cs`
- `Styx/Logic/Combat/SpellManager.cs`
- `Styx/Logic/Combat/SpellManagerEx.cs`
- `Tools/QuestRecoveryRegressionTests/MovementLatencyRegressionTests.cs`
- `Tools/QuestRecoveryRegressionTests/RoutineCompilationRegression.cs`
- `runtime-snapshot/Bots/WholesomeAutoQuest-master/`
- `runtime-snapshot/Routines/Singular wotlk/`
- `runtime-snapshot/Plugins/`
- `runtime-snapshot/Quest Behaviors/`
- `runtime-snapshot/Default Profiles/`
- all files under `runtime-logs/`, prioritizing `runtime-logs/2026-09-12_1701_45740.log`

The runtime snapshot is evidence of what was installed; core source outside it is the implementation source. Determine whether any installed file is generated/copied from another source before editing it. Do not patch only a runtime copy when a canonical source exists.

## Known evidence that must be reproduced and explained

### Thunder Bluff / Mesa Elevator failure

In `2026-09-12_1701_45740.log`, the character was a level 33 Blood Elf Paladin and the Questing bot selected vendor Grod at `(-1152.76, 71.41, 145.87)` in Thunder Bluff.

- At `18:20:46.675`, stuck detection fired at `(-1350.26, 167.19, 48.68)`.
- At `18:20:46.709`, the nearest game object was entry `4171`, type `Transport`, name `Mesa Elevator`, distance `49.78` yards.
- At `18:20:48.991`, it was still stuck and the Mesa Elevator was `49.31` yards away.
- At `18:21:20.653`, a path from approximately `(-1135.72, 142.12, 91.15)` to Grod loaded 330 mesh tiles and took 1041 ms. `MoveTo` then failed with `pathCount=90 pathIndex=90 partial=True`.
- From `18:21:25` through `18:21:56`, navigation repeatedly exhausted two-point partial paths beneath an upper-level destination, remained stationary for three-second windows, and invoked generic jump/strafe/dismount recovery.
- There is no `Preferring nearby elevator` or elevator-controller state/decision log for this incident.
- At `18:22:01`, stopping interrupted a timed `WoWMovement.Move` in `DefaultStuckHandler.Unstick`, producing `ThreadInterruptedException`.

The current `MeshNavigator.ShouldPreferNearbyElevator` rejects candidates when `player.Distance2D(transport) > 35f` or `destination.Distance2D(transport) > 70f`. The observed live Mesa Elevator was about 49 yards from the first stuck location. This is a strong candidate explanation for why lift transit never engaged, but it is not sufficient by itself: the lift object's live position changes, the boarding/landing geometry must be proven, and the original mesh path may not contain safe landings. Instrument and test the complete decision chain.

The existing elevator regression uses a Freewind fixture where the player is extremely close to the transport. Add a Thunder Bluff regression fixture using the captured coordinates and Mesa Elevator entry `4171`. It must reproduce the pre-fix failure and prove the final behavior selects a safe route to the lift, boards only at a validated landing, rides without generic obstacle recovery, exits onto destination-side navmesh, and resumes the vendor route.

### Navigation failures are systemic, not one incident

An initial aggregate over the uploaded logs found:

- 36 log files with navigation-related events
- 293 `We are stuck` events
- 345 `[Nav] MoveTo failed` events
- 338 `partial=True` events
- 365 `Skipped N path nodes` events

Recompute these values from the repository rather than trusting them blindly. Cluster events by map, location radius, destination/POI, failure mode, and time window. Identify recurring hotspots and distinguish:

1. disconnected or corrupt mesh polygons/tiles;
2. vertical transitions requiring elevators, ramps, doors, bridges, portals, or off-mesh connections;
3. live obstacles absent from the static mesh;
4. unsafe look-ahead or waypoint skipping;
5. route oscillation caused by repeated regeneration, changing blackspots, or unstable destination selection;
6. stuck recovery acting when the route is impossible rather than physically blocked;
7. quest/profile or vendor selection sending the character to an unreachable floor;
8. movement and plugin stalls that make a valid route appear stuck.

### Pulse stalls and visible pauses

During the Thunder Bluff window, ordinary Questing bot-root pulses repeatedly took about 250–300 ms despite a nominal 13 TPS target. During partial-path recovery, pulses took about 0.5–1.8 seconds. AutoEquip2 repeatedly consumed about 0.55 seconds by itself every roughly ten seconds. The 330-tile Thunder Bluff path query took 1041 ms synchronously.

Measure the real distribution (p50/p95/p99/max) by subsystem and state. Trace synchronous path generation, timed movement, Lua/injection work, object updates, targeting, plugins, routines, logging, and behavior-tree work. The movement loop must remain responsive; replace blocking duration-based actions with cancellable, tick-driven state where appropriate. Do not hide latency by raising warning thresholds.

### Spell-manager lifecycle/subscription multiplication

Near the restart at `18:22:11`, the latest log contains 91 `Building spell book` messages, 13 `SpellManager Initialize` messages, and 78 repeated `Subscribed to LEARNED_SPELL_IN_TAB, ACTIVE_TALENT_GROUP_CHANGED` messages. Inspect the exact counts again.

`TreeRoot` subscribes its own `OnBotStart` handler once, but that handler calls `SpellManager.Initialize()`. `SpellManager.Initialize()` subscribes another `BotEvents.OnBotStart` handler every bot start and forces a refresh. `SpellManager.Shutdown()` exists but appears not to be part of ordinary start/stop teardown. Confirm whether this creates an increasing number of handlers and spellbook rebuilds on each start. Audit equivalent lifecycle patterns across core, Singular, bot bases, and plugins. Initialization and event ownership must be idempotent, symmetrical, testable, and safe over repeated start/stop cycles.

## Workstream A: navigation architecture and behavior

Build a root-cause matrix for every major symptom: back-and-forth motion, wall banging, obstruction stalls, repeated jump/strafe, pauses, partial paths, lift failures, and cliff/drop incidents. For each, record evidence, responsible layer, reproduction, proposed fix, regression risk, and verification method.

Audit at least:

- path request ownership, destination stability, route caching, regeneration throttles, and cancellation;
- partial-path semantics, especially a path ending below/above a destination with a large Z gap;
- elevator discovery independent of a moving platform's instantaneous position;
- stable transport identity, shaft/landing geometry, approach routes, dock detection, boarding, attachment, riding, exiting, and timeout/fallback behavior;
- static navmesh flags/off-mesh connections versus runtime transport inference;
- waypoint precision, `SkipPassedWaypoints`, corner cutting, switchbacks, and live collision probes;
- dynamic blackspots, mob-pack avoidance, local detours, and whether changing geometry makes routes oscillate;
- door, ramp, bridge, water, indoor/outdoor, mount, and transport transitions;
- obstacle classification and bounded recovery; an impossible vertical route must not be treated as a small physical obstruction;
- forward ground/support probes, slope/drop limits, fall state, jump authorization, and cliff-edge clearance;
- behavior when the target/POI is on another floor and when the nearest valid access point is not close to the destination in 2D;
- tick latency, blocking waits, thread interruption, cancellation, and stop/pause correctness;
- ownership conflicts between navigation, mount-up, buffs, combat, quest recovery, and plugins.

Design a navigation state model with explicit route states and transition reasons. At minimum distinguish normal path following, waiting for path generation, local-obstacle detour, vertical-transition acquisition, approaching a transport, boarding, riding, exiting, genuine physical stuck recovery, unreachable destination, and safe failure/escalation. Only one owner may issue movement at a time. Every state transition must be logged in a rate-limited structured form with route ID, state, reason, map, start, destination, current position, path index/count, partial flag, Z gap, active blackspots, selected transport, pulse latency, and recovery attempt.

Do not simply increase elevator radii or add hard-coded Thunder Bluff coordinates without proving safety and generality. A small registry of known transport metadata may be appropriate if the runtime API cannot reliably infer stationary shaft/landing data, but it must complement a generic mechanism and have validation/tests.

### Navigation acceptance criteria

Establish a baseline from the logs and then define measurable targets. At minimum, prove:

- the captured Thunder Bluff route reaches Grod using the Mesa Elevator or a validated walkable ramp, without repeated two-point partial-path recovery;
- no repeated alternating direction/replan cycle occurs without a recorded environmental or destination change;
- normal obstruction handling steers/replans around supported obstacles instead of repeatedly pressing into them;
- an unreachable vertical destination fails or chooses a vertical-transition strategy within a bounded time, rather than jumping/strafe-cycling indefinitely;
- cliff/drop validation prevents selecting or skipping to unsupported ground and generic recovery cannot jump toward an unsafe edge;
- lift riding suppresses ordinary live-collision and stuck recovery unless transport-specific safety declares failure;
- stop/pause cancels movement promptly without surfacing `ThreadInterruptedException` as an operational error;
- path generation and recovery do not block the control tick for long duration-based movements;
- repeated identical path requests are coalesced/cached and expensive path work is budgeted or moved off the critical movement tick safely;
- before/after replay or live-run evidence demonstrates materially lower stuck, partial-path, reversal, and long-pulse rates.

## Workstream B: Singular combat-routine audit for every class

Audit every supported WotLK class and spec, with Retribution Paladin as the first deep implementation target. This code has been ported from later Singular/Honorbuddy eras and contains many WotLK correction comments. Comments are not proof of correctness. Validate spell names/IDs, ranks, levels, talent/glyph semantics, aura ownership, resource systems, target restrictions, cooldown/GCD behavior, and priority order against WoW 3.3.5a behavior and the runtime spellbook. Identify remaining Cataclysm/MoP assumptions, dead/commented rotations, duplicate context-specific logic, and behavior-tree failure/spam patterns.

For every class/spec produce a concise scorecard covering:

- compilation and behavior discovery for Normal, Instance, and Battleground contexts;
- pull, combat, heal, rest, pre-combat, combat-buff, and death behavior composition;
- level/spell/talent/glyph-aware decisions;
- single-target, cleave, AoE, execute, interrupt, dispel, cooldown, defensive, threat, mana/resource, pet/form/stance, and movement logic;
- GCD/cast/channel/latency handling and retry/backoff behavior;
- target validity, null-safety, aura ownership/refresh windows, and range/LOS/facing;
- navigation/mount cooperation and whether out-of-combat buffs cancel movement or mount attempts;
- decision cost, log spam, event subscriptions, cache invalidation, and long-pulse contribution;
- automated decision tests and remaining live-game validation needs.

Do not attempt to rewrite every class in one giant PR. Use the scorecard to prioritize correctness, safety, and high-frequency defects, then implement coherent slices.

## Workstream C: Retribution Paladin deep audit

Review active code in:

- `runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Retribution.cs`
- `runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Common.cs`
- `runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Lowbie.cs`
- `runtime-snapshot/Routines/Singular wotlk/Settings/PaladinSettings.cs`
- shared `Helpers/Spell.cs`, `Helpers/Movement.cs`, `Helpers/Rest.cs`, `Managers/TalentManager.cs`, and routine composition/lifecycle code.

Investigate these concrete concerns:

1. The normal/instance rotation only casts Divine Storm when at least four enemies are nearby. In WotLK it is also part of single-target/low-target-count damage priority once learned. Worse, Crusader Strike is suppressed at four or more enemies whenever Divine Storm is known, even while Divine Storm is on cooldown. Verify the actual behavior and replace target-count gates with a correct first-come/priority policy that does not idle usable attacks.
2. `ShouldCastExorcism` accepts `targetInMeleeRange` and `autoAttacking` but currently ignores both when Art of War is not known and returns true unconditionally. The existing regression explicitly requires low-level melee filler hard-casts. Re-evaluate that test and policy using measured opportunity cost, movement/cast interruption, target time-to-die, range, and whether the character is safely stationary. Preserve a useful ranged opener without forcing repeated melee hard-casts that reduce damage or cause pauses.
3. Exorcism is duplicated in some context rotations and retry behavior may report dispatch rather than server acceptance. Verify GCD/cooldown/range/LOS backoff and eliminate failed-cast spam without starving other attacks or movement.
4. Seal selection is duplicated between Paladin Common and Retribution combat. Verify level-33 behavior, faction variants, Seal of Command availability, single-target versus multi-target policy, mana constraints, and unnecessary seal twisting/GCD consumption.
5. During the Thunder Bluff recovery, dismounting allowed Singular to cast Blessing of Kings and Seal of Righteousness; many `Mount-up request cancelled before casting` lines followed. Define explicit coordination so routine maintenance buffs do not fight an active travel/recovery/transport action.
6. Validate Judgement choice, Hammer of Wrath execute rules, Crusader Strike, Divine Storm, Consecration, Holy Wrath target restrictions, Art of War procs, Avenging Wrath/cooldown stacking, racials, Divine Plea, interrupts, defensives, healing, and mana thresholds for leveling, solo, dungeon, battleground, boss, undead/demon, and AoE contexts.
7. Remove or isolate obsolete commented rotations after their useful history is captured, and reduce duplicated Normal/Instance/Battleground code through clear shared policy without speculative abstraction.

Build deterministic rotation-decision tests using synthetic snapshots across representative levels (including the captured level 33 Paladin and level 80), learned spells, talents/glyphs, faction, target type, target health, target count, range, movement, GCD/cooldowns, mana, and proc/aura state. Tests must assert both the chosen action and why higher-priority actions were rejected. Add a recorded combat-decision trace suitable for before/after comparison; do not claim DPS improvement from priority inspection alone. Use live or simulator evidence where possible and state limitations.

## Architecture review

After root-cause analysis, decide whether focused refactoring or a broader redesign is justified. Evaluate boundaries among:

- behavior-tree scheduling and lifecycle;
- navigation planning versus movement execution;
- transports and other off-mesh transitions;
- obstacle sensing and recovery;
- quest/POI intent;
- combat/buff/mount arbitration;
- plugin execution and pulse budgets;
- logging, telemetry, replay fixtures, and runtime snapshots.

Prefer explicit interfaces and ownership over global mutable state and cross-layer side effects. Initialization/teardown must be idempotent. Long operations must be cancellable. A failed layer must return a typed reason that the caller can act on, not only `Failed` plus a generic jump. Preserve compatibility with the 3.3.5a host and existing public plugin/routine APIs unless a breaking change is separately justified and migrated.

## Required deliverables before implementation

Create an audit document in the repository containing:

1. executive summary and highest-confidence root causes;
2. evidence ledger with exact log timestamps and source locations;
3. event clusters and performance baseline from all logs;
4. system/component and movement-state diagrams (Mermaid is acceptable in Markdown);
5. root-cause matrix with confirmed/inferred/unknown status;
6. mesh-data versus code/policy versus profile/plugin attribution;
7. Singular all-class/spec scorecard and detailed Retribution findings;
8. target architecture, alternatives considered, risks, and migration plan;
9. prioritized backlog with severity, confidence, effort, dependencies, and test plan;
10. proposed staged PR sequence.

Do not start a broad rewrite until this audit is committed and the evidence supports it. You may implement a minimal instrumentation or deterministic reproduction PR first if necessary to prove causes.

## Pull-request strategy

Never commit directly to the default branch. Work in a separate branch/worktree. Keep generated files, runtime captures, binaries, credentials, account data, and unrelated changes out of PRs. Preserve the privacy of uploaded logs and do not repost them publicly.

Use small PRs whose boundaries can change based on findings, but start with this candidate sequence:

1. **Audit and observability:** evidence report, log analyzer/replay fixtures, structured route-state diagnostics, performance baselines, and lifecycle-count tests; no behavior change unless needed for safe instrumentation.
2. **Lifecycle and pulse correctness:** idempotent SpellManager/core/routine/plugin initialization, symmetrical teardown, stop/cancel safety, and elimination of blocking timed movement on the bot tick.
3. **Thunder Bluff vertical transition:** captured Mesa Elevator regression, stable transport/landing discovery, partial vertical-path classification, and safe elevator routing.
4. **Obstacle, oscillation, and cliff safety:** route stability, safe look-ahead, live obstacle detours, bounded typed recovery, blackspot interaction, and fall/drop protections.
5. **Plugin and path-performance budget:** AutoEquip2 hot path, mesh-tile loading/query cost, caching/coalescing, and demonstrated p95/p99 tick improvement.
6. **Singular foundation:** decision instrumentation, event/cache lifecycle, common spell/GCD/retry/movement arbitration, WotLK validation utilities, and class/spec scorecard tests.
7. **Retribution Paladin correctness and performance:** unified level-aware FCFS/priority policy, seals/buffs, context rules, test matrix, and before/after combat traces.
8. **Remaining class/spec fixes:** grouped into reviewable PRs by shared root cause or class family, not one monolithic change.

For each PR:

- state the reproduced failure and evidence;
- add a failing regression first when practical;
- make the smallest maintainable fix at the owning layer;
- run focused tests plus the relevant wider suite/build;
- record before/after metrics or traces;
- document compatibility, risks, rollback, and unverified live-game assumptions;
- inspect the diff for accidental runtime logs, binaries, secrets, or unrelated edits;
- push the branch and open a PR with a reviewer-friendly description;
- stop after opening the PR if review or live-game validation is required before dependent work.

## Completion standard

The work is not complete because code compiles or a synthetic test passes. Completion requires an evidence-backed explanation, deterministic regressions for confirmed bugs, clean builds/tests, repeated start/stop verification, before/after log analysis, and live-game validation for behaviors that cannot be proven offline. Report any remaining mesh assets, maps, client-memory behavior, or game scenarios that are unavailable, and provide exact steps/data needed to close those gaps.

Begin now with repository inspection and the audit deliverable. Do not ask for routine choices you can resolve from evidence. Ask only if authorization, unavailable external data, or a materially different architecture decision truly blocks progress.
