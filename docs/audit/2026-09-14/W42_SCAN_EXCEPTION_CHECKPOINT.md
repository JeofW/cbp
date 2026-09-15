# W42 scan-exception and cancellation checkpoint — draft PR41

Recorded14 September2026. This continuation read the uploaded PR39 handover, reconciled live PR39/40/41, extended existing PR41 and ran new Windows Actions. Earlier saved repairs and executions were recovered, not recreated or attributed to this continuation. No merge/deployment occurred.

## Bounded production repair and exact source

Tested commit **7bf54c1ede756867968b250e8472d9c9428337af**, tree **8d9b058cdfe2a7e4edd16fb552c070a5f1227e8b**. Compare against clean test-first **c3d455d0d8c6988d8b3f796f02b7928053022b51**: exactly two production files, four added lines in QuestScheduler and one changed catch line in WholesomeAutoQuest. All fixtures/normalizers/build targets are unchanged. Later checkpoint commits must be documentation-only before inheriting these results.

`QuestScheduler.ScanAndRefresh` had guards for unavailable input, but valid input followed by identity/log/context failure left the previous LastSchedule/CurrentProfilePath executable. The repair calls the existing InvalidatePublishedWork before those observations. Existing timed-idle retry remains; ActiveQuestIds stays conservative for scheduled-item sale protection. There is deliberately no unconditional late invalidation catch: an old failure must not erase a replacement generation's publication.

`WholesomeAutoQuest.DoScan` caught all Exception, swallowing ThreadInterruptedException and OperationCanceledException. Its catch now excludes those control signals. The real RunPendingRefresh finally releases its RefreshGate lease when cancellation propagates. Public signatures, the exact original exception, ordinary-error behavior and the stale-generation control remain.

The fourteen new cases exercise actual Memory.Read, QuestLog enumeration, QuestRecoveryRuntime, ScanAndRefresh, DoScan, RefreshGate and an already-running WholesomeExecutionGate. Only external cached memory/handle values and optional player observations are controlled. Prior publication is seeded to test revocation; this is NOT an end-to-end successful raw-slot-to-profile-generation proof.

## Fresh failing-before / passing-after evidence

| Source and execution | Result |
|---|---|
| c3d455d0, run34803073140/art10332330397 | All14 new cases execute:4controls pass,10intended assertion failures,0unexpected; normal assertion-only process exit1. |
| 7bf54c1e, focused34803757188/art10332441880 | Same14cases pass14/14,0assertions,0unexpected. Prior13revocation and6retry also pass; broader W42 remains red. |
| 7bf54c1e, actual combined34803757306/art10332172475 | All17 entries build/setup0;14run0,three W42-containing entriesrun1. New14/14 and prior13/13+6/6 reproduced in this combined execution. |
| 7bf54c1e, host34803757195/art10332122465 | Host build0,3278warnings/0errors; explicitly tests_run:false. |

Every outer digest was checked against GitHub metadata; ZIP CRC and all45 focused /69 combined internal hashes were verified. Focused archives have46 file members; combined70; host4 with no internal hash manifest. All1,680 tracked input hashes match focused-to-combined. Red-to-green only the two production files differ; all25 generated normalization files are byte-identical. Complete logs and every result were inspected. Runtime is Windowsx86/.NET10.0.12; x64 SDK10.0.401 compiles the x86 targets. Analyzers are Python, not mislabeled x86 C#.

Original retained Wholesome groups and Main finish. Relation23/23, original sale23/23, completion-owner15/15,33 analyzer tests and99 Singular source files compile/pass as applicable. The old supplemental PR39 retained-baseline project was not recreated or claimed newly run: PR40 normalization already makes those retained sources execute in the original project.

See W42_SCAN_EXCEPTION_EVIDENCE.json for immutable hashes and a case-by-case transition map. Ten failing assertions are not ten independent bugs; suites overlap and offline dispatch observations do not prove items were deleted or accepted by a server.

## Native fixture counterevidence — not a hidden green badge

Initial test-only eb236c45 and constructor-isolation6877382d executed all14 new cases (4pass/10assertions/0unexpected inside the group), then the process aborted after later groups/Main with a garbage-collected System.EventHandler callback error, exit0x80131623. Those are NOT clean assertion-only baselines. Avoiding Memory's constructor alone did not prevent fasmdll_managed loading. Source/binary inspection identifies a native module-uninitialization callback boundary, not a complete root-cause proof or a production repair.

The clean c3 baseline adds a clearly marked, runtime-guarded deny-dispatch Fasm.ManagedFasm type in the PRIVATE Wholesome test output. It has no public constructor or assembler API, so native dispatch cannot be simulated as success. Actual managed owner bodies still execute. The repository Lib/fasmdll_managed.dll, host output, Navigation.dll, installed bot and meshes are unchanged. This isolation does not validate native assembly/injection/shutdown.

