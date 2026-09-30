# Smaller continuation: prerequisite metadata and source membership

PR61 is merged and deployed. Reviewed head `3f5715b6cc292b2086946a80471c4145471cbdd7` was merged as master `3bc97e1e0b446aede269f7414c0c7c6358fdc192`. All 429 deployed runtime files match the merged package; 29 files were added/replaced, 400 already matched, and 2,004 unrelated local files remained unchanged. All 13 actual production runtime components compiled against the deployed host. `PR61-deployment-manifest.json` and `PR61-production-verification.json` retain those completed results. The merged archive SHA256 is `efef84bf6683a803b21184770aac7b5707e1ab940d1212002fae3d0acbbdc661`.

This continuation starts from that deployed master in `D:/Dev/CB-QuestDependencies-20260930`, branch `audit/next-quest-dependencies-20260930`. It changes the external prerequisite metadata exporter and source classification, retains the existing runtime C# owners, and leaves the deployed PR61 milestone unchanged. The containing Git commit is the continuation candidate; exact-commit acceptance receipts must be read from `D:/Dev/CopilotBuddy-Evidence/postmerge-20260930-pr61` before making a publication or validation claim.

## Repaired omission

The repair exporter returned early for protected baseline quests before requesting metadata for their external predecessors. Protection against overwriting proven quest content unintentionally prevented independent prerequisite facts from being supplied to the existing runtime dependency catalog.

Protected quests now request that metadata only after primary fields and the exact primary predecessor membership agree. Existing complete-negative-group and conflicting-base-group rejection remains intact. The new source owner implements pinned TrinityCore `ObjectMgr.cpp:5328-5347`: positive non-breadcrumb direct predecessors and incoming positive `NextQuestID` relations. Negative direct predecessors remain active-quest requirements, handled independently by the runtime. `Player.cpp:15232-15320` establishes the separate direct/dependent checks and full negative-group requirement.

The pinned revision is `95657f54779467effea8a1749a61ff93abc1d707`; ObjectMgr.cpp SHA256 is `67884417a66f8ce294f73e4cb88f728316bba02309b365d69104e40c0b3f8f69`, and Player.cpp SHA256 is `36a817da38b318740ee5aa31f256518175dfdd02d1dd7a68ebb0805fc43275d8`. Exact SQL row locations and incoming-edge identities are retained per quest. This is reference-server evidence, not proof that the customized realm uses identical prerequisites.

The pack adds 11 records, increasing dependency metadata from 358 to 369. Every old record is unchanged. All other repair families are identical: 14,080 reference points over 689 entries, 645 deliveries, 154 supplemental acceptance-supply contracts, two relation additions and the existing quest metadata. The base 4,335-quest model and the effective quest/spawn model are unchanged. The three recipes for quests 9066 and 9447 are unchanged; only their exact repair-pack binding is updated and their real lifecycles are rerun. See `repair-delta.json`.

Under controlled observations, the metadata closes the sole remaining source obligation for quests **1271, 3129, 3444, 11501, 12325 and 12577**. These records do not create schedulable quests or fabricate rewarded history. Inventory, completion-authority, eligibility, ownership, navigation and recovery gates continue to decide actual execution.

## Stronger evidence and exact ledger

The full primary predecessor-membership comparison found 130 source/model disagreements. Sixty-seven rows were already DATA-INVALID/INCOMPLETE, 53 were already SOURCE-UNCERTAIN, and ten previously GENERIC-PROVEN rows now require source reconciliation: **1106, 9837, 11123, 11313, 11613, 11978, 12188, 12224, 12225 and 12328**. The exporter records both missing primary dependencies and unconfirmed model dependencies, preserving the actual runtime order. It does not silently overwrite potentially customized base relations. Membership agreement alone does not prove ordering equivalence or realm completion.

| Classification | Deployed PR61 milestone | This continuation |
| --- | ---: | ---: |
| GENERIC-PROVEN | 3,004 | 3,000 |
| STRATEGY-PROVEN | 2 | 2 |
| DATA-INVALID/INCOMPLETE | 945 | 945 |
| SOURCE-UNCERTAIN | 310 | 314 |
| LIVE-ACCEPTANCE-REQUIRED | 16 | 16 |
| UNSUPPORTED-SCRIPTED | 58 | 58 |
| Total | 4,335 | 4,335 |

