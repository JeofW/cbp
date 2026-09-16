# W51 — verified spell-row lookup and Paladin role fallback

Evidence runs: 16 September 2026 UTC. Repository `jeofwong/CopilotBuddy-private`, ID `1367174964`, draft PR47, branch `audit/next-47-flight-owner-boundaries-20260916`.

## Current state

Latest focused-verified production is **c97d699ae4019549619facb925743bd48ca060a5**, tree **45697de646100c98d8794b813013d4284d262ecf**. This continuation published two new test-first repairs, not another upstream merge. All four focused Windows x86 projects build and run successfully, with 66 Wholesome and three QuestLog aggregate groups passing. The two new groups report 24/24 and 28/28. The retained upstream compatibility group reports 24/24.

Master remains **71c79d1c5a3de54e2f3a207a93373b59a8f55984**, the approved PR46 integration. PR47 remains draft/unmerged. Backups c43c50d8d5d6775055f19bf018b52930a264d4a4 and 8382a7ec05a64212ea0a237159dca427a0767425 and exclusions PR25/43/45 remain. No deployment, force push, installed-binary/mesh replacement or visibility change occurred.

**This is a focused-verified checkpoint, not a full release sign-off.** Integrated and host jobs on c97 were skipped. Full original-client acceptance, independent approval, exhaustive completion and measured DPS improvement are not established.

## Correction to W49/W50: some post-port execution had already succeeded

The previous records correctly identified skipped integrated and host runs, but incorrectly generalized that to no post-port passing execution. Original focused run **35105135595**, artifact **10449349602**, actually ran at **057a153bc543e149eab2b145fa0f88353a7b427a** and passed all four focused projects, including **24/24 upstream compatibility cases**.

This continuation recovered and verified that original archive and its actual f9f40612 test-only predecessor: **6/24, 18 intended assertions, zero unexpected errors -> 24/24**. All original and normalized tests and workflow inputs are unchanged. Only MerchantItem.cs, SpellManager.cs and ForcedQuestTurnIn.cs differ. The other 66 aggregate groups retain their named outcomes. These are recovered executions, not new W51 executions or an integrated pass.

The focused workflow already has no private-visibility guard and is configured for this branch. Its normal push-triggered execution remained available. No workflow, guard, permission or event was altered, no old private event was replayed, and no guarded full suite was moved into another runner.

Public-CI adaptation of the existing integrated and host workflows has been requested from the owner, not assumed. Their private-only conditions remain. Fresh c97 checks:

| Job | Run | Job ID | Result |
|---|---:|---:|---|
| Focused current | 35119678023 | 104873958136 | Actual execution; all four projects pass |
| Integrated combined | 35119678001 | 104873960170 | Skipped |
| Host build | 35119677890 | 104873959590 | Skipped |

After explicit authorization, adapt only the relevant trusted-repository/branch validation jobs, preserve read-only permissions, pinned actions, existing tests and failure conditions, and review public artifact contents. Do not broadly enable the historical private source-export/native/LFS workflows. Until then, do not claim full integrated or host success on these revisions.

## New repair 1: spell-row lookup must not retain obsolete addresses or misses

### Reproduction and mechanism

`WoWSpell.FromId` retained a static dictionary from spell ID to `WoWDb.Row`, including null entries. A missing row was therefore not retried when the table later supplied it. A valid row could also outlive its removal, the table's replacement, or an observed memory-owner change.

The new `SpellRowLookupRegressionTests` executes the actual FromId -> DbTable.GetRow -> Memory -> Row.GetStruct chain. Its fixture publishes real table headers, row-array pointers and SpellEntry data in test-process storage. It does not replace the lookup method, open a game process, create a native executor or cast spells.

| Revision | Meaning | Result |
|---|---|---|
| d189b4eb3a4dd0288b316206e3dea4e9e9af8904 | Initial test-only addition | Fixture compilation error: missing ClientDb namespace; no behavioral result |
| 07a4929ff2b88ec7523e6abac2956bd3cc5fbd8b | Test-only import correction | 11/24; 13 intended assertion failures; zero unexpected errors |
| 7283609f068e3dbaf5ce73679c719db464b32c46 | Production repair | Same 24/24 cases pass |

The correction adds only `using Styx.Patchables;`; all 24 scenarios/assertions remain. The repaired production diff removes the redundant `_rowCache` field and initialization and resolves the currently published table/row on each FromId call. Existing lower-level Memory caching and the separate spell-info cache are not changed. There is no global cache clearing every pulse.

Controls retain stable repeated metadata, unrelated IDs, out-of-range/negative IDs and the already-materialized SpellEntry value snapshot. Failure cases cover late hydration, repeated misses, row removal/replacement, table removal/replacement and observed memory loss/replacement. Both new repairs leave this group 24/24 at final c97.

### Limits

The fixture explicitly refreshes the controlled Memory observations. This does not establish raw frame/descriptor freshness, unobserved ABA continuity, correct packed-row decoding, same-table header refresh, or safe concurrent/in-flight publication. Already-held WoWSpell instances and the separate `_spellInfoCache` have other lifetime concerns. No performance gain is measured; removal of an unsafe cache is not evidence of a speedup.

A separate source check falsified a tempting hypothesis: `SpellManager.HasSpell(string)` uses learned `_knownSpells`, not the catalog-wide `SpellDb.HasSpellByName`. The latter has no callers in the reviewed C# source set despite its stale comment. The learned-spell gate was preserved; catalog existence was not relabeled as character knowledge.

## New repair 2: leftover Righteous Fury must not imply a tank specialization

