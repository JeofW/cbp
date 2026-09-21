# W92 - verified gossip interaction lifetime; handoff reconciliation

21 September 2026, Asia/Kuala_Lumpur. Repository jeofwong/CopilotBuddy-private
(1367174964), PR51 draft/unmerged, approved branch
audit/next-55-equipment-observation-20260917.

## Publication versus tested source

This containing commit publishes the documentation that was missing after the
verified repair. Resolve its publication SHA from GitHub. It makes no new
production/test/workflow change and does not represent a new C# execution.
Tested source and last production:262228f1abf855c0a1e61e6c7eb1c8128c8cf446,
tree fb4ffab6fcb16b85407a8d73942bc059472f24db.
The earlier W92 local handoff remains an accurate historical record of its
then-unpublished documentation. That refusal did not undo the two code commits.

## Original compilation failure and corrected behavioral baseline

Entry e12ab0c60c2b6fe73bd7ec5b063a17d04820b366 failed Wholesome compilation:
CS0103, missing WoWObjectType name at normalized fixture line514.
Run35545306499/job106170029060/art10616277811 has build1/run=null for
Wholesome; other16 integrated entries passed. The matching scenarios did not
execute, so this is not behavioral red.

Fixture-only f7a598ad3a15c2e9b216e965113c255c538e37fc qualifies the existing
Styx.WoWObjectType.Unit, one line in the existing constructor-dispatch fixture.
No assertion, case, production file or workflow changed. Run35564802997,
job106224401617/art10624156135 then compiled and executed: matching4/10,
six intended disposal/player-replacement assertions at dataset indexes0/3/17,
zero unexpected matching-case errors. All cases reached the actual interaction/
refusal boundary. Ready/no-replay and bounded-refusal controls passed; original
constructor/lifecycle12/12, scheduler53/53 and other16 entries passed.

## Minimal production repair and controls

262228f1 adds four lines only to runtime-snapshot/Quest Behaviors/GossipEvent.cs.
After target.Interact returns it checks existing OwnsActor/IsDone before
publishing Counter, interaction GUID, timestamp or status. Synchronous host
callbacks can dispose the owner or replace its player during the call; the old
continuation unconditionally recorded pending state afterward.

The unchanged-owner counter still records attempts, not successful submission
or quest credit. No shared/native ABI, offset, source recipe/schema, scorer,
workflow or test expectation changes in the production repair.

## Exact-source recorded verification

Integrated35565276547/job106225735651/art10623907327 completed successfully:
17/17 build/run entries, matching10/10, original constructor/lifecycle12/12,
scheduler53/53, gossip lifetime58/58, controlled generated dispatch18/18,
gossip strategy14/14. Named matching assertions/unexpected errors are zero.
Host35565276548/job106225735756/art10623138863 completed:exit0,3344 warnings,
0 errors, tests_run=false. Seven checks on the tested source completed/success.
All game_attached=false; required execution is Windows/x86 in GitHub Actions.

Red/green each contain1837 input paths, with no additions/removals and only
GossipEvent.cs changed. All155 normalized fixture members are identical.
The namespace correction precedes this unchanged-fixture pair and is disclosed
separately. W92 verified four downloaded outer digests and596 inner manifest
hashes across three integrated archives with complete membership. No separate
ZIP-CRC verification is claimed. Host has no inner manifest. Exact artifacts,
hashes, bytes and counts are retained in W92_EVIDENCE.json.

These tests execute the actual loader/scheduler/XML/compiler/factory/wrapper/
WoWUnit.Interact refusal path with allocated observations and no native executor.
They do not prove successful native submission, server credit, original-client
acceptance or independent review. W89/W90/W91/W92 evidence layers must remain
separate; adding their counts does not produce native end-to-end evidence.

## Handoff and publication recovery

The prior atomic documentation request was safety-blocked; read-back showed
head262228f1, unchanged master and W91 root pointers. No hidden documentation
commit was found when this continuation reconciled the branch. The existing
root pointer blobs were309783209e5ec7efe794677ae426d1a7b24a58ef and
e02adc8670d78f6586b15e243eb3a5ebd4a4fc2f.

Authenticated gh reads, artifact downloads and expected-head code publication
worked. Core's UNKNOWN_TOOL error explicitly reported no dispatch, whereas a
completed terminal session can deliver its older result beside a later poll
error. The words refused/refusal inside matching-recipient tests describe game
request rejection, not GitHub authorization. These outcomes must be classified
separately. No permanent routing repair is claimed. See HANDOFF_POLICY.

Use a reviewed five-path documentation change with expectedHeadOid, then verify
the returned parent, changed-file set, postimage bytes and direct refs. Do not
use the incomplete local clone/index; its apparent deletions are not proposed
repository changes. Prior safety refusals stay recorded, not relabelled success.

## Remaining work and retained authority

Next R08: obtain actual-boundary regressions for the reward Lua boolean receipt
and UTF-8 byte-length source findings, then make minimal justified repairs.
Preserve quest/item identity and scoring; do not treat a controlled C# boolean
return as native Lua execution or broadly change the bridge without caller review.

R03 source-backed special-action mapping and successful native dispatch;
R04 physical displaced/foreign cursor/same-slot popup/lifecycle; R06 same-content
menu generation/native concurrency; R07 actual compiler inputs/static state/full
refresh; and all earlier Wholesome/navigation/Singular/shared/independent/native/
supervised gates remain open. Preserve W91 ordinary credit metadata/publication,
strict v1 fields/ObjectiveProgress deferral, QuestComplete admission, CAST refusal,
independent collection, dataset/raw/item namespaces and navigation guards.

Retain W91/W90/W89/W88/W87/W86 and W80 checkpoint/evidence/ledger, all earlier
failed intermediates and separate red/green pairs, MIR protections, dense-pull
containment, auction withdrawal and exclusions. W80's140-commit review after
18 September2026 21:58 Malaysia /13:58 UTC is complete and must not restart.
Backup audit/backup-pr51-before-w92-lifetime-20260921 holds f7a598ad; preserve
older backups. Direct master was b2324913e2499ba30b239dd67224ca2c655c05cc.

Original WoW3.3.5a/build12340, TrinityCore3.3.5 primary/AzerothCore WotLK secondary.
No guessed recipes/thresholds/offsets/coordinates, AuctionHouse/ProfessionBuddy,
PR25 integration, installed-file change, local bot/C# build/run or CoS modification.
Never recreate PR58 or merge capability/W88-red reference branches. No master
write, merge, deployment or independent/native acceptance is claimed. Existing
conditional merge authority requires all mandatory gates, direct refs, backup,
exact-source/head validation and a reviewed integration result.
