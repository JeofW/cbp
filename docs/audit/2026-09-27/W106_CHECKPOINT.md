# W106 — existing equipment acknowledgement verified

27 September 2026. The pre-existing equipment acknowledgement repair is implemented and hosted-verified through W106. Final recovery review of retained native receipts identified a further actionable spell-observation/terrain-return slice, which continues as W107; overall actionable scope is not yet closed. Remaining live/client/server/independent acceptance and deliberate deferrals are enumerated in `W106_PROTOCOL_AND_SCOPE.md`. This is not full PR51 acceptance, merge readiness, every-quest support or a claim that no future defects can exist.

## Exact frontier

Repository `JeofW/cbp`, id1367174964, PR51 OPEN/DRAFT; only approved branch `audit/next-55-equipment-observation-20260917`. Workspace `D:\Dev\CopilotBuddy-PR51`; external evidence `D:\Dev\CopilotBuddy-Evidence`. Master remains `b2324913e2499ba30b239dd67224ca2c655c05cc` at the latest successful publication readback.

| Role | Commit | Tree |
|---|---|---|
| Validated source and final test adapter | `1db1454fa7cccf7daa71dddcf1e99f93ed7c00ad` | `9ff2fab1a3961b3d837fb01d07bd264a92de0f96` |
| W106 production repair | `941667d89eda29f3f66b7ca41d7063ffc5717036` | `c5d4d6346870398b0b5b45cf27cd177cafac88e3` |
| W106 test-only behavioral red | `12ac96f2dc125e1dc1405fca55c1dde47b040b0c` | `59282761dc17c86ef3498e12bf71d54ab3288858` |
| Prior W105 documentation checkpoint | `f9d73a2852be069e7840955eb8cbccf6908242c9` | `b9b772e9dd0292d35f796d464eb259c85300a044` |

The containing commit is documentation only. Its final SHA/tree are in external `W104_ALREADYDOCS_PUBLICATION_20260927.json` and `LATEST_CONTINUATION.md` after publication. Do not substitute it for the validated source.

## Proven defect and repair

`LocalPlayer.CarriedItems` includes equipped objects. Initial EquipItem admission could choose its already-equipped GUID, attempt bag pickup, fail to find that GUID in a bag and discard pending state/deadline. Every later tick could repeat the same setup. AutoEquip could likewise encounter an already-equipped candidate and start another pickup or by-name request. The existing equipment acknowledgement helper knew the exact physical GUID result, but initial admission did not consult it.

Both owners now check `IsPendingEquipAcknowledged()` after capturing and validating pending actor/item/slot identity and before any cursor setup or by-name request. A satisfied result keeps the captured pending state and original deadline for the next ordinary acknowledgement/cursor-release tick. A foreign or unknown cursor still prevents early completion and never acquires mutation authority. The code also rechecks the existing context after the new equipment observation before setup.

This adds exactly12 lines, including comments, across `runtime-snapshot/Quest Behaviors/EquipItem.cs` and `runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs`. It changes no client API, native ABI, slot mapping, inventory scoring, quest credit, pickup/cleanup helper, deadline, GUID comparison, synchronous pending-index rule or return-buffer lock. Already-equipped acknowledgement does not expand by-name transfer ownership into duplicate-copy selection or arbitrary equipped-to-equipped movement.

## Actual red, correction and green evidence

| Stage | Hosted integrated result | Meaning |
|---|---|---|
| Test-only12ac96f2 / run36306515764 | 16/17; new acknowledgement10/18 | Eight intended assertion failures, zero unexpected errors: four already-equipped cases per owner. All builds and the normal/foreign/missing/revoked controls pass. |
| Production941667d8 / run36307025152 | 16/17; new acknowledgement18/18; old continuation45/46 | The new behavior passes. One older restart fixture still reported the same carried GUID already equipped while requiring a new pickup. This was a contradictory controlled observation, not a reason to remove the new guard. |
| Test adapter1db1454f / run36307402619 | **17/17; acknowledgement18/18; continuation46/46** | Three added fixture lines supply another destination GUID before the fresh-restart request. Every existing assertion and all new18 cases remain unchanged. |

The first test-only commit also corrected the continuation fixture's initial world: it begins with a different item equipped and supplies the exact requested GUID only after the initial request. This preserves the meaning of its normal-request and lifecycle assertions. The final adapter corrects the analogous restart setup. Neither change relaxes an assertion or replaces a production return value with a fabricated success.

Final integrated run **36307402619**, job **108586618955**, artifact **10927807991**: all17 aggregate groups pass with no game attached. Archive931001 bytes; SHA256 **`4bedeb551e955b4073da554f0b42616afb93fb35a9c4fcbdb99ff3edf4770ebd`**, matching metadata. All215 archive members/214 internal hashes verified. All1852 source inputs match the validated checkout.

Final host run **36307402690**, job **108586619076**, artifact **10928196158**: Release/x86 build **0 errors,3344 warnings**; tests_run=false,game_attached=false. Archive83069 bytes; SHA256 **`23f0e7b20c83e7126bf84f9adcb8f679079a839c159394cf95aeb871b2c0c02c`**, matching metadata. Actual result.json and build summary inspected. The warning count is retained, not represented as a warning-free build.

