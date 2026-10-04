# Quest navigation, mounted combat and corpse collection

## Scope and observed failures

This change completes a further causal review of the user's full navigation/mounting/dismounting/looting request against the production log appended after PR #100 was installed. Baseline: merged master `624077a48f5b94d60ae1a05098b83726cb808beb`, whose complete tree equals installed candidate `9e225965bb045b7f372890dd58508a64ed6960e9`. Target: WoW 3.3.5a build 12340. Earlier startup verification did not establish subsequent quest travel acceptance.

Frozen evidence is retained under `D:/Dev/CopilotBuddy-Evidence/navigation-final-review-20261004/`. The latest log `2026-10-04_1651_1816.log` has SHA256 `cceff5b428e2fa8945c9809dcc6203b10033ee61ac1e975127f8fef655fe2a56`. Its later Wholesome session, rather than its Combat Bot startup, contains the Ikeyen and Marsh Dredger failures.

At 17:28:15, travel to Ikeyen selected flight. After Tawny Wind Rider mounted, the changed run speed made the next comparison select ground movement before takeoff. At the subsequent stall the solid surface was at Z=19.276442, while a liquid plane below it was at Z=18.358114. The old support test rejected any liquid hit, including buried water underneath solid terrain. A separate landing trace had supported contact while the Flying flag was still set and a small horizontal drift; the old narrow descent-column test sent the actor back up toward its approach point.

At 17:31 the active kill objective for Marsh Dredger was inside an `If`. Required-combat detection cast only the outer order, missing the active objective. Ordinary pursuit then asked the ground navigator to start in mid-air, blacklisted mobs for ten minutes, and competed with hotspot flight. These were separate lifecycle failures with a shared effect: the currently actionable work did not own the correct movement transition.

The frozen `2026-10-04_1220_40964.log` also shows optional Kings buffing after a corpse was selected, fixed stop-delay code, and completed corpses being selected again while their loot flags lagged. For example, a corpse selected at 12:33:24.306 was delayed by Kings at 12:33:27.645 before interaction at 12:33:28.830. After slot dispatch and cleanup at 12:33:29.332, it was selected again at 12:33:30.282. Plugin delays and server loot-window acknowledgement are separate from these avoidable waits.

## Architecture and resulting behavior

The existing shared `GroundTransition` remains the movement owner. A selected and geometrically admitted flight now survives changed mount economics through mount acknowledgement and takeoff. Current flight capability is still checked. Flightor does not build or submit an aerial waypoint as ground movement until the client reports lift-off. Its shared journey retains finite progress/recovery limits. Diagnostics distinguish mount pending, takeoff pending, airborne travel, landing contact and dismount acknowledgement.

Support checks compare solid and liquid hits in the same observed column. A liquid plane beneath solid support does not make that footprint wet. Water at or above the surface remains a rejection; invalid observations remain unavailable. Supported contact during descent waits for the Flying flag to clear without climbing back toward the approach waypoint or removing a mount in the air.

`QuestExecutionSelection` captures the active leaf and every selected `If`/`While` ancestor without evaluating another branch. Required-target lookup and target acquisition retain the same order, node, behavior, run and profile. Replaced ancestry cannot lend authority to an earlier target acknowledgement. Required mounted combat preempts a running hotspot and uses the existing shared approach/landing/dismount owner. Both ordinary and custom roaming pursuit use the same physical ground-admission rule; aerial/falling/transport/unknown movement cannot create a ground-path failure or consume an old chase timeout.

Ready corpses preempt retained quest movement on its next pulse. Optional pre-combat buffs yield to collection; recovery and actual combat retain priority. Corpse movement uses the same collection approach owner as GameObjects when mounted or airborne. The shared context narrowly admits the exact dead source for Loot/Skin without admitting dead combat targets. Revived, replaced or cancelled corpses retire their pending movement. Ordinary unmounted corpse looting, including stationary swimming, avoids an unnecessary ground-projection round trip.

The loot interaction issues stop once and waits for the observed stop, with a bounded timeout, instead of sleeping for lag. Interaction entry rechecks the exact actor, mover, process/executor, run, profile, source, POI generation, position, range and physical state. A denied native attempt is not treated as an interaction acknowledgement.

