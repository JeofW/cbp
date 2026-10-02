# Owned ground transitions, recovery and quest completion

## Scope and source identity

This change targets the original World of Warcraft 3.3.5a client, build 12340. TrinityCore 3.3.5 and the already pinned TDB/DBC/source contracts remain the primary reference. No Wrath Classic or Retail contract is substituted. The last deployed baseline is merged PR94, `2d123d63eec35b675822571396a067e68cb7d281`.

The original working repository metadata was deleted outside this implementation. Existing source bytes were recovered and verified against the retained input receipt, not reconstructed from memory. Commits `02ac741b7d5d39122b3d20f2109bf0980dc664ff` and `5f953bcb2dc3c1303131d19b02e108e07e589f9a` preserve that recovery. Original unpublished commit objects and index state are not claimed recovered. Subsequent implementation was preserved across interrupted turns and independently checked from source, saved worker reports and command receipts.

The final candidate and merged/deployed identities are established by the release receipts and Git/PR head, not by an intermediate hash printed in this report. Development receipts with a dirty tree or concurrent source changes are not release certificates.

## Shared ground-transition behavior

`GroundTransition` retains one actual context and runtime while a caller is running. It separates exterior search, flight, supported descent, observed mount removal and ground mesh execution. `ActionMoveToPoi` retains this owner for quest and service POIs; mounted committed/protective combat uses the same ground stack. Nearby targets do not bypass it merely because their coordinates are within interaction distance.

An exterior candidate requires actual collision support, a matching allowed mesh surface, a supported footprint, clearance, liquid/blackspot rejection and, for interaction, a complete onward mesh leg with matching endpoints and floor. Radial seeds are bounded query inputs, not invented doorway coordinates. The ground navigator must still execute the route and the caller must observe current interaction range and collision line of sight. Movement commands and static paths never acknowledge physical arrival.

Protective combat lands around the actor's captured position, not an unrelated displayed enemy or the old travel destination. Player-only and exact-pet threats are retained separately. The actor-local destination may be airborne, so the support projection also probes a bounded 512 units below the observed actor. This is a query budget, not a known ground height; no hit, missing mesh or unsafe footprint remains unresolved.

The runtime binds actor/mover references, GUIDs/base addresses, map, memory/process/handle/executor, bot/run, profile, selected work and movement tokens. A semantic work generation is distinct from the POI's coordinate-route revision. An explicitly leased combat landing can survive a moving *same* target's coordinate refresh, while ordinary unleased routes still invalidate. POI/target/type/GUID/entry/wrapper replacement revokes both the work and its dependent route authority. The route lease validates semantic context independently of the tokens whose invalidation is being decided; it does not recursively depend on those tokens.

Unavailable searches, repeated invalidation and no-progress no longer leave the same state machine permanently running without a recovery path. A bounded actor/session cooldown allows a fresh probe without granting attack permission or letting POI churn reset retries. A later authoritative supported, unmounted observation can resolve the handoff immediately. Cancellation detaches managed ownership before cleanup and does not stop a reentrant successor's input.

## Native interaction and dismount boundaries

World collision/outdoors queries have complete owned result contracts rather than failed-read defaults. The original build12340 calling conventions and interaction vtable offset 176 are retained. Result booleans, points, ranges and executor epochs are validated. UNKNOWN is not a clear ray, an outdoor answer or permission to interact.

The final native interaction guard contains only pure memory/state observations. A previous version indirectly called the legacy vehicle Lua query after preparing the interaction command, clearing the shared assembly buffer and returning a managed success with zero interaction entries. Complete vehicle observation now occurs before preparation; native entry rechecks the same actor/subject/session, raw movement, transport, mount/form, input recipient and observed position without another Lua/collision query.

The same boundary rule applies to dismount. The strict Lua observation path accepts a pure caller predicate and checks it after recovery-admission callbacks immediately before native execution. `Mount.TryDismountOwned` retains a captured original session, raw movement/descriptor identity and exact mount/form. The shared ground caller also binds the position of its positive support observation through final entry, so a preparation callback cannot borrow support from a previous footprint. Cancellation and actual process/executor loss are not swallowed as ordinary rejection.

