# W80 — post-cutoff source review: changes requested

Reviewed 20 September 2026. Repository `jeofwong/CopilotBuddy-private` (1367174964), draft PR51, branch `audit/next-55-equipment-observation-20260917`.

## Result and scope

**The fixed-range history accounting and retained-source review are complete. The change set is not approved: eight finding groups require remediation. W80 makes no production fixes.** Completing this review is not the same as completing all repairs or accepting live gameplay.

Owner cutoff: **18 September 2026, 21:58 Malaysia /13:58 UTC**. Base **4c6b1b2e75af4dd0eaffde11d38ebbc84a21f4c0**; frozen endpoint **a3bb6940705dedd5f159810f683ed77d2c5d9939**. Native comparison is140 commits ahead,0 behind. The ledger accounts for all140 identities chronologically. Retained-source review covers changed methods/new files in all30 net non-test production/tool paths, with relevant callers and tests. Historical identities and subjects are mapped to surviving change families; this is NOT a claim that each transient snapshot was independently certified or rerun.

Subject-prefix counts are70 test,42 fix,20 docs,5 feat,2 chore,1 revert. These describe commit intent, not independently established patch-purity counts, gameplay progress or quality scores. In particular, test-fixture/locator corrections are not additional gameplay fixes.

Last tested production remains **96d1259f1d3c7035b85b1fa4bf00d05fe52d4ee6**, tree **bed704f539f0e21697cec2862906096d30139f03**. The frozen endpoint is its documentation successor. No new C# test run or live-game reproduction was performed for the findings below; their evidence is source control flow and integration-contract analysis. No independent reviewer is claimed.

Original WoW3.3.5a/build12340 only; TrinityCore3.3.5 primary, AzerothCore WotLK secondary. Focus remains Wholesome questing, navigation and Singular. Do not resume AuctionHouse/ProfessionBuddy expansion. Read the four governing original-client/core/provenance policies and W77–W79 alongside this checkpoint.

## Quality and complexity assessment

The owner's concern is justified in specific areas. The automatic dense-pull controller grew before its actual-route and normal-pull interactions were proven. Several item/UI state machines record pending intent but do not consistently prove current actor, request, frame or physical-item ownership. Strategy parsing, scheduling and execution are tested separately despite incomplete hand-offs. Some tests check source names/patterns rather than execute the full scenario suggested by their names.

This does not justify reverting every guard or the whole140-commit range. Aura allocation bounds, the corrected breath budget, conservative dependency publication, legacy fingerprint preservation, quarantined map conversion, complete reward-set observation and the later narrow W77–W79 repairs have defensible purposes. Finish or contain existing contracts, reduce duplicated incomplete policy, and avoid another general framework. Neither commit metadata nor these findings establish an AI-model benchmark or reliable model authorship.

## Findings

P1 means resolve or contain before approving deployment of this branch. P2 means resolve before certifying the affected subsystem. These are review priorities, not claims of observed in-game damage. Every source below is pinned to a3bb6940705dedd5f159810f683ed77d2c5d9939. All eight groups remain open; none was fixed in W80.

### R01 / P1 — automatic dense pulling lacks complete route and lifetime validation

Sources: `Bots/Grind/Levelbot/Actions/Combat/PullIsolationCoordinator.cs`, its `LevelBot.cs` hook, `IIsolationPullProvider.cs`, and Singular's provider/Ret factory.

The coordinator checks hazard envelopes along straight segments plus path existence, then asks navigation to choose the actual movement route. It does not inspect that traversed path for the same hazards. Its five-second engagement budget starts at plan creation, before approach finishes. Existing-plan checks do not retain all actor/routine/context identity used at admission. The isolation hook precedes the ordinary pull gate, while the Ret provider automatically exposes the capability without a separate user setting.

A safe chord can have an unsafe routed detour; approach can consume the engagement budget; existing plans or alternate range policy can outlive their original context. These are source-traced gaps, not newly executed navigation failures.

