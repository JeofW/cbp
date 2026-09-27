# PR51 W110 — verified receipt repairs and evidence closure

**27 September 2026 · original WoW 3.3.5a/build 12340 · no live acceptance or merge claim**

W110 completed two new, source-proven shared-combat repairs through separate hosted regression-first cycles and expanded the Ret/group-support and remaining-scope audit. The remaining limitations have named native, protocol, realm, world or independent-review inputs; they were not relabeled as tested gameplay. No W104-W109 implementation was recreated.

## Verified frontier

| Identity | Exact value |
|---|---|
| Repository | `JeofW/cbp`, ID `1367174964`, PR51 |
| Approved branch | `audit/next-55-equipment-observation-20260917` |
| Final tested source and last production change | **`dabad63a3b3d24cde08f2e6039356f41c9bec6ef`** |
| Final source tree | `7ca1b83d85e84d951b8f53f359133886626f6ffe` |
| W109 entry source / published documentation | `ae7e3f2325be14ea155f76167e31d25c9b2da51a` / `1aaccc08e64a84443d6e275cae863c4c8974d346` |
| Master, unchanged | `b2324913e2499ba30b239dd67224ca2c655c05cc` |
| PR at the prepublication check | OPEN, DRAFT, UNMERGED, head equal to final source; direct API session 31515 |

The containing W110 documentation commit is **not a new tested source revision**. Its exact hash, tree, parent, changed paths and remote bytes belong to external `W104_WCLOSUREDOCS_PUBLICATION_20260927.json`; final reconciliation belongs to `W110_FINAL_VERIFICATION_20260927.json`. Those records are written only after their actual operations. This prepublication checkpoint does not invent its own future containing commit or a CoS release.

## Repair 1 — completed local spell dispatch is no longer fabricated

The actual manager's bool wrappers called a void backend that could return early without an executor or catch an exception, then reported success. This affected direct Cast, synchronous CastSpell and random Cast/Buff propagation; the complete ground factory could consequently acquire wait/terrain authority from that fabricated bool. This is below W109's already-repaired bool-to-TreeSharp mapping, not a recreation of W109.

The minimal production change is confined to `Styx/Logic/Combat/SpellManager.cs`. All four public void `CastSpellById` overloads remain. A private bool helper retains the original native sequence, target handling, locking, exception logging and completed-dispatch verification throttle; existing bool callers propagate its result, and synchronous waits are not entered after a failed dispatch. Random candidate selection no longer treats a failed candidate as a successful cast. A true receipt means **completed local dispatch**, not native/server acceptance; an exception does not prove that no native side effect occurred.

| Cycle stage | Actual evidence |
|---|---|
| Initial test candidate | `f48b3fd7638db9a7585b9fa65a21a2c4435b662f`; retained normalizer/build failure, **not behavioral red**. Its raw-string fake namespace violated the existing fixture normalizer. |
| Corrected test-only red | `8e2a6bdb24ede3f6c93485133e116133fb61949f`; integrated `36326636018`: **45/97 pass, 52 intended assertions, 0 unexpected**; Wholesome compiled, other 16 groups passed; host `36326635995` passed. |
| Minimal repair and green | `8d51722190228b215e2acc60feac9ef242d0a682`; integrated `36327308956`: **97/97**, **17/17 groups**; host `36327308939`: **0 errors, 3344 warnings**. |
| Frozen cycle comparison | **175 normalized fixtures unchanged**; no added/removed captured inputs; only `SpellManager.cs` changed. **1857 source inputs / 219 inner hashes / 220 archive members** verified. |

The fixture uses complete tracked manager methods plus the real ground factory and TreeSharp; it controls executor/world leaves. Missing executor, preparation/execute exceptions, healthy dispatch, target GUID/combination policy, synchronous waits, random candidates and legacy signatures are actual executed cases, not merely source-string assertions.

## Repair 2 — failed terrain completion no longer becomes ground success

The actual core `ClickRemoteLocation` already returns a bool using the retained original low-byte result and cleanup contract. The final ground action instead called its legacy void adapter, discarding false and allowing TreeSharp success after failed terrain submission/completion. The repair changes **one line** in `runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs` to map that existing bool to `RunStatus.Success` or `Failure`.

The original UTF-8 BOM, native method, stack/arguments, AL observation, cleanup, legacy public API, pending wait and initial submission guards remain unchanged. This does **not** introduce exclusive healthy cursor/request origin, freeze recipient/location through the wait, change the timeout policy or prove server effect.

| Cycle stage | Actual evidence |
|---|---|
| Test-only red | `d5a5ad7289bd19036a30d11842a44634b13752b6`; integrated `36328123006`: **20/46 pass, 26 intended assertions, 0 unexpected**; the first dispatch suite remains 97/97; other 16 groups and host `36328122996` pass. |
| One-line repair | **`dabad63a3b3d24cde08f2e6039356f41c9bec6ef`**, exact expected-parent/path/tree/remote-byte publication verified. |
| Final hosted green | Integrated **36328547595**: **46/46 ground**, **97/97 dispatch**, **17/17 groups**. Host **36328547612**: **0 errors, 3344 warnings**. |
| Frozen cycle comparison | **176 normalized fixtures unchanged**; no added/removed captured inputs; only the single production `Helpers/Spell.cs` path changed. |

