# W90 R03 identity mapping review — not permission to enable progress recipes

Reviewed source: 75ab55985b3e594177f43b730173b5414db34d82 (production unchanged from W89). This records source inspection and remaining test obligations, not a new runtime fix or a claim that all mappings are safe.

## Existing contracts

`runtime-snapshot/Bots/WholesomeAutoQuest-master/DataLoader.cs`, `QuestStrategyPackLoader.Load/ParseRecipe`, accepts only `quest-strategy-pack-335-v1`, exact fields, build 12340, the matching dataset digest and unique `(QuestId, ObjectiveIndex)` owners. The source declaration and digest bind the recipe pack to dataset bytes. They do not create a mapping from a dataset index to a raw normal-objective counter. There is no declared raw-counter field. Do not weaken strict field validation or silently reinterpret existing v1 recipes.

`DataModels.cs` defines `QuestObjective.Index` and `QuestStrategyRecipe.ObjectiveIndex` without a separate raw-counter identity. `QuestScheduler.AddObjectiveWork` uses those indexes for dataset ownership. Its W89 `CanScheduleWholeQuestStrategy` permits only implemented, source-bound whole-quest strategies and defers `ObjectiveProgress`; it neither upgrades that declared success condition nor renumbers it. Unsupported CAST, Escort, GameObject item actions and missing BelowHp thresholds stay deferred. Independent valid collection remains eligible.

`QuestScheduler.ScanAndRefreshOwned` reads accepted quests' raw `ObjectivesDone` arrays separately from carried item counts. The live producer supplies a carried-item dictionary. `IsObjectiveComplete` uses item identity/count for collection work when that dictionary exists; a null dictionary in a pure snapshot can reach the legacy raw-array fallback. That pure-input fallback is not evidence that the live producer normally lacks carried counts.

The legacy ordinary-kill path still compares `objectiveCounts[objective.Index]` against `KillCount`. Its raw-array index is not accompanied here by an explicit dataset-to-credit identity proof. This is a remaining review/fixture target, not an assertion that the W90 request cases tested or repaired it.

`ProfileBuilder.BuildUseItemOnStrategyGuard` and `BuildGossipEventStrategyGuard` retain the dataset index in generated `ObjectiveIndex` attributes. Whole-quest owners do not use that index for raw progress. `UseItemOn` and `GossipEvent` read raw slots only for their explicit `ObjectiveProgress` mode. Testing dataset indexes 0, 3 and 17 with `QuestComplete` therefore proves namespace non-interference in that mode; it does not prove that any of those dataset indexes is a valid mapped progress slot.

## Next acceptance obligations

1. Reproduce ordinary-kill scheduling with a real allocated QuestLog, independently configured dataset index and cached objective metadata/credit identity. Test matching identity, a reordered/different credit target, duplicate or ambiguous identities, count disagreement and missing metadata. Verify both ordinary scheduling and completion suppression; do not stop at a pure helper.
2. Keep collection identity based on carried item identity and required count, including alternative collection sources and independent valid work. A same-numbered collection index must never authorize a normal raw counter.
3. Before enabling a progress recipe, establish source-backed credit identity independently of its action recipient. A matching action target or a number within 0..3 is not enough. No new schema, field, slot or recipe is authorised by this review alone.
4. Hold the mapping stable across actual loader, admission, materializer, CodeNode, constructor and request/acknowledgment observations. Include changed quest/player/data/metadata before navigation/publication and before a request. Preserve the existing W89 navigation-mutation tests.
5. Keep the current safe deferral when mapping is unavailable. Preserve ready-state turn-in, whole-quest strategies, unsupported CAST refusal and independent valid work. Never change ObjectiveProgress into QuestComplete to make a case pass.

The W90 controlled matching-recipient tests cover request versus acknowledgment for already-supported QuestComplete recipes. They do not exercise the native item implementation, execute the emitted Lua, validate a new mapping protocol, or provide original-client acceptance. The retained W89 actual compiler/factory/wrapper cases and W90 controlled request cases are separate evidence layers, not a single native end-to-end test.

## Pinned source references

- DataLoader.cs blob e49b592af5b80065dbef26199f83ed798c9b1839, lines 325–490.
- DataModels.cs blob 5344e51d789e37f7c70a7f545c547d5ef6aa4c21, recipe/objective models.
- QuestScheduler.cs blob 001d1c42dd139c5f7a3d87f3ee638ba5adb0720b, live capture, AddObjectiveWork, CanScheduleWholeQuestStrategy, IsObjectiveComplete and ReadObjectiveCounts.
- ProfileBuilder.cs blob 10338e75a8604d9a800679295437053e28f5c182, strategy lookup and materialization.
- UseItemOn.cs blob 37a5af82fb49ae1e8b4e774cc9da1ebbdf3b8a2f; GossipEvent.cs blob 9ec3346672a5d6c42a3e35284241e7b585e8808a.

Original WoW 3.3.5a/build 12340; TrinityCore 3.3.5 primary, AzerothCore WotLK secondary. Retain all four policies and R04/R07/R06 gates. No game offsets, coordinates or real recipes were invented by this review.