Minimal direction: contain the new automatic path or make it explicitly opt-in until tested; preserve normal pulling/combat and the already-fixed Ret registration. Separate approach/engagement deadlines and validate actual navigation semantics rather than building a second movement engine. Required tests: actual coordinator ticks with a safe chord/unsafe detour, slow approach, changed player/map/routine/spec, and ordinary pull-distance controls.

### R02 / P1 — single-target item use can stall after an unacknowledged submission

Source: `runtime-snapshot/Quest Behaviors/UseItemOn.cs`, especially `UseCapturedItem`, `CurrentObject` and `CreateBehavior`; generated `WaitForNpcs` in ProfileBuilder.

A submitted recipient is immediately added to `_npcBlacklist`, before acknowledgement. Consider one recipient, a reusable tool, unchanged quest progress, Counter1 below MaxAttempts3, and WaitForNpcs=true. After the acknowledgement window, the consumed-item branch does not defer because the tool still exists. The only recipient remains excluded, no further attempt increments the counter, and the waiting path has no overall target-wait deadline. Thus a nominally bounded operation can wait indefinitely.

Minimal direction: bounded retry/defer transitions with a deliberate per-attempt target-eligibility lifecycle. Never treat local invocation or exhausted retries as quest success. Required test: complete owner submission -> unchanged progress -> timeout -> same recipient still present -> retry or bounded deferral, plus valid delayed-acknowledgement controls. The existing consumed-item deadline test is not this scenario.

### R03 / P1 — strategy parsing, scheduling and execution are not one complete contract

Sources: Wholesome `DataLoader.cs`, `DataModels.cs`, `QuestScheduler.cs`, `ProfileBuilder.cs`; UseItemOn/GossipEvent and `QuestObjectiveCompletion.cs`.

`AddObjectiveWork` applies `Supported(quest, objective)` before materialization. CAST-credit KillMob work is rejected without consulting the declared supported strategy that would perform it. Tests calling ProfileBuilder with a prebuilt plan bypass this scheduler exclusion. Retain the cast-credit safety rule; do not fix this by allowing ordinary killing.

UseItemOn can borrow objective hotspots without the target/anchor identity check present for GossipEvent. Recipe ObjectiveIndex is passed to a raw normal-counter observer without an explicit namespace/mapping contract; general dataset, collected-item and displayed/compressed indices are not interchangeable. This is a missing mapping proof, not a claim that every supplied index is wrong.

Kind parsing uses Enum.TryParse without the IsDefined check used for other enum fields. Unsupported/unimplemented kinds can fall through to legacy objective generation. The current Escort test accepting legacy XML does not establish safe handling of an explicitly declared unsupported strategy. Selecting a GameObject recipient is also not itself a proven item-target/ground-cursor protocol.

Minimal direction: expose supported recipe authority at scheduling, bind target/location and raw-slot semantics, reject/defer unknown or unimplemented kinds, and verify actual recipient dispatch. Required tests must traverse loader -> real scheduler -> XML -> behavior for CAST with/without recipes, sparse/raw/collection indices, mismatched targets, numeric undefined Kind, unsupported Escort and GameObject request semantics. Keep incomplete Gordunni/escort work deferred rather than inventing a recipe.

### R04 / P1 — pending equip bypasses caller admission and lacks displaced-item proof

Sources: `runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs`, `runtime-snapshot/Quest Behaviors/EquipItem.cs`, shared WoWItem container helpers.

AutoEquip Pulse/DoCheck service HasPendingEquip before the later running/combat/death/ghost/battleground gates. Attached callbacks can therefore process pending work in states that reject new equip operations. Pending state needs the same deliberate actor/runtime/transaction admission, not just GUID/entry fields.

