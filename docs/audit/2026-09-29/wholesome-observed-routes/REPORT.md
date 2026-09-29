# Wholesome Auto Quest — complete dataset audit and observed-route continuation

Implementation: `370fa0ef56fd03385a80aba14688fe7ece8753f1`. Dataset: **4,335 unique quests**, 4,273 giver relations
and 4,236 ender relations, SHA256 `f2ca79318694afaa4214fc09392e88c3d9d1eae5128f323d0d65b1b9e91a7e3f`.

The repaired implementation, complete per-quest ledger and offline validation are
finished. **Live acceptance remains outstanding.** No game-specific recipe was
invented, no live quest completion is claimed, and production CB was not replaced.
The previous checkpoint is preserved under `../wholesome-quest-audit/`.

## Final classifications

| Classification | Quests |
|---|---:|
| GENERIC-PROVEN | 1,816 |
| STRATEGY-PROVEN | 0 |
| DATA-INVALID/INCOMPLETE | 1,381 |
| UNSUPPORTED-SCRIPTED | 81 |
| SOURCE-UNCERTAIN | 882 |
| LIVE-ACCEPTANCE-REQUIRED | 175 |

Every row has structural findings, simulation results, source references,
dispositions and remaining obligations. Exact IDs are in
[classification-ids.json](classification-ids.json); overlapping unresolved
categories are in [remaining-category-ids.json](remaining-category-ids.json).
The full ledger is [quest-ledger.jsonl.gz](quest-ledger.jsonl.gz).

Generic proof is limited to actual bot planning/profile/behavior acknowledgement
under the recorded supplied observations. It does not prove travel, combat,
native gameplay or this realm's server completion. Original static-route results
remain intact when an additional observed-item route supplies a passing pipeline.

## Proven systemic repairs

Pickup no longer treats QuestLevel as an upper acceptance limit. It uses MinLevel,
explicit MaxLevel and the existing user preference for avoiding low-level quests.
Both direct pickup and ancestor correction are covered. Fresh loaded NPC/GO
observations can provide missing/stale typed geometry while preserving map/Z,
actor/GUID/time, navigation, recovery and publication checks.

Optional class/skill/reputation/maximum-level fields preserve absent evidence as
unknown. Failed quests cannot produce objective or turn-in work. Validated recipe
admission matches accepted execution. Cast and exploration/event pickup require
the applicable explicit strategy; already accepted independent work and
authoritative turn-in remain available.

Generic progress resolves a unique typed objective identity/count to its physical
counter slot. Sparse indexes, duplicate credit IDs, different objective types and
changed requirements cannot fabricate completion. GO item collection uses carried
item identity. Failure/abandonment or stale actor state revokes collection completion.

## Item-started pickup and credit aliases

All seven exact item-started quest IDs were tested: 136, 594, 624, 654, 2882, 4881 and 9672.
Pickup requires an observed original-client item-to-quest association, fresh actor
and item identity, current carried slot, known history, available log capacity
and an inactive/unaccepted quest. The final Lua request revalidates that association
before opening the item. The existing quest-dialog owner still validates and
acknowledges actual acceptance.

The compatibility StartQuestId property now reads BeginQuestId, not stationery.
A missing starter defers pickup instead of stopping the bot. Item behavior does
not fall into NPC movement/interaction. IDs 136, 594, 624, 4881 and 9672 also complete the
controlled item-pickup→accepted-log→turn-in→rewarded-history→next-scan pipeline.
Those five rows gain generic proof; 654 and 2882 retain their other data/source gaps.

All 124 missing-spawn credit rows were examined and tested. **40 rows** have ordinary
alias routing under explicit fresh, alive/attackable/selectable client observations
and matching runtime quest metadata. The generated profile retains the original
credit identity; the real generic host recognizes the observed creature cache alias
and acknowledges the physical objective counter. A supplied-positive test does
not authorize killing actual friendly captives, corpses or vehicle actors.

**84 script-only rows** have no invented ordinary route. Every matching source
script is recorded with event/action enum meaning, parameters, source line,
linked-script parents or timed-action-list callers. Exact dispositions are in
[credit-script-dispositions.json](credit-script-dispositions.json). This source
graph review is not an executable strategy or a claim about a customized realm.

## Structural flags and diagnostics

All initial flags were verified and classified: 112 quests without givers, 103 without
enders, 65 giver and 45 ender relation rows without spawns, 375 kill and 57 gameobject
objective rows without spawns, 240 nonzero-SpecialFlags quests and 1,133 StartItem quests.
The sweep also records 1,204 collection-objective rows without creature spawns.
StartItem means an item supplied on acceptance; it is not a starting-item recipe.
Every flag has an exact row disposition in the ledger, including observed routes
that supplement an absent static relation. The installed strategy and provenance
sidecars are absent; this was not hidden as successful special-quest coverage.

Enable **Diagnostic logging** for the `quest-audit` snapshots. They retain player
and log/history authority, loaded giver identities/status, DB quest relations,
static/live positions, reached admission gates, exact rejection reasons,
navigation and recovery state/retry. New item-starter and creature-credit rows
include their observed identities and source. Every relevant nearby quest receives
a row; long names and geometry samples are bounded without dropping candidates.
Snapshots are rate-limited to 30 seconds per scheduler.

The original 16:28 Hellfire log omitted the loaded NPC GUIDs and positions. The code
defects have deterministic reproductions, but naming those exact visible NPCs
requires a fresh live diagnostic sample. Cached dialog status is not a current
quest-specific offer.

## Evidence and validation

Primary server contracts: TrinityCore 3.3.5
`8fda442f6c30ca21a622638063ab8b28376f1b25`. Secondary data/contracts: AzerothCore
`8337a378ac325e62a6a91e00c6a5e944205e8536`. Source URLs, revisions and hashes are
retained in the manifests. Read-only IDA reconfirmed the exact 32-bit build 12340
binary and item/creature/dialog/quest observation layouts. It does not establish
server SQL or custom scripts.

Final validation at the implementation above:

- **246,892** full-dataset checks plus **1,034** exact-route checks: **247,926 passes,
  zero failures**, covering all 4,335 quests and the seven item/124 credit rows.
- **202 focused new regression cases** across the main audit and continuation.
- **34 optimized Windows/x86 integrated stages** pass with **1,945 stable source
  inputs**, including the regenerated immutable boundary fixture and host build.
- Analyzer suite: 125 passes and one Windows symlink-permission skip out of 126 tests.
- Self-contained win-x86 candidate: all 13 runtime components compile, including
  Wholesome and Singular, with zero runtime-source errors. Native libraries and
  the installed navigation engine are preserved.

Per-row interruption cases exercise the existing executor revocation guard;
separate shared-root regressions cover actual trigger observations. These are
controlled tests, not live interruption experiments or all possible state products.
No independent reviewer is claimed. Existing compiler/package warnings remain in
the retained logs.

Candidate: `D:\Dev\CopilotBuddy-Wholesome-Candidate-370fa0ef`. **Not installed in production.**
Full raw evidence: `D:\Dev\CopilotBuddy-Evidence\postmerge-20260929\wholesome-dataset-audit`. Reproduction:
`Tools/EvidenceAudit/WHOLESOME_335_AUDIT.md`.
Final stage results, hashes and proof limits: [verification.json](verification.json).
