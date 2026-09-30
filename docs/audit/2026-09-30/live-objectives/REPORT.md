# Observe ordinary direct quest targets when static locations are missing

This small continuation starts at validated PR68 `af74d52837889acfacf6eb0572785a32fa6f168f` on `audit/next-quest-live-objectives-20260930`, in `D:/Dev/CB-QuestLiveObjectives-20260930`. The containing commit is the candidate. Exact clean local and hosted acceptance is recorded externally after that commit, not inferred from the precommit fixtures.

## Defect and repair

The actual `GrindObjective.IsMobObjective` accepts a matching creature entry before consulting optional cached credit aliases. Wholesome's capture and materializer only admitted the alias fields. An ordinary loaded mob with the exact required entry and no alias row was therefore invisible to objective planning when static locations were absent; the scheduler could report `no-known-hotspots` despite that observed target.

Capture now retains a valid direct entry even when optional alias metadata is absent or unreadable. Valid aliases are still read and retained, including when an actor's direct entry also matches another objective. Cancellation/interruption still propagate. Materialization accepts direct entry or either alias only behind the existing exact native objective identity/count, accepted/nonfailed state, same actor/scan/map, finite three-dimensional location, range, safety, navigation and recovery gates. The live point is a search observation, not a guaranteed kill, safe path or completion receipt. Global spawn data is never overwritten. Existing inventory and supplemental-return authority remains unchanged.

Debug diagnostics identify the observation as loaded entry plus optional cache credits. The existing debug-only/rate-limited emission path remains in place. No item source, giver relation, cast/event strategy, native dispatch or new ABI is inferred.

## Source evidence and scope

Pinned TrinityCore335 `Player.cpp:16190-16261` gives ordinary direct-entry credit and optional alias credit separately. The host's existing target predicate has the same distinction. The source audit found360 ordinary matching objective rows without global static geometry, with336 subject-source-consistent rows across275 quests. Seventeen of the360 rows also had existing quest-scoped credit hints. Twenty-four rows retained source conflicts and were excluded from the positive source-bound fixture; their obligations remain unchanged.

All336 source-consistent rows pass actual scheduling, generated profile, real generic kill-owner identity, zero/partial/complete native progress and repeat-suppression checks. Their supplied live positions are controlled observations; this does not prove that those creatures are spawned, phased, attackable or reachable on the configured realm. `source-frontier.json` binds all native normal/item slots to exact primary quest and actor row hashes. The full4335-quest dataset acceptance remains separately required.

Read-only IDA again verified original32-bit build12340 with SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. The broader daily/spell study remains retained but unimplemented; its count-only daily API and initialization-sensitive spell absence are not authoritative new condition inputs. A descriptor-accessor search produced no adopted Entry API. This repair uses existing host observations and does not change native offsets or calls.

## Regression evidence

The corrected red run produced10 intended failures across42 scenarios with zero unexpected exceptions. Earlier setup failures are retained separately. All42 now pass, including actual loaded-object capture without an alias cache, scheduler-to-profile-to-behavior progress, turn-in and rewarded-history acknowledgement. The tests also cover partial/completed/failed state, wrong native identity/count, incomplete logs, stale/future/foreign observations, map/elevation/range, unsafe/unreachable/quarantined paths, half-open budgets, two actors, unchanged static fallback and prevention of invented item/giver/script ownership. Four existing focused geometry/publication groups also pass.

The extended source fixture passes336/336 rows with zero assertion or unexpected failures. Full clean-commit validation must retain the original dataset sweep and both strategy lifecycles. No old test count is forced; the new source-row suite is a separate336-row result and the new focused group has42 cases. Source comparison, workflow checks and raw logs will name the containing candidate SHA.

## Ledger and delivery state

Quest knowledge bytes and the exact six-category ledger are unchanged from PR66/68:2964 generic,2 strategy,932 data,364 source,16 live,57 scripted =4335;1369 remaining IDs. Static source/data obligations are not erased merely because a live fallback now works under supplied observations. Only strategies9066 and9447 remain vetted. The separate PR67 condition ledger has1334 remaining IDs and is not arithmetically combined here.

PR67 `0722b9c4d7e2d958a23c0986e05864f2f6b68a0a` and PR68 `af74d52837889acfacf6eb0572785a32fa6f168f` are already ready with exact local and downloaded hosted acceptance. The prior next-availability-worktree request and the guarded first dependency merge of PR62 each remained provider-blocked after their single identical retry. Those operations were stopped. This is a separate direct-objective repair; it does not recreate that held branch or perform the held merge. Master and production remain PR61 `3bc97e1e0b446aede269f7414c0c7c6358fdc192`.

Continue from external `RECOVERY_PASS2_20260930.md`, final publication receipts and the exact remaining IDs. Integration needs supported transaction recovery and fresh combined validation before any combined ledger or release claim.
