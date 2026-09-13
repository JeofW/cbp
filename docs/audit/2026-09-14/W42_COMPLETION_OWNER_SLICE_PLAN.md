# W42 completion authority — bounded implementation plan

**Goal:** Repair only the reproduced loss of raw acceptance when optional quest-cache materialization returns null; retain the full W42 closure frontier explicitly.

**Architecture:** Keep existing public QuestLog APIs and lookup semantics. At GetQuestCompletionSnapshot, use raw acceptance to choose whether current accepted completion or historical completion is eligible. A positive raw acceptance plus missing PlayerQuest metadata must return accepted/Unknown; it must not enter the historical branch. This is not a whole-log snapshot or an atomicity claim.

**Target:** Original WoW 3.3.5a build 12340, Windows x86 .NET 10. No native binary, installed bot, deployment, merge or mesh work. No Lua change is planned in this slice.

**Inputs:** W42_SLICE_A_CHECKPOINT.md; uploaded W42_TEST_PLAN.md/W42_GRAPH.json; original handover and later counterevidence. #35/#36 already exist in the branch and must not be rebuilt.

## Test-first steps

1. Preserve the uploaded 24 assertions and the original 23 sale assertions. Add Tools/QuestCompletionOwnerRegressionTests linking full production QuestLog and PlayerQuest to the existing controlled external boundaries. Explicitly avoid the unrelated ready-log module initializer so all focused cases can execute.
2. Run the new 14 cases on the unchanged production commit. The five cache-miss/hydration/eviction assertions must fail for the intended reason, while nine compatibility/cancellation/history controls must pass. Inspect build, runtime architecture, every outcome and source hashes. Do not treat a fixture error as a red assertion.
3. Modify only the authority branch in Styx/Logic/Questing/QuestLog.cs:GetQuestCompletionSnapshot. If ContainsQuest(questId) is true, materialize that accepted ID; a null materialization returns new QuestCompletionSnapshot(true, QuestCompletionState.Unknown). Only raw nonacceptance can reach the existing historical-completion branch. Leave GetAllQuests, GetQuestById, cancellation propagation, resolution helpers and historical-cache mechanics unchanged.
4. On that exact repaired commit, rerun both W42 fixtures, the combined regression workflow and host Windows build. Expect the focused 14 assertions to pass and the corresponding three uploaded completion assertions to become passing; do NOT expect unrelated sale/scheduler W42 failures to vanish. Compare test source hashes before/after and inspect original sale 23/23. Record remaining red results, rather than weakening them to manufacture a green aggregate badge.
5. Save exact red/green commit identities, artifact hashes, directed evidence/test/fix/PR links and an updated continuation. Keep PR #38 draft/unmerged. Explicitly state that full W42 and the exhaustive architecture/refactoring audit remain incomplete unless later slices actually close them.

## Required subsequent W42 closure (not claimed by this patch)

Raw occupied IDs and incomplete identity reads must survive independently of metadata. Actual player/session/observation provenance, ABA and publication/side-effect revalidation cannot be substituted with equal scans or counts. Test the actual ScanAndRefresh -> MaterializeSchedule -> profile write/load boundary and already-running WholesomeExecutionGate; stale/unknown observations must invalidate ongoing work, not merely prevent the next helper result. Native frame locks have their own runtime lifetime and require owner-level investigation. Do not infer live client/server behavior from offline fixtures.