`ReturnDisplacedCursorToSource` accepts any held item whose entry differs from the newly equipped item when the remembered source slot is empty. That is not proof that it is the actual displaced item. Entry/type/slot checks do not identify the physical GUID, and a prior legitimate submission does not exclude a later foreign same-slot popup.

Retain W77's deadline fix and W79's successful-local-submission guard. Minimal direction: recheck pending actor/context, avoid moving an unidentified cursor item, and preserve normal successful equip. Tests: pending ticks after Stop/combat/death/player replacement, foreign held items, same-entry substitution and foreign same-slot popup after a legitimate submission. Do not invent a native ABI from an offset symbol or restore unconditional ClearCursor.

### R05 / P1 — destructive deletion has request/lifetime and scan-continuation gaps

Sources: MrItemRemover2 `Methods.cs`/`MrItemRemover2.cs`, `DeleteItems.cs`, shared container observations.

MrItemRemover's `TryConfirmPendingDelete` checks HasPendingDelete but does not require `_pendingDeleteRequested`; pending intent alone can admit confirmation. Pending service also precedes normal world/combat checks. `CheckForItems` returns after its first BeginDelete attempt, including refusal. Completing/resetting the pending item does not itself continue remaining candidates; another timer, loot or manual trigger is needed. With no next trigger, the scan does not finish.

Null/incomplete inventory and candidate observations must also remain distinct from confirmed requested deletion. One-operation-per-pulse is a sensible goal; missing continuation is not a reason to remove quest-item protection or delete more aggressively.

Minimal direction: own successful request plus current actor/context before confirmation, preserve protected/quest-item guards, schedule a bounded continuation after acknowledgement, and keep unknown reads as defer/unknown. Tests: foreign confirmation before request, refused pickup, multiple items with timer scanning disabled, Stop/world replacement and read failure versus requested deletion acknowledgement.

### R06 / P1 — gossip selection and cleanup do not consistently retain menu ownership

Source: `runtime-snapshot/Quest Behaviors/GossipEvent.cs`.

NPC-frame identity is checked before reading options; other observations occur before selection without establishing the same NPC/menu again at the final request. Cleanup can close a visible menu without proving it is still the interacted one. Disposal/old-tree continuation also needs a consistent lifetime check. W78's final quest-completion check is useful but does not establish menu identity.

Minimal direction: revalidate exact NPC/menu and owner immediately at selection and restrict cleanup to that interaction. Test mutations during option observation, completion checks and cleanup; disposed callbacks; unchanged-menu success. Do not substitute a first-option fallback.

### R07 / P2 — plugin cache omits compiler inputs and an atomic replacement contract

Source: `Styx/Plugins/PluginManager.cs`; compared with ClassCollection, DynamicLoader, DllLoader and SourceCompiler.

The reuse fingerprint covers .cs/.resx, whereas SourceCompiler can consume referenced assemblies/compiler options. A dependency-only change can leave that fingerprint unchanged. Cached construction catches individual constructor failures and returns a subset that refresh can publish. Reusing a compiled assembly also retains static state; fresh instances alone do not establish equivalent reload semantics.

Attribution correction: DllLoader already swallowed individual constructor failures before this batch. That part is an inherited pattern duplicated into the new cache path, not a wholly new defect. Caching has a valid performance purpose.

Minimal direction: include actual compilation inputs and explicit reload semantics, and preserve the prior set when required construction fails. Tests: referenced DLL/options changes without source edits, one constructor failing among several, old-instance preservation/disposal and static-state reload policy. Avoid a broad new plugin framework.

### R08 / P2 — reward observation is not rebound at final selection

Sources: `Bots/Quest/Actions/ActionSelectReward.cs`, downstream `Styx/Logic/Inventory/Frames/Quest/QuestFrame.cs`.

Complete bounded reward-set observation and removal of arbitrary first-reward fallback are improvements. However, the later selection is a numeric index passed to QuestFrame.SelectQuestReward, whose implementation only clicks QuestInfoItem(index+1). It does not verify that the shown quest/selected reward still matches the earlier observation.