Final integration additionally rechecks the pure caller after the last Lua-state memory read and rechecks work ownership after reading the supported actor position. These reads are observable callback boundaries, not permission to reuse an earlier admission. The public legacy `Mount.Dismount` void wrapper defers `ObservationUnavailableException` without clearing quest/POI state or claiming a result; the internal owned API stays strict and direct/reflection-wrapped cancellation or native ownership loss still propagates.

Pending dismount is scoped to the actor/session rather than a target or rebuilt POI. A 12-second monotonic managed lease and a matching owned client-Lua lease suppress duplicates, including ambiguous post-entry responses. A failed pre-entry request does not poison all future work; an expired unacknowledged request produces a bounded recovery path before fresh grounded/support admission. The client lease validates its schema, finite clock/expiry and owner; foreign or malformed state is not overwritten. A return receipt means local action submission or an already pending owned request, not observed mount removal.

## Recovery, rest and Paladin behavior

Shared recovery ownership coordinates Singular spell/heal/buff calls, health/mana items and DrinkPotions. Preparation, native submission, observed casting, authoritative effect and failure/timeout are separate states. Exact intended recipients and source/cast/aura identities remain bound. Another caster's already-active aura can prevent duplicate application but cannot acknowledge our own submitted action. Health-affecting items respect in-flight health recovery; proven mana-only requests do not acquire a false health reservation. No predicted healing quantity or new health threshold is invented.

The complete original-client Lua collector preserves bounded event batches, acknowledged cursor, counted loss, frame ownership and cancellation. The shared pulse maintains observations while routines yield. Repeated already-blocked admissions avoid unnecessary Lua queries, with a final atomic/current-owner check at actual dispatch.

Rest consumes complete bag/item/stack and supported original DBC aura-family observations. Missing metadata, unreadable inventory, incomplete roster or unknown movement/support does not become "no food/drink" or safe admission. Between-pull instance rest remains eligible only under known-safe player/pet/group conditions. Cannibalize has an explicit start and continuation policy; a valid active channel is not rejected merely because channeling is true, and danger or owner replacement retires the exact ongoing lifetime. A cast/channel log does not prove healing success.

Group-cache reuse validates current wrappers/base identities and never performs hidden Lua in a query-forbidden final admission path. Raw recovery suppression is fenced by the original spell/execution context so an old owner cannot suppress a successor's command or clear its reservation during reentry.

Wholesome's routine rest pause treats unavailable liquid geometry as **not admitted**, both at the main pulse and on resumed consumable retries. It releases only that optional pause and sends no consumable/movement request; it does not turn UNKNOWN into dry terrain or swallow cancellation/native control loss. Fresh positive dry observations preserve normal rest behavior.

Paladin support and Retribution consume proven original aura families for seals, blessings, auras, procs and relevant restrictions. Complete source compilation, actual factory registration and decision tests protect the retained context/readiness/rotation semantics. This is not a claim of optimal damage performance or complete live acceptance of every class/spec. The runtime-compiled routine retains the existing opaque `TreeRoot.RunIdentity` through a public read-only accessor; the token grants no execution authority.

## Quest completion and loot handoff

`QuestTurnInCompletion` owns each reward submission. Accepted-log departure, frame closure, a button request and elapsed time cannot establish a reward. Ordinary quests require current authoritative completed history correlated to the owned submission. Exact daily descriptor membership can acknowledge the matching daily's absent-to-present transition, without copying daily completion into permanent history. Unsupported repeatable/weekly outcomes remain conservative rather than receiving an invented acknowledgement.

Wholesome attributes an owned reward only to the submitting behavior. A successful recovery attempt releases its exact generation, advances its recovery cycle and records success evidence. The manager's `Completed` state is a permanent quest terminal, so it is not used to prevent a daily's later reset period. Duplicate observation is idempotent and a new period receives independent ownership.

