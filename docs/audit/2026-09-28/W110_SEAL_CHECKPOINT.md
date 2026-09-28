# W110 seal recovery and writer diagnostic - verified source checkpoint

28 September 2026. Original WoW 3.3.5a/build 12340; TrinityCore 3.3.5 primary, AzerothCore WotLK secondary. Same PR51 W110 assignment. No merge or live-gameplay certification.

## Verified outcome

**Tested source:** `a199953f7b465fd91d0ad7faa10252e6605ae8b2`. **Source tree:** `ca793d49ed77d4d99debf3977379b0955efc9988`. The containing documentation commit is not a new tested source revision; its exact identity belongs to the separate publication and final-verification records.

The requested situational seal/PvP review is completed at the available-evidence boundary. An opt-in solo Seal of Light policy is implemented and verified: **78/78 cases**, after **26 intended behavioral assertions and zero unexpected errors**. The existing default damage, manual seal and player-target choices remain. Settings default off; no saved user configuration or production executable was altered.

A real recurrence also closed the saved Goal's publication-diagnostic investigation. The actual optimized writer error path lost its wrapper frame. A new 13-case regression produced **seven intended assertions, including six actual error-path failures, and zero unexpected errors**. A single NoInlining annotation now retains the diagnostic boundary without modifying the writer body, permissions, exception types or any original assertion. **13/13 diagnostic cases and all 21 original publication cases pass**.

## All stages retained

| Stage | Source | Actual result |
|---|---|---|
| Seal test-only baseline | `4ce0f02adcd7a1c3102a342afa415c27340407fa` | 52/78; 26 intended assertions, zero unexpected; other 16 integrated groups pass. |
| First seal implementation | `5b41250c8e5568bcd947e544cd6a98745a96e8f0` | Seal 78/78 passes, but aggregate 15/17: missing settings in a separate support-test fake and one original publication diagnostic assertion. Not called fully green. |
| Diagnostic test-only baseline | `a1a330e7b9444d058f8e52a22823101ad94c8a69` | Seal 78/78 and support group pass; diagnostic 6/13, seven intended assertions, zero unexpected; other 16 groups pass. |
| Final diagnostic repair | `a199953f7b465fd91d0ad7faa10252e6605ae8b2` | Seal 78/78, diagnostic 13/13, publication 21/21 and **17/17 integrated groups** pass. |

The missed support fake gained only disabled-default accessors. The original seal and publication assertions were never weakened. Frozen comparison one verifies all **183 normalized members** unchanged from seal red to its targeted-pass candidate; comparison two verifies all **184 normalized members** unchanged from diagnostic red to final green. The intervening additive test-only baseline is explicitly recorded, not misrepresented as an unchanged interval.

## Final artifacts

**Integrated:** run **36395295682**, artifact **10957877657**, 1005809 bytes; SHA256 `e8b047d662bd72bea93311dd6432ec0169c4a8a0c47d136f102b1871cc6760e6`. Source/fixture/tree, all **1868 source inputs**, **228 inner hashes** and **229 archive members** were verified against the exact final source. All 17 groups build and run successfully; no game is attached.

**Release/x86 host:** run **36395295496**, artifact **10957977889**, 83083 bytes; SHA256 `25c3fc37da0f8db4a5bba3054440c9daad6f1053513e2dd96205372e54bcf38f`. **0 errors / 3344 warnings**, compile-only; tests_run=false. Host compilation is not substituted for behavioral green.

## User-facing behavior

With **Seal=Auto** and **Use Solo Seal of Light** enabled, eligible solo Normal-context combat against a non-player, non-elite, non-boss can enter Light at 50% health, above the existing emergency-heal threshold, in melee with at least 30% mana. An observed Light stays below 75% health, then ordinary damage selection resumes. The defaults are configurable policy, not a claimed best damage/survival threshold. Existing healing and defensive behavior may take priority.

Command remains the existing cleave/low-level option. Vengeance/Corruption remain the sustained NPC/boss policy; Righteousness remains the Auto player-target choice. Learned manual Justice, Light and other seals remain authoritative. **Judgement of Justice is independent of Seal of Justice**. Existing reactive Hammer of Justice/Repentance behavior is preserved; no unverified proactive diminishing-return or off-target CC planner was added.

## Scope and continuity

Read W110_SEAL_EVIDENCE.json, W110_SEAL_PVP_FINDINGS.md, W110_WRITER_DIAGNOSTIC_FINDINGS.md, W110_SEAL_EXECUTION.md, W110_SEAL_GOAL_DISPOSITION.md and W110_SEAL_CONTINUATION.md. These new-scope documents retain the earlier complete W110 objective/native/Ret/core matrices and their exact limitations; they do not reconstruct an old refused package or native map.

PR51 stays open/draft/unmerged; master and both intentional root edits remain untouched. Numeric preparation remains 3 historical / 0 new / 0 remaining. DensePullIsolationValidated remains false. No subagents, local project build/test, production CB, native/IDB mutation, force-push, master write or merge/deploy. Live native/server/realm/world/protocol and independent-review acceptance remain separate. The actual CoS finish state is recorded after its real call, never inferred from this checkpoint.
