# Wholesome and host behavior verification frontier

Reference master: `8382a7ec05a64212ea0a237159dca427a0767425`. Combined follow-ups: `c14bd264da90bdd384ebd9bf2bcc8f7b7a9a8217`. This is an explicit plan/coverage ledger, **not a declaration that exhaustive verification is complete**. Direct bot-directory source reading is not transitive host verification.

## Scope and evidence levels

Inventory every active Wholesome entry point and each transitive host owner that reads game state, selects work, changes shared state or issues an external action. Record the exact symbol, source identity, callers, outputs, side effects and test owner. Mark dynamic calls/runtime-loaded components separately from semantically resolved edges. Distinguish:

- **Source-confirmed:** a contract/path is present in inspected code; its historical runtime effect may be unknown.
- **Reproduced:** controlled execution fails for the intended reason, before repair.
- **Regression-verified:** focused and affected combined tests pass on recorded repaired bytes.
- **Live-unverified:** original-client observation, geometry, timing or server acceptance is not established offline.

The earlier whole-repository census/graph and log incident work remain available; do not regenerate them without a changed-input reason. Use the frontier below to prevent a few passing fixtures from being mistaken for all behavior coverage.

## Behavior / host-owner matrix

| Behavior family | Main owners to trace | Existing verified scope | Unfinished verification and adversarial cases |
|---|---|---|---|
| Startup, settings and runtime loading | WholesomeAutoQuest; DataLoader; SettingsForm; source compiler; TreeRoot | Dataset identity/load failure, tracked-source compilation, central worker/start ownership | Every setting reaches its actual decision; missing/duplicate installed layouts; repeated starts; callbacks during load; stale settings; failed profile writes; compiled/runtime source identity |
| Work selection and eligibility | QuestScheduler; QuestSchedulingPolicy; quest log/cache; recovery manager | Carried-item ordering, per-point navigation evidence, deferral and missing offers | Full/partial quest log; prerequisites/chains; class/race/faction/level/reputation; repeatable/daily differences; cyclic/missing references; accept/abandon/turn-in during scan; unknown versus zero |
| Giver/ender relation and generated profiles | QuestScheduler; ProfileBuilder; PickUpNode/TurnInNode; forced behaviors | PR #35: 23 relation cases, including creature/object ID overlap and ancestor eligibility | Live object interaction; moving/despawned/respawned sources; type-aware persistent retry keys; XML escaping; profile generation failures; old plan executing after replacement |
| Pickup, dialog and completion | QuestPickupDialogPolicy; QuestPickupMismatchTracker; WholesomePickupMonitor; ForcedQuestPickUp/TurnIn | Existing missing-offer, stale-cycle, readiness and inventory fixtures | Dialog ownership changed to another NPC; delayed/duplicated events; log full; bag/reward space; unavailable reward; prerequisites change; stop during button activation; actual acceptance acknowledgment |
| Collection and inventory evidence | CollectItemObjective; quest cache; inventory/loot APIs; progress monitors | Required item already carried; unsupported unneeded source avoided; no invented whole-quest completion | Raw occupied slots versus cache materialization; bank/equipment/carried distinction; split/stack/mail/sell/consume changes; partial item counts; negative/wrapped data; shared item across multiple quests; incomplete snapshot |
| Loot and interaction | LootTargeting; interaction helpers; object manager; quest behaviors | Selected existing core/adapter regressions, not complete loot certification | Loot rights; locked/despawned corpse/object; inventory full; pickup failure; competition; item cannot fit; repeated no-credit interaction; stale GUID; dynamic LOS; confirmation before progress |
| Sale, repair and purchasing | Wholesome SellByQuality; MerchantFrame; Vendors; consumables; ProtectedItemsManager | PR #36: 23 exact-method sale cases and combined full-class compilation | Snapshot completeness; mutation between check and dispatch; one-stack acknowledgment; merchant disappears; cancellation during a batch; protected names/ranks/locale; funds; partial purchases; duplicate operations |
| Shared protection, mail and discard | CollectItemObjective lifecycle; ProtectedItemsManager; vendor/mail/discard callers | Sale forwarding of existing shared protection, not lifetime ownership | Overlapping owners; manual pre-protection; double dispose; exception cleanup; reset/reload atomicity; destructive caller bypasses; invalid/unknown item metadata; preserving public Add/Remove behavior |
| Vendor/trainer discovery and settings | VendorDataLoader; main Pulse/Root; enable flags; host selection | Existing endpoint backoff/typed travel outcomes | Installed master-suffix folder versus assembly-local data; disabled auto-vendor/train; explicit manual requests; faction/access; unavailable inventory; stale selected vendor; path cost versus XY distance |
| Rest, pause, stop, death and restart | WholesomeRestPolicy; main lifecycle; TreeRoot; death/progress monitors | Rest ownership, worker and shutdown suites | Rest flags/timeout/merchant flag after restart; pause while dialog visible; death/loading/taxi/transport during work; extension catches absorbing interruption; stale cleanup affecting a new run |
| Routes, obstacles and transports | Navigator/MeshNavigator; StuckHandler; elevator controller; profile transport insertion | Earlier typed terminal outcomes and lift safety/continuity regressions | Full route acquisition/cost; alternate ramps/lifts; unsupported exits; dynamic platform identity; node budget; partial-path cause; cancellation already inside native; measured total latency and live support |
| Group combat and support | GroupCombatSafety; shared cast/auto/pet/wand; Paladin support; roles/roster | Merged no-selection-only-pull, role tuple, support and cleanse cases | GUID-to-slot reorder; departed members; persistent AoE/active pets; all cones/chains/ranks; full target identity across yields; simultaneous aura ownership; protected encounter dispels; live outcomes |
| Special behavior dispatch | ObjectiveType adapters; scripted/escort/vehicle/event/item-use/profile behaviors | Existing modeled categories and limited adapter tests | Explicit supported/unsupported capability table; item-started quests; escort failure/respawn; vehicle exit; scripted progress; timers; faction/phase; quest-specific use targets; bounded fallback without false data quarantine |
| Data, performance and observability | Global/zone data; graph tools; structured events; plugin/BT cadence | Content fingerprints, 33 analyzers; selected route/timing results | Reconcile data conflicts individually; indexed lookup costs; scan/query budgets; memory/GC; uncensored p50/p95/p99; event storms; state/correlation IDs; fair scheduling and bounded retries |

