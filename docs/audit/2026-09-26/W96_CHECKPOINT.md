# W96 — actual compiler-input cache identity verified offline

26 September 2026. JeofW/cbp ID1367174964, open/draft PR51, branch audit/next-55-equipment-observation-20260917. Full audit/Goal remain in progress. No subagents.

## Repair

Plugin cache identity now includes actual resolved reference paths/content and effective compiler options in addition to source/resx bytes. SourceCompiler shares reference resolution and option creation between identity and compilation. Source preparation is idempotent. Rejected reference candidates still contribute their content, preventing changed corrupt inputs from silently reusing stale types. Metadata validation disposes each image reader before continuing; content hashes stream file bytes.

Unchanged inputs reuse compiled types and their static state while constructing fresh instances. Changed inputs require compilation and a new type/static state. Failed compilation preserves the last valid cache entry; restoring those inputs permits reuse. Required preparation/construction failures preserve the active set. See [plugin refresh policy](../../plugins/REFRESH.md) for explicit runtime reload and failure limits.

## Exact source and hosted evidence

Prior W95 documentation9c01a5dc1a05c622edcfce1434871e557e380da6. Red-only2752b4d25f9935ccddf5a088648ae657557b00aa; first production candidated2c1a36f0b714f43b47b5b0753422470729d34a0; corrected production007ceff9b6eeceff36b04507ae036cd8b6deb950 (treead468c45fd9e39240d4e6e50fd3715359f5a2aa6). Final tested sourcebcb962ed06631978f41347878c5c123594d4e27b (tree5b88356525729fc2d2805b376edaf354fef47cf4) adds only two option-identity tests. This checkpoint's containing publication is documentation-only.

- Red integrated36223759044: new compilation-input cases3/8,5 intended assertion failures,0 unexpected; aggregate16/17. Host36223759033 passed.
- First candidate integrated36224197180: new cases8/8, but retained reuse8/9 with an unexpected x86 OutOfMemoryException in metadata allocation; aggregate16/17. Host36224197181 passed. This failed intermediate is retained, not accepted as green.
- Corrected production integrated36224676389: original new cases8/8 and retained reuse9/9,0 assertion/unexpected failures; aggregate17/17. Host36224676390 passed. The correction removes the additional retained Roslyn metadata allocation from fingerprinting.
- Final test supplement integrated36225027541/job108357269457/artifact10900766494: compilation-input8/8 plus compiler-identity2/2, all17/17 groups pass. Host36225027471/job108357269185/artifact10900895254 passed.

Eight original cases use public RefreshPlugins and actual SourceCompiler with a generated referenced DLL: fresh instances/retained statics, DLL-only constant change with restored timestamp, missing/corrupt dependency, restored valid cache, dependency-induced constructor failure, source edit/static reset and effective optimization compilation. The two follow-up cases directly check option changes/restoration, exclusion of random output names and successful compilation after identity preparation. These two follow-ups have no separate assertion-level red; do not include them in the original red/green claim. Public Refresh has no mutable compiler-options API.

All project execution was GitHub-hosted Windows/x86, with no game attached. Each host build was Release/x86,0 errors/3344 warnings, tests_run=false. Original red/candidate/green integrated archives each have204 members/203 verified inner hashes and1841 source inputs. Original red to corrected green: all159 normalized members byte-identical; only SourceCompiler.cs and PluginManager.cs inputs changed. Final supplement:205 members/204 inner hashes,1842 inputs, all1842 hashes matched local files; production identical to corrected green. Normalized member count160; only the new fixture and generated manifest/driver differ. All eight outer archive digests matched GitHub metadata. Host archives each have4 members/no inner manifest; no separate ZIP CRC check claimed. Exact IDs, bytes, hashes and comparisons are in W96_EVIDENCE.json and external W96_RED_GREEN_COMPARISON_20260926.json/W96_SUPPLEMENT_COMPARISON_20260926.json.

Retained final results: cached7/7,lifecycle12/12,fresh8/8,reuse9/9,preparation6/6,reward Lua33/33,lifetime18/18. The older reuse log's 'no Roslyn' denotes controlled compilation; cache checking now prepares source/metadata without invoking Roslyn compilation. Assertions were not weakened.

## Limits and next work

Before/after input observations are not an immutable filesystem snapshot. Changed compile-time constants establish recompilation, not replacement of an already loaded runtime dependency. The default load context does not unload old assemblies; restart after replacing possibly loaded dependencies. Arbitrary callbacks, cross-thread/reentrant operations and activation-side-effect rollback are not transactional. Old plugins are still retired before replacement activation; failed activation does not restore them. Direct self-review is not independent acceptance.

Next R04: exercise the real equipment-owner cursor/popup/lifecycle boundaries and establish conservative ownership using source-backed evidence. External R04_R06_FRAME_XML_RESEARCH_20260926.md and FrameXML-12340-9640af74/PROVENANCE.json retain mirrored source explicitly labelled Build12340 at commit9640af74c40affd56fd46d6815917f96fc5c9540. The 3.3.5 tag was Build12213 and is comparison-only. Build12340 UI code confirms slot-valued bind dialogs and cursor/gossip events, not physical cursor GUID, native ABI, event ordering or server acknowledgement. Do not invent native calls from offset symbol labels. R06 same-content close/reopen remains separate from W88 content comparison.

R03 independent special-action credit mapping/native recipient request/ack remains unproven. Keep strict v1/ObjectiveProgress deferral, QuestComplete, CAST refusal, independent collection and W91/W92 protections. R04 physical/same-entry foreign cursor and same-slot popup, R06 generation/native concurrency, R07 concurrency/activation limits, R08 shared native/client/server and all wider independent/supervised gates remain open. Offline success is not whole-audit completion.

## Publication and retained constraints

Only ordinary fast-forward publication on the approved PR51 branch, against expected parents with exact parent/path/blob/tree/head read-back. Documented process-scoped GIT_LFS_SKIP_PUSH only for proven non-LFS files, preserving hooks and restoring environment. Historical provider refusals retained; no blocked combined request replay. Numeric preparation3 historical/0 new/0 remaining. Direct masterb2324913e2499ba30b239dd67224ca2c655c05cc unchanged.

Retain W95/W94/W93/W92/W91/W90/W89/W88/W87/W86/W80 evidence/ledger, backupsaca2f1cd/f7a598ad and all failed intermediates. Preserve W93 reward numeric1/0 receipt, UTF-8 bytes.Length and scoped workflow guards; W94 preparation, W95 lifecycle, R01/R02/R05 dispositions, MIR/navigation/lifetime protections, dense-pull containment and auction withdrawal. Retain HANDOFF_POLICY.md, WOTLK_335A_RESEARCH_POLICY.md, TRINITYCORE_335_COMPATIBILITY.md, QUEST_DATA_PROVENANCE_335.md and ADDON_EVIDENCE_335.md. No W80/W92 restart, PR58 recreation, excluded PR25, guessed offsets/recipes, weakened assertions, local project execution, production access, force-push, master write, merge or deployment. Original3.3.5a/build12340; TrinityCore3.3.5 primary/AzerothCore WotLK secondary. Checkpoint each verified slice.
