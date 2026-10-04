# Continuous travel and special quest execution implementation plan

> **For agentic workers:** Use superpowers:executing-plans in the current isolated checkout. Retain all existing work and causal receipts.

**Goal:** Eliminate unnecessary stops at travel waypoints and moving-NPC updates, retire obsolete quest-stage ownership, and bind special quest actions to verified original-client source contracts.

**Architecture:** The shared travel owner retains one semantic journey while local path points and a selected NPC's coordinates change. Positive geometry permits a successor flight leg before the current leg ends; final landing, unsafe observations and replacement work retain their own stop authority. A source-bound per-objective execution catalogue distinguishes ordinary actions from item, gossip, event and scripted credit and is consumed by both scheduling and profile generation. Arelion's Mistress receives an explicit observed workflow derived from its pinned source.

**Tech stack:** C#, original WoW3.3.5a build12340, TrinityCore3.3.5/TDB335.25101 at95657f54779467effea8a1749a61ff93abc1d707; existing Windows/x86 and stock Lua5.1 fixtures; Python source audits.

**Spec:** User's October4 report and screenshot: all MoveTo travel is continuous/fluid, moving Magister Aledis must remain catchable, Arelion's Mistress is a lure/item quest rather than an ordinary kill, and all4335 dataset quests require source-informed special-behaviour vetting.

## Global constraints

- Preserve exact actor/process/executor/mover/run/profile/POI/subject identities, cancellation, unknown observations and separate command/acknowledgement contracts.
- Preserve incidental mounted escape; only observed unmount and supported ground release ordinary combat. Explicit quest combat retains its separate landing authority.
- Preserve user settings, data, older worktrees and every failed receipt. Never launch or stop the bot/game; deployment requires an idle target installation.
- Do not call a structural/source audit or supplied test counter proof of live quest completion. Audit membership is exactly4335 unique quest IDs.
- Current source is the isolated branch audit/next-fluid-travel-special-quests-20261004 at merged PR98 b5e735a0b12ca4160497000e4bb8256625844a65. Evidence is D:/Dev/CopilotBuddy-Evidence/fluid-travel-special-quests-20261004.

## Review focus

1. A reached intermediate waypoint must not be reissued or introduce a stop/idle pulse when a safe successor is available.
2. Same-NPC coordinate updates must retain the selected journey; wrapper, GUID, base, work, profile and provider replacements must still revoke it.
3. Preparing a later flight leg must not grant descent or interaction from incomplete or obsolete geometry.
4. Pickup acknowledgement can precede branch cleanup; the next objective must not inherit the old giver's POI or clear a successor's combat/service work.
5. A positive required creature ID is a credit identity, not sufficient proof of a death objective; special source scripts and missing executors must prevent generic fallback.

## Task 1: Continuous flight segments

**Files:** Styx/Logic/Pathing/GroundTransitionMachine.cs; GroundTransitionRuntime.cs; Flightor.cs; Tools/IndoorApproachRegressionTests/FlightJourneyCases.cs; Tools/WholesomeQuestRecoveryRegressionTests/FlightorWaitContinuityRegressionTests.cs.

**Interfaces:** Retain GroundTransition.Tick/TickTransit and the ownership APIs. Add an internal prepared successor-search boundary only if required to retain the current validated leg during incremental planning.

- [ ] Add causal tests asserting no extra MoveStop, no already-reached waypoint command, and no command gap when advancing safe local flight legs. Include multiple reached PolyNav points and a final point that is never skipped.
- [ ] Add deterministic simulated position/time advancement over a long route instead of relying only on teleporting to submitted points.
- [ ] Capture failing Windows/x86 receipts before production edits.
- [ ] Retain active flight while preparing/validating the next local leg, switch before arrival, and consume reached native queue points without selecting the dequeued old point.
- [ ] Verify final landing, missing geometry, reentrant owner replacement and previously green flight/routine gates.

## Task 2: Stable moving-subject journeys