Red and candidate host builds also pass0errors/3344warnings. All six stage archives, including both failed integrated runs, remain retained. `W106_EVIDENCE.json` records their run/job/artifact/outer-hash/inspection identities and actual behavioral summaries.

Across original red and final validated source, all1852 captured input paths remain; only the two production owners and the one continuation fixture differ. Of170 normalized members,168 are identical; the continuation fixture and its normalization manifest change. The new acknowledgement fixture is byte-identical from red through final. Do not claim every fixture or whole artifact is identical.

## W104 and W105 remain validated

The interrupted turn had already completed W104 equipment ownership and W105 combat fixes. Recovery reconciled their actual commits and archives instead of recreating them. Final W106 evidence retains:

| Boundary | Final result | Evidence level |
|---|---|---|
| Pre-existing exact equipment result, both owners | 18/18 | Actual tracked admission/tick/ack/context/cleanup; controlled observations |
| W104 cursor cleanup/lifetime | 57/57 | Actual owner/generated Lua5.1; controlled cursor transitions and native admission |
| Synchronous pending-index ownership | 24/24 | Actual generated Lua5.1 with controlled inline events |
| Full physical GUID guard | 8/8 | Actual emitted instruction contract; no game execution |
| Equipment Lua/continuation/contexts | 87/87,46/46,37/37,57/57 | Existing actual-source controlled boundaries |
| W105 start-only autoattack | 26/26 | Actual Common/LocalPlayer Lua and TreeSharp; controlled native attack state |
| W105 targeting/sight/facing movement | 85/85 | Complete tracked Movement and TreeSharp; controlled world/navigation |
| W105 cast-time facing | 16/16 | Actual WaitForCast overloads and owned facing child; controlled casting |

Read the retained W104_VERIFIED_CHECKPOINT and W105_CHECKPOINT for their earlier red/green sources and limitations. W103's physical GUID guard, synchronous pending-index handling, return-buffer lock and all previous actor/quest/deadline protections remain. No repairs, tests or receipts were recreated to recover an interrupted stream.

## Remaining requirement assessment

Fresh source reads confirmed the R01 disabled provider, R03 build/dataset-bound recipe admission and its deliberate raw-counter/unsupported-materializer deferrals, R07 bounded refresh policy, and current equipment and gossip owners. Original-client IDA plus pinned TrinityCore3.3.5/AzerothCore serialization establish that gossip responses lack a per-attempt request nonce; a menu ID is not request provenance. By-name item selection also remains a separate native lookup contract. Exact functions, source paths, revisions and remaining inputs are recorded in `W106_PROTOCOL_AND_SCOPE.md` and hashed in W106_EVIDENCE.json.

Further retained-receipt review identified two actionable reader mismatches for W107: the terrain-click function defines its boolean return in AL while the wrapper tests all of EAX, and the client pending-spell getter reads a pointer at0xD3F4E4 then the spell ID at+32 while LocalPlayer still uses two different direct fields. These require hosted behavioral red and bounded repairs before closing actionable scope. Other remaining work is explicit: first-menu origin/exclusive interaction evidence; by-name duplicate-copy/broader-transfer contract; contained dense-pack isolation route/lifetime work; unsupported quest recipes/raw-slot mappings; deliberately excluded general plugin-refresh extensions; and original-client/server/native/independent/supervised acceptance for the retained behavior. The earlier intermittent W105 publication stack-provenance assertion remains undiagnosed, with its unchanged predicate and improved failure diagnostic retained. It is not declared repaired by a later passing run.

Actual terrain/Z/lifts/cliffs/water/LOS, taxi reachability and acknowledgement, gathering/remount, stun/threat continuity, native inventory changes and server quest credit need an authorized development client/server acceptance environment. An open IDA database and controlled hosted observations do not provide that environment. Neither these limitations nor source green relaxes a mandatory merge gate.

## Custody and continuation rule

Publication receipts are `W104_REDALREADY_PUBLICATION_20260927.json`, `W104_GREENALREADY_PUBLICATION_20260927.json` and `W104_ALREADYRESTART_PUBLICATION_20260927.json`; the historical W104 label prefix is the retained helper's namespace. Each verifies expected parent, exact paths/blobs/decoded remote bytes/tree and direct refs. `W106_EXECUTION_20260927.md` preserves the exact one refused inline local ZIP query and the separately successful official hosted-evidence operations. No refused request is relabelled successful, and no global connector recovery claim is made.

This equipment implementation slice is complete at the stated evidence level; W107 and full PR51 acceptance remain open. Continue from the reconciled latest frontier with the two demonstrated reader mismatches before closing actionable scope. Do not loop on unchanged blockers, replay rejected transactions, restart W80/W92, or recreate completed W104–W106 work.

No subagents, local project code/build/test, production CB access, game/IDA launch retry, executable/IDB mutation, PR58 recreation, excluded PR25, force-push, master write, merge or deployment. Numeric preparation remains **3 historical / 0 new / 0 remaining**. Preserve all policies, assertions, acceptance and merge gates. Direct self-review is not independent review. Session finish, when invoked, is recorded externally after actual implementation completion; it does not certify missing live acceptance.
