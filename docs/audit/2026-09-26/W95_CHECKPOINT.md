# W95 — plugin lifecycle failure state and cleanup verified offline

26 September 2026. JeofW/cbp ID1367174964, open/draft PR51, branch audit/next-55-equipment-observation-20260917. Full audit/Goal remain in progress. No subagents.

## Repair and evidence

Red test-only f0ebc5a5a91802668c4548b21c626ab5d86904d6 follows W94 documentationf9ac8698. Repaird917c90c11a0b416d3052bd45531902534f23000, treedc60e248fe044da93ce70bb34f85d47a72b0b39b, changes only Styx/Plugins/PluginContainer.cs. This checkpoint's containing publication is a documentation successor, not another tested source.

Previously an Initialize/OnEnable exception left Enabled=true, prevented a new enable attempt and allowed Pulse to call a failed plugin. OnDisable failure skipped Dispose. PropertyChanged ran before lifecycle work, letting observers see premature success or interrupt cleanup. The repair restores false after activation failure, cleans up partial activation, guarantees disposal through a finally, and notifies the completed state. Settings-list update remains in notification finally. Existing successful callback order and repeated-assignment behavior remain.

Twelve new cases use the actual public container, public RefreshPlugins, manager Pulse and SourceCompiler with generated plugins. They exercise healthy idempotence; Initialize/OnEnable failures; retry; OnDisable/Dispose failures; failed-activation cleanup failure; notification timing/failed-binding recovery/throwing observers; failed replacement pulse suppression and old-plugin disposal during refresh. Generated plugin types do not alter the retained test assembly's complete-type fixture. No installed plugin or game is executed.

- Red integrated36222740538/job108350921858/art10898844556: all builds passed; aggregate16/17; lifecycle2/12,10 intended assertions,0 unexpected. Host36222740571/job108350922012/art10900030678:exit0,3344 warnings,0 errors.
- Green integrated36223013392/job108351682376/art10900190012:aggregate17/17; lifecycle12/12,0 assertions,0 unexpected. Host36223013395/job108351682327/art10899199346:exit0,3344 warnings,0 errors.
- Both retain W94 preparation6/6, plugin cached7/7/fresh8/8/reuse9/9, W93 Lua33/33 and reward lifetime18/18.

Actual GitHub-hosted Windows/x86 execution; all game_attached=false. Host is compile-only, tests_run=false. No local project builds/tests. All four archive digests match GitHub metadata. Each integrated archive has203 members/202 verified inner hashes and1840 source inputs. Only PluginContainer.cs differs red to green; all158 normalized fixture members are byte-identical. Host archives4 members/no inner manifest; no separate ZIP-CRC check. Exact hashes/sizes/receipts are in W95_EVIDENCE.json and external W95_RED_GREEN_COMPARISON_20260926.json.

## Limits and next work

Direct source/fixture self-review performed; not independent acceptance. These tests establish settled lifecycle outcomes and cleanup. They do not establish cross-thread/reentrant atomicity or reverse arbitrary plugin side effects. The existing refresh order still retires old plugins before replacement activation; W95 does not preserve the old set after a replacement's activation failure. Construction/preparation rejection preservation from W89/W94 is a separate verified contract. Do not claim complete atomic refresh or close R07.

Next: reproduce dependency-only cache staleness through public RefreshPlugins with actual SourceCompiler references. Include actual resolved references/effective options in cache identity using shared compiler logic, preserving caching, prior valid entries and unchanged existing assertions. Explicitly test/document fresh instances versus retained static state and the default load context's dependency-reload limits. Read external R07_COMPILER_INPUTS_INVESTIGATION_20260926.md for inspected source and a concrete DLL-only regression design. It is investigation, not implemented/accepted behavior. Original W80 R07 asks for actual inputs/reload semantics and prior-set preservation on required construction failure; avoid a broad plugin framework.

R03 mapping still needs independently source-backed credit identity and native recipient request/ack proof. Preserve strict v1 and ObjectiveProgress deferral, W91 ordinary matched credit/publication, W92 lifetime, QuestComplete, CAST refusal, independent collection and navigation guards. R04 physical displaced/foreign cursor/same-slot popup/lifecycle, R06 same-content generation/native concurrency, R08 shared native/client/server and wider Wholesome/navigation/Singular/independent/supervised gates remain open.

## Publication and retained constraints

Both source commits and documentation are ordinary fast-forward publications to the approved PR51 branch only, reconciled against expected parents and read back for paths/blobs/trees/head. Source-only pushes used documented process-scoped GIT_LFS_SKIP_PUSH=1 after non-LFS checks, restoring environment and retaining hooks. Historical provider refusals unchanged; no blocked combined request replay/new refusal. Numeric API-preparation budget3 historical/0 new/0 remaining.

Retain W94/W93/W92/W91/W90/W89/W88/W87/W86/W80 checkpoint/evidence/ledger, all earlier failed intermediates and backups, HANDOFF_POLICY.md and the four original-client/core/provenance/addon policies. Preserve W93 numeric reward receipt/UTF-8 size and scoped workflow fixes, R01/R02/R05 dispositions, MIR/navigation/lifetime protections, dense-pull containment and auction withdrawal. No W80/W92 restart, PR58 recreation, excluded PR25, guessed offsets/recipes, weakened assertions, local project execution, production access, force-push, master writes, merge or deployment. Direct masterb2324913e2499ba30b239dd67224ca2c655c05cc remains unchanged. Original3.3.5a/build12340; TrinityCore3.3.5 primary/AzerothCore WotLK secondary. Checkpoint each verified slice and keep exact tested source distinct from documentation successors.