## Highest-priority source findings still open

### W42: occupied quest slots can be lost during materialization

At master, `QuestLog.GetAllQuests` (`Styx/Logic/Questing/QuestLog.cs:101-110`) drops null results. `GetQuest:116-125` reads a nonzero quest ID and calls `PlayerQuest.FromId`; `Styx/Logic/Questing/PlayerQuest.cs:169-174` returns null when cache data is missing. Baseline blobs: `cdceb09dd4c3f2cfdff00beb761c6dbc5807a850` and `a13d62739f7c29863977ed38feac2eb1e4be2996`.

This establishes source information loss, not how often it occurs in play. A non-null returned list does not establish an empty/complete log. Sale protection and scheduler decisions must not treat omitted materialization as nonexistence. Required failing tests: occupied ID with missing cache, hydration later, empty real slot, replaced player, abandon/accept between reads, same-count different IDs, duplicate/invalid IDs and timeout. Preserve raw identities and an explicit completeness/version result; do not hide the issue with an arbitrary sleep or blanket invalid-data quarantine. No implementation is claimed yet.

### W43: item-protection release is not owner-scoped

`Bots/Quest/Objectives/CollectItemObjective.cs` adds protection during construction and removes it during disposal. `Styx/Logic/Profiles/ProtectedItemsManager.cs:157-175` forwards Add/Remove to a shared set (blob `86e4e0a51d902be26491ef2179107322768eb787`). Reproduce two simultaneous collectors, pre-existing runtime/manual protection, both disposal orders, repeated disposal and shutdown. A new scoped lease must compose with the existing public set API, not reinterpret every old Add as reference counting. Follow every destructive consumer after changing ownership.

The same manager's reload clears state before all input files have parsed. Investigate atomic publication under malformed/missing files and cancellation. Source ordering is confirmed; no regression result is asserted for this path yet.

### W44-W46: discovery, settings and lifecycle candidates

VendorDataLoader's search paths differ from DataLoader's installed master-suffix handling; an assembly-local data file is relevant counterevidence, so reproduce actual runtime layouts. Auto-vendor/auto-train flags are exposed in UI but their execution ownership needs verification. Recovery lifecycle reset does not clearly reset all rest/merchant flags, and some extension catches can absorb interruption. Trace active call paths and add failing fixtures; do not claim all these caused an observed incident.

## Refactor decision and execution rules

Prefer compatible, explicit observation/intent/action contracts: complete state snapshots, owner-scoped protection, typed route/work outcomes, single mutation authorization and acknowledgment. Do not rewrite every class merely to call the work a refactor. Separate shared host contracts from Wholesome adapters; preserve legitimate solo pulls and manual commands. Each slice must include counterexamples that preserve working behavior.

For each matrix row: name uncovered branches, reproduce priority failures using real owners with controlled external boundaries where necessary, verify red, repair at the owner, verify focused and combined tests, and add source/test/PR provenance to the directed graph. Use seeded state-sequence tests where ownership evolves over time. Report test-harness limitations and swallowed exceptions explicitly. Benchmark efficiency before and after rather than reporting operation counts as gameplay improvements.

New follow-up repairs remain in review branches. Keep a fresh small checkpoint at every completed slice; do not repeat the multi-hour broad audit after a chat failure. Original WoW 3.3.5a/12340, Lua 5.1 schemas and x86 boundaries remain mandatory. Meshes do not provide live attachment, collision, timing or server acceptance. Comprehensive offline evidence and supervised acceptance are separate completion gates.
