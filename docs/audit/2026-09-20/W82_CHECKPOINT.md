# W82 — strict cached and fresh plugin construction

20 September 2026. Repository `jeofwong/CopilotBuddy-private`, PR51, branch `audit/next-55-equipment-observation-20260917`.

## Exact verified coordinate

Tested production/test head **c46ede2c756c56618532c6af89932b73455bb14c**, tree **62f812bba43f4647cfa889518de24be7c53fcbd4**. Documentation successors are not new tested production revisions.

Integrated **35493042467/art10600005968**: **17/17**; cached construction **7/7** and fresh compilation **8/8**, both assertions0/unexpected0. Host **35493042471/art10599936166**: build exit0, **3344 warnings/0 errors**, compile only. All game_attached=false.

The user has conditionally authorised a merge after all review blockers and final gates are green. That condition is **not yet met**. Keep PR51 draft/unmerged; do not infer approval from this one green subsystem. Original WoW3.3.5a/build12340, TC3.3.5 primary and AC WotLK secondary remain fixed. No new Lua API, gameplay mechanics, installed-file change, master write, deployment or force push occurs in W82.

## Reconciled interrupted entry

The live branch was already at **eb256140e1a70a9e3a25c1d2410e1155412577d4**, tree d44b0ebb2186def9a55d25725f5c129a697a4694. It contained W81 and the test-only `PluginCachedConstructionRegressionTests.cs`. The PR description lagged at W80. W81 repairs were retained, not recreated.

Read W81 for recovered R01 containment, R02 reusable-item liveness, R04 AutoEquip context, R05 successful delete-request admission, R06 actor/NPC lifetime and R08 reward-selection identity. None of those narrow results closes every remaining subcase of its finding.

## Pair A — cached construction and nonempty partial compiler results

Clean red **eb256140**: run35491078980/art10599042808, SHA256 `cbbb85920178c81e85c64b189b0814c358621d1dc9f5458d01242562285167f0`. Actual PluginManager calls reproduce **1/7;6 intended assertions;0 unexpected**. All16 other integrated entries pass.

Repair **83190e180e2179e16bab289a27e9547b9cae5187**, tree475a3c2d9ce2cc6c7921f26c5f66305e57d1624d, changes only `Styx/Plugins/PluginManager.cs`:
- Construct the requested type set as one operation. On constructor failure, dispose only the newly constructed predecessors and propagate the original constructor exception rather than logging it and returning a subset.
- Cleanup exceptions do not replace that original error or dispose existing active instances.
- Reject and dispose a nonempty incomplete compiler result without overwriting the previous valid cache. Preserve the existing valid empty/no-plugin path and input-mutation cache policy for this narrow pair.

Green integrated35492518359/art10599980296, SHA256 `1e679f7538f31e762ccae1f7790b1426a25fbdc0d78fc55bf09f40bffce27a88`: **7/7;17/17**. Host35492518366/art10599970237, SHA256 `ad27ead5b023cbba73542654d57cce7da15197219595697e1461d2c375660f97`: exit0,3344 warnings,0 errors.

Both integrated ZIPs pass CRC and all **191 inner-manifest hashes**. Their input path sets contain1829 entries; only PluginManager.cs differs. All **147 normalized test members are byte-identical**. These cases execute actual cache/construction methods with separately compiled plugin types and controlled compiler results; no installed plugin set is loaded.

## Pair B — real fresh compilation must not turn failed constructors into empty success

Following the public call path showed that ClassCollection -> DynamicLoader -> DllLoader still logged and omitted failed constructors. If every constructor failed, the empty list could be mistaken for a legitimate source directory with no plugins. This is inherited DllLoader behaviour, not newly attributed to the cache.

New test-only **0d2029947048a37d852b4d1cb23d83c3d46f3feb** invokes the actual public PluginManager.CompileAndLoadFrom and actual SourceCompiler against temporary source. No compiler delegate is substituted. Tests cover healthy construction, no-plugin helper source, empty directory, missing path, C# diagnostics, all constructors failing, a partial set and cleanup failure.

Clean red integrated35492732190/art10600120287, SHA256 `0f0947ee27a18be268e28c20aae7b1d8049a9df8a47bce3da285fc251e478c80`: **5/8;3 intended assertions;0 unexpected**. Cached construction remains7/7 and all16 other integrated entries pass.

Repair **c46ede2c756c56618532c6af89932b73455bb14c** changes only the public CompileAndLoadFrom method. It uses the same SourceCompiler directly, preserves FileNotFoundException/CompilerErrorsException and valid empty/helper-only inputs, then invokes the already-tested strict construction helper. It does not change ClassCollection, DynamicLoader or DllLoader for other bot/routine consumers.

Green integrated35493042467/art10600005968, SHA256 `d25814c70c7253f8a4809512992513a7a036cd08d4eca25b6bf7e7c9dbe5a961`: **8/8 fresh;7/7 cached;17/17 integrated**. Host35493042471/art10599936166, SHA256 `de5317891f984d7299856db53e92a65ec25181fb632cfe8f45ed3d9fc729b25a`: exit0,3344 warnings,0 errors.

Both integrated ZIPs pass CRC and all **192 inner-manifest hashes**. Their input sets contain1830 entries; only PluginManager.cs differs. All **148 normalized test members are byte-identical**. Host archives contain four files, no inner manifest, and matching commit.txt/result.json; none is called a test run.

## Remaining gates — no blanket merge clearance

R07 is **partially repaired**, not closed. Actual compiler dependency fingerprints, static-state/reload policy and the complete refresh publication/initialization lifecycle remain separate from construction. The current source cache still hashes .cs/.resx only, still reuses compiled types, and retains its prior changed-input policy. PluginContainer still has inherited lifecycle exception handling; this turn does not certify arbitrary plugin side effects or rollback.

Keep the W81 remaining matrix: R03 loader/scheduler/materializer/index/target authority; R04 EquipItem lifetime and physical displaced/foreign cursor ownership; R05 plugin/player context, scan continuation and unknown inventory; R06 same-NPC menu and final option/cleanup ownership. R01 stays contained; R02 and R08 retain their local offline results and native-acceptance limits. Do not restart the completed140-commit W80 review or resume auction/ProfessionBuddy expansion. No Gordunni recipe or guessed coordinates were added.

Before merge, close or explicitly contain the recorded blockers with meaningful evidence, reconcile direct refs, inspect exact final integrated/host results and merge preview, preserve a backup and use an exact-head merge. A source checkpoint is not live deployment. Direct master must be checked independently of cached PR base_sha; last approved current ref is b2324913e2499ba30b239dd67224ca2c655c05cc, tree552eeab1233c7c282897dca0ed9f4334c5e8ed43.
