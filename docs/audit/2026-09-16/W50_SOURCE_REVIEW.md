# W50 — recovered checkpoint, updated upstream review, and remaining decision risks

Evidence date: 16 September 2026. Repository: `jeofwong/CopilotBuddy-private`, ID `1367174964`. Draft PR47, branch `audit/next-47-flight-owner-boundaries-20260916`.

## 1. State and release decision

This continuation recovered the W49 work rather than restarting from the older aquatic or W42 checkpoints. At review start, the branch was `164b5fe71f1b28c2b2836e3e01c2b49b11f08801`, a documentation-only descendant of the selective upstream ports.

* **Last passing code:** `3c0b67441d92a2b0e2e820e434b432f8b21d845b`.
* **Published code awaiting post-port verification:** `057a153bc543e149eab2b145fa0f88353a7b427a`, tree `47c880fcb4b0873b4f3d0d91cfc99f9f9bd0eca2`.
* **Master:** `71c79d1c5a3de54e2f3a207a93373b59a8f55984`, the approved PR46 merge.
* **PR47 remains draft and unmerged.** The present review is not independent approval or a release sign-off.

GitHub currently reports the repository as public. The retained Windows workflows require `github.event.repository.private == true`. A fresh job read confirms integrated run `35105135542`, job `104824313275`, was **skipped**. The W49 record also identifies skipped host run `35105135571`. These are not passes or assertion failures. No production, test, workflow, repository visibility, master, backup, installed binary or mesh change was made in this W50 continuation. Documentation is the only intended publication.

The user has been asked to authorize a public-repository CI adaptation. That authorization is not assumed. Do not remove a visibility guard, replay an old private event to evade it, change repository visibility, or substitute a privileged workflow. After authorization, constrain the relevant validation jobs to this repository and trusted branch/events, preserve read-only permissions and pinned actions, review public artifact contents, and retain every test and failure condition. Do not indiscriminately enable historical source-export or native/LFS workflows.

## 2. What the missing continuation had already completed

### Mounted hunting hotspots

The mounted-only target gate ignored an explicit `GrindArea.MobIDs` match and relied on either faction matching or a finite maximum target level. A valid hunting profile with an unlimited default maximum could therefore reject a nearby wanted mob only while mounted.

The saved test-only revision `fa9179649c3c1b06954b35494b2009954f3950f2` recorded 14/16 cases passing, two intended assertions and zero unexpected errors in both Windows execution paths. Repair `3c0b6744` recorded 16/16 with unchanged tests. The other range, farming, collection and level vetoes remain. Existing near-Kill/Hotspot dismount predicates already passed; no unconditional dismount at every waypoint was added.

These are recovered recorded results, not new Windows executions in W50. They establish the controlled target gate, not a live native dismount or the cause of every historical hotspot stall.

### Earlier reports

The W49 passing checkpoint retains MP5 alias 20/20; liquid observation 29/29; mirror timer bounds 14/14; rest-pause coordination 16/16; shoreline/legacy rest admission 35/35; Paladin decisions 42/42 plus 135 availability rows; tactics 104/104; Consecration 66/66; shared interrupts 36/36; player-seal/identity 36/36; support 44/44; blessing contribution 43/43.

The user's `ManaPer5Sec` warning was reported from the old, not-yet-updated bot. The source contains the alias to `ManaRegeneration`; the warning has not been reproduced against the new build. Water admission fixes do not constitute a complete depth/obstacle/air/shoreline planner. The Paladin tests do not establish maximum DPS or complete utility coverage.

## 3. Upstream comparison: August 29 ancestry, now 31 commits

The retained comparison uses our `75d5347d25a59ca0abf70dfe970eff9db18a9564` and upstream `4c0595d97d52644b9b64a823c2bb48c30b5c8dfb`. Its actual Git merge-base is `462729050723eaf89cafa0bf3e05ae272431de36`: 28 August 2026 22:41:18 UTC, or **29 August 2026 06:41:18 Malaysia time**. The user's August 29 recollection is correct for shared ancestry. Later copied/adapted fixes need semantic comparison, not merely matching commit IDs.

That comparison covers 29 commits and 42 changed files: seven local files already match upstream, eight still matched the ancestor before the new ports, and 27 diverged. W50 verified all 160 internal artifact-manifest members, the ZIP CRC and authenticated outer digest, and all 4,220 size/SHA256/Git-blob source identities, including the 4,095-file canonical local export. This is byte/source verification, not execution of upstream code.

