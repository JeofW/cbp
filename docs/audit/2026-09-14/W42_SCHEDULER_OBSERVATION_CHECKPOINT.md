# W42 scheduler observation — reproduced, candidate not executed

14 September 2026. Resume draft PR44 on `audit/next-42-scheduler-observation-20260914`, stacked against PR42 `audit/next-42-scan-entry-20260914`. No merge or deployment. The next production repair is NOT verified or promoted.

## Recovered progress and isolated ownership

The previous uploaded publication handoff at86ba6832 was stale. Live PR42 first reported1d08c16c and then advanced to e97263eef2dbf1bc6b050d80d9c3e51232ce42e2 during inspection. That latter commit already promoted the completion-project dependency and allocated-zone-string fixture corrections. Those fixes, raw/readiness1bee278c and sale1a74f798 belong to the preceding continuations, not this one. This continuation created PR44 at e97263ee to avoid overwriting the moving PR42 branch. PR42 was reread at e97263ee before this checkpoint; its PR body/root handover still describe the older publication slice. Do not regress to that stale source or recreate these repairs.

This continuation downloaded and verified paired run34829035555/art10341398183 at1d08c16c: complete113-member archive/112 outer internal hashes, both50-entry inner manifests, all1690 source/config inputs and32 identical normalized outputs. Only WholesomeAutoQuest.cs differs between the local-only red/green candidates. Sale11/28 (17 assertions/0unexpected) becomes28/28; observation23/25 becomes25/25; boundary16/24 becomes24/24. Those are recovered runs, not new execution here. Their local-only candidate commit IDs are NOT published GitHub commits.

## New test-first execution in this continuation

Test source: 35cc917435f74d33558d0250e566bcb7ad9a6641, tree54aa82050d6dad8de98a7b567f25efc470f5468f. New file `Tools/WholesomeQuestRecoveryRegressionTests/QuestSchedulerRawPublicationRegressionTests.cs` adds23 scenarios through actual allocated Memory/QuestLog -> ScanAndRefresh/materializer -> XML/file -> ProfileManager -> already-running gate. The old five scheduler assertions and all original test groups remain unchanged. The shared allocated fixture now supplies matching nonzero raw object/descriptor GUIDs; that is controlled external input, not a mocked raw-reader return. Its original21 publication cases and nine acceptance cases pass with that correction.

| Evidence at35cc9174 | Run / artifact | Inspected outcome |
|---|---|---|
| Current focused, unchanged scheduler production |34831110419 /10342172741|All4 projects build. New7/23:16 intended assertions,0unexpected. Existing scheduler2/5:1 null-input behavior failure and2 missing-contract assertions. Other3 focused executables run0.|
| Actual combined17 |34831110320 /10342527041|All17 build/setup0;16run0; only Wholesome aggregate run1. New7/23,16 assertions/0unexpected also execute here. No retained entry was removed.|
| Host compilation |34831110267 /10341888242|Build0,3278warnings/0errors,explicit tests_run:false; no game attached.|

All complete archives, authenticated outer SHA256 values, CRCs, every supplied internal manifest, embedded source identities and result/log files were checked. Focused54 members/53 internal hashes; combined78/77; host4/no internal manifest. All1690 focused/combined source/config hashes match. All33 normalized outputs match byte-for-byte after mapping the expected `<project>/0/` focused directory to `<project>/W42Normalized/` combined directory, with collision checks. Do not claim those archive directory names are identical. The54-file nested focused source export matches its manifest entries; it does not export every host source.

Retained passing results include publication21/21+acceptance9/9, raw-memory18/18, raw-ready16/16, ready5/5, ready lifecycle3/3, sale-observation28/28, original sale23/23, completion15/15, boundary24/24, observation25/25, relation23/23, scan-entry15/15, scan-failure14/14, retry6/6, revocation13/13,33 analyzer tests and99 compiled Singular source files. Existing lift/native-boundary tests execute and pass, but no native game or lift traversal was exercised. All managed runs use Windows x86/.NET10.0.12; analyzers use Python.

The16 new assertion failures are overlapping scenarios, not16 independent bugs or16 live incidents. Several show missing TimedIdle/retained-protection behavior rather than proving that every incomplete-input case actually moved the player. Same-count/progress/owner changes at real navigation/XML/loader boundaries and duplicate/invalid occupied entries are covered, along with seven successful/cancellation/replacement/empty/grind controls.

## Candidate repair — saved, NOT compiled or executed

