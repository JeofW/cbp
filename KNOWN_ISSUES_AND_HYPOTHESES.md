# Known issues, risk signals, and investigation hypotheses

This document seeds a whole-system audit. It is not a verdict list. An auditor must reproduce each item, trace it to the owning layer, and retain or reject it based on evidence.

## Confidence labels

- **Confirmed observation**: directly visible in the uploaded logs or source.
- **Strong inference**: source and runtime evidence support a likely causal explanation, but the proposed causal chain still needs a focused reproduction.
- **Investigation hypothesis**: plausible risk that must not be presented as a defect until verified.

Raw log-match totals below are signals, not unique incident counts. Stack traces and repeated diagnostics can produce many lines for one episode.

## Evidence-scale signals

Across the uploaded runtime logs, an initial literal-pattern scan found:

| Signal | Raw matches |
|---|---:|
| `Slow bot root` | 50,477 |
| `Slow plugin` | 8,591 |
| `Exception` | 5,364 |
| `NullReferenceException` | 1,247 |
| `Building spell book` | 880 |
| `[Nav] MoveTo failed` | 345 |
| `partial=True` | 338 |
| `We are stuck` | 293 |
| `Blackspot` | 203 |
| `Mount-up request cancelled` | 98 |
| `PathGenerationFailed` | 88 |
| `Slow path generation` | 44 |
| `ThreadInterruptedException` | 24 |

These must be re-counted, deduplicated into incidents, clustered by session/map/location/POI, and linked to source paths. Do not use raw totals as defect counts.

## Confirmed observations

### Navigation and movement

1. **Thunder Bluff lift handoff did not activate.** The latest log detects Mesa Elevator entry `4171` about 49 yards from the player, but contains no nearby-elevator selection or elevator state transition before the failure.
2. **The elevator preference gate excludes that observation.** `MeshNavigator.ShouldPreferNearbyElevator` rejects a live transport more than 35 yards from the player. The observed Mesa Elevator was about 49 yards away.
3. **A large vertical route became an exhausted partial path.** Navigation attempted to reach vendor Grod at Z `145.87` from approximately Z `90`, completed the available path, and repeatedly reported `pathCount=2 pathIndex=2 partial=True`.
4. **Impossible-route handling falls into physical stuck recovery.** The exhausted vertical partial path triggered jump, forward-strafe, dismount, side-strafe, and more jumps even though no small obstacle could bridge the floor gap.
5. **Timed unstick movement blocks and is interrupted on stop.** `DefaultStuckHandler` calls duration-based `WoWMovement.Move`; stopping at `18:22:01` interrupted the wait and emitted `ThreadInterruptedException` through the navigation stack.
6. **Path generation can block the active route.** The Thunder Bluff query loaded 330 tiles and took 1041 ms synchronously before returning a path that still ended partial.
7. **Long bot-root pulses are pervasive.** The newest incident includes ordinary 250–300 ms bot-root pulses and recovery pulses between roughly 0.5 and 1.8 seconds despite the configured 13 TPS cadence.
8. **AutoEquip2 is a repeated major pulse consumer.** It commonly consumes about 0.55 seconds per slow invocation in the newest incident.
9. **Waypoint skipping is frequent.** Uploaded logs contain hundreds of `Skipped N path nodes` records; the correctness of each skip is not currently explained by the log.
10. **Blackspots participate in recovery.** The stuck handler can add a blackspot and clear the path, while other systems such as mob-pack avoidance can also change traversal geometry.
11. **Movement, mounting, and routine buffs can overlap.** During Thunder Bluff recovery, dismount was followed by Blessing of Kings and Seal of Righteousness casts plus repeated mount-up cancellations.

### Lifecycle, events, and startup

12. **SpellManager initialization multiplies across bot starts.** `TreeRoot.OnBotStart` calls `SpellManager.Initialize`, and `Initialize` adds another `BotEvents.OnBotStart` handler. There is no ordinary stop-path call to `SpellManager.Shutdown` in the inspected source.
13. **The multiplication is visible at runtime.** The latest restart logged 13 SpellManager initializations, 78 repeated Lua-event subscription messages, and 91 spellbook builds.
14. **Spellbook work is repeatedly forced by resetting the known count.** Each accumulated start handler sets the last-known count to zero and runs a full refresh.
15. **Quest-behavior discovery logs the same paths many times during startup.** The latest startup repeatedly reports the same `LoadProfile.cs` behavior path. Whether this is duplicate scanning, duplicate source roots, or merely noisy logging remains to be determined.
16. **The corpus contains a large volume of exception text.** This requires incident-level clustering; it cannot responsibly be treated as thousands of independent bugs.

