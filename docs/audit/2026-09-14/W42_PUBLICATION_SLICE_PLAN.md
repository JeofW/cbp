# W42 publication-owner implementation plan

> Execute inline using systematic debugging, test-driven development and verification-before-completion. This implements the next bounded investigation already requested in NEXT_CHAT_PROMPT.md; it does not restart W42 or the broad audit.

**Goal:** prevent preparation, failed profile publication, and obsolete refresh work from granting execution permission to an already-running quest/grind child.

**Architecture:** trace the existing ScanAndRefresh -> MaterializeSchedule -> ProfileBuilder.BuildProfileXml/WriteProfile -> DoScan/ProfileManager.LoadNew -> WholesomeExecutionGate owners. Keep prepared work separate from executable publication wherever failing-before evidence requires it. Use actual refresh-lease ownership, not an invented quest-observation/session version. No unconditional late invalidation may erase replacement work.

**Tech stack:** original WoW 3.3.5a build12340, Windows x86/.NET10, existing normalized and integrated Actions, Python evidence verification. No attached game.

**Specification:** NEXT_CHAT_PROMPT.md and W42_SCAN_ENTRY_CHECKPOINT.md at 0f4fee6058ce4a60af00b42cb7f40395bfd0a034. Existing scan-entry production/test source is 01ec5542448ae5b5087fccd4ca2f17c61906e6d5, not new work in this continuation.

## Established starting point

- [x] Read live draft PR42, current handovers, evidence/graph/plan, recent open PRs and protected references before writes. No newer PR was returned; PR42 head was 0f4fee60.
- [x] Compare tested01ec5542 against checkpoint0f4fee60: only six Markdown/JSON paths differ.
- [x] Inspect the supplied ZIP and execute its standard-library verifier: all four saved archive digests/CRCs, supplied internal hashes, source identities, normalized-output comparisons and saved result counts pass. This is not a new C# execution.
- [x] Read actual scheduler, builder, execution/refresh gates and host ProfileManager. LastSchedule assignment precedes XML construction/write. WriteProfile(null destination) returns null. LoadNew returns void and can return after failed profile compilation without reporting success.

## Constraints

Preserve master8382a7ec and backupc43c50d8, PR34's prior approved merge, inherited35/36, and excluded25. Extend the existing PR42 branch. Do not merge, force-push, deploy, alter installed/native files, download meshes for managed tests, or edit runtime captures. Retain all old tests and their assertions, original23 sale cases, historical14 integrated entries plus3 W42 entries, and the private deny-native-dispatch assembler boundary. No Lua changes are planned.

Read and retain GITHUB_CONNECTION_RECOVERY.md. Native writers are exposed in this session; this plan's commit/readback, not that discovery, establishes a real publishing operation. No token request/export, GET write, protection weakening or inferred workflow-dispatch action. Existing push-triggered workflows provide the execution path.

## Task 1 — test-first actual-owner reproduction

Create Tools/WholesomeQuestRecoveryRegressionTests/QuestPublicationRegressionTests.cs. Existing normalization discovers module-initialized groups automatically; do not replace saved test files or aggregate entry points.

- [x] Construct a controlled external player/memory observation that reaches real materialization and generated XML, and validate a successful-output control first. Do not seed the new result or replace scheduler/builder/loader method bodies.
- [x] Seed prior publication only to start an already-running production execution gate. Observe the child's ticks/stops during the real new publication chain.
- [x] Exercise XML/build argument failure and actual XML serialization failure, actual filesystem write failure, null/no-output destination, and profile-load failure or refusal. Separate fixture errors from behavioral assertions.
- [x] Exercise ordinary exceptions, exact cancellation/interruption propagation, and finally-based refresh release without changing the existing contracts merely to make a test pass.
- [x] Exercise a successful obsolete scan and generation replacement during preparation/publication. Retain replacement work and preserve existing obsolete/failure controls.
- [x] Retain normal complete/empty observations and validated-grind compatibility. A null builder destination is a characterized no-output mode, not permission to load an old profile.
- [x] Publish only tests before production changes. Inspect the current variant's complete Windows artifact for intended assertion failures and zero unexpected errors. The historical pinned-production matrix variant is not the new clean red baseline.

Representative owner assertion, with fixture helpers defined in the new test file:

```csharp
// Prior publication starts the child; this invocation executes the new scan.
Invoke(bot, "DoScan", scheduler, lease);
Check(root.Tick(context) == RunStatus.Failure && child.Ticks == 1,
      "unpublished refresh authorized the old running child");
```

## Task 2 — minimal evidence-supported owner repair