The old global loot counters, empty global chat-loot listener and separate raw corpse descent branches are removed. Timed source-bound receipts suppress rapid reselection of a completed corpse and allow other available corpses to proceed. Unacknowledged attempts receive bounded retries instead of a speculative long blacklist. Source/run replacements invalidate those receipts; dispatch receipts never establish inventory or quest progress.

Loot count observations are strict. Missing, malformed, negative or out-of-range counts do not become successful empty frames. Slot and close commands use the existing observed Lua transport's final native-entry predicate. A foreign frame never inherits close authority. The eighteen-record guard matches the retained original-client memory layout and the original-client analysis recorded in TrinityCore discussion 28157 (July 2022); it is not a Classic API assumption. Successful script return remains distinct from item delivery.

## Preserved adjacent contracts

The earlier fluid-waypoint, tile/mesh continuation, moving-NPC pickup/turn-in and route-safety changes remain in place. The aerial policy derives known opposing settlements from pinned original-client faction/TDB data, adds its planning clearance, checks whole segments and smoothing, and includes faction/exclusion revisions in route ownership. Unsafe or incomplete detours cannot fall back to a direct shortcut; the existing inside-exclusion escape policy remains covered. Its 80-yard planning margin is not a promise about every realm's guard behavior.

The full gate includes the original takeoff/water/indoor/moving-recipient, mount ownership, hostile-route, recovery, combat and quest-collection fixtures. The new repair does not claim that an emitted movement request, dismount request, loot event or local script return proves physical arrival or quest credit.

## Causal verification and limits

Failure receipts were retained before the repairs: `causal-red1` through `causal-red6`, plus the actual corpse-context failures in `repair5`. Fixture setup corrections are recorded separately from production assertion failures. The latest focused results include 49 shared transition cases, 25 actual flight journeys, 12 layered-support cases, 369 Flightor ownership/wait cases, 204 roaming ownership cases, 82 full chase cases, 23 required quest-combat cases, 8 loot-priority cases, 393 actual loot-handoff cases, and 35 actual collection/ground-transition integration cases. The existing mounted-ground integration remains 19/19.

The review was performed inline because this session has workers disabled. It included the complete production diff and actual callers; it is not an independent second-agent review. Native action and world observations are controlled in the deterministic fixtures. The Windows/x86 complete gate, native mesh replay, exact 4,335-quest closure, hosted checks and release identity are recorded outside the source tree against the final committed candidate. Their receipts determine release acceptance; this report does not substitute focused totals for that gate.

No post-repair live Ikeyen/Marsh Dredger traversal is claimed. A successful build, installation or startup must remain distinct from live route and quest completion. AutoEquip2 and other plugin stalls seen in the input logs are not attributed to repaired loot waiting and remain outside this movement/collection repair.

## Resumed integration

The initial complete candidate `bd1f3590` did not pass release acceptance. Its retained run exposed aggregate source normalization, a stale standalone loot fixture, and omitted collection POI values in the world-query fixture. Source also changed during that run, so its successful stages are not a stable release gate. The resumed integration carries the current loot scenarios into the standalone fixture while retaining its independent POI generations, address identity and stricter admission controls.

The complete aggregate then exercised two additional dependencies: its isolated combat region needed an explicit collection-priority observation, and the network-to-flight fixture expected horizontal travel before supplying lift-off. The latter now checks both pending and separately acknowledged lift-off across all six original network/readiness cases. Optional-buff tests retain recovery priority and test a newly available corpse during a yielded buff.

The actual quest-root fixture also previously inherited the global loot provider's observation from earlier groups. An unobserved provider is not a known-empty list, so the root correctly deferred. Each root fixture now owns an explicit empty collection observation and restores the previous provider. A separate negative case preserves deferral when that observation is unavailable. The diagnostic runner's behavior assertions passed after this correction, but its standalone shutdown failed with a garbage-collected-delegate callback; that diagnostic executable and its failure remain in the evidence directory. Release acceptance uses the canonical aggregate process and its normal exit result, not the diagnostic executable's assertion totals. No production guard was weakened to repair these fixtures.
