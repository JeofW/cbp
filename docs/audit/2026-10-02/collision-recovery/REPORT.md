# Bounded collision recovery and native polygon ownership

Confirmed collision recovery previously registered a session-long avoidance region, accepted nonfinite or different-floor hits, and could clear replacement movement after a callback. The preserved repair now confirms finite three-dimensional hits, rechecks the actual movement owner around observations and callbacks, and registers a 30-second collision lease. Repeated reports do not slide its deadline; actor, map, navigation provider, native navigator, profile, Stop and clock boundaries invalidate it.

The native polygon manager now observes both original area and flags before marking. UNKNOWN originals never authorize a fabricated walkable mask. Failed restoration keeps those exact originals for retry; another live owner protects overlapping polygons. Explicit permanent/global registration remains independent of temporary coverage, including identical and partially overlapping regions.

Marking reserves managed ownership before native submission and retains original bytes until all reentrant writes return. Removal, replacement with the same-valued region, native callbacks, map/actor/provider changes, and cleanup cannot authorize stale writes. Coverage queries ignore expired/revoked collision leases even before the next pathfinding maintenance pulse, so the caller can admit a fresh collision observation. Fallible native cleanup is deferred while unwinding an exception; cancellation remains cancellation and saved originals remain retryable.

## Recovery and evidence

The original seven interrupted files are retained in a2c07d21c7ad9573feb9a42d30d681bf18cefa6f, based on 1e891aae50371b51393a866f85950e00f552a738. They were reconciled with PR90 master c2f70e4c74a4c524f2e1075e51d571c4153a370a through merge 75cdf42216a3f42d5a2e16829fba80165c0c6875. No original patch was recreated, reset or discarded. The fixture's previously corrected three-argument global registration call was preserved.

The complete current BlackspotManager executes in 70 deterministic cases with only native, time, world/profile and persistence boundaries controlled. The actual collision tracker and MeshNavigator execute in 22 cases, including replacement owners and direct/reflection-wrapped cancellation. All 92 cases pass in their recorded focused/full-owner runs. Incremental behavioral-red receipts retain 15, 17, 1 and 5 intended assertions respectively; these are overlapping stages of one evolving fixture and must not be added as distinct defect counts. The expanded cases remained unchanged across each repair. test-receipts.json retains exact input/output hashes and all assertion text.

The full QuestRecovery regression process passes after the route cancellation correction. The complete 34-stage Windows/x86 local pipeline, exact 4,335-quest closure, candidate SHA, hosted artifacts and release state are separate publication gates recorded externally after this report is committed. A passing focused group is not a release receipt. Resolve the current candidate/merge/deployment from live Git and collision-resume-20261002 evidence instead of assuming a future SHA from this precommit report.

## Scope and remaining proof

The fixed target remains original WoW 3.3.5a build12340. This change does not alter client ABI, collision flags, spell decoding, server data/scripts, Navigation.dll, native engine configuration, mmap assets, or quest classification. It preserves the grounded GameObject collection protections and PR90 aura UNKNOWN/liveness repair.

These cases prove managed ownership, geometry rejection, lifetime, cancellation and native acknowledgement/restoration handling under controlled inputs. They do not raycast a real tree, establish physical passability, certify new navmesh routes, resolve native engine unload/reload identity, or prove live quest completion. Existing DATA/NAVMESH/physical-route/live tiers remain distinct. The 4,335 execution ledger receives no promotions.

PR90 was separately installed and verified before this collision publication: 19 changed runtime files, 434 payload hashes, 2,012 protected files and 13 deployed components compiled. The collision changes remain separate until their own exact-SHA gates and guarded deployment succeed. Live acceptance must still observe repeated/multiple obstacles, expiry/replanning, final approaches and actor/map/provider/profile/Stop transitions without stale avoidance or worker termination.

The next incident lane remains shared action acknowledgement and healing/defensive/consumable arbitration, followed by quest10161 progress/travel/combat reacquisition, food/drink admission, death recovery and recovery-ledger starvation. Historical aura 61988/56817 lookup-stage diagnosis requires the new live metadata diagnostics; no old decoder change or quest fix was replayed here.