Potential production owners: runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs and WholesomeAutoQuest.cs; Styx/Logic/Profiles/ProfileManager.cs only if the actual loader's success contract must be surfaced without breaking public callers.

- [x] After clean red, keep a candidate non-executable through every fallible preparation step demonstrated by the tests. Preserve existing public signatures and explicit no-output compatibility.
- [x] Commit executable schedule/profile authorization only for the current refresh and an actually accepted profile; do not infer success merely from a void method returning or from a profile path being assigned.
- [x] Revalidate the actual refresh lease at publication boundaries. A late obsolete continuation cannot overwrite or revoke newer work.
- [x] Preserve conservative ActiveQuestIds item protection during uncertainty, bounded observation retry, cancellation identity, normal empty/complete and validated-grind cases.
- [x] Keep test/fixture/normalizer/native-boundary bytes identical to the final clean red source. Record a narrower closure if the evidence does not justify changing the whole publication architecture.

Rejected shortcuts: helper-only permission model; a catch that unconditionally clears current work; treating equal scans/names/counts as session provenance; suppressing pending W42 assertions; returning a fake success contract from a test boundary.

## Task 3 — exact-source verification and checkpoint

- [x] Use existing push-triggered audit-w42-normalized.yml, audit-integrated.yml and the host compilation workflow at the same repaired commit. No separate dispatch action is exposed or assumed.
- [x] Download complete artifacts; check authenticated outer digests, CRCs, internal manifests, embedded source/fixture identities, every suite/build/run result, normalized outputs and red-to-green source changes.
- [x] Retain the broader red W42 groups and report their actual results, not a green badge or a unique-bug count. Independently rebuilt binaries are not presumed byte-identical.
- [x] Publish directed source/test/evidence/fix links, a checkpoint, updated root continuation pointers and a downloadable handoff. Preserve earlier PR39/40/41/42 records and non-linear ancestry.

## Explicit remaining dependencies

Raw read-success/occupied identity independent of metadata, true player/session/frame provenance, same-character reconnect/ABA, readiness history and sale-dispatch freshness remain separate. No atomicity or live client/server acceptance is established by this slice. After W42 retain the existing order: item-protection ownership; atomic FILE-only reload; merchant/mail/discard/consumption; vendors/settings; rest/restart/cancellation; special quests; roster; combat/support; native/lifts. Self-review is not independent review. Full W42 and the exhaustive audit remain incomplete.

## Completion record — 14 September 2026, bounded scope only

The preliminary checks above retain the plan author's historical observations. The later continuation recovered existing publication tests at243a444a, retained their two later level-refusal additions (21 total), and added nine actual-loader/protection/replacement acceptance tests atf0bba89d. It did not recreate the original tests. Clean redf0bba89d: original9/21 plus acceptance3/9,18 assertions,0unexpected; unchanged production.

Repairc237ca5b5a32e33780552784b33e36e6973b735c/treefb6e821f7ca1b4a018584647149d52564c614db5 changes exactly the three named production owners compared with that clean red. Focused run34814906143/art10335859057 and actual combined run34814906088/art10335973778 at the repair both execute21/21+9/9,0assertions/0unexpected. All17 combined entries build;14run0 and3broaderW42run1. Hostrun34814906071/art10335639675 builds0,3278warnings/0errors,tests_run:false. Full evidence/hashes and remaining groups are in W42_PUBLICATION_CHECKPOINT.md and W42_PUBLICATION_EVIDENCE.json.

The public standalone generation API remains generate-only; actual host acceptance is enforced on the running-bot path. This is not host-wide transaction atomicity, exhaustive concurrency coverage, whole-W42 completion or live acceptance. All1683 working-source hashes agree between focused/combined; only three production paths differ from red; all28 normalized outputs and original assertions remain unchanged. Temporary preflight source preparation files were removed when the exact verified blobs were committed.

W42_PUBLICATION_GRAPH.json extends the earlier graph. Root handovers point to this checkpoint; prior roots are preserved byte-for-byte in pre-publication/. The downloadable publication transfer retains complete evidence, the original handoff, exact source excerpts and an executed Python verifier; running that verifier only rechecks saved evidence.

New owner XYZ/lift/wind-rider concerns are source-traced and explicitly open in QUEST_TRAVEL_COVERAGE.md, with phase-parity/elevation/lifecycle test slices. Existing lift regressions passing is not proof that every route or reported live symptom is fixed. GITHUB_CONNECTION_RECOVERY.md's explicit @GitHub -> fresh selected conversation -> reconnect-last sequence supersedes historical reconnect-first text.
