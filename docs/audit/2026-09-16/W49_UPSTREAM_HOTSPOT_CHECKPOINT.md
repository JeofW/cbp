# W49 checkpoint: upstream study, mounted fix, and blocked port validation

16 September 2026. Repo `jeofwong/CopilotBuddy-private`, ID 1367174964. Resume draft PR47 / `audit/next-47-flight-owner-boundaries-20260916` after reading current refs.

## Current authority

Last passing code checkpoint: **3c0b67441d92a2b0e2e820e434b432f8b21d845b**. Both Windows paths pass the mounted-hotspot 16/16 cases, all four focused and seventeen integrated build/run entries, and all retained groups. There are 63 Wholesome and three QuestLog aggregate groups, 1,747 source/configuration hashes and 78 generated members matching between execution paths.

Published later code: **057a153bc543e149eab2b145fa0f88353a7b427a**, tree **47c880fcb4b0873b4f3d0d91cfc99f9f9bd0eca2**. Three minimal upstream ports are committed but **post-port Windows validation is not complete**. The test-only f9f40612 revision produced clean 6/24, 18 intended assertions, zero unexpected errors in focused and integrated execution. The final integrated run 35105135542 and host run 35105135571 were skipped: repository metadata now says **public**, while the retained workflows require `github.event.repository.private == true`. This continuation did not change visibility or remove that guard. Do not call skipped CI green, change access controls without explicit owner direction, or merge unverified ports.

Master remains **71c79d1c5a3de54e2f3a207a93373b59a8f55984**, the owner-approved PR46 merge. Preserve backups c43c50d8d5d6775055f19bf018b52930a264d4a4 and 8382a7ec05a64212ea0a237159dca427a0767425. Exclude PR25/43/45. No new merge, force push, deployment, installed bot/native library/mesh replacement or live game run occurred.

## Actual upstream ancestry

Pinned local source for the comparison: **75d5347d25a59ca0abf70dfe970eff9db18a9564**. Pinned upstream: **Likon69/CopilotBuddy@4c0595d97d52644b9b64a823c2bb48c30b5c8dfb**.

Actual Git merge-base: **462729050723eaf89cafa0bf3e05ae272431de36**, 28 August 2026 22:41:18 UTC / **29 August 2026 06:41:18 Malaysia time**. Upstream head is 15 September 2026 22:35:15 UTC / **16 September 2026 06:35:15 Malaysia time**. The August 29 recollection is correct for shared ancestry, but later fixes have also been copied/adapted without shared commit identity.

Upstream has **29 commits / 42 changed files** after that base; pinned local history has 397 commits after it. Of those 42 files, seven are byte-identical to upstream, eight still match the ancestor before our new ports, and 27 diverge. A `git cherry` plus sign is not proof a fix is absent. The study compares actual patches and current semantics, not just dates or hashes.

Read-only comparison run **35099721633**, artifact **10447866080**, original archive `w49-upstream-comparison.zip`, SHA256 **a0f513607a6b5bb5f72f8749aa1176906c8ef9e759245693b5a14705e8e710d2**. It contains every upstream commit patch, all 42 three-way versions, comparison.json, source-manifest.json, patch-equivalence.txt and a canonical 4,095-file local source export. Full archive digest/CRC, 160 artifact members and source SHA256/Git blob identities were verified. No upstream code was executed. Original retention ends 30 September 2026; the downloadable handoff preserves the archive.

## Mounted hotspot bug: genuine reproduction and repair

The general target filter accepts explicit GrindArea.MobIDs, but DecoratorNeedToFindTarget's mounted-only branch ignored them and required either a faction match or a finite TargetMaxLevel. Thus a profile with explicit hunting mobs and default unlimited maximum level could find a valid nearby target but fail to enter target acquisition while mounted.

Test-only **fa9179649c3c1b06954b35494b2009954f3950f2** gives **14/16, two assertions, zero unexpected** in both Windows paths. Repair **3c0b6744** adds explicit MobID admission after existing farming-mode/range/collection vetoes and retains minimum/maximum level checks. Existing near-Kill and near-Hotspot dismount predicates already pass. No unconditional waypoint dismount was added.

All 16 cases pass after repair; original/generated tests and workflow hashes are unchanged. The only red/green source difference is `Bots/Grind/Levelbot/Decorators/Combat/DecoratorNeedToFindTarget.cs`. These tests execute the actual target gate and Mount.ShouldDismount using controlled world observations. They do not perform a native dismount or prove every historical hotspot stall has the same cause.

## Three upstream ports at 057a153b: pending green

