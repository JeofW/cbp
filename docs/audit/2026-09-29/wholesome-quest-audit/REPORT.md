# Wholesome Auto Quest audit — 29 September 2026

Source `ee1b4df7b06758988745e499554f70842f17d2e1` repairs the level-60 pickup ceiling and the additional proven
quest-state and completion defects. All **4,335 dataset rows** have a persistent
classification, structural findings, source references and deterministic test
results. This is an offline bot-logic audit; **zero live quest completions** are
claimed. The production CB folder has not been replaced by this candidate.

## Dataset and execution classification

Dataset SHA256: `f2ca79318694afaa4214fc09392e88c3d9d1eae5128f323d0d65b1b9e91a7e3f`. The inspected source and installed copies match.
There are 4,273 giver relations and 4,236 ender relations.

| Classification | Quests |
|---|---:|
| GENERIC-PROVEN | 1811 |
| STRATEGY-PROVEN | 0 |
| DATA-INVALID/INCOMPLETE | 1386 |
| UNSUPPORTED-SCRIPTED | 81 |
| SOURCE-UNCERTAIN | 882 |
| LIVE-ACCEPTANCE-REQUIRED | 175 |

`GENERIC-PROVEN` means the recorded generic planning/profile/behavior-completion
path passes under supplied observations. It does not prove native interaction,
combat, path travel or realm completion. All classifications and exact IDs are in
[classification-ids.json](classification-ids.json); overlapping unresolved
categories and their IDs are in [remaining-category-ids.json](remaining-category-ids.json).
The complete per-row evidence is [quest-ledger.jsonl.gz](quest-ledger.jsonl.gz).
Source conditions and omitted reference requirements prevent promotion to generic
proof even when the supplied-observation pipeline passes.

## Repairs

Pickup uses `MinLevel` and an explicitly declared `MaxLevel`, preserving the user's
low-level quest preference. `QuestLevel` no longer acts as an upper pickup limit,
including the ancestor-correction path. The dataset contains 170 quests with
MinLevel <=60 and QuestLevel >60, including 135 at quest level61/62.

Fresh typed loaded NPC/GO observations can supply missing or stale giver/ender
geometry, including map and Z differences. Actor, scan time, object namespace,
GUID and finite nearby 3D coordinates remain bound to the observation; existing
navigation, recovery and publication checks still apply.

Nullable class, maximum-level, skill and reputation requirements preserve the
difference between missing evidence and an explicit zero. Failed quests do not
generate objectives or turn-in work. Pickup and accepted objective execution now
agree on supported bound strategies. Cast and exploration/event pickup require
the applicable explicit recipe; already accepted independent ordinary work and
authoritative turn-in remain available.

Kill and gameobject completion resolve a unique typed identity and required count
to the original four physical counter slots, including sparse slots. A displayed
index, same-numbered other object type or duplicate identity cannot prove progress.
The generated-profile executor also checks objective type. Gameobject collection
uses carried items where appropriate. Carried items retained after failure or
abandonment no longer complete a stale collection behavior.

## Diagnostics and the original Hellfire report

Diagnostic logging emits `quest-audit` JSON snapshots no more than once per
30 seconds per scheduler. It records player and quest-log/history authority,
loaded giver identities and cached client dialog status, linked quest IDs,
stored/live geometry, reached admission gates, strategy status, navigation and
recovery scope/state/retry, and a final rejection reason for each relevant quest.
Every relevant nearby quest gets a row; the activity-text exclusion limit does
not hide it. Long names and geometry samples are bounded without dropping quest
identities. A loaded giver without a DB relation is recorded explicitly.

The old 16:28 log did not record the NPC GUIDs or positions seen on screen. The
level and geometry defects have deterministic reproductions, but attributing
those exact visible NPCs requires a new live diagnostic sample. Cached NPC status
does not establish a fresh offer for a particular quest.

## Structural flags and source review

Verified: 112 quests without giver relations, 103 without enders, 65 giver and45
ender relation rows without spawns, 375 kill objectives and57 GO objectives
without spawns, 240 nonzero-SpecialFlags quests and1,133 StartItem quests. The
sweep additionally found **1,204 collection-objective rows without creature spawn
data**. See [flagged-rows.json](flagged-rows.json) and each ledger row's disposition.

Secondary reference joins identify seven missing-giver quests with item-started
routes and124 missing-spawn objective rows with credit-alias or script evidence.
These joins do not invent executable recipes. `StartItem` is a provided-on-accept
item, distinct from `item_template.startquest`. Nonzero SpecialFlags alone is not
an invalid-data verdict. There is no installed strategy pack or provenance
sidecar; no new game-specific recipe was fabricated.

Primary server semantics are pinned to TrinityCore3.3.5
`8fda442f6c30ca21a622638063ab8b28376f1b25`; secondary comparison uses AzerothCore
`8337a378ac325e62a6a91e00c6a5e944205e8536`. Full URLs and hashes are in the source
manifests. Read-only IDA verified the original-client cached dialog-status field
and Lua observations, including the packet handlers -> object+0x90 write and
GetFactionInfoByID total-standing result. IDA is not evidence for server SQL or
custom realm scripts. See [ida-client-evidence.json](ida-client-evidence.json).

## Validation and practical limits

The final dataset run passes **246,892 checks**, zero failures,
covering every quest ID. **2,561** controlled generic pipelines
reach their supplied acceptance/progress/turn-in/next-scan endpoints; structural
and source limitations still govern their final classifications. The nine new
focused groups contain **142 cases**, with retained intended
red reproductions and passing final aggregate results.

The optimized Windows/x86 gate passes **34 stages** against **1,938 stable source
inputs**, including the full Wholesome suite and freshly extracted immutable
boundary fixture. Analyzer results are116passes and one Windows symlink-permission
skip out of117tests. Host compilation succeeds; existing compiler/package warnings
are retained in the logs. The isolated self-contained win-x86 candidate compiles
all13runtime components, including Wholesome and Singular, with zero runtime-source
errors. The real assembler and installed navigation engine identities are retained.

Per-row interruption cases exercise the shared executor revocation guard. Separate
root-owner regressions cover actual trigger observations. Neither is a real-server
interruption experiment. Simulated progress is supplied as an observation; this
audit does not claim autonomous quest completion through native gameplay, all
possible Cartesian state combinations, or independent reviewer approval.

Candidate: `D:\Dev\CopilotBuddy-Wholesome-Candidate-ee1b4df7`. Full raw evidence remains in `D:\Dev\CopilotBuddy-Evidence\postmerge-20260929\wholesome-dataset-audit`.
Reproduction instructions: `Tools/EvidenceAudit/WHOLESOME_335_AUDIT.md`.
Final hashes, stage receipts and deployment status are in
[verification.json](verification.json).
