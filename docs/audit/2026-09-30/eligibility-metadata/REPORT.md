# Preserve explicit eligibility requirements across baseline protection

This continuation starts from validated PR65 `d36bddce5600bb67418c8180a38fdb8c40346f61`, branch `audit/next-quest-eligibility-20260930`, worktree `D:/Dev/CB-QuestEligibility-20260930`. The containing Git commit is the candidate; final local/hosted acceptance must name it exactly. PR61 production remains unchanged at master `3bc97e1e0b446aede269f7414c0c7c6358fdc192`. Prior PR62–65 work must not be recreated.

## Root cause and resulting behavior

The primary repair exporter returned early for previously generic-proven quests before filling absent eligibility metadata. The retained observed-route ledger marked quests9145,9155,9173,9192 generic-proven; its repair-pack-v3 records show `protected_baseline=true` with no metadata changes. Stronger primary evidence later reopened those rows, but the omitted requirements were not restored by the narrowly scoped geometry/acquisition updates.

Baseline protection now preserves objectives, relations and geometry while allowing an explicit nondefault acceptance requirement to fill a missing field. Default-only protected rows still add no metadata. Existing non-null values remain authoritative input and are never overwritten. Paired skill/faction identities and thresholds are processed as one contract: when a known half disagrees with primary evidence, the missing half is not borrowed from that other source. Boolean, invalid signed/unsigned identities and conflicting values stay explicit unresolved evidence.

The applied delta contains only four source-confirmed minimum-reputation contracts. Quests9145,9155,9192 require faction922 at total reputation3000; quest9173 requires faction922 at9000. Every previous source repair, delivery/supplemental contract, objective count/ID, dependency and geometry field is unchanged. The existing runtime loader and scheduler already support these contracts; no new native API or observation synthesis is introduced.

## Original-client and server evidence

Pinned TrinityCore335 `Player.cpp:15409-15448` checks minimum reputation inclusively and maximum reputation exclusively. Revision is `95657f54779467effea8a1749a61ff93abc1d707`, TDB335.25101 SQL SHA256 `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`. Exact quest/addon row references and source hashes are retained in `eligibility-review.json.gz`, `metadata-delta.json` and `source-contracts.json`.

The enabled read-only IDA MCP was used to inspect `GetFactionInfoByID` at0x5D11E0, its result builder0x5D0DA0 and total-standing reader0x5D05B0 in the exact original build12340 binary SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. The sixth result is total reputation; an invalid faction can still supply a numeric zero alongside a missing name. The existing production capture already requires a valid string name and numeric sixth value, and binds the read to the player GUID. That implementation is preserved. Source thresholds never substitute for a live reputation observation.

## Exact coverage

| Primary classification | PR65 | This continuation |
|---|---:|---:|
| GENERIC-PROVEN | 2,963 | 2,964 |
| STRATEGY-PROVEN | 2 | 2 |
| DATA-INVALID/INCOMPLETE | 932 | 932 |
| SOURCE-UNCERTAIN | 365 | 364 |
| LIVE-ACCEPTANCE-REQUIRED | 16 | 16 |
| UNSUPPORTED-SCRIPTED | 57 | 57 |
| Total | 4,335 | 4,335 |

Only quest9173 becomes generic-proven. All four receive the missing reputation contract, but9145/9155/9192 keep their other recorded source obligations; see `reputation-closure-review.json`. Exactly1,369 remaining IDs have regenerated correlations. The ledger remains a mutually exclusive partition, with separate overlapping secondary flags. Uncompressed ledger SHA256 is `fefa4c179b40af0e44d1803b45e5a63b9a75428e766bf23c87c9996f12ef3435`.

All730 global spawn entries/14293points,56 quest-scoped credit records/1237searchpoints,46 GameObject objective repairs,645deliveries,154supplemental return contracts,369metadata-only dependencies and2relations remain unchanged. The base dataset SHA256 is unchanged. Only the existing9066/9447 strategy recipes remain vetted, rebound to the exact new repair bytes.

## Tests and acceptance

The actual tracked knowledge loader/scheduler fixture first produced13 intended assertion failures across34cases with no unexpected exceptions. All34 now pass: exact source metadata, missing/below/at/above reputation, wrong-faction input, completed-history authority, completed turn-in preservation and unchanged original dataset. Three existing focused runtime groups also pass. The negative pickup tests retain the positive at/above and completed turn-in paths.

Thirteen Python exporter regressions cover protected nondefault requirements, default-only preservation, signed reputation thresholds, matching and conflicting paired fields, source/base conflicts, invalid types, separate maximum reputation, class/level/skill requirements and preservation of prior facts. The corrected red run has10 intended failures and0errors; an earlier red had8 assertion failures plus2 test KeyErrors, retained separately. The full analyzer suite now has300tests, zero failures and one expected Windows permission skip.

The actual controlled dataset sweep passes258497 checks across4335rows; both vetted strategies pass39 lifecycle checks. Dirty-tree outputs are comparison fixtures only. Final clean-commit local34-command acceptance, exact four-stage closure reproduction and applicable hosted Windows/x86 workflows must pass at the containing candidate SHA. Full raw run/job/status/artifact and source-identity receipts stay external. These tests prove behavior for supplied observations, not live quest completion, customized-realm rules or path availability.

## Continued audit

The next high-fanout source family has125 quests and263 availability-condition rows. Most depend on quest rewarded/taken/complete/none/state observations; others require item, skill/spell, daily completion, area or level evidence. The AND/OR grouping, negation, target semantics and exact server quest-state rules must be verified before a condition model can close those source obligations. Unknown observations must remain unknown under negation, and a partial supported subset must not erase unsupported conditions.

Continue from the exact1369 IDs using pinnedTC335, original-build12340 read-only IDA and source-backed actual owner regressions. Preserve the distinction between unconditional data facts, current player observations and final server completion authority. No new strategy or live-completion claim is made by this slice.
