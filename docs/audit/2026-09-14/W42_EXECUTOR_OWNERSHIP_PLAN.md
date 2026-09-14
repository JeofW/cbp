# W42 actual executor ownership — implementation plan

## Scope and retained authority

Continue PR44 in private organization `jeofwong/CopilotBuddy-private`; do not restart the broad audit. Baseline production is unchanged from the scheduler repair `c6d091eef533bec0eb2017da197f7c600cc90303`. Existing quest-action tests advanced independently to 25 cases at `e8f8f9f5ebab481d422d99e0e158e79eabb54a9b`. Source export commit `75685b24a7c95772954f2bcc80b4bf71d8e24528` changes only the existing read-only export workflow's organization guard and branch trigger.

The supplied investigation's C01-C06 requirements expose a separate executor lifetime boundary. Complete this bounded owner slice before changing Wholesome publication admission. The existing 25-case publication/root suite and every older test remain unchanged. This plan does not authorize a merge, deployment, master/backup write, native binary/mesh/runtime-capture/Lua change, or claim of live acceptance.

## Recovered baseline evidence

Existing Windows x86 run34862574827, current artifact10355831998, SHA256 `5dc7b1808dc23abfdab11836c1956e9655967e23dcccd397347bdd36327a9b1c`: all four focused builds0; Wholesome run1 and other three run0. Quest-action freshness8/25,17assertions,0unexpected. Original raw-publication23/23, publication21/21, acceptance9/9 remain. Complete ZIP CRC,54 internal hashes and source identity checked. These are recovered executions, not new tests from this slice. The cancellation-rescan case's trigger still needs tracing; its assertion alone is not proof of a production cancellation swallow.

Read-only source-export run34864874293/artifact10356108635, SHA256 `1de3cb1edfa2f0fa876187f6594d846fc1cc8661e8c4463e833e67e94dc656ea`, exported4026 files. All exported Git blob IDs, SHA256 values, sizes, ZIP CRC and commit identity were verified. Excluded meshes/logs are not claimed inspected through this export.

## Source-confirmed ownership structure

`Bots/Quest/Actions/ForcedBehaviorExecutor.cs` derives directly from Composite. Its nested `CurrentBehavior.Branch` is not registered as a GroupComposite child. Its inner Running loop rereads mutable CurrentBehavior, bypasses the earlier IsExecutionDeferred check, and reaches Branch.Stop only on normal trailing execution. Completion disposal can reenter and replace the order before the old code clears CurrentBehavior/advances. These structures justify tests, not an assumed repaired result.

`TreeSharp/Composite.cs` detaches/disposes its own iterator and drains registered cleanup, but cannot discover an unregistered forced branch. ThreadInterruptedException is a stop signal; OperationCanceledException currently follows the ordinary failure catch. PrioritySelector can interpret that Failure as permission for a fallback. The intended cancellation contract must be executed at the child, Start, OnTick and observation boundaries, including throwing cleanup.

## Bounded design

Prefer a captured, idempotent nested-branch cleanup owner over making the entire executor a GroupComposite or globally changing priority-selector scheduling. Capture behavior, node collection and node identities before callbacks. Recheck those identities after callbacks and before each continuation. Stop only the captured branch; never tick-before-Start, stop, dispose, clear or advance replacement work. Temporary deferral should end the executor's current cycle without completing/discarding the retained behavior, allowing the existing quest-order success shield and the next protective selector cycle to run.

Retain normal branch Success/Failure behavior and normal completion/advance. A stopped/restarted executor must release its old iterator before replacing it. Registering the captured branch's cleanup with Composite gives parent stop, cancellation and exceptional exit the same ownership path. A stop signal must not become a fallback authorization; if the new actual tests confirm the proposed OCE contract, extend the existing Composite cancellation path rather than wrapping a synthetic helper around the executor.

Do not yet append raw freshness to the entire Wholesome root, mutate QuestBot's shared static root, bypass service exclusivity globally, or return Failure outside the quest-order idle shield. Those separate root/admission requirements remain in the uploaded30-case specification and existing25-case suite.

## Test-first implementation steps

1. Add only `Tools/WholesomeQuestRecoveryRegressionTests/QuestExecutorOwnershipRegressionTests.cs`. The executable23-case group in the same commit is the concrete test specification: unchanged running control; direct/parent stop; initial/running deferral; running completion; between-tick/OnTick/child/cleanup/deferral-observation replacement; reentrant completion disposal; node collection replacement; restart; normal Success/Failure; exact ThreadInterrupted/OCE propagation at child/OnTick/Start/observation; ordinary-cleanup failure precedence. Tests instantiate the actual executor and actual TreeSharp parent with a controlled terminal ForcedBehavior. They restore QuestOrder.Instance and assert cleanup before test teardown. They do not claim the complete Wholesome root, a built-in quest action or a native/game effect.
2. Existing W42 normalization automatically discovers the new module-initialized group. Preserve all25 existing quest-action cases and all historical groups. Verify the actual Windows x86 focused and combined executions include the new group. Record build exits, per-case assertion/unexpected outcomes, full archive digests/manifests and exact test/source identities. There is no predetermined red pass count.
3. Only after a clean actual red, change `Bots/Quest/Actions/ForcedBehaviorExecutor.cs` for captured owner checks and registered branch cleanup. Change `TreeSharp/Composite.cs` only for the reproduced OCE stop boundary; extend GroupComposite cleanup precedence only if actual tests justify it. Keep factories/public compatibility and unrelated source unchanged. No assertion weakening or fixture changes in the repair commit.
4. Run the same focused, actual combined17 and host workflows at the exact repair commit. Verify every entry, not only the new group's log. Compare fixture/source manifests red-to-green; record expected production-only changes. Wholesome may still fail the separately retained publication/root cases: report those failures rather than deleting them or calling this full W42 closure.
5. Review the diff against ownership/cancellation/reentrancy invariants, publish an exact evidence/graph/checkpoint, then continue the separately scoped publication/root admission repair. Preserve supervised original WoW3.3.5a build12340, session/frame/ABA, typed-cache, protection/reload/merchant acknowledgement and `docs/audit/2026-09-14/QUEST_TRAVEL_COVERAGE.md` requirements as open unless separately established.

## Current state

Plan and test source only. No execution of these23 new tests and no executor/Composite production repair has occurred at plan creation. Source claims and recovered prior artifacts must not be relabeled as newly executed repair evidence. Publishing tools and native comment/file writes work in this conversation; no connection-recovery detour is needed.