### Singular / Retribution Paladin

17. **Divine Storm is gated to four or more nearby enemies in active Retribution rotations.** It is absent from the lower-target-count priority once learned.
18. **Crusader Strike is suppressed at four or more enemies whenever Divine Storm is known.** The condition does not depend on whether Divine Storm is presently usable, so both can be unavailable by policy while Crusader Strike is actually ready.
19. **Low-level Exorcism ignores two supplied policy inputs.** `ShouldCastExorcism` receives melee-range and auto-attack state but returns true unconditionally when Art of War is not known.
20. **The current regression enshrines low-level melee Exorcism hard-casting.** It explicitly requires the policy to return true while in melee and auto-attacking; the performance premise needs to be re-evaluated rather than preserving the test blindly.
21. **Retribution policy is duplicated by context.** Normal, Battleground, and Instance rotations repeat seals, cooldowns, attacks, and healing with divergent predicates.
22. **Seal selection is duplicated across Paladin Common and Retribution combat.** This creates multiple policy owners and possible GCD/mount churn.
23. **A full combat composite is also annotated as a Heal behavior.** The active normal Pull/Combat method carries `BehaviorType.Heal`, while a separate Retribution heal behavior also exists. Composite construction and ordering must be verified.
24. **The routine contains extensive later-expansion correction history and dead/commented rotations.** WotLK comments show prior era drift, but do not prove all active spell and talent rules are now correct.
25. **Automated Singular coverage is narrow.** Existing checks mainly prove source compilation, a post-kill null case, and the current Exorcism predicate rather than complete rotation decisions or DPS behavior.

## Strong causal inferences to reproduce

1. **Thunder Bluff loop:** upper-floor POI → partial mesh path under the destination → elevator rejected by proximity policy → generic stuck recovery → no meaningful displacement → the same partial route again.
2. **Recovery/mount/buff loop:** stuck handler dismounts → pre-combat maintenance becomes eligible → buff/seal cast cancels mount-up → travel pauses → progress monitor sees continued stationarity → recovery advances again.
3. **Start-handler growth loop:** bot start → TreeRoot calls SpellManager.Initialize → another start handler is registered → next start invokes more handlers → each handler refreshes and re-subscribes → startup work grows with every run.
4. **Latency/recovery loop:** synchronous plugin/path/movement work delays ticks → progress sampling becomes coarse or stale → movement is classified as stalled → recovery clears/rebuilds paths → more synchronous work and visible pauses.
5. **Blackspot/replan loop:** failure adds or updates avoidance geometry → path changes → movement reverses or route becomes partial → another failure changes avoidance geometry again → back-and-forth movement.
6. **Look-ahead/corner loop:** a future waypoint is selected beyond a turn → live geometry blocks the shortened segment → local detour or blackspot changes the route → the next path/skip selects back across the corner.
7. **POI-floor ambiguity loop:** a vendor/quest objective is selected by world distance without access-layer awareness → navigator approaches the correct XY on the wrong Z → partial-path recovery cannot change the intent → the bot repeatedly retries the same inaccessible endpoint.
8. **Multiple movement-owner loop:** quest POI navigation, stuck recovery, mount logic, avoidance, combat movement, and routine buffs each make locally valid decisions without a single arbiter → commands cancel or counteract one another → oscillation and pauses.

## Investigation hypotheses: navigation and geometry

