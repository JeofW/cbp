# PR51 W110 scope and evidence-closure record

27 September 2026. This record answers each remaining W109 item using new source inspection and the W110 regression-first work. Exact final source, hosted results, hashes and publication identity are in the accompanying W110 checkpoint/evidence; this narrative is not a substitute for those receipts. It neither repeats W104-W109 nor publishes the historical refused W108/root handoff transaction.

## Scope outcome and meaning of closure

W110 identified two additional concrete shared-combat receipt defects, below and after the W109 bool-to-TreeSharp fix. They were treated as implementation work, not disguised as unavailable live acceptance: actual spell-manager local dispatch could fail while callers returned success, and the ground factory could discard the actual terrain method's false result. The required test-only behavioral-red, minimal repair, hosted green, artifact and unchanged-fixture gates are recorded separately for each. Neither repair introduces an exclusive native request protocol or claims a successful server effect.

The source/core audit also resolves several ambiguities without speculative changes: SAME_EFFECT overlap is not whole-aura replacement; configured Paladin aura applies while unmounted; the actual shared spell readiness includes IsUsableSpell; the dense isolation path is deliberately disabled; the strategy sidecars in the inspected repository dataset directory are absent; standalone ammunition does not inherit the pending gear owners' acceptance proof. These findings are not evidence that every gameplay issue has been fixed.

Every external boundary below identifies what was examined, what remains unknown and the specific evidence needed next. **A completed repository audit is not completed supervised gameplay acceptance.** No subagent/independent reviewer, local project execution, production CB access, IDA mutation, deployment or release is represented here.

## 1. Build12340 native/API coverage

**New work:** traced the actual manager, ground wrapper/factory, aura collection, spell/readiness APIs and retained build12340 native contracts into real hosted owners. Expanded immutable TC335/AC spell/aura/stack-category evidence to eight source files and a reproducible hash/category manifest. Core reference commits are TC `8fda442f6c30ca21a622638063ab8b28376f1b25` and AC `8337a378ac325e62a6a91e00c6a5e944205e8536`.

**Retained limit:** the previous fresh IDA health operation was provider-refused with no result/session. It was not rerun, disguised or divided. No independently verified current native read path was established in this execution, so no fresh function/address, byte, breakpoint or health result is claimed. IDA is not declared globally unavailable. A new public TC recursive tree-discovery request was separately refused and preserved; the successful known-path reference downloads do not recover that listing.

**Exact next input:** independently justified authorized native read access, bound to current client identity and the specific function/API contract being investigated—not a reissue of either refused transaction. New effective aura amounts, proc-adjusted cast times or request identity must be established at that boundary, with exact source/version/bytes and uncertainty retained. Original build12340 and the four research/core/quest/addon policies continue to exclude unconfirmed Classic 3.4, Retail and later-expansion assumptions.

## 2. Ground-request ownership and terrain effects

**Inspected:** full `Helpers/Spell.cs::CastOnGround`, actual `SpellManager.Cast`/native dispatch/`ClickRemoteLocation`, legacy adapter, `LocalPlayer.HasPendingSpell` and the retained W107/W109 observation tests. New fixtures execute the complete relevant owners and real TreeSharp, controlling only executor/world/memory leaves.

**Newly repaired:** missing or exceptional local dispatch no longer becomes successful Cast/synchronous/random-wrapper completion; failed actual terrain completion no longer becomes successful final ground action. Compatibility overloads, stack/arguments, correct low-byte return, cleanup, pending waits and existing admission guards remain. The first result is local dispatch completion only; the second propagates the existing terrain receipt. Neither is an authoritative damage/heal/server receipt.

**Still unproven:** healthy cast/wait/click origin, exclusive cursor/request lifetime, same-spell competing initiators, recipient/location changes across waiting, native cross-frame freshness and authoritative resulting terrain effect. A matching pending spell index/name, elapsed timeout or a successful local method alone cannot supply origin.

**Exact next input:** a verified originating-request and lifetime contract, recipient/location binding and native cursor behavior for the original client, plus supervised competing-initiator/timeout/cancel/target-change scenarios. Do not redesign the healthy timeout policy, claim exclusive ownership or clear foreign cursors from a guessed token.