Mandatory Pickup/TurnIn work bounds residual incidental/required loot and follows the actual selected nested If/While order. Exact open-window/POI ownership is preserved. Combat-interrupted collection retains intent rather than a stale movement path; resumption requires fresh exact-object eligibility and current route observations. Missing, consumed, replaced or materially worse candidates are not silently relabelled the original source.

The source-bound `GossipEvent` caller retains a separate bounded UNKNOWN retry lifetime when strict interaction observation is unavailable. UNKNOWN grants no interaction GUID, future-menu ownership, option-submission timestamp or quest credit. Actor and exact registry recipient are rechecked around diagnostics; disposal or same-address recipient replacement cannot consume an obsolete attempt. The shared strict interaction API is unchanged and the caller catches only the optional observation exception, not cancellation or native ownership loss.

## Representative causal receipts

Receipts are retained under `D:/Dev/CopilotBuddy-Evidence/action-flight-20261002` unless a sibling directory is stated. They distinguish compiler/setup failures from behavioral assertions. The full logs and before/after source hashes, not a directory's "green" name, establish the result.

| Boundary | Failing-before evidence | Corrected development evidence |
| --- | --- | --- |
| Protective actor-local support projection | `postmerge-20260929/protective-support-resume-red1`: eight geometry assertions and two combined protective-flight assertions | Geometry 29 cases; actual combined mounted/ground 19 cases |
| Moving committed target and actual shared runtime | `postmerge-20260929/combat-poi-resume-red1`: canonical actual root 8/12 | Canonical actual root 12/12; actual ordinary-versus-leased route cases remain separate |
| Native ground admission replacing its prepared command | `postmerge-20260929/native-ground-command-red1` | Complete actual ground/context/world/interaction native-entry fixture; UNKNOWN/control cases retained |
| Lua/Mount final preparation callbacks | `lua-mount-entry-resume-red2`: 310/348, 38 intended assertions, zero unexpected failures | `lua-mount-entry-resume-green1`: all six focused groups, including 348 transport/Mount cases |
| Pure retained native session capture | `postmerge-20260929/native-owner-capture-red1`: eight intended assertions | `postmerge-20260929/native-owner-capture-green1` |
| Client dismount lease clock/foreign/malformed state | `mount-lease-lua51-red1`: 10/16, six assertions, zero unexpected failures | `mount-lease-lua51-green1`: 16/16 through stock Lua5.1 plus seven adjacent groups |
| Support-position change before dismount entry | `postmerge-20260929/dismount-position-support-red1`: one actual runtime assertion | `postmerge-20260929/dismount-position-support-green1`: all 12 build/run stages across six adjacent suites |
| Rest/channel/group/raw suppression | `rest-release-repairs/REPORT-WORKER3.md` and its preserved red directories | Fresh host-linked rest 91, coordinator 95 and Singular rest 58 cases in the named focused gate |
| Complete runtime source visibility | First full diagnostic Singular compilation failed on internal run-token access | `postmerge-20260929/runtime-public-token-x86-green1`: actual Singular compatibility exits 0 |
| Legacy public dismount deferral and last-memory-read reentry | `final-native-compatibility-red1`: eight public-wrapper/control assertions and eleven actual Lua/Mount late-state assertions | `final-native-compatibility-green1`: public Mount 103/103, complete Lua/Mount 359/359, stock Lua16/16 and mandatory handoff18/18 |
| Work replacement during supported-position observation | `postmerge-20260929/final-support-reentry-red2`: one actual runtime assertion; red1 is a missing-import setup failure only | `postmerge-20260929/final-support-reentry-green1`: runtime16/16; its aggregate stage still records unrelated failures |
| Wholesome liquid UNKNOWN at initial/resumed rest | `wholesome-liquid-deferral-red1`: seven actual owner exceptions (21/28) | `wholesome-liquid-deferral-green1`: rest pause28/28 and six adjacent groups |
| Source-bound gossip caller compatibility | `ground-recovery-release/worker8-quest-gossip-compat`: 13 exact-recipient assertion failures before the caller repair, with12 constructor controls still green | Actual recipient13/13 and constructor12/12; `gossip-final-adjacent2` additionally passes complete gossip strategy, objective restart, Wholesome pause and mandatory handoff groups |