1. **MerchantItem.cs**, blob **474c0a4b213a185229f931534e3559afb49c4ab4**: port the 32-byte MerchantItemData layout from b91a6016, removing two reserved fields. Preserve quantity fallback and public one-based Index. This does not yet fix every merchant indexing API or unit-to-pack purchase conversion.
2. **SpellManager.cs**, blob **48cc484c0f0b098d50e1a02be241997572915e72**: port a89ffcb1's terrain-click ABI: 0x80C340 = 8438592, TargetGuid then Location, four-byte MouseButton flags with Left=1. Keep all local cooldown, cast and lifecycle code. LegacySpellManager already delegates; retain its public signature. Tests inspect compiled metadata, not live execution of the native address.
3. **ForcedQuestTurnIn.cs**, blob **af42e23964f3a5289d456d29f94572c8812f28d2**: port 034f5c39's four-argument constructor through the existing constructor. Preserve local five-argument and typed six-argument forms, quest identity and POI safeguards.

The first contract fixture **f5087fd1** failed compilation due to a missing WoWPoint namespace. **f9f40612cf2b3629ce4c8aefa63fba58b9b1c9bf** adds only that import and yields clean **6/24, 18 assertions, 0 unexpected** in both Windows paths. All builds then pass; only the intended Wholesome test run fails. Tests include actual MerchantItem reads of three distinct test-owned records, constructor execution and metadata-only terrain ABI assertions. Final 057a153b changes exactly three source files, 14 additions / 10 deletions; all prepared/native blobs match. **No post-port compilation or 24/24 pass is established.**

## All 29 upstream decisions

These are source-review dispositions, not claims to have executed every upstream change. Full messages and patches remain in the comparison archive.

| Upstream commit | Subject | Decision for this fork |
| --- | --- | --- |
| ed15eea7 | Ground-route NoNinja | Already represented locally, including raw mount flag and 10-yard check. |
| 4d6ef716 | Direct movement only while swimming | Already represented; do not bypass mesh on every partial path. Not a full underwater planner. |
| b74b5d5e | Exact mount-error capture | Already present; retain surrounding local guards. |
| 34ca64c2 | SwimSpeed/RunSpeed for stuck expectation | Do not copy blindly. Our arrival/motion sampling and commanded-no-progress detector use different contracts. Neither has comparative live superiority proof. |
| a06a67dc | Version 1.6.6 | Do not overwrite fork release identity. |
| 1a27e97b | README mirror | Already in the byte-identical current README. |
| 613c6af8 | README mesh/extractor direction | Already present; not authorization to replace installed meshes. |
| 1105b7ef | ConditionGlobals helper inheritance | Already adapted; preserve explicit unknown/failed-condition semantics. |
| 011ca412 | Developer Tools lazy object binding | Current file byte-identical to upstream. |
| 16683fbe | SwimmingForwardSpeed alias | Current file byte-identical; compatibility is not navigation proof. |
| 7f2aab68 | Quest behavior assembly references | Current QuestBehaviorHelper file byte-identical. |
| 71d9d967 | Batched profile expressions | Already adapted; retain local publication/unknown-state protections and batch regression coverage. |
| b947c766 | Vendor/Mailbox UsableWhen | Already adapted with conservative unknown handling. |
| 9e06d201 | Public NpcFlags and unit aliases | Missing additive compatibility; needs representative stock-behavior compilation tests. Not ported. |
| 034f5c39 | Four-argument turn-in constructor | Minimal delegating overload committed at 057a153b; pending green. |
| 4bfb1397 | Version 1.6.7 | Do not overwrite fork release identity. |
| cde98cde | InstanceDeathLocation sentinel/entrance | Genuine gap: zero differs from NaN-based Empty. Adapt with map/finite entrance validation, not blind Z=0 navigation. Not ported. |
| dbcf439e | Select required service gossip option | Partial overlap; actual gossip-to-merchant transition and mappings remain missing. Adapt with captured ownership and nonblocking retries, not sleep/current-POI blacklist. |
| a89ffcb1 | Terrain-click ABI | Core fields/address port committed at 057a153b; preserve local wrapper API. Pending green/native acceptance. |
| c3ed816a | Vendor fallback blacklist | Local richer exclusion/backoff predicate already covers the core issue. Do not replace it with the smaller upstream path. |
| 5aa80a4f | Merchant item-cache hydration | Partial overlap: local catalog waiting is nonblocking, but per-item metadata readiness still needs assessment. Do not import a three-second blocking sleep. |
| 5bd76ab5 | Explicit corelib reference fallback | Defensive candidate; upstream machine-specific failure not reproduced locally. Test missing-reference path before repair. |
| b91a6016 | Merchant layout/index/packs/profile publication | Split the commit. Layout subset committed; legacy index and pack-count gaps remain. Preserve stronger local profile publication fencing. |
| a225bf3f | InteractRange reduced globally | Superseded about 13 minutes later upstream; reject standalone port. |
| c0506547 | Separate LootRange and consumer | Missing. Port API plus loot caller together; keep normal NPC InteractRange, test reach/range boundaries. |
| 87075e05 | DBC address comment | Comment-only, not a runtime correction. |
| fd79fa02 | Packed Spell record decoder | High-priority missing capability. Raw patch lacks clear unmanaged-buffer release and robust read/output bounds. Requires safer adaptation, not blanket import. |
| f617176c | Client quest-item sale flag | Missing additional veto. Add to both relevant sellers while retaining accepted-quest/item/lock/identity safeguards; do not copy the bulk seller. |
| 4c0595d9 | Version 1.6.7.4 | Do not overwrite fork release identity or imply all changes merged. |