## 3. R06 first gossip response

**Inspected:** complete `runtime-snapshot/Quest Behaviors/GossipEvent.cs`. The owner captures current actor/NPC GUID and context, admits only a completed interaction, rejects pre-existing menus, checks the same NPC, uses bounded exact full-menu observations and an observer lifetime, invalidates on relevant events, and reobserves content/token in the same Lua mutation request. Option and objective mapping are strict; cleanup does not indiscriminately close foreign work. Menu selection itself is not quest credit.

**Retained boundary:** the same NPC can produce indistinguishable responses to separate requests. Exact menu text, greeting/options, menu ID, event count, local generation or time since interaction can establish consistency but not a missing server per-attempt origin identifier. W109's inspected request/response contract remains the authoritative retained protocol evidence; W110 did not invent a nonce.

**Exact next input:** an exclusive request/lifetime mechanism or independently origin-aware development evidence that binds the first response to the admitted interaction under identical same-NPC and delayed-response cases. If the verified original protocol truly supplies no such distinguishing observation, document that impossibility boundary and obtain an approved serialization/behavioral contract rather than fabricate provenance. Supervised correct option and authoritative quest-counter/complete acknowledgement remain required.

## 4. Shared combat, facing and all-class support

**Inspected:** actual shared Spell/SpellManager, Unit predicates, full Movement and GroupCombatSafety, three Ret factories, support/settings, live spell-usability adapter, linked decision and registration controls. W110's new shared receipt fixes are not Ret-only adapters; exact callers are exercised. W104-W109 attack/facing/movement/target-gap controls remain present.

**Established distinction:** a registration test is not a rotation tick; a real TreeSharp tick with controlled world/native leaves is not live game acceptance. `GroupCombatSafety` has a narrow Combat Bot/dungeon condition, not universal enforcement in every bot/routine/context. Movement and cast guards must be evaluated together; absence of one factory-level WaitForCast alone does not prove native interruption.

**Exact next input:** authorized original-client attack/facing/stun/interrupt/threat/range/LOS and target-loss observations for the actual class/routine/context. A complete other-class audit requires that class's actual owner and requirements; this Ret plus shared-owner audit cannot certify all rotations. For Ret, the companion compatibility matrix supplies exact remaining rank/talent/proc/effective-buff/encounter evidence requirements.

## 5. Navigation, elevation, water, lifts, taxi and gathering/remount

**Inspected new source:** `MeshNavigator.RequestOwnership.cs`, `LiquidEnvironment.cs`, full `ElevatorTransitController.cs`, mounted Flightor aura path, `FlightPaths` request/publication observations and taxi-open handler, and Gatherbuddy's actual node approach/interaction/loot continuation. These are source observations, not a reconstructed route simulation.

The elevator policy differentiates approach/boarding/exit permissions, observes the selected transport GUID, finite XYZ and ground/falling state, and invalidates stale or interrupted dock dwell. Managed navigation identity captures actor/address/memory/mover/provider references but explicitly does not establish unobserved native ABA/frame freshness. Liquid classification remains conservative under missing evidence; its trace/cache is not shoreline connectivity proof. Taxi managed publication protects its context, while node/frame/native response and actual takeoff still require separate evidence. Gather interaction/loot/waypoint progression is not proof of a successful subsequent mount/fly request; old HB/WoD comments are not original-client authority.

The complete mounted target owner `DecoratorNeedToFindTarget.cs` and `Mount.ShouldDismount` were also inspected. Explicit requested MobIDs remain subject to level, pull-range, hotspot and ground-farming restrictions; nearby Kill and hotspot/target contexts request dismount, while distant transit does not. The retained 16-case mounted-hotspot fixture exercises the actual target/dismount-admission owners. A true ShouldDismount result or that controlled fixture does not establish actual native dismount, safe landing or subsequent engagement. Escort movement cannot be certified while its source-backed strategy/materializer remains unavailable; it is explicitly included in the recipe/route boundary below.

