# Original-client Wholesome quest audit

The audit reads the exact installed `quest_data.json`. Its SHA256 binds every
ledger row to the input bytes. Reference SQL is parsed as data, never executed.
No tool in this workflow installs a quest recipe, edits the dataset, attaches a
test to WoW, or certifies completion on a customized realm.

## Evidence contracts

Primary server contract: TrinityCore 3.3.5 revision
`8fda442f6c30ca21a622638063ab8b28376f1b25`. Secondary comparison: AzerothCore
WotLK revision `8337a378ac325e62a6a91e00c6a5e944205e8536`. The retained
`source-downloads.json` manifests contain file URLs, revisions and byte hashes.
Client analysis uses read-only IDA against the recorded build12340 executable;
it cannot establish server SQL, scripts, or a realm's customizations.

`GENERIC-PROVEN` is a bounded bot-logic result: actual scheduler decisions,
generated profiles, generic behavior construction and completion acknowledgement
under explicitly supplied observations. Native interaction, travel, combat and
real quest rewards are outside that claim. The dataset harness supplies progress
and server-completion observations; it does not discover those observations by
playing. `STRATEGY-PROVEN` additionally requires a bound, supported recipe and its
own execution evidence. Absence of `quest_strategies.json` is recorded explicitly.

`DATA-INVALID/INCOMPLETE` describes missing or invalid bot data, not proof that a
server quest is broken. `UNSUPPORTED-SCRIPTED` identifies a declared nonordinary
contract without a supported recipe. `SOURCE-UNCERTAIN` records unresolved source
or realm equivalence, including omitted reference requirements, availability
conditions and explicit reference escort/event contracts. Other unproven routes
remain `LIVE-ACCEPTANCE-REQUIRED`. These classifications are mutually exclusive;
each row also preserves all overlapping findings and live obligations.

The per-row interruption cases exercise the same real executor revocation guard
with different labels. Actual combat, death, rest and service trigger observations
are covered by the separately named shared root-owner regression groups. These
two layers must not be presented as separate live interruption experiments.

## Reproduction

Use Python 3.11 or later. Install the pinned `requirements.txt` in a virtual
environment to run all analyzer tests. The quest ledger and SQL reader themselves
use only the standard library.

```powershell
python -m unittest discover -s Tools/EvidenceAudit -p 'test_*.py' -v
python Tools/EvidenceAudit/quest_dataset_audit_335.py --dataset <quest_data.json> --references <reference-tables> --simulations <simulation.jsonl> --output <new-structural-ledger-directory>
python Tools/EvidenceAudit/enrich_quest_source_335.py --input <new-structural-ledger-directory> --evidence <retained-evidence-directory> --output <new-reviewed-ledger-directory>
```

The evidence directory contains `reference-tables`, `special-contracts`, their
download manifests, and `tc-questdef.h`. All destinations are create-only so an
earlier result remains available for comparison.

For the C# sweep, build the existing `PostMergeAuditRegressionTests` project for
Windows/x86 with its retained deny-native-dispatch boundary. Set
`CB_QUEST_SIM_DATASET`, `CB_QUEST_SIM_OBSERVATIONS` and `CB_QUEST_SIM_OUTPUT` to the
input JSON, the hashed reference-observation JSONL, and a new output filename.
Invoke `QuestDatasetSimulationRegressionTests` through the focused runner using
the x86 .NET runtime. `CB_QUEST_SIM_LIMIT` is only for a smoke test; omit it for
the complete dataset. Keep the full integrated regression gate in addition to
this focused invocation. The retained source manifests distinguish actual test
inputs from the current branch name.

## Observed item and creature-credit routes

The continuation adds a route companion for exactly seven item-started quest IDs
and124 missing-spawn credit-objective rows. `QuestObservedDatasetRoutesRegressionTests`
uses `CB_QUEST_SIM_DATASET`, `CB_QUEST_ROUTE_CASES` and `CB_QUEST_ROUTE_OUTPUT`.
Its case file is preserved with the final evidence. Run it through the same
Windows/x86 focused runner and deny-native boundary as the main sweep.

`extend_quest_routes_335.py --input <reviewed-ledger> --route-results <route.jsonl>
--route-cases <cases.json> --evidence <pinned-evidence> --output <new-directory>`
attaches the additional proof while preserving the original static pipeline result.
It decodes script event types separately from event flags and records linked-script
and timed-action-list parents. A script giving kill credit does not by itself
authorize an ordinary kill, item use or gossip action.

An observed item-start association must be current and carried; `StartItem` remains
distinct from that association. Ordinary alias routing requires a fresh loaded,
alive/attackable/selectable creature, its observed client credit fields and a unique
matching current quest objective. A supplied-positive alias test does not prove
that a real captive, corpse, friendly NPC or vehicle quest can be completed by
killing. These rows retain their source/realm and whole-quest obligations.

## Live diagnostic capture

Enable Diagnostic logging in the host. Wholesome emits `quest-audit` JSON lines
for zero work or nearby-giver comparisons, limited to one snapshot per 30 seconds
per scheduler. A snapshot has player, giver, per-quest and end records. Every
relevant nearby quest receives its own rejection row; a geometry sample limit
does not remove its identity or final rejection reason. Cached NPC dialog status
is identified as cached, without claiming it is a fresh per-quest offer.

The 2026-09-29 16:28 log did not record the loaded nearby giver identities.
Deterministic admission and geometry regressions establish systemic defects;
identifying the exact NPCs from that play session still requires a new live
diagnostic sample. Do not fabricate a retrospective NPC-to-quest attribution.