Boundary source/targets and all normalization outputs are byte-identical between red and green. Rebuilt boundary binaries are NOT byte-identical: red SHA256096d08bbfa982622e4fe09442e6e72b36af5d3b3ae52fd0537bbcb019cbc4857; repaired focused AND combined SHA2569c95b8c11ba7a15107b0f67bd672cd2a68f5d9de704ddf045ec82858cf52fb05. The binary difference reason was not independently established; no binary equivalence is inferred. Both runtime guards verify the explicit marker and lack of any public dispatch API. Earlier wording about identical boundary bytes means source/targets, not a claim about those different rebuilt binaries.

## Recovered older work and corrected handover

PR39 head318c0a7d contains its bounded completion-owner repair; PR40 head877de5cd already normalized all groups. PR41's old retry repair dc441aaa was not new work here. Its recovered red08cbcd13 run34797113354/art10330331511 executes3/6; recovered dc441aaa run34797871176/art10330048504 executes6/6. All archived fixture bytes match; only QuestScheduler changes in that source comparison. Recovered dc441aaa combined34797871182/art10330043513 retains17 entries,14run0. These historical results are not substitutes for the new same-7bf verification above.

The user-uploaded completion ZIP SHA256cedd3771ed4b5185a3bf113014152258635e402e6a6ff4cedd1078156a2b7152 has30members/29internally listed hashes verified. It is a historical handover, not the latest branch. Do not assume PR40 inherited PR39's later supplemental runner/docs: it branched from the earlier repair. W41 root instructions were stale on PR41 and are now retained separately, not reused as the starting point.

Authenticated native file/tree/commit/ref writes succeeded here, and a native ref update published7bf to existing PR41. A temporary hash-locked GitHub Actions job created only immutable candidate Git objects using its own short-lived token in memory; no ref, test or credential export was performed by that preparation. The two temporary preparation files are absent from7bf. The native connector separately published the candidate, triggering the actual Windows workflows. No account permission metadata or local attachment was used as proof of publishing.

## Remaining W42 — explicitly not fixed

Observation main4/25 includes19 missing proposed CaptureSnapshot-contract cases plus2sale assertions. Ready1/5 retains4controlled metadata/readiness/player-history failures. Scheduler2/5 retains1real null-accepted-to-pickup assertion plus2missing proposed HasCompleteQuestLog contracts. Boundary16/24 has8assertions/0unexpected, covering unmaterialized/mixed/full logs, dataset uncertainty, hydration/eviction and sale freshness. Missing contracts are not reproduced defects and do not justify invented APIs or observation epochs.

Next close the real read-success/raw-slot and session/frame owners, then actual ScanAndRefresh -> MaterializeSchedule -> ProfileBuilder.WriteProfile -> DoScan/ProfileManager.LoadNew -> running gate. Current LastSchedule assignment precedes XML build/write; vendor discovery and validity/world reads precede this slice's new revocation point. Exceptions there, successful stale publication, native completion side effects, owner replacement, same-character reconnect, ABA, readiness history and sale revalidation remain open. Successful identity reads and metadata completeness must remain distinct; zero from failed ReadBytes is not proof of an empty slot. Equal scans/counts are not an atomicity or lifetime proof.

Self-review checked the exact two-site diff, unchanged assertions, exception identity, retry and protection preservation, running-child Stop behavior, stale-generation replacement, source hashes and complete suite results. No independent reviewer or original client was attached. Source-confirmed/reproduced/regression-verified applies only to these bounded controlled owners; runtime frequency and client/server outcomes remain live-unverified. The exhaustive architecture/refactoring audit is NOT complete.

## Durable operating instruction and safety

Read ../../../../GITHUB_CONNECTION_RECOVERY.md and root NEXT_CHAT_PROMPT.md. If GitHub actions disappear/stall, one bounded check, remind the owner to reconnect GitHub, then rediscover/retry once; do not repeat an extended read-only audit. Reconnection appeared helpful, not a proven diagnosis. Never request/export credentials, use GET writes or conflate local CLI failure with native connector failure.

Preserve master8382a7ec05a64212ea0a237159dca427a0767425 and backupc43c50d8d5d6775055f19bf018b52930a264d4a4. PR34 already merged;35/36 retained;25excluded. No merge/deployment without new approval. No installed bot, Navigation.dll, runtime-capture or mesh changes. Continue after W42 in the saved dependency order: owner-scoped item protection; atomic FILE reload (not runtime/profile clearing); merchant/mail/discard/consumption; vendors/settings; rest/restart/cancellation; special quests; roster; combat/support; native/lifts.
