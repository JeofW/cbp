# Travel efficiency and live-log follow-through

## Installed baseline and evidence scope

PR95 was already merged at `2165449abbe0dfab09b0df72dabf6f0a7fb73b66` when this follow-through resumed. Its normal-CB deployment was completed before further feature work: 29 files replaced, all 436 payload files and 17,160 protected files verified, and all 14 deployed runtime components compiled successfully. `CB - Copy` was not modified. The backup is `D:/Dev/CopilotBuddy-Production-Backup-PostMerge-20261003-000213`; the exact transaction and smoke receipts are in `D:/Dev/CopilotBuddy-Evidence/action-flight-20261002/deployment-pr95-2165449a`.

One earlier exact merged local run failed a persistence-mutation test. That failed receipt was preserved. A fresh complete run against unchanged clean source passed all 59 stages, and all three applicable merged Windows CI workflows were independently downloaded and source-equivalence checked before deployment. A rerun is not evidence that the earlier failure never happened.

The follow-up feature in this report is a separate revision. Its final source, PR, merge and installation identities must come from its own acceptance and deployment receipts, not from the PR95 receipt above.

## Latest Copy logs

All four October 2 logs and all four October 1 logs were snapshotted, hashed and scanned. Immutable copies and event indices are in `D:/Dev/CopilotBuddy-Evidence/action-flight-20261002/live-followup-20261002T155206Z`. The latest source is `2026-10-02_2006_26684.log`, 8,955 lines. Its executable-path header and installed DLL hashes identify **Copy running the earlier PR94 payload**, not the newly deployed PR95 build. The static startup version banner is not used as revision proof.

Concrete findings from that latest log:

| Evidence | Interpretation and response |
| --- | --- |
| Lines 1451–1462: corpse loot at 20:09:52, next kill objective, then aura61988 UNKNOWN in targeting and flight-mount selection | A real unavailable-observation path, not proof that no targets or mounts exist. PR95 separates raw ID/rest/Paladin observations from unrelated missing metadata. This follow-up also removes an optional aggro-ranking dependency that still blanked target publication. |
| Lines 1478–1480 and 1537–1539: mount attempts produce roughly 3.15s and 2.88s root ticks | Mount startup is a material cost. Distance thresholds and optimistic riding-rank speed estimates are insufficient for repeated short trips. These records alone do not prove each particular trip would have been faster walking. |
| Line 1519: food at 20:10:14, then a mount at 20:10:26 and a selected enemy at 20:10:32 | The log supplies a food request and later continuation, but no continuous HP/MP trajectory proving exactly when recovery finished. PR95's actual rest completion, activity and retry tests address the previous ownership/metadata defects. |
| Lines 4918–4919: worker exit followed much later by selection of CombatBot | The large logging gap is a stopped/configuration interval, not a 27-minute Wholesome stall. |
| Lines 5310–5348: metadata deferrals, repeated no-admission waits, then food at 20:52:31; surrounding root marker names CombatBot | This interval must not be described as an actively questing Wholesome root. Generic mechanic-dependent consumers can still legitimately defer when their required metadata is unavailable. |
| Repeated AutoEquip2 slow-plugin records | A separate responsiveness cost. The retained source audit identifies full equipment scans and synchronous eligibility checks. No claimed CPU percentage is derived from throttled slow-call logs, and no unverified equipment caching was deployed in this change. |

The reference investigation `AURA_REST_INVESTIGATION.md` independently binds missing61988 to an absent original-client Spell.dbc row and the pinned server shield-marker supplement. This change does not fabricate a client spell record, globally ignore61988, clear quest quarantines or relabel an unknown mechanic as absent.

## Mount-versus-foot comparison

`TravelTimeEstimator` is an optional preference calculation. It never grants flight, interaction, combat, collision or mount-removal permission. Existing safety owners still admit those actions.

Walking uses a complete, finite mesh route and the actor's current on-foot run speed. The ground-mount alternative uses the same route, the **selected mount's original spell effect**, its cast time, a one-second ground transition allowance and a further one-second minimum saving. A configured minimum mount distance remains a lower bound. Instances and battlegrounds no longer bypass the comparison by returning true for every destination.

Flight uses the selected flying mount or flight form, not the best riding tier as a substitute for its actual speed. It includes cast time, the existing normal 40-yard ascent/descent budget in both directions, and six seconds for takeoff/landing/dismount transitions. A known stopping/pull radius is removed from travel distance. Close calls stay on foot. Already airborne or mounted movement is not interrupted merely to redo remount economics.