The completed adjacent owner tests additionally cover actual extracted Flightor movement/anti-stuck leases (350 cases plus 12 travel-cost cases), linked navigation ownership (37), mounted combat/action callers (86), and retained loot/ground handoff (359). Their controlled leaf boundaries are documented in `combat-poi-release/ADJACENT-INTEGRATION.md`. A subsequent canonical Windows/x86 run replaced an incompatible x64 SDK Roslyn reference with the same portable Roslyn package family used by other fixtures; x64-only `dotnet run` was not treated as x86 acceptance.

## Full release gates and proof limits

The workflow's actual project population is the authority for complete acceptance. At integration it registers 28 projects, including the **actual** `CombatPoiTransitionRegressionTests` root and `GroundDismountRecoveryRegressionTests`; the old diagnostic combat model is not substituted for actual acceptance. The local driver derives the project list from this workflow and the release verifier requires every build/run, host build, actual Singular mode, analyzers, correct order and unchanged log/source hashes. Historical 34-stage or focused receipts are insufficient.

Before publication, one clean candidate must pass the complete local pipeline, exact 4,335-row observation/declared-owner closure, pinned native incident replay and a 14-component source-bound self-contained win-x86 package. Hosted checks must run on that exact PR head and their source manifests/results must be read back. Merge and deployment are separate gates; a candidate package is not evidence that production was updated.

The native incident input contains six 10161 coordinate legs and two prospective 9387 NPC-to-NPC legs. The 9387 starts are explicitly synthetic uses of logged NPC coordinates, not captured player trajectories. The selected navigation engine is SHA256 `702038dfbb52fed104c946a8ba666d5b93ee35e4c257e3e5dbd68ac977f7d875`; 1,790 supplied map001/map530 files are individually verified. The pre-freeze eight-leg replay builds and runs successfully, but final release validation repeats it on the frozen identity.

Controlled owner replay, stock Lua5.1 execution and static native mesh queries do not prove physical doorway traversal, collision-world safety, actual flight/landing or live realm completion. The exact quest closure grants **zero new full-execution or live-completion promotions**. Ordinary repeatable/weekly reward evidence and uncorrelated instant/indirect recovery effects retain their documented conservative limits.

The only authorized deployment destination is normal `D:/World of Warcraft 3.3.5a/CB`. `CB - Copy`, maps, navigation binaries, unrelated runtime source, settings and persistent state are preserved. The historical DrinkPotions comparison hold was resolved by installed-versus-PR94 normalized byte evidence; fresh baseline/protected-file checks remain mandatory at deployment. Query-only process inspection identified the running app as `CB - Copy`, not permission to stop it or modify its files.

## Review and decisions

Independent domain review exposed the unavailable-state, moving-target and pending-dismount defects, followed by actual combined integration and causal corrections. Final independent production review found the legacy public dismount compatibility regression; its fix and subsequent full-gate caller integration are backed by separate failing-before tests. All reported implementation blockers are repaired at the development freeze. The full clean-commit release gate and delivery identities remain separately evidenced; a test boundary is never made permissive to replace missing production proof.

Three important interpretation decisions are retained in the plan ledger: the 512-unit search depth is only a bounded probe; daily recovery success is a generation/cycle/evidence release rather than a permanent quest sentinel; and the run token is public read-only solely for runtime-compiled consumers. Each retains its underlying safety/ownership requirements. Obsolete fixture assumptions—null-pointer crashes, native entry-only reaction caches and later-client aura helpers—are not restored to production to satisfy old tests.
