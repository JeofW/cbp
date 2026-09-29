# W110 final source pass — combat, quest and observation continuity

29 September 2026. Tested production/source is **a3c01d48e887745d3b0e478e4018b01cad4f1edf**, tree **18164f54945583570378235bfd8b86c7c95dbfb8**, parent **9e61f0328a8af610c4e3ff257860ec0578ac5d7f**. This is the continued expanded Goal, preserving the earlier published movement/mount/flight source `f12c57267fa071305085915f4ec2a2bb9e52c144` and all W80–W110 work. The containing documentation commit is separate; its exact identity belongs to the external publication receipt and `LATEST_CONTINUATION.md`.

The complete 55-row requirement map is `W110_FINAL_PASS_REQUIREMENT_MAP.md`. The source changes cover four production owners and twenty test files, with eight meaningful local behavioral red/green cycles. All demonstrated source-actionable defects found in this pass are repaired. Original-client, server, causal-protocol, realm-data, physical-route and independent acceptance remain explicit D1–D6 dependencies. PR51 remains open, draft and unmerged on `audit/next-55-equipment-observation-20260917`; master is unchanged at `b2324913e2499ba30b239dd67224ca2c655c05cc` at source publication.

## Resulting behavior

**Movement and target control.** Singular captures the selecting actor and recipient references/GUIDs through movement, stopping, facing and target acknowledgement. Missing/dead/replaced participants, disabled movement, casting/channel restrictions and invalid coordinates revoke the relevant action. A failed navigator result does not become successful handled movement. Explicit off-target casts keep their selected recipient; ordinary target-facing and pursuit retain their existing roles.

**Original-client cast observations.** Hash-bound read-only native evidence shows that cast and channel end timestamps are tuple value 6. UnitCastingInfo value 8 is a numeric byte counter; noninterruptibility is value 9. UnitChannelInfo uses noninterruptibility value 8. The managed getters now consume those proven positions, clamp missing/expired time to zero, and require explicit affirmative interrupt permission. Failed or malformed observations do not grant permission. No native address, ABI or executable/database mutation was introduced.

**Cast, heal and quest-item continuations.** Name/ID casts and all five Heal factories retain their admitted actor and recipient across setup and waits. Replacement casts/actors are not adopted, channels keep their existing continuation policy, and failed submission remains failure. UseItemOn binds its objective baseline, approach, submission and delayed acknowledgement to one behavior actor lifetime, rechecks the intended object and movement conditions, and preserves authoritative progress/selection/receipt requirements. Local completion or a use attempt is not server quest credit.

**Ground placement.** One cast retains one destination. A later target position cannot redirect it during the pending wait. Placement checks finite coordinates, current actor, range, caller requirements and area safety. Timeout does not authorize a click, and a matching pending-spell observation remains necessary. CanCast is not repeated after submission, so the cast's own GCD cannot invalidate a healthy placement continuation. Matching names remain insufficient proof of causal native request ownership.

**Adjacent shared helpers.** Cast waiting uses one remaining-time observation and keeps its actor through facing. Duplicate-cast cancellation revalidates the observed actor, selected subject and spell, requires an active own-caster aura from the complete collection, and preserves a foreign same-name aura separately. Named melee admission uses the requested unit's reach rather than borrowing the displayed target's reach. Existing player/NPC arithmetic and queue-window policy remain. Native cast-instance/original-recipient provenance and physical range acceptance are still separate dependencies.

## Behavioral evidence

| Cycle | Passing cases before repair | Intended failures | Passing cases after repair |
|---|---:|---:|---:|
| Combat movement | 143/259 | 116 | 259/259 |
| Control/facing/target continuity | 343/471 | 128 | 471/471 |
| Actual Lua5.1 cast observations | 33/52 | 19 | 52/52 |
| Heal continuation | 36/79 | 43 | 79/79 |
| Cast continuation | 61/136 | 75 | 136/136 |
| Quest-item movement and reusable acknowledgement | 29/107 | 78 | 107/107 |
| Ground-cast continuation | 14/59 | 45 | 59/59 |
| Shared wait/cancel/melee observations | 26/109 | 83 | 109/109 |

All eight behavioral-red baselines have zero unexpected errors. Case families overlap and must not be summed as unique coverage. The first six pair records retain 192–197 unchanged normalized fixtures at their own intervals. The two later pairs independently verify unchanged source-member sets and identical regression assembly hashes, with only `Spell.cs` changing in each interval. Later compatibility adaptations are outside those frozen intervals. Compiler/normalization failures are retained separately and never substituted for behavioral red.

The final local Windows/x86 gate is **17/17**, full host build exit **0**, with all **1892** working source inputs unchanged throughout execution and rechecked before publication. The earlier 15/17 aggregate is preserved as a failed intermediate. Full tracked Singular compilation and actual TreeSharp are exercised; controlled leaves and process-owned test memory do not attach to a game.

Hosted integrated run **36466136560**, job **109076571119**, artifact **10989179321** is **17/17** at this exact source. All **251 archive members**, **250 inner hashes** and **1892 source inputs** were checked, including equality to the development checkout. Archive SHA256: `e0573a6c72a72e7bc4b9e5c4652d36adbd3cb11ac53b0aa1c402ff2f0c28fbcb`.

Hosted Windows/x86 run **36466136368**, job **109076569891**, artifact **10988963899** compiles with **0 errors**; its exact warning summary is retained in the evidence JSON. Archive SHA256: `216f4297a9f746a85f6b2287bc8fb51f37e79fa6fa37821fbf877801d01510f5`. This host job is compile-only. Neither hosted job runs WoW or proves supervised acceptance.

## Preserved scope and remaining inputs

The seal, Judgement, blessing, aura, Hand, defensive, dispel and Ret Normal/Instance/PvP paths were reviewed against the retained policy and support evidence. Optional Seal of Light remains off by default with its existing solo Auto thresholds. The earlier equipment, cursor, gossip, flight/taxi, gather/remount, world-state and native aura repairs remain intact. No speculative universal DPS/DR planner or realm recipe was added.

Wholesome still rejects unsupported scripted Escort materialization; generic escort observations are not a proof of a particular NPC script or completion protocol. `DensePullIsolationValidated=false` remains unchanged. The reported live Hammer of Justice target-switch/run-away symptom still needs a synchronized native/target/movement/server trace; the managed fixes do not justify dismissing it as a resolved live symptom.

D1–D6 in the map identify the exact remaining client/frame/ABA, server, causal menu/cursor/cast, realm recipe, physical-route and independent-review inputs. Existing historical provider refusals remain frozen. The new three-refusal source-search operation remains unverified; a separate test-layout operation succeeded on its third identical attempt. Neither is treated as global read-only status. No source/test/CI work was repeated merely to bypass a refusal.

No merge, master write, deployment, production CB access, native image/IDB mutation, subagent or numeric-budget renewal occurred. The preserved dirty root handoffs are local-only. Actual finish protocol state belongs to `W110_FINAL_PASS_FINISH_STATUS_20260929.json`; passing evidence is not a RELEASED result.
