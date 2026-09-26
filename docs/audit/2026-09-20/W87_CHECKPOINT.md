# W87 — finite inventory scans and busy-state continuation verified

20 September 2026. Repository `jeofwong/CopilotBuddy-private` (1367174964), draft PR51, branch `audit/next-55-equipment-observation-20260917`.

## Exact verified state

Tested code/test head **db4a16beca29b6decf1b580f53343aa19c4321b7**.
Tree **1aff8741017c2f7127643e38f7539aeb65219f90**.

Integrated **35503906697/art10602953497** passes **17/17**; MIR finite scan **27/27**, assertions0, unexpected0. Host **35503906699/art10603700944** exits0 with **3344 warnings/0 errors**, compile only. Both job conclusions are success. Documentation successors are not newly tested production revisions. No game attached.

Integrated SHA256 `7bd8983e43b729c5198731a5d9630529890fdf3eeb05c355f10920ad84382758`.
Host SHA256 `c407752e9461ee80b1ef3eb065d5b92dc8ed1c17127c96d80a7943d5dcf807d7`.

Read the four original-client/core/provenance policies, `docs/audit/HANDOFF_POLICY.md`, W86 and W80's completed review. Original WoW3.3.5a/build12340, TrinityCore3.3.5 primary/AzerothCore WotLK secondary. Keep questing/navigation/Singular and directly related safety scope. No AuctionHouse/ProfessionBuddy expansion or invented Gordunni/escort recipe.

## Reconciliation and duplicate-test correction

Entry **6ccba96775d6f1fb46b5167d2635081aa306b5af** was already a test-only scan commit newer than W86's root handoff. W86 had saved the instance/run/player/operation deletion guards and the mandatory handoff policy. Those changes were retained, not recreated.

This continuation initially added a redundant harness at **e3ed01c0e5f929815dddbc3b89fb88ad8b78e238** before recognizing the existing scan test. It was removed, and only that file removed, at **b065b5e090e32857c2ecf7f72cfc74998ee21d3b**. The resulting tree **91aac55947fb04a4537409c84ab252cc61b951d4** is exactly the entry tree. The two history commits remain; no production or existing test changed in that correction. Reuse `MrItemRemoverScanRegressionTests.cs`; do not re-add the duplicate.

## Pair A — one trigger lost later inventory candidates

The previous scan returned after its first BeginDelete attempt, including refusal, and Pulse consumed the trigger. Once pending tracking ended, later candidates needed another timer, loot or manual event. A returned/refused first item could also monopolize repeated passes.

The already-saved test-only **6ccba967** executes the tracked Pulse, CheckForItems, selection/protection and deletion lifecycle together with controlled inventory, timer, list-file and recording-Lua boundaries. It does not execute native deletion or simulate a server acknowledgement.

Clean red **35501635902/art10602283253**: **10/22,12 intended assertions,0 unexpected**, other16 integrated entries passing. SHA256 `7004a610e107b95dc0e765371671bf1c2e6f78cb73ec6b2eb3a21309136adfa3`.

Production **7f28d5fd83374f429c4cb777407469ccf4d00fd1**, tree **694f78e941579cfb37e1f56949c6eff916048c79**, changes the two existing MIR partial-class files only:
- Keep one captured, deduplicated original candidate queue for a triggered pass. Dequeue before visiting; refused or returned candidates do not loop or starve later candidates in that pass.
- Finish the existing pending transaction before the next deletion attempt. Pulse makes at most one new deletion attempt; it does not immediately rescan the same inventory.
- New arrivals wait for a fresh trigger. Reordering retained candidates does not lose them; replaced item objects are not borrowed into the captured pass.
- Bind the pass to player reference/GUID and the existing run lifetime. Recheck after yielded observations and diagnostics, and before the existing item actions. Stop/start and disable revoke the old queue.
- Unknown inventory ends/defer local scanning without a deletion-success claim. Keep the existing quest-item, keep-list and bag-list exclusions and removal predicates. Preserve manual opening when removal is disabled.

Green **35503301689/art10603395909**: **22/22 and17/17**. SHA256 `ff92835d45e52b219413b13c3ce14fe2759a030b4357e381996ee0a0dba102af`.
Host **35503301695/art10603246028**: exit0,3344 warnings/0 errors; SHA256 `a893a9f17edaa093bcedd10afa2e99c47eb91e21e8644b9ba37fc8148b16e9a5`.

All **153 normalized members are byte-identical** across the primary red/green pair. Both working-input manifests have1835 paths, differing only in `Methods.cs` and `MrItemRemover2.cs`. Both integrated archives pass CRC and all197 inner hashes. The add/remove duplicate-test correction does not alter this comparison because its net tree is identical to the red entry.

## Pair B — review caught discarded work during temporary busy states

The first repair passed its22 cases, but follow-through found another issue in that repair: rejecting a scan during combat, casting or pause discarded its queued trigger or remaining candidates. Such a temporary busy state should postpone scan work, not make another event necessary.

