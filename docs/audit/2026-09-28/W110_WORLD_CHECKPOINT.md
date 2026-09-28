# W110 - verified unknown-world-state admission repair

28 September 2026. Original WoW 3.3.5a build12340; TrinityCore3.3.5 primary, AzerothCore WotLK secondary. This is the checkpoint for the newly delivered world-observation task only. It neither reconstructs the refused earlier successor/native package nor closes all W110 acceptance gates.

## Exact source and change

Repository JeofW/cbp, ID1367174964; PR51; branch `audit/next-55-equipment-observation-20260917` only. Final tested source **16f52aa7f328e1297a5f06a9b3a18328639b8b4e**, tree **458c110241d4d60936d4ac06e2a784ae70db3dfd**. Test-only parent **3d04bbe88e025fd4ee3c3988a608be8eabf05f72**; earlier source f8f50616 remains a historical artifact-green predecessor, not work to recreate.

The actual `StyxWoW.GameState` getter returns `GameState.Unknown` when its memory read fails. The former `IsInWorld` predicate accepted that value, and undefined numeric values, whenever the independent in-game byte was readable. The actual AutoEquip ammunition consumer consulted that property before and after cursor observation, but its earlier fixture supplied an IsInWorld boolean and did not exercise the getter.

The repair changes only `Styx/StyxWoW.cs::IsInWorld`: preserve the IsInGame short-circuit; observe GameState once; reject Unknown, Zoning and undefined enum values. Existing known non-Zoning/non-Unknown behavior, public signatures, enum and memory-read address remain. The source publication has14insertions/2deletions, including its contract comment. No era mapping, new address, actor/frame atomicity or supervised gameplay claim follows from this managed admission repair.

The complete140-line `AmmoWorldObservationRegressionTests.cs` executes the actual world/in-game getters, actual host enums and complete ammo/context/cursor methods with controlled memory/API/actor leaves. It was newly authored after the alleged earlier draft could not be recovered; it is not represented as an applied recovered draft. No fixture or assertion changed between this cycle's red and green.

## Hosted regression-first evidence

| Evidence | Behavioral red | Verified green |
|---|---|---|
| Source | 3d04bbe88e025fd4ee3c3988a608be8eabf05f72 | 16f52aa7f328e1297a5f06a9b3a18328639b8b4e |
| Integrated run | 36363585728 | 36364095453 |
| Integrated job / artifact | 108745451724 / 10946224118 | 108746928958 / 10947190554 |
| World cases | 3/8 pass;5intendedassertions;0unexpected | **8/8 pass;0assertions;0unexpected** |
| Integrated groups | 16/17; only new world group fails | **17/17 pass** |
| Source inputs / inner hashes / members | 1861 / 223 / 224 | **1861 / 223 / 224**, all matched |
| Host run / job / artifact | 36363585776 / 108745452051 / 10947035582 | 36364095471 / 108746929261 / 10946309532 |
| Host result | Release/x86;0errors;3344warnings | **Release/x86;0errors;3344warnings** |

Final integrated archive:977725bytes, SHA256 **fd76e493d3068afc4d8059ff1ca488772e04a2176b8e11f8156c2a26e68ed7c1**. Final host archive:82916bytes, SHA256 **fd88f33da675cb44fef87914ea55e8d7612e22ca71abf98b87aee1d13e98a7a0**. Download digests matched GitHub artifact metadata. The host evidence is compile-only, tests_run=false. All hosted records have game_attached=false.

The eight scenarios retain healthy known-state admission, Zoning denial and out-of-game denial, and prove rejection of explicitUnknown, failed state read, undefined state, and both post-cursor transitions to unknown/failed state. This is controlled original-owner behavior, not verification of the historical address/enum commentary against a running original client.

**All179normalizedfixturemembers are identical for this world cycle only.** There are no added or removed captured inputs; the only changed source input is Styx/StyxWoW.cs. The current run also verifies retained AutoEquip51/51,metadata165/165,dispatch97/97,terrain46/46 and W109groundsubmission11/11 with zero assertions/unexpected errors. The earlier W104-W109 ownership, cleanup, movement/facing, quest, Paladin/group and assertion groups remain in the17/17 aggregate.

## Reproducible evidence locations

External root: `D:\Dev\CopilotBuddy-Evidence`.

World plan/execution: `W110_WORLD_OBSERVATION_EXECUTION_20260928.md`. Resumption/verification ledger: `W110_WORLD_COMPLETION_RESUMPTION_20260928.md`. Source publication: `W104_GREENAMMOWORLD_PUBLICATION_20260927.json`. The helper's legacy filename date is not the actual28September event date.

The paired `w110-world-red-*-3d04bbe8` and `w110-world-green-*-16f52aa7` ZIP/receipt/inspection files bind exact source identities. `W110_WORLD_RED_CASES_20260927.json`, `W110_WORLD_GREEN_CASES_20260927.json` and `W110_WORLD_FIXTURE_COMPARISON_20260927.json` retain the actual case and byte-comparison proof. See the adjacent W110_WORLD_EVIDENCE.json for exact identifiers and limits.

This containing commit is documentation only, not the tested source. Its actual expected-parent/path/blob/remote-byte/ref receipt will be `W104_WORLDDOCS_PUBLICATION_20260927.json` after successful publication. Do not invent its hash before that operation. The external latest pointer and world finalization record establish subsequent state.

## Scope, refusals and acceptance remain explicit

This new two-file world checkpoint is not the refused eight-file f8 successor package. None of its eight destinations or source/core/Ret/native payloads is reproduced here. The refused f8 final case extraction/normalized comparison and detailed run/jobs request remain unexecuted/unverified. The new179-fixture3d04bbe8-to16f52aa7 proof does not establish the older refused178-fixture interval.

The native-map packaging refusal, original W109 IDA-health refusal, TC-tree refusal and corrected-commit CI-list refusal remain exact. No retry, disguise, split or alternative reconstruction occurred. No current IDA connection or combined native map is claimed. Existing successful static evidence and the earlier published Ret/core/scope matrices retain their original limits.

The prior full successor documentation package, old comparison/status gates and original-client/server/world/independent acceptance remain open. Ground/gossip origin, effective buff magnitude, realm-specific recipes and safe3Dretreat are not supplied by this getter fix. Dense isolation remains disabled. **Overall W110 completion and merge readiness are not claimed.**

Last prepublication local/direct-ref/PR observations match16f52aa7; PR51 is OPEN/DRAFT/UNMERGED and master remains b2324913e2499ba30b239dd67224ca2c655c05cc. The intentionally unpublished AUDIT_RESUME.md/NEXT_CHAT_PROMPT.md edits are preserved and excluded. No local project build/test, productionCB access, subagent, native/IDB mutation, force-push, master write, merge or deployment occurred. Numeric preparation remains3historical/0new/0remaining.

Self-review of actual getter/consumer and hosted evidence is not independent review. CoS release is also separate: the last retained actual state is HELD. A later RELEASED result permits final turn delivery but cannot waive any blocked verification, gameplay, independent or merge gate.