`Tools/W42TestAggregation/prepare_scheduler_candidate.py` was committed as98c6c4a3. It strictly checks the scheduler preimage SHA256 c603c55f18ed3b8c5f57cb0b57134240d52d63082afa209110322ca8c462b9a9, then prepares one production-file candidate. The local postimage has SHA2566741ddc6c2058cbcbc5722f5e0982972eff37ac45dc83abcd9593c9dd86d2ad8 and calculated Git blob61fac208910fc182918cf17a1020bc0d868e765d. This blob has NOT been stored by a successful preflight or promoted into a production commit. `W42_SCHEDULER_CANDIDATE_UNVERIFIED.diff` records the candidate; it is not a deployment instruction.

The candidate consumes existing CaptureSnapshot/IsSnapshotCurrent in the actual scheduler, derives HasCompleteQuestLog from the real observation, rejects incomplete/null pure snapshot input before selection/recovery side effects, and rechecks the same observation before scan-state changes, file writing, host loading and final publication. Nested real refresh-lease checks are intended to prevent a late obsolete observation from invalidating replacement work. Pure explicitly constructed snapshots retain a complete-input default for API compatibility; the live producer must not rely on that default. No original assertions were changed to obtain a pass. There is no passing-after evidence yet.

## Actual blocker, not a publishing-permission failure

Preflight workflow `.github/workflows/audit-w42-scheduler-preflight.yml` was committed as9a84d797ec6a77f9967c68ffe2117946e10d6843. Run34831727583 failed before runner assignment in both attempts. Attempt1 job103936377807 and retry job103937050245 each have runner_id0, empty runner name and zero steps; the retry ran from10:11:48Z to10:11:51Z. Thus neither candidate preparation, C# compilation nor test execution ran. No candidate artifact or stored verified blob is claimed.

The retry check exposes one error annotation, but the connector rejects its direct check-runs/annotations URL. Its text/cause was not retrieved. Do not diagnose billing, capacity, token expiry or lost repository permissions. The owner needs the failure annotation from GitHub Actions run34831727583. Native GitHub branch/file/GitDB/PR writes and the explicit workflow retry succeeded here. Reconnecting GitHub is not the established remedy for this execution failure. One retry was already performed; do not launch repeated blind retries.

The temporary candidate script/workflow are retained deliberately for exact resumption, not silently left behind after a successful repair. They restrict source preparation to this private repo/branch and an exact source hash. The job token is used only inside Actions to store one verified immutable source blob; no credential is printed/exported and no workflow remote commit/ref update is performed. Remove the temporary script/workflow when a verified source blob is eventually promoted.

## Exact continuation and review boundary

1. Read PR44/current heads and this checkpoint; reconcile any newer PR42 work. Inspect the actual Actions annotation, resolve the runner-start failure, then rerun the saved preflight once. Do not recreate tests or infer green from a retry request.
2. Require all23 new scenarios, all5 existing scheduler cases and every retained focused group to execute with the identical clean-red fixtures. Correct fixture/harness issues transparently if discovered; do not weaken assertions. Inspect complete candidate artifacts before native GitDB promotion.
3. Promote only the exact inspected scheduler blob, remove temporary preparation files, and run affected+actual combined17+host on the SAME committed source. Download/verify complete evidence and compare all source/fixture identities. A local-only candidate commit or host compilation is not a final regression pass.
4. Publish the tested-source/checkpoint separation, updated graph and independent-review request. No merge/deployment without separate owner approval.

The candidate has static self-review only. In particular it does NOT add continuous per-tick observation revocation: the outer Wholesome gate encloses combat/service behavior while pending refresh is deferred during combat, so a broad raw-progress veto could suppress combat. Audit that ownership separately. Legacy typed completion/objective reads are not made immutable by this candidate; raw-sample equality does not prove cache freshness, atomicity or ABA/session continuity. Recovery/host side effects are not a universal transaction. Independent review and live original WoW3.3.5a build12340 acceptance remain required.

## XYZ / lifts / wind riders and protected references

Retain `QUEST_TRAVEL_COVERAGE.md` and the owner's requirements: elevation-aware safe route selection, cliffs/bridges/caves/floors, lift discovery/boarding/support/docking/exit and finite safe failure, actual pickup-versus-turn-in taxi parity and route alternatives. None is fixed by this scheduler-observation candidate. Existing planar ranking, taxi thresholds/cooldown/nearest-origin selection and lift discovery/lifecycle limitations remain investigation targets, not a diagnosis of the user's live incident.

Preserve master8382a7ec05a64212ea0a237159dca427a0767425 and backupc43c50d8d5d6775055f19bf018b52930a264d4a4; PR34's prior merge;35/36 and W42 ancestry; separate capability-test43; exclude25. No merge, force push, installed-bot/Navigation.dll/mesh/runtime-capture/Lua change or Work/Codex switch. GITHUB_CONNECTION_RECOVERY.md remains the recovery authority only if publishing tools themselves disappear: explicit @GitHub retry, fresh selected-plugin conversation, reconnect last. Full W42 and the exhaustive audit remain incomplete.