**Files:** Styx/Logic/Pathing/GroundTransitionContext.cs; GroundTransitionRuntime.cs; CommonBehaviors/Actions/ActionMoveToPoi.cs if causally required; Tools/CombatPoiTransitionRegressionTests/ActualAcceptanceProgram.cs and boundary/project links.

**Interfaces:** Explicit retained-travel context uses BotPoi.CurrentWorkGeneration and the already available route lease; default one-action contexts keep exact raw generation and destination checks. Coordinate replacement retires only the predecessor route admission, then submits the new destination under the same journey.

- [ ] Add actual BotPoi/invalidator tests for pickup, turn-in and service travel against a moving selected NPC, including ground and airborne journeys.
- [ ] Assert stable owner, no coordinate-only MoveStop and prompt forward movement; retain exact semantic replacement and unmount acknowledgement tests.
- [ ] Capture causal failures, then implement the explicit journey lifetime and destination refresh under that lifetime.
- [ ] Run moving-POI, indoor, mounted-ground, dismount, collection and flight ownership suites.

## Task 3: Quest-stage handoff

**Files:** Bots/Quest/Actions/ForcedBehaviorExecutor.cs and the actually implicated pickup/objective owner; complete quest-execution fixtures in Tools/WholesomeQuestRecoveryRegressionTests.

**Interfaces:** Retire a completed stage's own residual quest POI only while the exact completed stage, actor, run and POI remain observed; successor combat, loot, service and external publication must retain ownership.

- [ ] Reproduce accepted Pickup advancing before its clear-POI branch runs, followed by an ordinary objective.
- [ ] Assert the old giver cannot remain the new objective's selected subject and no action is credited from navigation.
- [ ] Repair the causal publication/cleanup boundary and verify replacement, cleanup reentry and complete pickup/objective/turn-in sequences.

## Task 4: Source-informed execution catalogue and Arelion's Mistress

**Files:** New focused audit and catalogue modules in Tools/EvidenceAudit and runtime-snapshot/Bots/WholesomeAutoQuest-master; DataModels.cs, DataLoader.cs, QuestScheduler.cs and ProfileBuilder.cs integration; bound quest strategy/knowledge data; existing item/gossip/quest owners or a focused new custom behaviour.

**Interfaces:** One validated catalogue maps every dataset quest/objective to source-backed action requirements and an implemented executor or an explicit unresolved obligation. Scheduler and materializer consume the same decision. Catalogue bytes participate in the execution fingerprint and reject mismatched/duplicate/unbound records.

- [ ] Audit all4335 IDs against the pinned quest templates, item spells, conditions, SmartAI chains, C++ scripts, relations and acquisition sources; save exact per-member evidence and exclusive totals.
- [ ] Add tests where SpecialFlags is zero but source SpellHit/gossip/escort credit requires a special executor; absence or invalidity must not produce an ordinary kill.
- [ ] Derive quest9472's scroll23693/spell30077 and Viera17226 credit chain, including the actual9483/Cenarion Spirits prerequisite and observed relocation requirements.
- [ ] Implement the required workflow using existing strict action/quest ownership; test absent stock, acquisition, exact NPC selection, lure acknowledgement, moving chase, item targeting, delayed credit, cancellation and recovery without killing Viera or treating a request as success.
- [ ] Verify all catalogue members and all admitted recipe kinds; preserve explicit source/script/live limitations for any unimplemented compound contract.

## Task 5: Integrated verification and delivery

- [ ] Review the complete diff and match each user scenario to its actual-runtime causal test.
- [ ] Commit a clean candidate, run the complete canonical Windows/x86 gate, source/strategy/catalogue consistency checks and applicable native replay.
- [ ] Verify hosted artifacts and exact merged source identity, then stage the complete changed runtime population and rollback.
- [ ] Apply only to an idle authorized CB installation; preserve active processes and user data.
- [ ] Report roots, fixes, actual case totals, full4335 audit accounting and the precise remaining live/source limits.