1. Transport selection depends too heavily on the platform's moving live location instead of a stable shaft/landing definition.
2. Elevator injection is only attempted at path-generation time and may be skipped after waypoint look-ahead or when the relevant platform is temporarily outside the threshold.
3. `Success`, `Partial`, `Failed`, and exhausted-path meanings are not consistently propagated between the native path query, `MeshNavigator`, callers, and recovery code.
4. The navigator lacks a typed “vertical transition required” result, so callers cannot choose a lift, ramp, door, portal, or different POI.
5. Fixed global distance, precision, Z-gap, slope, and timeout thresholds do not fit cities, open terrain, interiors, transports, mounts, and swimming equally.
6. Repeated identical path requests may not be coalesced across callers or may be invalidated by small position/destination changes.
7. Tile loading/query scope may be much wider than the local route requires, explaining hundreds of tile loads for a short city path.
8. `SkipPassedWaypoints` may be safe in synthetic tests but unsafe at switchbacks, narrow ramps, stairs, bridges, doors, cliff edges, or changing collision geometry.
9. Live collision sees walls/objects that the mesh does not, but local detours may lack sufficient clearance, support, or persistence to guide a stable replan.
10. Dynamic blackspots need provenance, TTL, confidence, merge rules, and route-scoped ownership; otherwise stale or competing blackspots can poison valid paths.
11. Mob-pack avoidance polygons may change quickly as units move and can cause route churn or oscillation when combined with path smoothing.
12. Stuck detection may confuse low tick rate, casting, transport motion, loading, combat control loss, or an intentional wait with a physical obstruction.
13. Stuck recovery actions may run without validating forward ground support, slope, drop height, water, platform motion, or cliff clearance for the proposed direction.
14. A jump that is line-of-sight-safe is not necessarily ground-support-safe; LOS alone cannot authorize a cliff-edge jump.
15. Movement cancellation and state cleanup may be incomplete when POI, map, destination, transport, combat state, death state, or bot state changes.
16. Route smoothing may optimize geometric length while ignoring turn angle, character acceleration, CTM arrival radius, corridor width, or human-looking continuity.
17. Final NPC/object approach exemptions may prevent needed collision handling near large models, walls, counters, or vertically separated targets.
18. Door, bridge, zeppelin, boat, tram, elevator, and portal transitions may each implement unrelated timing/attachment semantics instead of one off-mesh-transition contract.
19. The complete `mmaps` dataset is stored through Git LFS. The audit must verify that LFS objects were actually pulled before inspecting tile contents, identify the exact map/tile set used by each incident, and still capture runtime polygon/query evidence that static binaries alone cannot reveal.
20. Profiles with explicit `UseTransport` behavior may work while automatically selected vendors/POIs do not, revealing a split between scripted and generic vertical navigation.

## Investigation hypotheses: questing, POIs, and recovery

1. Wholesome AutoQuest, Questing core, profiles, and recovery adapters may each retain or replace destination ownership without a shared route/intention ID.
2. Vendor selection may optimize straight-line distance instead of navigable cost, faction/access restrictions, floor, transport wait, and path confidence.
3. A failed vendor POI may be reselected immediately because failure/blacklist state is not shared with the selecting bot base.
4. Quest recovery may interpret navigation non-progress as quest non-progress and rotate objectives unnecessarily.
5. Hotspot rotation can fight navigator recovery if both react to the same stall on different timers.
6. LoadProfile/forced-behavior execution may rebuild state or rediscover behaviors more often than necessary.
7. Blacklists may be scoped by GUID when the real failure is location, floor, quest step, route, or temporary world state—or vice versa.
8. Death, ghost, corpse, vendor, repair, mail, train, and quest POIs may not share a consistent reachability and fallback contract.
9. Map/zone transitions may leave cached routes, blackspots, transports, or recovery counters from the previous context.
10. Repeated “no progress” logic may lack hysteresis and a circuit breaker that stops safely with a concise diagnostic bundle.

## Investigation hypotheses: scheduling, performance, and lifecycle

1. More components may repeat SpellManager's non-idempotent event-subscription pattern.
2. Static constructors and start handlers obscure lifecycle ownership and make teardown order difficult to prove.
3. Delegates may be attached by recreated instances or lambdas that cannot later be detached reliably.
4. Talent events and learned-spell events may trigger overlapping SpellManager refresh and Singular behavior rebuild work.
5. Source compilation or behavior discovery may be repeated per start or per source root without fingerprint caching.
6. Plugin pulses run synchronously in the same critical cadence as movement without budgets, isolation, or backpressure.
7. Slow-plugin warnings identify cost after the fact but do not prevent one plugin from starving movement.
8. Logging volume itself may amplify stalls during event storms, path failures, or failed spell retries.
9. Thread interruption is being used as a cancellation mechanism across managed waits, producing expected-stop exceptions and potentially incomplete cleanup.
10. Paused, stopping, loading-screen, disconnected, and unhydrated-player states may still allow components to read invalid or stale game objects.
11. The large number of raw NullReference and exception matches may reveal repeated nullable-object lifetime assumptions across pulse boundaries.
12. Cache invalidation may be too broad (full rebuild) or too narrow (stale route/spell/talent/object state), with few ownership assertions.
13. Performance tests may assert rate limiting but not end-to-end p50/p95/p99 tick budgets or starvation behavior.
14. x86 process constraints and large asset/object caches may create memory pressure or GC pauses not visible in current diagnostics.
15. Native injection/framelock work may be counted under the wrong subsystem, hiding the true latency owner.