Fresh upstream head is **`89cccaf1fa8f1c19d7eeb0924beddf2c3991088e`**, dated 16 September 2026 14:19:12 UTC / 22:19:12 Malaysia time. It is two commits beyond the saved comparison:

| Additional upstream commit | Actual change | Disposition |
|---|---|---|
| `bf0944591d369edab2ef7148754dc85aeadae0c1` | README version label and prebuilt Mega download link | Documentation/distribution only. Do not imply its binary contains this fork's audit changes or replace installed files. |
| `89cccaf1fa8f1c19d7eeb0924beddf2c3991088e` | Adds Natfoth to the startup credits string | Attribution only; no gameplay correction. Preserve attribution in any eventual aligned release. |

There are therefore **31 commits after the shared ancestor** at that pinned upstream head. Both new paths were already in the 42-file inventory. No new gameplay implementation appears in these two commits. The complete 31-row disposition is in `UPSTREAM_DISPOSITION.md` in the offline package; the original 29 patches and three-way versions remain in the recovered comparison archive.

### Selective ports already committed, but not approved for merge

`057a153b` contains only the 32-byte merchant-item layout, terrain-click ABI/address correction, and a four-argument turn-in constructor delegating to the existing typed implementation. It preserves surrounding local safeguards. The clean pre-port contract test recorded 6/24 with 18 intended assertions and zero unexpected errors. **There is no post-port 24/24 result.** Native ABI metadata tests are also not independent original-client validation of the address.

Keep the existing disposition against wholesale imports: do not replace local profile publication, unknown-state handling, vendor backoff/protection, nonblocking catalog waiting, or runtime ownership with a smaller upstream implementation. Merchant indices/packs, per-item hydration, gossip transition, client quest-item sale flags, corpse entrance validation, separate loot range, stock aliases and corelib fallback remain individually testable adaptations.

## 4. New source findings in W50 — not yet C# regression reproductions

The 13-path native comparison from the exported local snapshot to `164b5fe7` confirms that the source paths below were unchanged. Each finding is a source/control-flow review, not a newly executed bot test or a claim of historical live causality.

### R1 — Ret can still be inferred as a tank through shared role fallback

`Helpers/Group.cs:19-45` accepts a Tank role, then tank-specialization fallbacks, then any tank aura, including Righteous Fury. It does not let an explicit damage assignment or known Retribution specialization veto that final aura fallback. Thus a Ret character with leftover Righteous Fury can produce `MeIsTank == true`. `SingularRoutine.cs:153-158` then pulses TankManager outside battlegrounds while grouped, and `Group.Tanks` also consumes the result.

**Important counterevidence:** TankManager maintains targeting/taunt-candidate information; this trace does not itself prove that the Ret tree casts a taunt. The prior shared-interrupt Protection gate remains separate and must be preserved. No automatic Righteous Fury cancellation was found in the reviewed Singular source set.

**Required tests:** explicit damage role plus Fury; known Ret with missing role plus Fury; Protection tank controls; role/spec changes between observation and dispatch; another member's Fury; invalid player; explicit off-role assignment policy. Tank-role permission, utility protection and removal of leftover threat buffs need separate contracts. Do not make blanket changes to Death Knight or Druid role assumptions as an incidental Ret fix.

### R2 — Transiently absent spell rows become durable negative cache entries

`Styx/Logic/Combat/WoWSpell.cs:467-484` inserts the result of `db.GetRow` into `_rowCache` even when it is null. Subsequent calls find that key and do not ask the table again. The reviewed file has no row-cache removal/invalidation path. A later-loaded row can therefore remain invisible to this API. Non-null pointer rows are also keyed only by spell ID, not their memory/table owner.

**Required tests:** missing row followed by hydration; unrelated spell independence; valid cached repeat; table/header/memory-owner replacement; negative or invalid IDs; short reads; cancellation; stale owner during publication. Do not “fix” this by globally clearing every cache every pulse. Unknown/read failure must not become permanent absence.

### R3 — Database discovery marks itself initialized before it has succeeded

`WoWDb.cs:25-76` sets `_initialized = true` before the fallible registration walk. It adds entries directly to the shared dictionary, has no explicit iteration/byte bound on the `while (true)` walk, and catches exceptions without restoring retryability. Missing memory at entry is handled, but later failed or partial discovery is a different case. `DbTable` also retains its constructor-time header.