Test-only **0f4a072f4d9f40a41834eeadc98c0d2dce6ebe70**, tree **c3cf0b4bdd3e51af40f49d3ab58803de58c57ec7**, adds five actual Pulse transition cases. All22 original cases and the controlled boundary are unchanged. Tests cover an initial loot trigger through combat, manual triggers through pause/casting, and the remaining pass through combat/pause.

Clean red **35503627665/art10603166887**: **22/27,5 intended assertions,0 unexpected**, other16 entries passing. SHA256 `f922ef8d5614bc709a349c0f7f5fe8a829116f44f45cb8af0467a3ac877c4df7`.

Production **db4a16be** adds only a nine-line comment/admission guard to Pulse. It postpones scan/trigger consumption while the current valid player is temporarily busy. Existing pending-deletion processing stays before this guard, so unsafe pending requests are still revoked by their established context checks. Stop/world/player invalidation is not replaced by a busy-state exemption.

Final green is **27/27 and17/17**, with the exact head and artifacts above. All153 normalized members remain byte-identical across **0f4a072f -> db4a16be**; only `MrItemRemover2.cs` changes among1835 inputs. Both integrated archives pass all197 inner hashes.

These are **two separate unchanged-fixture production pairs**. The whole6ccba967-to-db4a16be span is not an unchanged-fixture pair: five tests were deliberately added between the production pairs. Do not conceal that distinction or count fixture additions as production repairs.

## Preserved code and verification limits

Fourteen existing method bodies in Methods.cs remain byte-for-byte unchanged from the W86 entry: SellVenderItems, BeginDelete, CanDeleteNow, OwnsPendingDeleteContext, DeleteItemConfirmPopup, TickPendingDelete, PendingDeleteItemStillObserved, ReadOwnedCursorState, TryIssueDeleteRequest, TryConfirmPendingDelete, both delete Lua builders, ResetPendingDelete and IsQuestItem. ResetDeleteLifetime additionally clears the scan. Existing opening/combining expressions and selection predicates are retained inside the guarded scan; no new Lua API, native offset, gear weight or combat policy was introduced.

The source trace in ObjectManager (blobd18a0981c55102326f6234a71a7db9c60a65c072) reuses existing dictionary objects on ordinary updates; WoWObject (blob655948fd1782086e49fd25ac5395baa83d1ac2a7) caches observed GUID/entry. This supports the deliberate reference checks; it is not proof of complete native inventory visibility, absence of every concurrent replacement or an adversarial identity guarantee.

The candidate set is finite and individual deletion tracking retains its timeout. This is not a claim that every legacy opening/combining sleep or native call has a new wall-clock bound, nor that this is a fully nonblocking inventory engine. Shared cursor/popup ownership and supervised gameplay remain separate gates.

Final retained checks include MIR context82/82, deletion observation18/18, EquipItem context67/67, AutoEquip context46/46, reusable-item acknowledgement10/10, delete-popup request14/14, gossip lifetime17/17, reward-selection lifetime18/18, strategy Kind9/9/materialization16/16, collection restart34/34, normal objective restart37/37, Ret registration9/9, acknowledged equip timeout10/10 and popup submission18/18. Python analyzers:89 tests,OK. These groups retain their distinct source/pure/controlled-execution/compilation limits; no live game or independent reviewer is claimed.

The evidence package verifies seven original archives: W86 entry, primary red/green/host, busy-state red and final green/host. It checks outer hashes, CRCs, source identities, all inner hashes for five integrated archives, host compile results and both unchanged-fixture comparisons. The standard-Python verifier executes no bot/test binaries and makes no network calls. Archive verification is not a local C# rerun.

## Remaining work and conditional merge

W80's fixed-range source review remains complete. W84/W86/W87 now cover the named local R05 unknown-observation, mutation-context and finite-scan cases; they do not certify the full native deletion/UI system. Preserve the earlier fixes and all previous requirements, backup refs and exclusions.

Next priority: **R06 exact same-NPC changed-menu identity at final selection and cleanup**. Other open gates remain **R03 CAST scheduling and dataset/collection/raw-counter mapping/dispatch**, **R04 physical displaced/foreign cursor and same-slot popup/full-lifecycle ownership**, and **R07 compiler dependency fingerprints/static-state reload/full refresh publication**. Keep unsupported actions rejected rather than substituting ordinary kills or inventing special-item recipes. No new Gordunni/escort recipe or coordinates were added.

The user already authorizes merge only when all blockers and final exact-head validation/merge-preview gates are green. They are **not all satisfied**; no merge was performed. Do not ask for the same authorization again. When ready, reconcile direct refs, preserve backup, inspect the exact merge preview and use an exact-head conditional merge. Merge is not deployment/live acceptance.

Direct master remains **b2324913e2499ba30b239dd67224ca2c655c05cc**; W87 made no master write, force push, deployment or installed-file change. W77's disclosed historical documentation add/revert remains. Every publication requires explicit nonempty approved PR branch, expected parent/blob and reviewed content/message.

Always finish with a copy-ready new-chat prompt and a downloadable UTF-8 handoff, as required by HANDOFF_POLICY.md. Update AUDIT_RESUME/NEXT_CHAT_PROMPT and retain the checkpoint's tested head separately from its documentation successor.