## Investigation hypotheses: Singular for all classes

1. Later-expansion source heritage may leave invalid spell names, IDs, talents, glyphs, mechanics, aura semantics, or resource assumptions in active paths.
2. String-based spell and aura matching may be rank-, locale-, faction-, or ownership-sensitive.
3. Normal, Instance, and Battleground copies may have drifted and may silently omit attacks or defensives in one context.
4. Lowbie versus detected-spec composition may switch at the wrong level/talent state or combine overlapping behaviors.
5. Multiple methods can contribute to the same behavior type; ordering and duplicate action attempts may not be obvious from source annotations.
6. Cast dispatch may be treated as server acceptance, causing premature success, retry spam, or starvation of lower-priority actions.
7. GCD, queue window, client latency, channeling, movement, facing, range, and LOS policies may be inconsistent between spells and classes.
8. Buff maintenance may preempt mounting, transport use, eating/drinking, quest interaction, or urgent combat movement.
9. Aura checks may ignore caster, stacks, remaining duration, immunity, dispel type, or debuff ownership.
10. Target-count queries may include irrelevant, crowd-controlled, unreachable, neutral, tagged, or non-combat units and cause incorrect AoE policy.
11. Cooldown stacking may be hard-coded without encounter duration, time-to-die, boss detection quality, trinkets, racial constraints, or immunity phases.
12. Healing/defensive thresholds may conflict with DPS rotation or cast slow heals while movement/combat risk makes them unsafe.
13. Resource conservation and consumable policy may be tuned for another expansion or endgame and perform poorly while leveling.
14. Pet, form, stance, rune, combo-point, totem, poison, disease, DoT, and proc state require class-specific deterministic coverage that is currently absent.
15. Unconditional or poorly gated `Thread.Sleep` remains in active Singular Shadow Priest behaviors and can freeze the behavior tick.
16. Commented-out legacy blocks increase audit ambiguity and can conceal which context implementation is truly active.
17. Routine performance lacks a decision trace explaining selected/rejected actions, so failed casts and DPS gaps are difficult to attribute.
18. Compilation success does not establish rotation correctness; each spec needs golden decision cases and live/simulated combat traces.

## Investigation hypotheses: Retribution Paladin

1. Divine Storm and Crusader Strike target-count gates cause direct DPS loss and dead GCD opportunities.
2. The priority order may not implement WotLK first-come-first-served behavior correctly across gear, set bonuses, glyphs, undead/demon targets, and execute range.
3. Hard-cast Exorcism can pause melee movement/auto-attacks or finish after a leveling target is nearly dead.
4. Exorcism retry throttling may mask a target/range/LOS/server-acceptance defect instead of reporting the actual rejected reason.
5. Holy Wrath may be attempted without correct creature-type, range, mana, or target-count value checks in some contexts.
6. Consecration target counts, positioning, mana cost, and expected target lifetime may make it a loss while leveling or moving.
7. Judgement choice may be fixed where mana, health, debuff ownership, party composition, or seal changes call for a different judgement.
8. Seal of Command, Righteousness, Vengeance, and Corruption selection may be wrong by level, faction, target count, target lifetime, or configured preference.
9. Re-evaluating seals every combat pulse can consume GCDs or oscillate near a target-count threshold unless selection has hysteresis.
10. Avenging Wrath/racial/profession cooldown logic may underuse cooldowns during solo play and overconstrain them to target count or boss classification.
11. Divine Plea, healing, and defensives may not consider Forbearance interactions, incoming damage, mana trajectory, target time-to-die, or movement safety consistently.
12. Pull and combat share one large composite; ranged pull behavior, movement-to-melee, and hard casts may contend.
13. Blessing selection may refresh during travel or choose party recipients in a way that delays the active route.
14. Level 33 needs its own decision fixtures because it does not have the same abilities or priority as level 80.
15. DPS improvement must be demonstrated with action timelines, idle-GCD rate, failed-cast rate, melee uptime, resource use, and target-type scenarios—not asserted from reordered code.