**Exact next input:** reproducible development-route identity and map/mesh provenance; time-ordered original-client movement/transport/ground/liquid observations and actual route results for lifts, cliffs, shorelines and 3D LOS; native taxi frame/node acknowledgement, arrival and route ownership; and after-gather completed loot, next travel target, mount availability/settings and observed mounted/flying state. Fixed Z, straight-line distance, passing pure controller vectors or a log saying Harvested do not close these acceptance cases.

## 6. Dense-pack ranged pull and retreat

**Inspected:** `SingularRoutine.cs:89-121` and the Ret optional isolation helper/registration controls. `DensePullIsolationValidated` remains **false**. Both advertised capability and creation are gated; the optional Exorcism opener does not replace ordinary Normal/Instance/Battleground Pull/Combat registrations.

The full `Bots/Grind/Levelbot/Actions/Combat/PullIsolationCoordinator.cs` and `IIsolationPullProvider` contract were newly reviewed. The plan carries target GUID/entry, provider/opener, pull/retreat endpoints and deadlines; it does not establish a captured actor/map/routine lifetime token. The coordinator checks current route reachability, endpoint blackspots and cached hostile envelopes, but its risk calculation is against the straight segment rather than the actual navigated corridor. Its alternate headings explicitly set candidate Z to the pull point's Z. Initial engagement and retreat deadlines, single-engaged-target and separation/melee stop guards exist, but those controls do not prove a safe 3D route or ownership across actor/provider replacement. These are concrete reasons the retained disabled capability is not validated, not permission to activate it or invent a larger route system without evidence.

**Exact next input:** verified safe pull/retreat route with risk and line-of-sight assessment, actual original-client aggro/assist behavior, actor/routine/target lifetime and arrival/retreat observations. A configured maximum range, straight line or fixed elevation is not a validated corridor. No activation, unsupported guarantee or unrequested risk-planning framework was introduced.

## 7. Quest items and unsupported recipe/materializer paths

**Inspected:** actual DataLoader, complete relevant strict strategy loader, ProfileBuilder mappings and `UseItemOn` captured-item/recipient/context/acknowledgement loop. Loader binds recipe schema `quest-strategy-pack-335-v1`, build12340, source kind/revision and the exact dataset hash, restricts fields/ranges/duplicates, and preserves unsupported-strategy isolation. A declared unsupported special strategy is not silently converted into ordinary killing.

The repository dataset path is `runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.json`. Exact checks returned false for `quest_strategies.json` and `quest_data.provenance.json` **in that directory**; tracked-file lookup corroborated the dataset path. This is not a claim about every external deployment or an uninspected configured dataset location.

Supported captured item use revalidates physical item/recipient/actor and quest/range/LOS around waits and setup, records actual submission before acknowledgement, and requires authoritative raw objective/complete state for the structured strategy. Item consumption may remove the captured item after a legitimate request, so continued bag presence is not a universal acknowledgement prerequisite. Legacy InvocationCount is not promoted to structured quest-credit proof.

**Still absent:** a complete versioned realm recipe and raw-counter/recipient mapping for unimplemented GameObject/ground/BelowHp/Escort materializers. BelowHp schema v1 lacks the needed health-threshold evidence and is rejected, not guessed. Objective text or CAST flags do not supply those missing fields.

**Exact next input:** the actual realm/core/database revision, supported quest and item/target IDs, raw objective identity, all required recipe parameters, authoritative source and dataset hash; then regression-first materializer work and supervised native item/server-credit validation. No source-backed recipe was fabricated during W110.

## 8. Inventory, deletion, rewards and standalone ammunition

**Inspected:** AutoEquip normal/pending and standalone ammunition entry, InventoryManager, `DeleteItems`, retained equipment/physical cursor/item wrappers and published W104-W109 controls. The two pending equipment owners preserve physical item choice and acknowledgement/context rules; the W108 default-destination repair is retained, not reopened as an unimplemented by-name migration.

