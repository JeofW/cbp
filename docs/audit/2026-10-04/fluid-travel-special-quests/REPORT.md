# Continuous travel and source-specific quest execution

The reported pauses were regressions in PR98, not an intended delay. Its local flight handoff explicitly stopped movement after reaching each short leg. Its tests required a later continuation but permitted that stop. The new tests require continuous movement and reject both intermediate stop commands and idle simulated frames.

The same release also retained contradictory ownership rules for a walking NPC and could discard a nested pickup during profile refresh without disposing it. Separately, the dataset's `KillMob` label for quest9472 was treated as a death instruction even though the pinned source awards that credit from a spell hit. This repair covers those boundaries as one integrated travel and execution change.

## Captured production evidence

The diagnosed installation was PR98 merge `b5e735a0b12ca4160497000e4bb8256625844a65`, with installed host SHA256 `650b95da266068b92807e0676bcd4655386d270f2b9e64e0436fbc5e52f23344`. The user supplied `2026-10-04_0842_28344.log`; its frozen snapshot, configuration hashes and parsed observations are in `D:/Dev/CopilotBuddy-Evidence/fluid-travel-special-quests-20261004`.

Thirty-three recorded local-flight handoffs had continuation gaps from approximately0.45 to1.25seconds, averaging0.85seconds. During the walking Aledis20159 chase, ordinary coordinate updates repeatedly replaced a non-combat travel owner. After pickup, the execution diagnostic for quest9472 still reported Carinda16793 as a `QuestPickUp` POI, even though the new objective's credit entry was17226.

## Continuous movement and stable journeys

`GroundTransitionMachine` prepares a successor before the current local flight waypoint is reached. The runtime validates the next bounded segment while retaining the current route. A ready successor is submitted in the same pulse without `Hold()` or an extra idle tick. If future geometry is unavailable, movement stays bounded by the independently validated current endpoint; no route beyond unknown geometry is invented.

`Flightor` now consumes already reached PolyNav vertices and selects the next queued vertex. Previously it reused the vertex returned by `Dequeue`, so it could command a point the character had already reached. The final exact destination remains in the queue and is not skipped.

The shared journey context uses semantic work identity for both ground and air travel. Updates to the same selected NPC's position retain that journey and its route lease. Wrapper, GUID, base address, selected work, process, executor, profile and provider replacement still revoke it. Landing geometry is a separate observation and is not silently moved to a new endpoint. Coordinate refresh retires the previous mesh predicate without issuing a movement stop.

Optional mount-cost review is now observational. Ordinary motion invalidates an old cost-path length, not the entire ability to compare travel modes: flight economics can use a current-position straight-distance lower bound. Stopping remains part of an actually selected mount preparation, interaction or safe final landing, rather than a recurring cost query. Mounted escape during incidental combat remains intact.

## Quest-stage retirement

`QuestState.InitializeFromProfile` now retires the complete previous behavior lifetime before publishing another order. `QuestOrder.RetireCurrentBehavior` detaches the old selection before stopping/disposal callbacks; nested `ForcedIf` and `ForcedWhile` owners retire their children. Reentrant profile publication cannot be erased by the predecessor.

Pickup POI cleanup now requires the exact POI instance published by that pickup. Matching quest/NPC values alone do not let an old stage clear a same-quest successor. Tests reproduce real profile replacement during the nested accepted-pickup state, rather than calling `Dispose()` directly and assuming that production always does so.

## All4335 quests: mechanism audit and shared execution policy

The source audit covers every unique quest ID and all5771objective rows of the **effective repaired dataset**. It uses the actual repair loader, not the raw import alone. The inputs include the pinned original quest templates/addons, items, conditions, vendor and relation rows, SmartAI credit/event chains, and indexed C++ script ownership. The primary source is TrinityCore3.3.5/TDB335.25101 at `95657f54779467effea8a1749a61ff93abc1d707`; fullSQL SHA256 is `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`.

| Exclusive quest category | Quests | Meaning |
| --- | ---: | --- |
| PrimitiveCandidate |3729|Source mechanism can use an existing primitive, subject to its live data, navigation and action guards.|
| ImplementedStrategy |3|Validated strategy contracts cover9066,9447and9472.|
| HandlerOrSourceRequired |603|At least one objective has a specific unresolved handler, source or data obligation.|
| **Total** |**4335**|Exactly one category per quest.|

The5771objective rows partition into4836primitive candidates,931held rows,3declared strategy rows and1compound Arelion workflow. There are four strategy rows across three quests because9066has two source targets.

`quest-execution-review.csv` provides all4335per-quest categories and obligations. `all-4335-execution-requirements.json.gz` retains detailed row/source correlations; `primary-inputs.json` retains table and C++ file identities. These are mechanism and admission categories. They do **not** certify live completion of3729quests or claim that the603remaining compound/source cases are implemented.