This leaves an inherited late-selection gap; it does not invalidate the complete-set repair. Minimal direction: bind the final request to captured shown-quest/item identity and reobserve/defer after changes. Test changed quest, reordered/replaced choices and unchanged-set success. Do not alter gear weights or restore arbitrary fallback in this slice.

## Retained evidence and test adequacy

Keep Ret registration06733822, collection admissionf0a8b316, normal-counter restart44cfdca7, popup submission96d1259f and acknowledged timeoute22844e8. Keep auction withdrawald8b6476a; ledger121–125 are withdrawn historical work, not approved functionality.

Ten retained CI ZIPs were reverified in W80: recorded outer SHA256, CRC and source identities; all inner manifest entries for nine integrated archives; four unchanged-fixture comparisons with137/138/139/140 identical normalized members and only expected source-input differences. This is archive verification, not rerunning C# or gameplay. The failed W78 constructor fixture is retained separately and is not gameplay assertion red.

Final retained evidence is integrated35456734949/art10587779649,17/17 entries; popup18/18, normal restart37/37, collection34/34, Ret registration9/9, acknowledged timeout10/10, analyzers89. Host35456734963/art10588378360 records exit0,3344 warnings,0 errors, tests_run=false. All game_attached=false. W79 archive SHA256:8dd192f459654e2e28420fd3e9b1bb08a6d7c09fe1c638b418ab6b9b12cfdb66; host:fcb527df8e4d566c29f4d380a7282801908b360d5d6641b26353b47868d0acfd.

Strong tests inspect CLR registration, execute actual emitted conditions and selected real owner/method paths. Keep them. The dense-pull tests mainly cover predicates/geometry/source wiring, strategy tests can bypass scheduling, deletion tests mainly compile/check source patterns, and plugin-cache tests use a controlled compiler delegate without full dependency/refresh cases. Narrow passing tests do not settle R01–R08. Do not weaken assertions to make the next repair green.

## Original-client compatibility

Modern C# syntax is not evidence of a later WoW API. W78 introduces no Lua API/native offset; W79 changes C# admission while retaining Lua strings. The reviewed source references remain TC3358fda442f6c30ca21a622638063ab8b28376f1b25, AC8337a378ac325e62a6a91e00c6a5e944205e8536 and original UI wowgaming/3.3.5-interface-files@d0339b17b0221db76e6acd2dc2915d224a5b62ca. These are source contracts, not detected realm revisions or universal original-client acceptance.

PallyPower is opt-in/read-only/version/schema-gated; the user's installed original-client addon remains unverified. Provenance hashes bind bytes/declarations, not realm SQL/scripts. Quarantined addon XY is not runtime terrain/Z/floor or quest-recipe authority. No desktop mouse simulation is required by the cursor terminology: these are client item/targeting states, still subject to shared-state races.

Gordunni Cobalt shovel9466, cobalt9463/count12 and mound144064 records do not establish a complete dig/trigger/spawn/loot strategy. No recipe or coordinates were added in W80. Defer new features until review blockers are resolved; neither item invocation nor arrival is quest credit.

## Retained non-test path coverage

Each path below was inspected for its retained delta and relevant surrounding flow. This does not certify every unchanged method or historical intermediate snapshot.

