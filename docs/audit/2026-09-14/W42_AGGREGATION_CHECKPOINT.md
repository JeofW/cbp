# W42 aggregation checkpoint — draft PR #40

## Actual execution and reconciliation

This continuation successfully created an authenticated branch, committed files, opened draft PR #40, and executed GitHub Windows workflows through authorized pushes. Local Git still failed DNS; no local authenticated checkout or C# runtime existed. C# results below came from GitHub Windows runners, not local Python or account-permission metadata.

Branch: `audit/next-42-test-aggregation-20260914`. PR: https://github.com/JeofW/CopilotBuddy-private/pull/40 . Final tested source: `cd82129ec81f8e51fa12620f95b307e2ca5b34b6`, tree `36f2c1065c678644460a4f15a3152de00c51c9b4`. Later checkpoint commits are documentation only and must be compared to this tested commit before inheriting its evidence.

The branch starts at the PRE-EXISTING PR #39 completion repair `1dccead847f330430f148609858c6f89d5724ad8`. This slice adds NO new production repair. It preserves PR #38's fixtures and PR #39's repair, rather than recreating either. The PR #39 base independently advanced to `318c0a7d9e8518a468a52af05134aaad3e401eee`; read its `W42_COMPLETION_OWNER_CHECKPOINT.md` too. Its supplemental baseline and documentation are not overwritten or attributed to this slice. Original W42 head remains `2d3728feb22938428fe64d8ff8204d5779cb4e9e`.

## What changed

The original ready-log module initializer aborted before the 25 main cases. The scheduler initializer similarly hid retained Wholesome tests. Two test-only Directory.Build.targets imports now generate copies under obj, remove only recognized initializer attributes, and invoke the original group bodies from Main with independent exception boundaries. All assertions and original C# fixture files are retained; any group failure leaves a nonzero process exit. Unknown entry-point shapes fail normalization instead of silently losing coverage.

The actual integrated workflow retains all 14 historical entries and adds three W42 executables. Every managed executable uses Windows x86 .NET 10. The normalized comparison workflow runs identical fixtures against unchanged production at `2d3728f` and the inherited repair in the current commit. Missing proposed contracts and build/harness errors are not treated as reproduced production defects.

## Final executable evidence

Both workflows ran on `cd82129ec81f8e51fa12620f95b307e2ca5b34b6`:

- Comparison: https://github.com/JeofW/CopilotBuddy-private/actions/runs/34794252845
- Actual combined: https://github.com/JeofW/CopilotBuddy-private/actions/runs/34794252860

All three full artifacts were downloaded, ZIP-CRC checked, matched against GitHub SHA-256 digests, and inspected through their case reports, build/run logs, generated fixtures and source identities. The two comparison archives each contain 42 files with 41 verified internal hash entries; the integrated archive contains 66 files with 65 verified entries. The manifest itself is the sole excluded file. Exact digests and artifact IDs are in `W42_AGGREGATION_EVIDENCE.json`.

Runtime: Windows x86 .NET 10.0.12; full-owner and boundary reports confirm four-byte pointers. No game attached. The target remains original WoW 3.3.5a build 12340, not modern Classic. Existing compiler warnings remain; these are not warning-free builds.

| Group | Unchanged production 2d3728f | Current cd82129 |
|---|---|---|
| Main observation, 25 cases | 3 pass; 3 behavioral assertions; 19 missing proposed contracts | 4 pass; 2 sale assertions; same 19 missing contracts |
| Ready-log, 5 cases | 1 pass; 4 assertions | Same |
| Scheduler, 5 cases | 1 pass; 1 behavioral assertion; 2 missing contracts; 1 NullReferenceException | Same |
| Full completion owner, 15 cases | 10 pass; 5 assertions; 0 unexpected errors | 15 pass; 0 assertions/errors |
| Extracted boundary, 24 cases | 13 pass; 11 assertions; 0 unexpected errors | 16 pass; 8 assertions; 0 unexpected errors |
| Normalization utility, 8 Python tests | 8 pass | 8 pass |

All 35 observation/ready/scheduler case records are present in each comparison. The nine deferred Wholesome groups all start; eight pass, the pending scheduler group fails without masking subsequent groups, relation identity passes 23/23, and the original Wholesome Main reaches its final success message. That legacy message does NOT make the aggregate executable green: its exit remains 1.

The final integrated run has all 17 entries, all build exits 0, 14 run exits 0 and three run exits 1: WholesomeQuestRecoveryRegressionTests, QuestLogObservationRegressionTests and QuestObservationBoundaryRegressionTests. The original sale suite remains 23/23, analyzers 33/33, and Singular compiles 99 source files. No historical entry was dropped to change the badge. The full combined workflow correctly remains red.

## Source and evidence controls

The 21 normalized files/manifests are byte-identical between baseline and current. Every original/generated C# hash in the normalization manifests was checked against captured fixture sources. All 1,672 recorded source/config hashes match between current focused and integrated artifacts. Between baseline and current, the only changed production C# input is QuestLog.cs: the already-existing PR #39 fix. This re-verifies that bounded repair; it is not a new fix authored here.

The first run pair at `678c6d01f4de448b319d3f0c681ffed05448deeb` executed all cases, but integrated run 34793881514 failed afterward while hashing bracketed paths. Its ZIP digest is valid, but it lacks working-source-hashes.json and files-sha256.json. This was a workflow evidence-capture error, not a production defect. Commit cd82129 changed hashing to literal paths and reran both workflows; final manifests above are complete. Preserve the first artifact as superseded evidence, not a complete final validation.

## Actual frontier and limits

Source-confirmed and controlled-reproduced: optional metadata loss hides raw acceptance from sale protection, same-count changes can leave stale sale authorization, ready metadata/readiness/owner transitions fabricate departure refreshes, and null accepted scheduler input invents pickup work. These controlled calls do not prove real item deletion, actual turn-ins, abandonment, or client/server acceptance.

The 19 CaptureSnapshot and two HasCompleteQuestLog failures are missing proposed contracts, NOT 21 reproduced production defects. The scheduler unavailable-owner NullReferenceException is an unresolved fixture/owner-boundary error, NOT yet a clean behavioral reproduction. Isolate it and retain its full exception before claiming ScanAndRefresh closure. Counts overlap across suites and must not be summed as unique bugs.

Next owner chain: successful raw descriptor reads and occupied identities independently of optional metadata -> real ScanAndRefresh -> MaterializeSchedule -> ProfileBuilder.WriteProfile -> DoScan/ProfileManager.LoadNew -> already-running WholesomeExecutionGate. Unknown/stale data must revoke existing side-effect permission, not merely prevent the next scan. Full W42 remains unfinished.

Retain PR #39's cautions: ReadInternal<T> can return default when ReadBytes returns null; zero alone does not prove an empty slot. Default AcquireFrame is not automatically a hard native frame capture. FrameLock executor ownership, release and timeout need real evidence. Display names, PID, character/realm equality, counts and equal repeated scans do not provide reliable session generations or ABA immunity. Preserve genuine empty/complete behavior, cancellation and public APIs.

No new production fix, merge, deployment, installed-bot change, Navigation.dll replacement, mmaps download, runtime capture, or Lua change occurred in this slice. Master remains `8382a7ec05a64212ea0a237159dca427a0767425`; backup remains `c43c50d8d5d6775055f19bf018b52930a264d4a4`. PR #34 stays merged, #35/#36 stay inherited, and #25 stays excluded. Independent review remains pending. The exhaustive architecture/refactoring audit is not complete. Continue with `W42_AGGREGATION_NEXT_CHAT.md`; do not restart the broad audit.