The nominal mounted speed is based on original aura effects: ground32 and supported flight207/208/209/211. `SpellEffect` now preserves the original `EffectDieSides` so fixed values with die-sides0 are not incorrectly treated as raw base+1. Random, conflicting or level-scaled speed metadata remains unresolved. Zero cast time is accepted only for the supported original instant flight forms or an already-mounted comparison; a failed legacy cast-time lookup cannot become a free mount.

Example under explicit model inputs: at 7 yards/s walking and 14 yards/s mounted with a 3-second cast, a 45-yard ground route takes about 6.43s walking versus 7.21s mounted including transition allowance, so walking wins. A 200-yard route takes about 28.57s walking versus 18.29s mounted, so mounting wins. A winding 245-yard mesh route whose endpoints are only45 yards apart is evaluated as245 yards, not45.

Missing or partial ground paths do not justify a ground mount. For a flight comparison only, straight-line distance is a **lower bound** on walking time, not a claim of ground connectivity. Actual flight route execution and safe landing remain independently required. Additional mounted-speed buffs, detours, terrain and live server latency can change observed timing; these are estimates, not a global optimal-route proof.

Complete geometry is cached for at most one second under exact actor/session, provider, profile, POI/work and destination ownership. Actor movement prevents using the old length. Speed and mount choice are recomputed; a previous favorable result is not cached as authority. Optional mesh queries are rate-limited during movement. A bounded `[TravelCost]` diagnostic records the choice, compared times, cast time, spell and route evidence.

Ordinary `StateMount` and destination-based `MountUp` calls now use the same comparison. They retain the existing nearby-target veto and revalidate the exact destination before casting. The Flightor ground fallback forwards its current route admission to the existing mount owner. Explicit destination-free mounting retains its previous contract.

## Post-kill target-ranking continuity

`Targeting.DefaultTargetWeight` previously evaluated `unit.MyAggroRange` solely to add a 100-point preference. That aura-dependent optional bonus could throw UNKNOWN and cause the outer targeting pulse to discard the **entire** publication, despite complete candidate eligibility and other scores. The latest Copy log demonstrates this failure family after corpse loot.

The optional bonus now remains unapplied when its range is unavailable, with a bounded diagnostic. This does **not** assert that the aggro radius is zero or that an unsafe target is safe. Mandatory reaction, collision, blacklist and elite rules retain their existing behavior. Cancellation, actual process/executor loss and unexpected failures still propagate. Known aggro scores and in-combat scoring are unchanged; a later complete observation restores the bonus immediately.

## Failing-before and corrected coverage

Development receipts are under `D:/Dev/CopilotBuddy-Evidence/action-flight-20261002`.

- `mount-cost-red2`: actual previous decision methods, 4/23 passing, 19 intended assertions and no unexpected errors. `mount-cost-red1` is a fixture namespace/setup failure only.
- `mount-speed-metadata-red1`: actual original effect extraction lacks die-sides; corrected owner coverage passes4/4.
- `mount-cost-green1`: complete estimator with actual entry methods passes23/23, plus4/4 metadata cases.
- `mount-cost-wiring-red1`: three further causal assertions prove normal StateMount/destination wrappers bypassed the new comparison. Corrected `mount-cost-wiring-green1` passes six focused groups, including28 cost cases, original mount admission, Flightor continuity, flight eligibility and12 ground-object cost cases.
- `target-ranking-red1`: complete actual default weighting, 11/13 passing; two failures demonstrate optional-range UNKNOWN aborting current and subsequently hydrated ranking. `target-ranking-green1` passes13/13 alongside unchanged strict publication/deferral tests.
- `mount-selected-mode-red1`: an additional exact selected-companion mismatch fails28/29; the ordinary owner uses the configured ground choice when no flying name is configured. The estimator is corrected to match that selection rather than price an unrelated flight form.

The broad development integration run passed every one of its60 command stages, including actual runtime Singular and all28 registered projects. It correctly failed the separate source-stability check because a further test was added during the run. Only the subsequent clean, committed source gate can authorize the follow-up package; neither a dirty-tree pass nor a folder name containing "green" is release evidence.

These tests use actual production methods with explicitly controlled observation boundaries. They do not prove client physics, live dismount, all quest execution, an optimum damage rotation, or a complete absence of future stalls. Full clean-source Windows/x86 validation, runtime compilation and selective deployment are separate acceptance gates. The older target-observation tests still require incomplete mandatory filters to remain UNKNOWN.
