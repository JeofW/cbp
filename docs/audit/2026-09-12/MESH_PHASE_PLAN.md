# Mesh-backed integration and remaining repairs implementation plan

**Authorization:** The owner approved the prior architecture/testing recommendations and uploaded mmaps to master c43c50d8d5d6775055f19bf018b52930a264d4a4. Work only in audit branches; no merge to master or deployment. Read CODEX_AUDIT_PROMPT.md, KNOWN_ISSUES_AND_HYPOTHESES.md and AUDIT_CONTEXT.md as the governing audit briefs. The previous audit is a baseline, not complete acceptance.

**Goal:** Integrate the previously isolated fixes, acquire actual mesh evidence, and implement additional reproduced defects with failing-before/passing-after checks and coherent PR boundaries.

**Architecture:** Keep quest intent separate from geometry planning and movement execution. Do not equate unknown with unreachable, coarse retry cells with spatial proof, a safe wait point with a safe boarding segment, cast dispatch with acceptance, or compilation with gameplay correctness. No fabricated transport geometry. Preserve existing public APIs unless a separately documented migration is justified.

## Ordered work packages

### 07 — Combined baseline and native mesh replay
- [ ] Combine the unchanged tested source from PRs 2–7 with the uploaded mesh tree on a new branch. Record exact source hashes and parents; do not close or merge the existing PRs.
- [ ] Run the five Windows x86 quest/vendor executables, explicit Singular compatibility, portable elevator suite and analyzer regressions on the same tree.
- [ ] Retrieve LFS contents in a private job; verify sizes/hashes, not pointer presence. Archive compact inventory/results only; do not duplicate mesh binaries in code commits.
- [ ] Build a game-unattached native harness against the checked-in host and Navigation.dll. Replay the two recorded Grod origins, reverse queries and a same-floor control, cold and warm. Record path points, flags, polygon IDs, status/partial, nearest-poly observations, tile callbacks, full wall time and wrapper time. Fail the harness if no seeded route produces any path, rather than claiming asset compatibility from LoadMeshes alone.
- [ ] Interpret native failures before changing route policy; keep moving-platform acceptance separate.

### 08 — Content-owned quest dataset identity
Files: runtime-snapshot/Bots/WholesomeAutoQuest-master/DataLoader.cs and Tools/WholesomeQuestRecoveryRegressionTests/.
- [ ] Add regressions for changed bytes with identical size/mtime, unchanged data relocated, input ordering, role identity and missing/failed reads. Run on the baseline and retain expected failures.
- [ ] Replace metadata fingerprints with a versioned, streamed content manifest while preserving semantic file roles. Hash only at dataset load/update boundaries.
- [ ] Re-run the entire combined suite and inspect recovery invalidation consumers. Document one-time fingerprint migration and failure policy.

### 09 — Movement cancellation, transport permission and timing boundaries
Files: Styx/WoWInternals/WoWMovement.cs, Styx/Logic/Pathing/ElevatorTransitController.cs, MeshNavigator.cs, Tripper/Navigation/Navigator.cs and focused test harnesses.
- [ ] Reproduce stale scheduled stop, overlapping movement, and explicit cancellation cases through the production scheduler before changing it.
- [ ] Add a safe-wait/unsafe-board regression that tests the actual permission boundary; approach and boarding inputs must be distinct. Preserve selected-attachment ownership and fresh dwell after revoked permission.
- [ ] Validate the actual commanded segment before movement; do not prevent approaching a safe wait point while the platform is absent. Native platform support remains a live gate.
- [ ] Instrument caller-visible lock wait, native query, managed conversion and total time without moving thread-affine work to background threads.
- [ ] Use mesh replay to distinguish source-policy failure from geometry failure. Add typed internal outcomes only where consumers and cancellation paths are covered.

### 10 — Shared routine safety and yielding
Files: runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs, ClassSpecific/Priest/Shadow.cs, ClassSpecific/Paladin/ and core compatibility tests.
- [ ] Reproduce requirements evaluation with absent/replaced targets. Validate targets before dependent predicates; preserve selected identity through dispatch where the existing context contract permits.
- [ ] Replace proven blocking sleeps with condition/tick-based behavior, with cancellation, retry and follow-on-action tests.
- [ ] Add level-33/80 Paladin chosen/rejected-action cases before changing target-count gates or hard-cast policy. No DPS claim without timelines.

### 11 — Wider evidence closure
- [ ] Reconcile quest source-of-truth differences without bulk blacklisting data gaps. Expand multi-stage and native geometry regression coverage using recorded findings.
- [ ] Update graph edges with source spans, hypotheses, counterevidence, red/green runs, fixes and PRs. Never mark a proposed or merely compiled change fixed.
- [ ] State remaining independent-review, native-client and actual gameplay gates. Run all affected tests on the final combined tree; no silent dependency/sibling omission.

**Execution:** Inline, with separate reproductions and validation checkpoints. Independent reviewer tooling is not assumed. A missing client or C++ source limits the corresponding acceptance claim, not unrelated static repairs. Uncompleted packages remain explicitly open in the final evidence report.
