# Navigation lifecycle repair implementation plan

> **For agentic workers:** Use superpowers:executing-plans. Work is inline in the existing isolated worktree; workers are disabled. Checkboxes record completed work, not intentions.

**Goal:** Complete the user's end-to-end quest travel, takeoff, combat interception, dismount and corpse-loot repair, including failures observed after PR #100 was installed.

**Architecture:** Keep a chosen journey and its exact owner through preparation, mount acknowledgement, takeoff, flight continuation, landing and observed dismount. Resolve the active quest leaf through selected conditional orders so required combat preempts travel. Ground chase, flight and loot must acquire the appropriate shared transition before issuing their effects.

**Tech stack:** Existing C# host and runtime sources; .NET Windows/x86 regression executables; original Lua 5.1 and retained native mesh fixtures.

**Spec:** The user's complete opening instruction in COS_CONTEXT:19642. Preserve its full travel/navigation/mount/combat/loot/hostile-base scope. Earlier implemented coverage is recorded in `docs/audit/2026-10-04/team-combat-quests/REVIEW.md` and `docs/audit/2026-10-04/fluid-travel-special-quests/REPORT.md`; those reports do not establish live acceptance of this new log append.

## Global constraints

- WoW 3.3.5a build 12340; TrinityCore 3.3.5 primary and AzerothCore WotLK secondary. No Classic/Retail substitutions.
- Preserve current process, executor, actor, mover, run, profile, POI, recipient, provider and effect ownership. UNKNOWN never authorizes native effects or success.
- Preserve fluid waypoint continuation and safe faction-aware full-segment routing; do not discard previous repairs or valid user working-tree changes.
- Use production logs frozen under `D:/Dev/CopilotBuddy-Evidence/navigation-final-review-20261004/`; the latest 1651 log is SHA256 `cceff5b428e2fa8945c9809dcc6203b10033ee61ac1e975127f8fef655fe2a56`.
- Worktree starts clean from merged master `624077a48f5b94d60ae1a05098b83726cb808beb`, whose tree matches installed candidate `9e225965bb045b7f372890dd58508a64ed6960e9`.
- Do not manipulate the running game or bot to manufacture acceptance. Source tests and an installation/startup receipt are distinct from a live traversal result.
- Use session_finish only after implementation is complete at the user's requested final-verification boundary.

## Review focus

- Mount acknowledgement can change ground speed and lag behind CanFly flags; preserve departure intent while rechecking capability and takeoff geometry.
- An active objective can be inside If, ElseIf, Else or While; only the currently selected, still-owned leaf grants combat authority.
- A flying actor cannot establish an unreachable ground target by asking a ground pathfinder to start in mid-air.
- Support and flight flags can change on different pulses during descent; prevent climb/descend oscillation without treating a mount-removal request as acknowledgement.
- A corpse loot window can arrive promptly, after a server delay, or during owner replacement; advance on observed state and keep stale events from completing new work.

## Task 1: Preserve the flight departure and landing lifecycle

**Files:** `Styx/Logic/Pathing/GroundTransitionMachine.cs`, `GroundTransitionRuntime.cs`, `Flightor.cs`; actual linked tests in `Tools/IndoorApproachRegressionTests`, `Tools/GroundTransitionRegressionTests`, and `Tools/WholesomeQuestRecoveryRegressionTests/FlightorWaitContinuityRegressionTests.cs` plus travel-cost fixtures.

**Interfaces:** Continue using `GroundTransition.Tick`, `GroundMotion`, and the owned Flightor movement boundary. Explicit phase reporting must distinguish preparation/mount/takeoff from airborne continuation; effect submissions never fabricate movement acknowledgement.

- [x] Reproduce the Ikeyen sequence: choose flight, observe mounting, change run speed/economics before flying, remain grounded for several ticks. Assert no ground CTM, no unnecessary mount removal, and continued bounded takeoff.
- [x] Reproduce supported descent with a lagging flying flag and small landing drift. Assert no climb to the approach waypoint and no airborne dismount.
- [x] Cover failed takeoff, missing support/geometry, capability loss, cancellation and reentry; run the tests against the unchanged production source and retain causal failures.
- [x] Implement departure retention and explicit acknowledgement/recovery using the shared owner; preserve final interaction and route predicates.
- [x] Run the focused journey, transition, flight-owner and geometry suites. Record case counts and source hashes.

## Task 2: Make required combat preempt every active quest wrapper

**Files:** `Bots/Quest/QuestBot.cs`, the selected-order resolution boundary in `Bots/Quest`, `Bots/Grind/LevelBot.cs`, `Bots/Grind/Levelbot/Actions/Combat/ActionMoveToTarget.cs`, and focused mounted/roam/chase fixtures.

**Interfaces:** Resolve only selected `ForcedIf.ActiveOrder` / `ForcedWhile.ActiveOrder` ancestry. `HasRequiredCombatTarget`, `IsRequiredCombatObligation` and acquisition continuation must agree on the same leaf and root lifetime. Mounted required work must enter `MountedCombatTransition`; generic ground chase must not run from an unsupported aerial position.

- [x] Reproduce the logged If-wrapped Marsh Dredger objective in the actual published quest root with retained hotspot execution. Add While/nested/replaced-wrapper controls.
- [x] Assert that an airborne chase does not query ground reachability, blacklist the mob, or alternate movement with hotspot travel; retain ordinary grounded pursuit and target-range policy.
- [x] Repair selected-leaf resolution and shared chase admission, then prove landing, one pending dismount, observed unmount, combat, and return to quest work.
- [x] Run mounted, roam, chase and actual owner integration suites; retain reentry/stale-owner/UNKNOWN controls.

## Task 3: Remove measured corpse-loot delay

**Files:** Actual corpse-loot selection, interaction and event/acknowledgement owners in `Bots/Grind/LevelBot.cs`, `Styx/Logic/Inventory/Frames/LootFrame`, and their focused existing regression fixtures. Add a standalone runner only where it reuses the actual owner tests.

**Interfaces:** A loot attempt owns one corpse/POI/frame. Combat preempts it; successful slot requests do not establish quest credit. Use observed stop/frame/slot state instead of unconditional delays or repeated stale corpse reacquisition.

- [x] Correlate kill, corpse selection, approach, interaction, loot-open, slot dispatch and final-clear timestamps from the frozen logs; separate combat and plugin time from avoidable loot waits.
- [x] Reproduce each identified delay with actual tree/event owners before modifying production code.
- [x] Repair the causal waits or stale handoff, preserve bounded timeout and exact-recipient checks, and run loot/collection/quest handoff suites.

## Task 4: Integrate and verify the complete subsystem

**Files:** `docs/audit/2026-10-04/navigation-lifecycle/REPORT.md`; evidence receipts under the dedicated navigation review directory; release integration files only as needed.

- [x] Review the full production diff and the original request, including hostile-base clearance, moving NPC updates, mesh/tile continuation, and all adjacent transitions.
- [ ] Run the canonical Windows/x86 integrated gate on settled source; run native route/catalog/routine checks required by the release contract.
- [ ] Record exact source, binaries, tests, failures repaired and live proof limits. Perform authorized GitHub/release work using the actual verified candidate and current installation state.
- [ ] Call session_finish at the requested boundary, complete any delivered extension, and leave a self-contained final report.