### Reproduction and mechanism

`Singular.Helpers.Group.MeIsTank` checked explicit Tank assignment, then tank specializations, then a list of tank-like auras. The last fallback could promote a known Retribution or Holy Paladin merely because Righteous Fury remained active. Its callers include Singular's TankManager pulse gate and self-inclusion in the tank list.

The new `PaladinTankRoleRegressionTests` compiles all 99 tracked Singular C# sources through the existing compiler fixture and executes the actual Group predicate against real host aura and ordinary-party/solo role readers. It controls descriptors and spell-name observations, not Group's logic. It never attaches to a game or creates a native executor.

| Revision | Meaning | Result |
|---|---|---|
| 6acf21a0648a6c5eff2abe8d9a02b379db35e6df | Test-only role reproduction | 16/28; 12 intended assertions; zero unexpected errors |
| c97d699ae4019549619facb925743bd48ca060a5 | Narrow production repair | Same 28/28 cases pass |

The production change adds five lines only, including comments/spacing, in `Helpers/Group.cs`: known Retribution/Holy return false before the fallback. The existing explicit Tank-role branch remains ahead of it. Protection, unknown Lowbie and other-class paths are unchanged. The original UTF-8 BOM is retained.

Tests cover solo and ordinary-party Ret/Holy with and without Fury, Protection with and without Fury, Lowbie fallback, an unrelated blessing, aura addition/removal and specialization transitions. Protection controls remain available after transitioning out of Ret/Holy. All other 68 aggregate groups retain their named outcomes across this repair.

### Limits

This corrects inferred role, not the active aura itself. It does **not** cancel Righteous Fury, simulate an explicit server-assigned raid/LFG Tank role, tick TankManager or demonstrate a taunt cast. An explicit off-role assignment policy, the host raid/LFG role reader and safe cancellation of leftover threat buffs remain separate requirements. The prior Protection-only shared interrupt/Avenger's Shield safeguards are retained.

## Integrity, source identity and scope

Final c97 archive **10456688734** has SHA256 **c060085dfd56aa0f5582e3d6c2bf720573f270a3e2ab3af64f95b19f1fe3b06d**. Its CRC and all 101 internal-manifest entries were verified, along with 1,750 source/configuration input hashes, 104 exported source members and 81 normalized members. These are versioned counts, not a claim that the narrow source-export ZIP is a complete checkout.

Within each clean red/green pair, original tests, normalized fixtures and workflow inputs are identical. Only WoWSpell.cs changes in the first pair and Group.cs in the second. The final archive's two changed production files and two added test files match the prepared/native blob payloads. The first pair retains 67 other group outcome inventories; the second retains 68. All four final build/run exit codes are zero, as are runtime prerequisites and local normalizer utilities recorded by CI.

Seven original focused archives are retained in the handoff, including the initial fixture compilation failure. The verifier preserves failing and non-executed results rather than silently excluding them. Eleven local Python archive-verifier tests cover hashes, manifests, identity mismatch, missing logs and retention of assertion/fixture failures. These are utility checks, not additional bot C# scenarios.

The separately labelled historical baseline arm is not included in current pass claims and is not substituted for clean assertion-level red. There is no new host warning/error count because that job did not execute.

## Remaining user requirements and architecture

Retain the existing behavior-tree executor. Improve trustworthy observations, role permission, context-specific decision rules and last-moment dispatch admission rather than adding a second rotation engine. The two repairs address the first two boundaries; they do not complete every damage/utility choice.

The upstream study remains pinned through **89cccaf1fa8f1c19d7eeb0924beddf2c3991088e**: 31 commits after actual merge-base **462729050723eaf89cafa0bf3e05ae272431de36**, dated August 29 Malaysia time, across 42 unique changed paths. W49 has the detailed dispositions; W50 records the two additional documentation/credit changes. No new upstream import occurred in W51.

The earlier mounted MobID targeting fix, MP5 alias, situational seal/judgement controls, Kings/Might contribution selection, shared interrupt protections and aquatic rest/timer fixes remain. W51 did not recreate them. Focused tests do not replace the separate Paladin decision/support integration suites or a live hotspot/native dismount run.

Priority remaining work:

1. Complete integrated and host validation on the exact current code after the CI decision; obtain independent review before treating this as release-ready.
2. Test bounded/owned database discovery, packed Spell rows, header/memory freshness, retryability and the separate spell-info cache. Preserve learned-spell eligibility.
3. Complete the Paladin threat-aura/explicit-role policy, utility recipients, rank/magnitude-aware blessings, original-version gear/encounter priorities and PvP crowd-control decisions. Current blessing coverage is not a measured DPS optimizer.
4. Test the real breath/legacy-rest callers, then implement safe air/shoreline selection, depth/obstacle validity, finite progress deadlines and dry recovery admission. Existing water-rest guards are not a complete escape planner.
5. Adapt remaining upstream merchant/loot/quest-item/corpse/compatibility fixes selectively with actual tests, preserving local ownership/protection and unknown-state handling.

Original WoW 3.3.5a must remain distinct from Wrath Classic 3.4.x. The supplied Wowhead page is useful advisory material, not proof of maximum DPS or a complete implementation specification. Blizzard's February 2023 Classic Reckoning-glyph and Vengeance/Corruption stacking changes must not be imported into the original client by assumption. Refer to W50's external research and counterevidence.

**No new merge, deployment, independent approval, live acceptance, maximum-DPS result or exhaustive-completion claim accompanies this checkpoint.** There is no pending unexecuted test-only addition at the final code revision; broader requirements remain explicitly open.