The new fixture combines the complete tracked ground factory, legacy adapter and actual core terrain method with real TreeSharp. Its 46 cases include low-byte true/false with noisy high bits, missing executor, allocation/preparation/execute/read/free failures, XYZ preservation, ready/delayed pending state, failed admission and void compatibility. No native client or game executes in these controls. Two old full-factory fakes gained only a successful core-terrain adapter during the **intervening test-only publication**; their assertions/outcomes were unchanged. Neither repair changed its own red-to-green fixture bytes.

## Final hosted artifacts — retained and inspected

| Evidence | Exact identity and result |
|---|---|
| Integrated | Run **36328547595**, job **108645915899**, artifact **10934608676**, `integrated-dabad63a3b3d24cde08f2e6039356f41c9bec6ef`; **961196 bytes** |
| Integrated SHA256 | **`2a227b7a05f1fb49717d1d4c41d458578f4d2f8ddd7aa55319035daaf730340f`** |
| Integrated coverage | **221 members, 220 inner hashes, all 1858 source inputs matched the exact local source; 17/17 groups build/run exit 0**; source, fixture and tree identity checked; game_attached=false |
| Host | Run **36328547612**, job **108645916415**, artifact **10935171474**, `audit-validation-dabad63a3b3d24cde08f2e6039356f41c9bec6ef`; **83175 bytes** |
| Host SHA256 | **`464d8526847762f4db7ac734989f309e4361a1d539e1b18a93aac031b835f4cb`** |
| Host boundary | Release/x86 compile only: **0 errors, 3344 warnings**, tests_run=false, game_attached=false |

Download digests matched GitHub artifact metadata before inner-hash inspection. The final integrated/host source is not substituted with the later documentation commit. External retention and inspection completed in sessions 69477/51262; the final fixture comparison completed in session 46634. All ten stage archives, including the initial failed-build candidate, remain in the evidence directory with their own receipts. The manifest lists exact per-stage identities and hashes rather than hiding failed attempts.

Retained integrated controls include the previously published native/pending/ground-submission/default-equipment, cleanup and physical cursor guards; both equipment owners and continuation/popup boundaries; autoattack, facing/movement, mounted-hotspot, gossip/quest/publication, plugin refresh, Paladin decision/support and group-engagement suites. The final case extraction records their actual summaries and no failed aggregate group. Green controls are not a waiver of live/independent acceptance.

## New Ret, native/core and scope evidence

Read **W110_RET_PALADIN_COMPATIBILITY.md** for actual context-specific FCFS including the low-mana Judgement override; all seven original seals and explicit precedence; four normal/Greater families; all ten class recipient fallbacks; strict read-only PallyPower compatibility; multiple-Paladin coordination; seven original auras and travel/unmounted settings; learned racial/profession paths; defensive markers and conservative dispels. Optimal DPS, external effective aura magnitude, teammate talent/spec and unverified original-client proc timing were not invented.

**W110_CORE_EVIDENCE.json** retains eight immutable source hashes and 25 expanded category records from pinned TC `8fda442f6c30ca21a622638063ab8b28376f1b25` and AC `8337a378ac325e62a6a91e00c6a5e944205e8536`. SAME_EFFECT non-additivity is separated from whole-aura replacement and caster-specific exclusivity. Reference data does not identify the user's realm or expose client aura strength.

**W110_SCOPE_STATUS.md** addresses every remaining W109 item: ground origin/lifetime, R06 same-NPC first-response provenance, shared combat beyond Ret, lifts/Z/water/cliffs/taxi/gather/remount/hotspot/escort, disabled dense retreat, item/raw-credit and missing recipe materializers, inventory/deletion/ammunition, bounded plugin refresh, retained intermittent-publication diagnostics and independent/live acceptance. It records actual owners and exact required next inputs. The dense route's straight-segment risk approximation, same-Z fallback and missing actor/routine lifetime binding support continued containment, not safe activation.

## Refusals, restrictions and continuation

The earlier W109 IDA health transaction still has **no returned result/session/receipt** and was not replayed. No new IDA command was issued. A new pinned-TC recursive tree-listing request was also provider-refused; its full exact transaction is preserved in **W110_TC_TREE_REFUSAL.json** and the manifest. No retry, fragment, disguise, global outage conclusion or successful-tree-listing claim followed. Successful known-path source reads and independent requested project operations do not erase either refusal.

The historical refused W108/root-document publication remains refused and unpublished. W110 publishes only new W110 documents and does not embed/replay that refused payload. The two existing local root handoff edits remain intentionally untouched; the checkout is not described as wholly clean. The four original research/core/quest/addon policies, W80-W109 protections, exclusions and acceptance gates remain. Numeric preparation remains **3 historical / 0 new / 0 remaining**.

No subagents, local project build/test, production CB access, native/IDB/executable mutation, force-push, master write, merge or deployment occurred. Self-review is not independent acceptance. No all-class/all-quest, merge-ready or defect-free claim is made.

Read **W110_GOAL_CLOSURE.md** and **W110_CONTINUATION.md** with this checkpoint. `LATEST_CONTINUATION.md` and the external final verification/publication/finish records establish the postpublication state. The first completion-stage `session_finish` must occur only after those checks; **HELD is not RELEASED**, and normal final delivery waits for actual RELEASED or manual stop. This source checkpoint does not predeclare either outcome.