| Path | Review disposition |
|---|---|
| Bots/Gatherbuddy/GatherbuddyBot.cs | Keep backoff containment; Vendors terminal/defer caller traced; not sale acknowledgement |
| Bots/Grind/LevelBot.cs | R01 isolation hook vs normal pull/combat ordering |
| Bots/Grind/Levelbot/Actions/Combat/PullIsolationCoordinator.cs | R01 full new coordinator |
| Bots/Quest/Actions/ActionSelectReward.cs | Keep observation; R08 final identity |
| Styx/Combat/CombatRoutine/IIsolationPullProvider.cs | R01 interface/provider/caller |
| Styx/Logic/Inventory/Frames/Merchant/MerchantSaleAttemptGate.cs | Keep cadence/deadline/reset contract; no new concrete regression identified here |
| Styx/Logic/Questing/QuestLogSnapshot.cs | Keep reviewed dependency/status distinctions with provenance limits |
| Styx/Logic/Questing/QuestObjectiveCompletion.cs | Keep positive-only helper; R03 caller index mapping |
| Styx/Logic/Questing/Recovery/QuestPrerequisiteAuthority.cs | Keep narrow active-parent rule |
| Styx/Plugins/PluginManager.cs | R07 cache/refresh contracts |
| Styx/WoWInternals/WoWObjects/WoWItem.cs | Keep validated queries/pickup; R04–R05 limits |
| Styx/WoWInternals/WoWObjects/WoWUnit.cs | Keep bounded aura allocation |
| Tools/EvidenceAudit/addon_evidence.py | Keep Windows identity and quarantined XY; no runtime authority |
| runtime-snapshot/Bots/WholesomeAutoQuest-master/DataLoader.cs | Keep provenance; R03 strategy boundary |
| runtime-snapshot/Bots/WholesomeAutoQuest-master/DataModels.cs | R03 action/index contracts |
| runtime-snapshot/Bots/WholesomeAutoQuest-master/ProfileBuilder.cs | Keep collection guard; R03 target/strategy/travel contracts |
| runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs | Keep narrow prerequisites; R03 strategy admission |
| runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs | R03 strategy-aware wiring |
| runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs | R04 pending lifecycle; retain W77/W79 fixes |
| runtime-snapshot/Plugins/MrItemRemover2/Methods.cs | R05 request/scan lifecycle |
| runtime-snapshot/Plugins/MrItemRemover2/MrItemRemover2.cs | R05 pulse/trigger/disposal ordering |
| runtime-snapshot/Quest Behaviors/CollectThings.cs | Keep narrow breath-budget correction |
| runtime-snapshot/Quest Behaviors/DeleteItems.cs | R05 destructive lifecycle/unknown observations |
| runtime-snapshot/Quest Behaviors/EquipItem.cs | R04 pending/return/popup lifecycle |
| runtime-snapshot/Quest Behaviors/GossipEvent.cs | R03/R06 full new behavior |
| runtime-snapshot/Quest Behaviors/UseItemOn.cs | R02/R03; retain valid dispatch/quest guards |
| runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/PaladinSupport.cs | Keep opt-in version-gated bridge; installed compatibility not certified |
| runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Retribution.cs | Keep fixed normal registration; R01 isolation integration |
| runtime-snapshot/Routines/Singular wotlk/Settings/PaladinSettings.cs | Opt-in assignment default/settings inspected |
| runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs | R01 automatic capability exposure |

## Next work and repository integrity

The next priority is remediation, not another full inventory pass or unrelated feature expansion: contain pending UI/destructive requests R04–R06 and item-use stallR02; complete strategy hand-offsR03 and contain/verify dense pullingR01; then finish cache/reward contractsR07–R08. Obtain targeted behavioral red, preserve fixtures across minimal production changes, and inspect exact-head retained Windows evidence before claiming fixes.

W80 changes documentation only. PR51 remains draft/unmerged. Direct master remains expected at b2324913e2499ba30b239dd67224ca2c655c05cc, restored tree552eeab1233c7c282897dca0ed9f4334c5e8ed43, equal to the approved f462a9bb tree. W77's disclosed empty-document add/revert stays in history; no master write is authorized by this review. Reconcile live refs before publishing; explicit nonempty PR branch, reviewed parent/content, force=false only.

**Review completed; remediation, independent review and supervised original-client/server acceptance remain open.** This is a changes-requested disposition, not deployment permission or an absence-of-bugs guarantee.
