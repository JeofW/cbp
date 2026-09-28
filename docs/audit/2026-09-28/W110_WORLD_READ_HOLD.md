# W110 world-read validation hold - published candidate, not verified green

28 September 2026. Repository `JeofW/cbp`, ID1367174964, PR51; only branch `audit/next-55-equipment-observation-20260917`. This is a new world-address/read-validity hold record, not the previously refused combined native map or broad eight-document W110 package.

## Current source and completed work

Latest published source candidate: **450f1fe11af3004667e45f5c511b602a5ecec9a1**, tree **07af127bb4d06530a9516744d4e36155d8271b91**. Its parent is test-only **8b6186d40ee48067d45b843d591fad9bf493be18**. Only `Styx/StyxWoW.cs` changed: the numeric world-state getter now requires exactly four readable bytes before conversion, retaining Unknown on missing/incomplete/exceptional data. No global Memory behavior, cache policy, enum, in-game flag or other gameplay owner changed. All fixtures are unchanged from the behavioral-red baseline. Source publication readback is **W104_GREENWORLDREAD_PUBLICATION_20260927.json**; its filename uses the historical helper suffix but the actual publication occurred28September.

Last independently verified full integrated-green source: **bf0becdb2abeabbbe4a26a57921778886cafb407**. Its already-published numeric-address correction was recovered, not recreated. Current resumption verified **37/37 address cases,17/17 integrated groups,all1862 source inputs,224 inner hashes and225 members**, with **180 normalized fixtures unchanged** from final address baselinea3849e68. Host artifact records0errors/3344warnings. The separate denied bf0 compiler/status transaction is not represented as recovered; these results come from independently source-bound artifacts.

## New defect and actual behavioral red

The actual generic Memory.Read<uint> returns default0 when ReadBytes returns null. Catching exceptions in GameState did not distinguish that missing observation from a valid Idling0. The separate in-game byte could remain readable, allowing actual world/ammo admission on incomplete state data.

The new fixture executes complete tracked Memory.Read,ReadInternal,ReadBytes,cache fields and FastSize plus the actual world/flag/ammo owners, with only external transfer/API/actor leaves controlled. It does not open a process, execute native code or load the client image. Existing world8/address37 assertions are preserved with additive byte adapters in the test-only baseline.

At **8b6186d4**, hosted integrated **36370413478**, job **108765370523**, artifact **10949365599** produced **27/68pass,41intendedassertions,0unexpected**. The only failing aggregate group is the new WorldStateReadValidityRegressionTests; all other16groups pass. All **1863 checkout source inputs,225 inner hashes and226 members** were verified before production changes. Archive989998bytes,SHA256 **7b8d87f429cb347b7e5f8d7530bf67010c6c51e94f1592adc88dba9b43f200c1**. Red host36370413542/artifact10948689618 compiled0errors/3344warnings. Complete records/logs are retained externally.

## What is verified for the latest candidate

The separate host artifact is verified: run **36370924968**, artifact **10948414160**,83930bytes,SHA256 **747dcc790cf4e2fc77b3c272dd3b3d86c0054004ef17ce93f042de0b8b5d6fc4**. Actual result.json/build log records **0errors/3344warnings**,Release/x86 compile-only,tests_run=false,game_attached=false. Retention79160 matched the metadata digest and inspection77770 read the content.

The integrated artifact's **metadata only** was returned: run **36370924943**, artifact **10949321798**,989604bytes,advertisedSHA256 **cc672d035895364e2b0c478a751d4701c91a51d642598be594e0a1780c6f9c05**, bound to exact450f1fe1 and approved branch/repository. This does **not** prove any test result or independently verified archive digest.

The integrated watcher was provider-refused. The subsequent independently requested integrated-artifact download was also provider-refused, each with no result/session. Exact transactions are **W110_WORLD_READ_WATCH_REFUSAL_20260928.json** and **W110_WORLD_READ_ARTIFACT_DOWNLOAD_REFUSAL_20260928.json**. A direct read of the expected new integrated ZIP and receipt returned Not found. There is no completed download, green-case extraction, current integrated source/inner-hash verification or new red-green normalized-artifact comparison.

**Do not claim68/68,17/17,181unchanged normalized fixtures or full green at450f1fe1.** The source-only unchanged-fixture diff and clean host compile are not substitutes for those missing gates. The prepared green-checkpoint script remains unexecuted with its actual green requirements intact; this separate hold report does not weaken them.

## Continuation without replay

Do not rerun, rename, wrap, fragment or route the refused watcher/download through another endpoint/tool. Do not create source churn or rerun unchanged CI merely to obtain replacement evidence. Preserve the candidate and its valid red/source/host records until an independently authorized resolution of the blocked gate is supplied. Any genuinely new defect requires its own regression-first evidence and must not be invented as a workaround.

Also preserve the earlier denied combined native map, eight-document successor assembly, f8 detailed status/case/normalized comparison and old world final-ref transactions. This new report neither reconstructs their outputs nor declares them complete. Existing broad Ret/seal/ten-class/buff/Greater/PallyPower/aura/lockout matrices and addenda remain available at their stated scope; no new optimal-DPS, effective-buff-strength or live proc claim is made.

Native/current-client/server/realm/route/independent acceptance remains open: exact buffer size is not a frame-atomic freshness guarantee; current world observation does not prove ammo/server application; exclusive ground/gossip origin, missing quest recipes and supervised3Dnavigation/physical inventory are not supplied here. PR51 remains required open/draft/unmerged; masterb2324913 and productionCB are unchanged. Both intentionally unpublished root handoffs must be preserved. Numeric preparation remains3historical/0new/0remaining.

No subagents, local project build/test, productionCB access, native/executable/IDB mutation, force-push, master write, merge or deployment occurred. Overall W110 is **not complete** at this validation hold. The actual CoS finish result must be recorded separately; HELD is not RELEASED, and a later release would not waive the blocked validation/live/independent/merge gates.
