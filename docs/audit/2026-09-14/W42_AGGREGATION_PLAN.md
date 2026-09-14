# W42 test-execution normalization plan

## Reconciliation and scope

Start from existing completion-owner repair 1dccead847f330430f148609858c6f89d5724ad8, not master. The original W42 head 2d3728feb22938428fe64d8ff8204d5779cb4e9e remains the unchanged-production baseline. PRs #38 and #39 predate this slice. PR #39 advanced to 3d8493927adb76e886b1e1ccca03a93cce0cfb8b while this isolated branch was being prepared; its supplemental workflow is not overwritten here.

This slice changes test execution and evidence capture only. No new production fix, merge, deployment, native DLL, mesh, installed bot or captured logs. Preserve the 25 observation, five ready-log, five scheduler, original 23 sale and all historical combined entries. The existing completion fix is inherited, not recreated.

## Implementation

1. Add a test-only source normalizer and executable utility tests. Generate copies under each project's obj directory. Remove only recognized ModuleInitializer attributes; do not edit assertions or production sources. Call every formerly-initialized group from Main behind separate exception boundaries, maintaining a nonzero exit code on any failure. Preserve the legacy main body. Refuse unfamiliar initializer signatures or entry-point shapes.
2. Import the shared build target only in QuestLogObservationRegressionTests and WholesomeQuestRecoveryRegressionTests via their own Directory.Build.targets. Existing full production links and sale/ready extraction targets remain in force. Record original and generated SHA-256 per file and invocation order. Generated sources are test fixtures, never production repairs.
3. Run identical normalized fixtures against unchanged production at 2d3728f and this branch's inherited completion repair. Capture build errors, runtime errors, missing proposed APIs, assertion reports and controls separately. Missing contracts are not reproduced production defects.
4. Extend the actual integrated workflow from 14 to 17 entries, adding observation, completion-owner and extracted-boundary executables without removing the 23-case sale suite or other historical entries. Run focused and integrated verification on the same pushed commit; retain complete artifacts and generated sources.
5. Inspect every suite result and hashes. Keep remaining W42 behavioral failures red. Record the narrow completion result separately from raw-slot scheduling/publication/executing-plan gaps. Publish a draft review PR and a durable checkpoint, without any merge.

## Verification and frontier

Generator utility tests prove only source transformation behavior, not C# or gameplay correctness. Windows x86 .NET 10 execution is mandatory. Original WoW 3.3.5a build 12340; no live client. No Lua changes in this slice.

The real raw-slot -> ScanAndRefresh -> MaterializeSchedule -> ProfileBuilder/publication -> executing-plan invalidation chain remains unfinished. Equal scans/counts do not establish atomicity or a reliable session revision. Historical controlled results and new controlled tests do not establish live abandonment, turn-in or inventory deletion.
