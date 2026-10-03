# Travel priority and mounted quest continuation implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement the tasks below in the current isolated audit checkout.

**Goal:** Prefer eligible Outland flight, promptly retry mount preparation after interactions, and keep mandatory quest travel moving through incidental mounted aggro until an observed dismount releases combat.

**Architecture:** Retain the existing strict shared GroundTransition owner. Distant interaction journeys use collision- and mesh-supported local flight legs; those legs cannot acknowledge arrival or request descent. The existing complete ground approach remains responsible for final landing and interaction. Mount retry distinguishes rejected preparation from an actual outstanding submission. Mandatory quest admission must agree with the combat root's mounted travel policy.

**Tech stack:** C#, .NET 10, Windows/x86, original WoW 3.3.5a build 12340; existing deterministic native/client boundary fixtures.

**Spec:** The current integrated travel request and the user's October 3 correction: incidental aggro does not authorize voluntary combat dismount; explicit quest combat may land, and client-observed mount loss releases combat only on supported ground.

## Global constraints

- Preserve exact actor, process, executor, run, profile, POI, subject and navigation ownership and cancellation behavior. Missing observation remains UNKNOWN.
- Keep existing flight eligibility and original-client companion semantics. Do not replace them with Classic/Retail APIs or settings changes.
- Preserve useful mounted ground travel, bounded native work, and submission/observation separation.
- Preserve the installed runtime, settings, logs, older worktrees and failed receipts until a verified authorized release exists. Never launch the game for tests.
- Baseline: aa6f1fa4dd90ae45c8596bcf94702cc8c8a38d65; tree matches merged PR97, 5e993c98cf3a54e35321f1a616de9daa199bdb5c. The attempted new branch transaction was provider-blocked twice; continue independent local work in the existing isolated checkout.

## Review focus

1. Remote collision misses must not force a complete long journey to ground when a positively observed local flight leg exists.
2. A vendor awning can block takeoff even when IsOutdoors is true; ground departure must reconsider eligible flight after leaving cover.
3. An unsubmitted mount must not inherit the timeout for an actual pending native request; actual requests must remain bounded and single-owner.
4. Mandatory pickup/turn-in travel and combat admission must agree during one or several incidental attackers, including a forced aerial mount loss.
5. Local flight progress must never authorize final descent, wrong-floor interaction, unknown mesh areas, successor input, or completion from a command.

## Task 1: Rejected mount preparation

**Files:** `Styx/Logic/Pathing/GroundMountRequest.cs`; `Tools/GroundTransitionRegressionTests/GroundMountRequestTests.cs`.

**Interface:** Preserve `GroundMountRequest.Waiting(...)`. An actual submitted request retains the existing observation timeout and retry throttle. A rejected preparation becomes eligible after one second; a later observed mount clears the old preparation delay.

- [x] Add causal assertions for rejected admission then eligibility, bounded repeated rejection, and prompt remount following an observed mount/removal.
- [x] Run the Windows/x86 GroundTransition suite, retaining assertion failures before production edits.
- [x] Change retry accounting only at the submission/observation boundaries; keep cancellation and current-owner checks.
- [x] Verify the full suite including existing pending-request and revocation cases.

## Task 2: Local flight progress and safe departure

**Files:** `Styx/Logic/Pathing/GroundApproachGeometry.cs`, `GroundTransitionMachine.cs`, `GroundTransitionRuntime.cs`; existing GroundApproach, GroundTransition, IndoorApproach and flight ownership suites.

**Interface:** Extend the internal approach plan with an explicit progress-only role. Search near the actor for distant destinations, retain local support/mesh/liquid/clearance checks, and renew the plan after observed waypoint arrival. Only an ordinary final approach can descend. Preserve the complete onward path requirement at final arrival.

- [x] Reproduce both reported routes with collision observations limited to the actor's local region and a partial distant mesh path.
- [x] Add complete journey cases: multiple observed flight legs, no descent at intermediate waypoints, final indoor landing, newly blocked/UNKNOWN geometry, destination/owner replacement, and useful journeys longer than the landing deadline.
- [x] Prove covered departure and later flight eligibility without a permanent ground choice or repeated mount/dismount oscillation.
- [x] Implement the smallest bounded staged search and mode reconsideration using the existing owned flight and mesh paths.
- [x] Run all affected geometry, runtime, flight ownership and navigation suites.

Review additions: the final exterior ground handoff is protected from renewed flight selection; native terminal End/zero-area metadata is resolved only by a positive endpoint query; a ground-to-flight upgrade uses one supported, out-of-combat removal and separate acknowledgement. Causal receipts for each addition are retained.

The final moving-origin review adds a bounded stop/observation phase before an eligible flight cost decision, with ground fallback after an unacknowledged stop and immediate cancellation for incidental combat. Four causal cases and six affected suites pass in `focused-moving-flight-review1`; the flight-journey population is17.

## Task 3: Mandatory travel and combat ownership

**Files:** `Bots/Quest/QuestLootHandoff.cs` and any causally implicated mandatory/service caller; `Tools/MountedCombatRegressionTests` and actual mounted-ground integration suites.

**Interface:** Keep `CanRunMandatory(ForcedBehavior)` bound to the selected exact stage. Allow the already-owned mounted quest journey to remain scheduled during incidental aggro, while loot and interaction remain separately guarded. Observed mount loss lets the existing combat root preempt travel.

- [x] Add failing tests linking the actual mandatory gate, PublishedQuestRoot and mounted combat implementation, with controlled external observations only.
- [x] Cover pickup/turn-in, one and multiple attackers, pet-only threat, forced ground/aerial dismount, committed Kill, and selected-stage/POI replacement through the new mandatory cases and retained mounted combat suites.
- [x] Repair the contradictory admission gate and moving-NPC search veto without authorizing incidental voluntary dismount or weakening final interaction/ground combat checks.
- [ ] Verify the focused suites and the complete canonical Windows/x86 release gate against a stable source identity.

## Completion

- [ ] Review the integrated diff and causal receipts, update this plan and the audit checkpoint, and report exact source/test identities.
- [ ] Publish/deploy only through available authorized actions after matching release verification; report any actual tool blocker precisely.
- [ ] Use session_finish only after implementation is complete, then finish final verification and any delivered corrections.
