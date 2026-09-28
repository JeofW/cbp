# W110 current collection and native-contract closure

**28 September 2026. Tested source: `6f74e3cdf811c19f3445c13bfd626b897fb6b8e9`.**

The newly delivered aura calling/lifetime documentation and the observation-validity defect found while completing it are implemented and verified to the available-evidence boundary. This record supplements the complete earlier W110 objective/Ret/core records; it does not reconstruct a frozen historical map or waive live/independent acceptance. The containing documentation commit is recorded after publication in the external receipt and final verification, not invented here.

## Preserved frontier and exact repair

Repository `JeofW/cbp`, ID1367174964, PR51; branch `audit/next-55-equipment-observation-20260917`. Source tree **1a51dd739259c0f670556ab22f595e771fac9236**; behavioral-red parent **fc7e65308b66a8d4401ce1c39f04d15f0adadc94**. Prior published aura documents are **2ea5bee770bf58e7e4946165e9dec2180d3f8d07**. Master remains **b2324913e2499ba30b239dd67224ca2c655c05cc**, PR51 OPEN/DRAFT/UNMERGED, and the two intentionally unpublished root handoffs remain modified but untouched.

`WoWUnit.GetAllAuras` previously accepted failed typed-count reads as zero and discarded the actual bulk-transfer byte count. Incomplete acquisition could therefore hide an active external buff and permit a conflicting selection. The complete-owner hosted test exposed this, including through the actual Might/Battle Shout blessing selector.

Only `Styx/WoWInternals/WoWObjects/WoWUnit.cs` changed in production: **26 insertions,10 deletions**. Complete four-byte static/dynamic header reads precede the existing255-count allocation guard; the existing uncached raw bulk reader must transfer exactly24bytes per record. Invalid observations use the retained valid-world exception/non-world empty policy. Global Memory, native addresses/ABI, timer semantics, selectors and all fixtures remained unchanged during the production interval. Correct-size cached headers retain the existing cache contract; this is not an atomic native/frame snapshot implementation.

## Full regression-first result

| Stage | Exact result |
|---|---|
| Test-only red | `fc7e6530`;27/75pass, **48 intended assertions,0unexpected**. All17projects compiled; only the new aggregate group failed, other16groups passed. |
| Minimal production green | `6f74e3cd`; **75/75 collection**, **17/17 integrated groups**, no failed aggregate group. |
| Frozen fixture comparison | **183 normalized members unchanged**; no added/removed source inputs; only WoWUnit.cs differs. |
| Current source/archive integrity | **1866/1866 source inputs matched**, **227 inner hashes /228 archive members** checked; source/fixture/tree identity bound to6f74e3cd. |
| Host compile | Release/x86 **0errors,3344warnings**, tests_run=false, game_attached=false. |

The test uses complete actual Memory typed/byte/raw/cache methods, GetAllAuras/count guard, by-value aura fields/timer and blessing selector. External transfers, world/clock and spell/readiness leaves are controlled. Missing/partial static and dynamic headers, missing/partial bulk, zero/full counts, cached header behavior, uncached bulk, disappearing unit/world, repeated failure and later recovery are distinct cases. The preexisting count guard's locator gained only a test-only compatible spelling before the frozen baseline; its eight assertions were not weakened.

## Artifact identities

| Artifact | Run / job / artifact | SHA256 |
|---|---|---|
| Red integrated |36382368668 /108800593392 /10952678178;1003108bytes|`0311c1fac76d0ed89925669befe73639ac141d21ae608cc413a80bfd837d23c1`|
| Green integrated |**36383055053 /108802616797 /10952634132**;**1001629bytes**|**`93bdd21f9a085d59ea166e28cd559974d7b30273834c7ea805b0db82f35d1b86`**|
| Green host |**36383055065 /108802616862 /10952678653**;**83571bytes**|**`c3ba385b8d7e21bbfd664c2d71e99fe0713ef1d7f422aae66be2bcb7c5f58fa2`**|

Both green workflows have actual completed-success run/job receipts, not merely artifact metadata. Download hashes matched GitHub metadata. The current static verifier independently rechecked existing source/archive/native receipt integrity without rerunning CI or replacing original records. **W110_COLLECTION_CLOSEOUT_SOURCE_VERIFICATION_20260928.json**, SHA256 **4a67c82e533d86d5a26893f353e5b28b2097e3010e0d0defc3becacb96fb1aa5**, records that verification.

Current artifacts retain timer96, count8, world-read68, world-address37, world8, metadata165, AutoEquip51, dispatch97 and terrain46 passing controls, alongside W104-W109 cleanup/physical cursor/Lua/pending/quest/publication/combat/movement/Paladin groups. These are actual later-source results. They **do not** claim the historical450f1fe1 download, old normalized comparison or other frozen transaction succeeded.

## Delivered native contract completed

Read **W110_AURA_CALLING_CONTRACTS.md**. Actual hash-bound aura read evidence establishes the current UnitAura0x614D40,UnitBuff0x614CA0,UnitDebuff0x614CF0 caller operations and all410returned instructions across two pages of shared output0x6147C0. Caller-local selection descriptors are passed inEAX, Lua state on the stack, and the shared helper returns a32-bit Lua result count, not an AL gameplay receipt or an aura-record pointer.

The document distinguishes stack-descriptor lifetime, native lookup/record invalidation, copied managed snapshots, cached headers/uncached bulk, normal executor serialization, read-only analysis versus Lua stack writes when an API executes, actual downstream consumers, and exact unproven thread/frame/effective-strength/live boundaries. Five raw instruction-page receipts and their identities/hashes are verified in the accompanying evidence. No new native invocation, IDB change or guessed ABI dispatch was added.

## Whole-goal disposition and restrictions

**W110_COLLECTION_GOAL_DISPOSITION.md** reconciles the entire saved Goal with this new result and the complete prior objective/Ret/core matrices. Current implementation and scoped evidence tasks are complete; the specific original-client, server/realm-data, world/route, protocol and independent-review inputs still required are not fabricated. `DensePullIsolationValidated=false` remains. Unknown external effective aura magnitude is not permission to overwrite, and a managed receipt is not server acknowledgement.

Historical do-not-replay operations remain frozen and explicitly incomplete at their original identities, including the old combined native map and old successor package. New calls follow the published prospective amendment: exact provider-uncertain refusal permits at most3identical total attempts, with every outcome preserved; no disguised route or ambiguous mutation retry. The collection red retention's first2refusals/thirdsuccess and the caller read's firstrefusal/secondsuccess are retained as observed intermittent outcomes, not proof of their causes or earlier execution.

No local project build/test, production CB access, native/game/executable/IDB mutation, subagent, force-push, master write, merge/deployment or unchanged CI rerun occurred. Numeric preparation remains **3historical/0new/0remaining**. All original policies and excludedPR58/25/W80/W92 boundaries persist. Current self-review is not independent acceptance. CoS release is a separate actual handshake after completion, not a label inferred from this checkpoint.