## Priority dependency graph and next implementation requirements

**Spell table -> WoWSpell.FromId -> SpellEntry fields -> routine decisions.** The local path still reads raw rows. Upstream describes compressed original-client Spell records. This is a source-traced risk to spell mechanics/metadata, not an independently captured count of bad casts. The safe adaptation needs bounded 704-byte decoding, explicit complete-read checks, a defined buffer owner rather than leaked unmanaged rows, fresh table/session identity, uncompressed controls, malformed/truncated/overflow tests and original-client evidence. Do not cache an unavailable row as permanent absence.

**Gossip -> merchant catalog -> item metadata -> purchase/sale dispatch.** Correct struct size does not fix all indices, packs, hydration, frame transitions or client quest-item flags. Keep independent observation, decision and dispatch checks. Port additively with overflow-safe pack arithmetic, quantity/affordability/stock/identity validation and single-stack sale protection.

**Target list -> mounted admission -> Kill POI -> dismount.** The explicit-MobID gate is now verified. Native unmount/engage acceptance remains separate. Do not dismount at ordinary transit waypoints or on unsafe transport states.

**Liquid observation -> consumable admission -> Wholesome rest pause -> movement.** Earlier W48 repairs are retained and verified at recovered75d and mounted3c. This does not establish a safe shoreline route, depth-aware obstacle avoidance or oxygen escape planner.

**Instance death sentinel -> entrance observation -> corpse route.** Existing corpse recovery does not itself fix the missing Empty initialization or verify upstream's entrance coordinates. Preserve local recovery while validating the new input.

## Ret/Singular and prior user reports

No upstream file in the 42-file series replaces our runtime-snapshot Singular rotation policies. Core spell metadata/terrain changes can still affect them indirectly. Retain the existing behavior-tree executor and class/context policies, and adapt low-level contracts independently. Do not claim that our design or the upstream guide universally maximizes DPS.

Retained groups: Paladin decisions42/42 plus135 availability rows; tactics104/104; Consecration66/66; shared interrupts36/36; player-seal/identity36/36; support44/44; blessing contribution43/43. The blessing policy considers existing caster contributions, Battle Shout overlap and known local Ret role. It is not a gear/talent/rank/encounter-specific Kings-versus-Might DPS optimizer. It preserves a unique useful blessing to avoid oscillation. The supplied Wowhead guide is Wrath Classic/3.4.3 guidance; this target is original3.3.5a build12340, so version-specific mechanics must be filtered rather than copied automatically.

ManaPer5Sec alias20/20 remains in the code; the user's warning was from an old unupdated build. Aquatic observation29/29, mirror bounds14/14, Wholesome pause16/16 and shoreline/legacy rest35/35 remain. Their controlled tests must not be relabeled as full swimming/shoreline/live acceptance.

## Evidence and recovery

Nine original Windows archives were rechecked for outer hashes, CRC, complete internal manifests, exact identities, generated fixtures and named group outcomes. Twelve local Python verifier tests also pass; they are not additional C# bot tests. The historical older-production normalized arm is separate from current green and from the clean reds above.

Mounted red: focused35100724254/art10447659786; integrated35100724024/art10448182703. Mounted green: focused35101802221/art10448103401; integrated35101802174/art10448098631. Contract clean red: focused35103084292/art10449341478; integrated35103084339/art10449012627. Full hashes are in W49_EVIDENCE_INDEX.json and the downloadable verification JSON.

The original source-export and CI archives, full 29-commit/42-file study, reproducible verifier, port patches and current next prompt are packaged in the W49 handoff. Root pointers are updated and prior versions archived byte-for-byte. Reviews remain implementing-assistant review, not independent approval. The exhaustive audit remains open. Resolve visibility/CI with explicit owner direction, validate 057a153b or its exact-source descendant, and then continue the remaining tested adaptations without recreating saved work.