**Required tests:** complete registration table; missing terminator; truncated/unreadable entry; invalid table ID/address; address arithmetic bounds; partial-discovery failure and retry; reentrant/memory-owner replacement; preservation of cancellation/interruption. Publish a complete validated table set atomically for a captured owner. Do not couple this repair to an unbounded decoder rewrite.

### R4 — Upstream packed-row support needs more than a copied decompressor

Upstream `WoWDb.DbTable.GetLocalizedRow`/`Unpack` supplies missing original-client packed Spell-row handling, but the reviewed implementation has no explicit input-consumption budget, allows a repeated run to pass the intended 704-byte output boundary before copying a fixed prefix, rereads input bytes, and allocates unmanaged output without a visible release owner. Owned rows have a different field-access behavior from remote rows. These are reasons to adapt the capability, not reject the capability itself.

**Proposed boundary:** bounded decoder from complete input observations into an owned immutable 704-byte representation; checked input/output arithmetic; explicit owner lifetime; consistent field/struct access; fresh table identity; retryable unknown observations. Include uncompressed controls, malformed/truncated/overlong run streams, boundary records and repeated disposal/publication tests. Original-client evidence is still needed; the upstream assertion about native layout is not independent validation.

### R5 — Existing breath code can start a long recovery too late

`CollectThings.SwimBreathBehavior.IsBreathNeeded` calculates a safety-adjusted travel time, then applies `Math.Min(travelTime, 30.0)` despite a “minimum” comment. That caps its lead time at 30 seconds. As an arithmetic illustration, distance 100 units and speed 5 units/second produce a 70-second budget before the cap; with 40 seconds of breath remaining, the current comparison would not yet request recovery. This illustration is not an executed C# test or a measured game journey.

`WaterSurface` can return `WoWPoint.Empty` when its liquid trace finds no surface, and the breath policy does not validate that candidate before using its distance. `UnderwaterMoveTo` falls back to direct click-to-move after a navigation failure; this is not proof of a clear underwater route around a wall or roof.

**Required tests:** short/long travel budgets; zero/negative/non-finite speed; missing/non-finite air-source coordinates; visible zero breath; underwater ceiling; unreachable nearest source with a reachable alternative; repeated no-progress; target/world replacement; owned movement cleanup. Correcting Min/Max alone is insufficient without validity and route-safety checks.

### R6 — A separate water behavior excludes exactly zero remaining breath

`WaterBehavior.cs:234-240` requires `CurrentTime < 20000 && CurrentTime != 0` for its low-breath branch. The current mirror timer can legitimately clamp exhausted remaining time to zero. A visible, exhausted timer therefore fails that branch. A hidden timer and an exhausted visible timer need different treatment. This is not a claim that every profile loads WaterBehavior or that a native drowning incident was reproduced.

Existing breath logic is present in CollectThings and particular dungeon/angler behaviors. W50 scanned all 1,661 C# files in the export for breath/drowning/oxygen references and read the relevant owners; this is not a type-bound proof of every dynamic profile path. Ordinary questing does not automatically inherit those profile-specific handlers.

### R7 — The legacy blocking rest path has only an initial admission check

`Styx/Logic/Common/Rest.cs:44-123` now rejects invalid initial environments, preserving the prior aquatic repair. But its legacy `Feed` path logs, clears the current global target, stops movement, reads inventory, dispatches food/drink and waits without the same captured-owner rechecks used by `UseImmediate`. A callback or world/position change after initial admission remains a separate risk. The wait loop is also blocking.

**Required tests:** replacement during the initial log and inventory observation; water entry after admission; stopped/world-lost state during waiting; immediate callbacks retaining a new target; exact stop signals; dry legacy controls. Preserve the immediate-owner tests rather than assuming their result covers the legacy path.

## 5. Paladin architecture and guide assessment

**Recommendation: retain the existing behavior-tree executor, but finish the observation and policy boundaries.** Do not introduce a second rotation engine. Separate trustworthy spell/player/target observations, role permission, contextual action priority, and last-moment dispatch admission. Survival and environment safety can preempt ordinary DPS; their cleanup must release only their own movement or action.

The supplied Wowhead page is useful as a comparison reference, not a complete executable specification. The fetched page is labelled Patch 3.4.3, displays an August 11, 2022 update, and contains beta wording. Its published single-target order puts Judgement between Crusader Strike and Divine Storm, while the reviewed Ret tree retains a different order and earlier proc windows. There is no measured head-to-head DPS result that establishes the fork as universally superior. Do not silently rewrite the guide or describe it as already incorporating every later change.