The 16 transitions are exactly six newly proven quests and ten reopened quests. Remaining coverage is **1,333 IDs**, compared with 1,329 at the PR61 milestone. That increase exposes stronger evidence rather than concealing disagreements. All six primary categories remain mutually exclusive; secondary obligations never enter the top-level total.

The new exporter can consume its own prior closure ledger while retaining secondary provenance. It requires an explicit full baseline commit, and every row here names deployed master `3bc97e1e0b446aede269f7414c0c7c6358fdc192`. The baseline bytes were compared with that exact Git blob. The prior PR61 artifacts are unchanged. `classification-ids.json`, `classification-transitions.json`, `remaining-category-ids.json`, `coverage.json` and `quest-ledger.jsonl.gz` are regenerated together. All 1,333 unresolved IDs have updated source correlations; the new uncompressed ledger SHA256 is `842c93711631e0de9ad7c661a5862e3e94af43102beca48c1db018dc7ce99674`.

## Reproduction and acceptance

The new tests first reproduced the protected metadata omission and unsupported continuation format. Twenty dependency tests and eight continuation/provenance tests were added. The complete analyzer run has 237 tests, with one expected Windows permission skip. Focused actual-runtime tests cover external dependencies, knowledge loading, repair validation, supplemental supply and delivery ownership.

The actual Windows/x86 dataset owner completed **258,269 checks** over all 4,335 IDs, and both vetted strategies completed **39 lifecycle checks**, with zero assertion failures. Initial diagnostic invocation passed multiple group names as one comma-joined argument and was rejected before those groups executed; its failure is retained. The corrected invocation passed each group separately, reused the verified successful build and passed. Final candidate runs use the normal committed runner, not that failed invocation.

`closure-fixture-manifest.json` binds the current four knowledge files, unchanged controlled observations and the exact expected simulation/model/dependency/strategy outputs. The runner selects this retained fixture directory through the actual knowledge manifest. Run the following at the clean candidate to obtain exact-SHA evidence:

```powershell
python Tools/EvidenceAudit/run_quest_closure_335.py --repo . --runtime 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe' --output <new-external-result-directory>
```

The hosted Windows/x86 full-quest workflow runs the same owner checks. Preserve complete source identities, exact run/job/SHA/results and the full integrated checkpoint. The full local pipeline has 34 commands with a fresh W42 extraction; hosted integrated validation has 17 suite records. This Python/knowledge-only continuation triggers three workflows: integrated, host and quest closure. The separate observation-owner workflow also requires matching C# or owner-test paths, which this change does not touch; that owner suite still runs inside integrated validation. A final green claim requires actual receipts at the containing candidate commit.

The first exact candidate, `31d1079514ad5879fcdfb46b8fbb4cd3f1d18e13`, passed all 34 local commands and the hosted host/quest-closure jobs. Hosted integrated run `36659789834`, job `109711778464`, passed every C# suite but exposed two fixture assertions comparing resolved paths against Windows short-name aliases (`RUNNER~1` versus `runneradmin`). Both expected paths now use the same canonical path identity as the function contract. Path containment, traversal rejection and runtime behavior are unchanged. The failed run and raw analyzer output remain retained; final acceptance is rerun at the corrected commit.

## Remaining audit work

Continue from the exact 1,333 IDs. The 130 dependency-source disagreements now have explicit edge evidence and must be resolved through exact semantics, preserved customized-realm evidence or an appropriately supported model extension. Other unresolved families include geometry, acquisition routes, mismatched item ownership, unmodeled server conditions and explicit scripted execution. Use the per-quest secondary flags and correlations rather than adding overlapping family counts.

The primary live-required category contains 16 rows, but neither the other classifications nor a simulation pass establish actual live realm completion. Live spawn/phase/path availability, interaction acknowledgement, inventory supply and final quest-ready observations still require real observations where applicable. IDA localhost:13337 refused the last build12340 health check; no new unverified client API is introduced. Continue with original-3.3.5 authority, retain both vetted strategies only, and keep subsequent PRs logically scoped.
