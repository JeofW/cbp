# Team combat and quest execution audit implementation plan

> Execute inline with superpowers:executing-plans. Preserve all prior work and failing receipts.

**Goal:** Complete the user's October 4 audit of team-aware Consecration, protected instance players, moving quest NPC interaction, mounted objective combat, and broader quest execution.

**Architecture:** Keep shared combat safety separate from a particular bot's engagement policy, and check harmful effects at both selection and submission. Retain the existing semantic travel owner while a selected NPC moves; transition to observed interaction preparation at usable range, then revalidate identity, range and sight before submission. A legitimate quest kill obligation must acquire the existing ground combat owner before another hotspot can replace it. All quest execution remains bound to the source catalogue and observed credit.

**Tech stack:** C#, Windows/x86 host; WoW 3.3.5a build 12340; TrinityCore 3.3.5/TDB335.25101 at 95657f54779467effea8a1749a61ff93abc1d707; existing deterministic C#/Lua 5.1 and Python gates.

**Spec:** The complete original user request and opening instruction in this session. The user requires autonomous repair, regression tests, integration and verification for all listed scenarios; no further implementation authorization is required.

## Global constraints

- Baseline is merged master 85714dc9935c7c4bb8b59f9b21bc24ee5888d647. Worktree D:/Dev/CB-RegressionAudit-20261003, branch audit/next-team-combat-quests-20261004. Evidence D:/Dev/CopilotBuddy-Evidence/team-combat-quest-audit-20261004.
- Preserve continuous travel, exact actor/process/executor/mover/run/profile/POI/recipient ownership, UNKNOWN semantics, cancellation and action-versus-acknowledgement distinctions.
- Party/raid membership survives hostile reaction changes. All player targets in dungeons and raids are protected from harmful combat, including secondary area recipients. Friendly support remains usable.
- Consecration uses legitimately engaged nearby enemies, including group threat; selected targets alone do not prove engagement. Retain configured count, recovery mana, crowd-control and fresh-state checks.
- Do not launch or stop the client/bot, change user settings, reset/clean unrelated work, or deploy into an active installation. Never equate controlled tests with live server acceptance.
- Quest membership is exactly 4,335 unique dataset IDs. Primitive candidates and unresolved source/handler obligations must not be described as implemented or live-proven quests.

## Review focus

1. A moving NPC can cross a coordinate between native queries without changing its identity; tests must advance coordinates during those queries, not only between ticks.
2. A hostile controlled player can be excluded by ordinary pet/owner filters yet still be hit by AoE; hazard checks must precede those filters.
3. A target can change after spell preparation, facing, targeting, dismount or POI publication; no predecessor may authorize its successor.
4. A current hotspot can retain a running travel child while a required target becomes available; test the complete handoff, not merely the target predicate.
5. A special quest can have an ordinary-looking creature objective but receive credit from a script or spell; missing contracts never authorize an ordinary kill fallback.

## Task 1: Recover baseline and diagnose complete logs

- [x] Verify clean isolated source, merged/deployed receipts and applicable instructions.
- [x] Snapshot and index every October 3/4 production log, preserving hashes and full lines.
- [ ] Correlate the Mahuram interval, hotspot/combat transitions, repeated observation failures and scheduler stalls with actual code and test coverage.
- [ ] Run baseline GroupEngagementRegressionTests, ConsecrationStudyFocusedTests and CombatPoiTransitionRegressionTests under x86 and retain receipts.

## Task 2: Team-aware area decisions and protected combat recipients

**Files:** Styx/Logic/Combat/GroupCombatSafety.cs; actual shared targeting and spell submission boundaries implicated by the audit; runtime-snapshot/Routines/Singular wotlk/Helpers/Unit.cs; existing group, target, spell and Consecration fixtures.

**Interfaces:** Retain MayAttack(WoWUnit), IsEngagedWithGroup(WoWUnit), IsCombatActionSafe and existing action ownership. Add one shared protected-recipient predicate only where its callers need to distinguish friendly support from harmful combat.

- [ ] Add causal cases for charmed party/raid players, all instance bot modes, raid-only map flags, hostile area recipients with owner fields, target replacement, and friendly healing controls.
- [ ] Link actual engagement policy to actual area enumeration/Consecration selection and prove that group-threat mobs count without player aggro.
- [ ] Capture failing assertions, repair the shared selection/submission and area gates, and run adjacent target/autoattack/rotation suites.

## Task 3: Moving NPC pursuit through actual interaction

**Files:** Styx/Logic/Pathing/GroundTransition.cs, GroundTransitionRuntime.cs and existing context/machine only where required; CommonBehaviors/Actions/ActionMoveToPoi.cs and quest interaction callers if needed; actual CombatPoiTransitionRegressionTests and runtime submission fixtures.

**Interfaces:** Retain GroundTransition.Tick, CanInteractWith and TryInteractWith. Preparation may stop only an owned final approach with observed usable range; submission still requires fresh range/sight, exact recipient and grounded state.

- [ ] Reproduce moving actor/NPC positions during native queries, assert bounded pursuit-to-interaction and no distant/intermediate stops.
- [ ] Cover pickup, turn-in and service consumers, absent LOS, lost range, vertical displacement, replacement wrappers and delayed native/UI acknowledgement.
- [ ] Capture causal red, repair the readiness/preparation deadlock, and verify flight continuity, ground ownership and interaction suites.

## Task 4: Mounted quest obligations and broader execution families

**Files:** Actual quest/grind root, target selection and retained travel owners established by log correlation; existing MountedHotspotTargetRegressionTests, MountedCombatRegressionTests and MountedGroundIntegrationRegressionTests; Tools/EvidenceAudit and Wholesome catalogue/scheduler/profile execution if causally required.

**Interfaces:** Use MountedCombatTransition.TickCurrent/TickExplicit and existing semantic Kill POI ownership. Required objectives preempt hotspot transit; incidental mounted aggro retains travel policy.

- [ ] Reproduce required mobs becoming available during running hotspot travel, including airborne and ground-mounted actors; assert a retained Kill obligation, safe landing/unmount, attack only after acknowledgement, and travel recovery afterward.
- [ ] Audit all 4,335 source contracts and execution families, retaining an exclusive per-quest ledger and unresolved obligations.
- [ ] Add causal regressions for each substantiated systemic quest or log defect and repair shared paths rather than quest-specific exceptions.

## Task 5: Integrated verification and delivery

- [ ] Review the complete diff against every user scenario and test limitations.
- [ ] Commit a clean candidate; run complete canonical Windows/x86 integration, source/catalogue/strategy closure and applicable native replay.
- [ ] Complete authorized GitHub integration and prepare/verify deployment with rollback only while the production installation is idle.
- [ ] Use session_finish only after implementation is complete and final verification is underway; finish any new work before reporting.
- [ ] Record exact source/build/evidence identities, tested totals, remaining source/live limits and actual delivery status.