Blizzard's February 2023 primary announcement explicitly introduced the non-taunting Reckoning glyph and additional Vengeance/Corruption stacking from Crusader Strike/Divine Storm for Wrath Classic. That is a concrete reason not to copy later Classic assumptions into original 3.3.5a. It does not make every recommendation on a Classic guide wrong.

The current design already distinguishes learned/manual seals, player-target versus NPC choices, judgement aura ownership, mana recovery, emergency healing and Protection-only shared interrupts. Those controls should remain. What remains unproven or missing includes gear/set-bonus-aware priorities, encounter windows, PvP crowd-control/diminishing-return decisions, and some utility spells.

A literal review of all 99 Singular C# files found no Sacred Shield, Hand of Salvation or Hand of Sacrifice references. Hand of Protection references were enemy-aura checks in Warrior code, not Paladin cast calls. Repentance and Hammer of Justice are present in the shared interrupt helper. This is source wiring evidence, not proof that all dynamic spell selections have been explored. “Use every button” is not the acceptance criterion: utility needs a justified recipient, effect, opportunity cost and negative-action test, not indiscriminate automation.

### Kings versus Might

The retained blessing policy considers existing caster contributions, Battle Shout coverage by name, known local Ret/group context and explicit settings; it preserves a useful owned blessing to avoid oscillation. It does **not** compare learned rank, improved-talent magnitude, unbuffed stats, gear or the marginal DPS of one choice. A weak Battle Shout is currently treated as coverage without a magnitude comparison.

The appropriate extension is a versioned contribution model that first respects group assignments and stronger existing coverage, then compares only known, trustworthy marginal benefits. Unknown rank/stat data should preserve a useful contribution or defer, not invent a universal numeric threshold. This needs real rank/stat fixtures and encounter/gear benchmarks before being called a DPS optimizer.

## 6. Acceptance order after the CI decision

1. Verify the existing three ports on their exact source descendant with the unchanged 24-case contract group, all focused/integrated suites and host compilation. Stop on genuine compile/runtime failures; do not weaken tests.
2. Add actual-owner tests for Ret role interpretation and the spell-table/row-cache faults before narrowly changing production. These are higher-confidence prerequisites for smart decisions.
3. Test bounded packed-row adaptation and the remaining merchant/loot/compatibility ports separately. Preserve local protection and nonblocking operation.
4. Exercise the real breath/rest callers, then introduce an owned aquatic recovery policy with validated air/shoreline candidates, progress deadlines and safe rest admission. Reuse existing handlers only where their contracts are verified.
5. Evaluate rotation/utility and blessing policies with original-version mechanics and representative gear/encounter evidence. Independent review and supervised original-client acceptance remain required.

**No exhaustive completion, post-port green, new C# execution, measured DPS improvement, automatic upstream merge or live acceptance is claimed by W50.**

## Source references

Repository observations are pinned to the immutable refs above. Key paths: `Helpers/Group.cs`, `SingularRoutine.cs`, `Managers/TankManager.cs`, `ClassSpecific/Paladin/PaladinSupport.cs`, `ClassSpecific/Paladin/Retribution.cs`, `Helpers/Common.cs`, `Styx/Logic/Combat/WoWSpell.cs`, `Styx/WoWInternals/WoWDb.cs`, `Styx/Logic/Common/Rest.cs`, `CollectThings.cs`, `WaterBehavior.cs`, and `MirrorTimerInfo.cs`.

External references reviewed September 16, 2026:
* Supplied guide: https://www.wowhead.com/wotlk/guide/classes/paladin/retribution/dps-rotation-cooldowns-abilities-pve
* Blizzard announcement, February 4, 2023: https://us.forums.blizzard.com/en/wow/t/upcoming-adjustments-to-retribution-paladins/1511714/1
* Blizzard hotfix archive, February 6 entry: https://worldofwarcraft.blizzard.com/en-us/news/23892230/hotfixes-march-20-2023

Raw comparison artifact: run `35099721633`, artifact `10447866080`, SHA256 `a0f513607a6b5bb5f72f8749aa1176906c8ef9e759245693b5a14705e8e710d2`. The separate verifier and manifest describe the precise verification scope.