## Investigation hypotheses: architecture, testing, and evidence quality

1. Core source, installed runtime copies, packaged output, and generated binaries can drift; the canonical-source-to-runtime deployment path is not explicit enough.
2. Global/static managers create hidden dependencies and make isolated deterministic testing difficult.
3. Failure results are too coarse; callers need typed reasons such as unreachable, partial vertical, dynamic obstacle, missing mesh, transport required, canceled, loading, and invalid target.
4. Movement has multiple command producers but no explicit single-owner lease/arbitration protocol.
5. Tests are strongest around individual helpers and synthetic fixtures but weak at recorded multi-component replay.
6. Existing fixtures may encode historical behavior rather than desired game-correct behavior, as with low-level melee Exorcism.
7. Logs lack stable correlation IDs for route request, POI, behavior-tree decision, recovery episode, transport, and cast attempt.
8. Rate-limited prose logs are difficult to analyze statistically; structured sidecar events would improve replay and comparison.
9. There may be no durable mapping from a runtime symptom to its test, fix commit, PR, and validation capture.
10. Broad refactoring before evidence capture risks destroying the ability to reproduce and compare existing failure modes.
11. A single giant “navigation rewrite” or “all classes optimization” PR would be unreviewable and make regressions hard to bisect.
12. Live validation is inherently required for moving transports, native collision, injection timing, and real combat; offline tests must state what they cannot prove.

## Required causal/knowledge graph

The audit must create a directed, queryable graph instead of only a prose report. Use `/graphify --mode deep --directed` if available, or build an equivalent evidence graph. Keep structural facts separate from semantic inferences.

Required node types:

- symptom, incident, log event, session, map/zone, coordinate cluster, POI/destination;
- component, class, method, behavior, state, event, thread, plugin, bot base, routine;
- mesh tile/polygon reference, transport, blackspot, profile, setting, data asset;
- hypothesis, evidence item, counter-evidence, test, metric, fix candidate, risk, PR.

Required edge types:

- `calls`, `subscribes_to`, `emits`, `reads`, `writes`, `selects`, `owns`, `cancels`;
- `preempts`, `competes_with`, `invalidates`, `retries`, `replans`, `blacklists`;
- `observed_during`, `located_at`, `targets`, `produces`, `contributes_to`, `masks`;
- `supports`, `contradicts`, `requires_reproduction`, `tested_by`, `fixed_by`, `regressed_by`.

Every graph edge must carry provenance, confidence, and direction. Use `EXTRACTED`, `INFERRED`, and `AMBIGUOUS` labels. An extracted edge must cite a source line or log timestamp; an inferred edge must explain the reasoning and list a falsification check. Never turn proximity in time into causation without evidence.

At minimum, the graph must make the eight feedback loops in this document directly queryable and reveal high-centrality “god nodes,” high fan-in/fan-out managers, event cycles, shared mutable state, duplicate policy owners, and untested bridges between communities. Produce:

- interactive HTML for human exploration;
- GraphRAG-ready JSON;
- GraphML or equivalent for external inspection;
- a plain-language graph report;
- a wiki/index organized by detected communities;
- saved queries for Thunder Bluff, movement oscillation, cliff safety, pulse latency, SpellManager lifecycle, Wholesome quest ownership, and Retribution Paladin decisions.

Do not commit tool caches or a massive visualization blindly. Commit compact graph data/reports and document how to regenerate large interactive artifacts.

## Analysis discipline

For every high-priority issue, use this chain:

1. identify and deduplicate the incident;
2. reconstruct the timeline and participating owners;
3. trace log event to exact emitting code;
4. trace callers, state reads/writes, subscriptions, cancellation, and side effects;
5. state competing hypotheses;
6. find evidence that would falsify each hypothesis;
7. build the smallest deterministic reproduction or recorded replay;
8. measure baseline behavior;
9. choose the owning-layer fix and document rejected alternatives;
10. add a regression that fails for the reproduced reason;
11. implement one coherent change;
12. compare before/after metrics and live behavior;
13. link symptom → evidence → hypothesis → test → fix → PR in the graph.

This prevents circular analysis, premature mesh blame, and fixes that merely move a symptom between layers.