The standalone ammunition scanner is a **different path**: empty ammo slot, bag scan, matching ammunition equip-location and an `EquipItemByName` request/log. It is not the same physical-copy/pending-owner protocol and does not supply completed slot acknowledgement, weapon/ammo compatibility optimization, arbitrary transfers or a current actor/cursor lifetime contract. InventoryManager's empty equip-slot list for Ammo excludes it from normal gear scoring; it does not validate this separate scanner.

Deletion retains an explicit profile-item request, physical item capture, bounded refusal, observed pending cursor/popup and actual disappearance checks. A foreign cursor is not permission to clear it. Existing controlled guards are not proof of live irreversible item deletion or every global protection policy. Reward/item/equip application still needs client/server acknowledgement distinct from local command completion.

**Exact next input:** supervised disposable development inventory with known physical copies, safe protected-item controls, actual equip/popup/delete/reward/quest-credit results, and a separately specified ammo owner covering weapon type, learned ammunition restrictions, slot state, actor lifetime, cursor conflict and acknowledgement. Do not claim that W108 acceptance automatically covers ammo, all equipped-to-equipped moves or best-in-slot policy.

## 9. Bounded plugin refresh

**Inspected:** complete `Styx/Plugins/PluginManager.cs`. Existing content/compiler fingerprints, declared type-set validation, fresh instance creation, prior-set retention on failed preparation, rejected-instance disposal and bounded replacement logic remain. Default-context assemblies and shared static state are not automatically unloaded just because new instances are created.

**Retained boundary:** immutable filesystem snapshots, arbitrary dependency unloading, concurrent/reentrant refresh and rollback of arbitrary activation side effects are not provided by that bounded policy. Source fingerprint comparison is not atomic filesystem isolation. These extensions require a separately approved lifecycle design and independent/integration review, not speculative changes added to this gameplay audit.

**Exact next input:** intended concurrency and activation/deactivation contract, dependency lifetime/unloading requirements and reproducible failure scenarios. Preserve existing assertions and old-set retention until regression-first evidence justifies an extension.

## 10. Earlier intermittent publication assertion and retained diagnostics

**Inspected:** `QuestPublicationAcceptanceRegressionTests.cs` and `WholesomeAutoQuest.cs::DoScan`. The fixture distinguishes actual assertion failures from unexpected owner/harness exceptions and retains the exception object/stack; the assertion was not removed, loosened or converted to a warning. DoScan's production catch logs the scan message, which must not be mistaken for the complete original diagnostic stack.

The new hosted artifacts retain results plus complete relevant build/run logs and all inner hashes, including the initial W110 failed-normalizer candidate. Ordinary analysis-script errors and provider refusals are identified separately. Later green publication controls do not establish a root cause or repair for the earlier W105 intermittent assertion.

**Exact next input:** an actual recurrence's exception/inner stack, exact source/fixture identity, run/artifact and phase/lease/selected-profile context. Diagnose that evidence without rerunning unchanged CI merely to manufacture activity or suppressing the assertion.

## 11. Independent review, acceptance and finish

No subagents or independent reviewer were used. This implementer's source review, exact publication readback, hosted tests and artifact inspection do not satisfy a genuinely independent gate. No production client, live realm, mesh route or development gameplay scenario was executed locally, in accordance with the user's constraints.

PR51 remains required to be **open/draft/unmerged**. No merge readiness, deployment, all-class/all-quest support or absence-of-future-defects claim is made. The numerical preparation budget remains **3 historical / 0 new / 0 remaining**. Master and production CB are not mutation targets.

New W110 documentation publication excludes the historical refused W108/root handoff paths. Their frozen evidence/refusals and local root edits remain preserved; a new W110 continuation pointer does not pretend that the original refused transaction succeeded. Final source/documentation and PR/direct-ref checks must be recorded before reaching the CoS completion boundary.

`session_finish` is a separate release protocol, not a test outcome. It is called only when the requested repository/evidence work and final checks actually reach completion. HELD is not release: execute genuinely new delivered work or wait at that boundary in the same turn. No normal final response is authorized until RELEASED or explicit user stop. The external finish record must preserve the actual returned state rather than infer one from a successful CI run.