The runtime catalogue binds the original build/core revision and exact dataset, repair and strategy bytes. Data loading validates complete membership, objective row order and shape, supported driver names and exclusive accounting before atomically publishing contracts. Missing or invalid catalogue information does not restore ordinary-kill fallback. The catalogue is included in the execution fingerprint and knowledge manifest.

`QuestExecutionPolicy` is shared by scheduler and profile generation. A live NPC location or a nonzero credit ID is insufficient to override a spell-hit, gossip, escort or scripted-credit obligation. Unsupported work is held with a concrete source reason, rather than emitted as an ordinary kill and left to stall in game. Existing explicit synthetic fixtures now supply their own labelled, bounded source observations; the production loader has no test bypass.

## Arelion's Mistress: implemented source sequence

Quest9472requires credit17226from spell30077, supplied by Carinda's Scroll23693. The pinned source does not require killing Viera. It offers repeatable autocomplete quest9483while9472is active;9483consumes one Cenarion Spirits29112and starts Viera's relocation. The wine is a quest hand-in, not something to drink.

The new `ArelionsMistress` custom behavior performs these owned steps:

1. Observe quest9472acceptance, its typed17226counter, carried scroll and wine stock.
2. If wine is absent, travel to the source vendor, Innkeeper Coryth Stoktron18907, select his vendor service and buy one29112. Wait for observed inventory; purchase submission is not stock.
3. Travel to the exact Viera17226, select available quest9483using its observed physical gossip row, advance its progress/reward panels and submit the wine hand-in. Wait for observed relocation.
4. Follow the walking NPC using continuous transit. At the source lure endpoint, after movement stops and the source gossip flags are removed, acquire a separate supported/unmounted interaction observation.
5. Select the exact target, prepare facing, verify the carried scroll's identity/slot/cooldown and use23693. Complete only after the separately observed typed parent credit reaches one.

A resumed already-started lure follows the observed NPC without buying another wine. A consumed scroll remains an outstanding credit request; it does not restart stock acquisition. Purchases, lure rewards and scroll use have bounded client leases tied to actor/recipient and separate acknowledgement deadlines. Missing acknowledgement receives a neutral retry deferral rather than a fabricated success or inflated quarantine failure count. The wine and scroll are protected in the generated profile.

The complete production adapter rechecks actor/session, target wrapper/GUID/base, current position, supported-ground route owner, quest dialog, gossip row and carried slot at the final action boundary. It does not enumerate gossip or issue a collision query while a native command is prepared. Unrelated merchant/quest dialogs and successor work are preserved.

## Causal tests and integration

Retained pre-repair assertions reproduced the intermediate flight stops, old-vertex reissue, all twelve moving-NPC ownership cases, optional review stops, missing future geometry parking valid travel, nested profile-retirement leaks and wrong9472generic materialization. Further tests exposed owner revocation during move, consumed-scroll acknowledgement and the use of interaction stops while following Viera; each was repaired without weakening the underlying admission requirement.

Focused groups include:

| Actual production boundary | Cases |
| --- | ---: |
| Flightor queue/movement ownership |363|
| Flight versus ground economics |14|
| Continuous flight journeys, including time-stepped motion |23|
| Indoor/action integration |100|
| Combat and moving-NPC POI ownership |24|
| Real profile/nested-stage retirement |8|
| Compound9472controller and actual scripts in stock Lua5.1 |49|
| Complete9472runtime adapter with late boundary mutations |21|
| Full catalogue, scheduler/materializer and atomic rejection groups |15|

The time-stepped flight case advances at18.2yards/second over120frames and requires more than400yards of progress with **zero intermediate stop commands and zero idle frames**. Original ground-transition, mounted-combat, collection, dismount recovery, accepted-quest and cancellation suites remain active. All931held objective rows are checked against primitive/strategy admission; source-held materialization cannot silently emit a kill.

The initial aggregate failures included fixtures with absent mechanism catalogues and gossip counter recipes the scheduler could never execute. Synthetic fixtures now explicitly provide their controlled mechanism contract; positive gossip fixtures use their declared whole-quest completion contract. Separate negative tests retain rejection of unbound objective counters. The whole source population and all held rows remain tested.

Final commit/tree, complete local Windows/x86 gate, hosted results, native replay, staging and installed-payload status are recorded by the subsequent immutable release receipts in the evidence directory. A clean-source complete gate is required; focused results alone do not authorize deployment. No future commit identity or deployment is claimed by this precommit report.

## Remaining limits

No game or bot was launched or stopped for these tests. The live post-update route, merchant interaction, lure timing and realm quest acceptance remain to be observed. Deterministic supplied client/server observations and native mesh connectivity do not prove every live route or all quest scripts. The603explicit handler/source obligations are retained for further work; they are not described as solved. Installation must be idle before replacement, and every changed runtime/knowledge file must match the accepted release while preserving settings, logs, original client, navigation engine and rollback.
