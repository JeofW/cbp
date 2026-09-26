# W101 — refused interaction cannot acquire gossip ownership

26 September 2026. JeofW/cbp ID1367174964, open/draft PR51, approved branch audit/next-55-equipment-observation-20260917. Active unbudgeted Goal remains incomplete. This turn made verified source progress; it is not an unchanged blocked turn. No subagents.

## Repair

GossipEvent previously called void Interact and recorded the NPC GUID even when the host returned early for a missing executor. A matching menu subsequently appearing could then be selected. WoWObject now shares its existing interaction body through protected TryInteractCore. Its void virtual overloads retain their call chain. WoWUnit exposes TryInteract, and GossipEvent assigns pending GUID only when it returns true. The existing attempt counter, open deadline, authoritative quest acknowledgement and W92 post-call lifetime guard remain. A refused attempt still consumes an attempt and waits through the bounded window, but cannot capture/select/close a later matching menu.

The receipt reports completion of the existing local executor only. False after an exception may follow partial dispatch; true is neither server acknowledgement nor first-menu attribution. No native instructions, ABI, offsets, timer, or executor implementation changed. Existing WoWItem.Interact override remains untouched; no new item interaction method is introduced. Gossip's current target lookup uses exact WoWUnit types, not subclasses with overridden interaction behavior. Future callers/subclasses must not infer virtual override dispatch or server causality from this new method.

## Regression evidence

Test-only baseline adds one controlled full-owner foreign-menu case and three actual missing-executor ownership cases at dataset indices 0/3/17. Existing cases/assertions remain. Both recording fixtures gained TryInteract adapters in the red baseline, not after the repair. The controlled boundary provides the same callback/attempt behavior with explicit success/refusal. Actual-host cases run the loader, scheduler, generated XML, compiler, factory, wrapper and allocated WoWUnit through the existing missing-executor path. No successful native dispatch or game attachment is claimed.

Prior W100 docs head da53e99b7c2a446e0bacf624c45ddb51f2183e12. Red source 0a54f9d6020de947f2468fa0fd4d94a9fdeec3d3, tree 4a565d01e56e3624fb35b14e5a906600c7e9b35c. Final tested/production source 67b4898406a806b2b4a65360ca416a96dff7e1fc, tree c062da50978b8540850a5ae98fd258d8bd503208. Containing checkpoint commit is documentation-only.

- Red integrated 36231026305 / job108374085958 / artifact10901803718: gossip58/59 with1 intended assertion, real matching recipient10/13 with3 intended assertions, zero unexpected errors in both groups; overall16/17. Host36231026298 / job108374085822 / artifact10901848560 passed.
- Green integrated 36231317916 / job108374891358 / artifact10902785723: gossip59/59, real matching recipient13/13, W100 persistent Lua63/63, generated recipient dispatch18/18, restart37/37; overall17/17. Host36231317886 / job108374891392 / artifact10902229951 passed.
- Both integrated archives have209 members,208 verified inner hashes,1846 source inputs. All164 normalized fixture members are identical red-to-green. Only WoWObject.cs, WoWUnit.cs and GossipEvent.cs inputs differ. All1846 final hosted input hashes match local files.
- Four outer archive SHA256 values match GitHub artifact metadata. Each host archive has4 members, exact commit identity, Release/x86 build0 errors/3344 warnings, tests_run=false/game_attached=false. No host inner manifest or separate CRC check is claimed.

W101_EVIDENCE.json and external W101_RED_GREEN_COMPARISON_20260926.json retain receipts, final summaries and comparisons. All project execution was GitHub-hosted Windows/x86. Static local reads, hashing and artifact inspection only. Direct self-review is not independent acceptance. Retained W99 container58/58,W98 equipment83/83,W93 reward33/33, R07 cached7/7,fresh8/8,reuse9/9,preparation6/6,lifecycle12/12,compiler8/8,option identity2/2 and earlier controls remain green.

## Requirement assessment and remaining blockers

Read W101_REQUIREMENT_MAPPING.md and external REMAINING_REQUIREMENTS_20260926.md. The user answered that no additional development-only Build12340 trace/source evidence is available. Do not ask again for the same evidence or invent it.

R03 strict v1 identity/Kind, declared scheduling order, source recipient/anchor, whole-quest acknowledgement and independent collection are covered. ObjectiveProgress raw-slot mapping and unsupported GameObject/ground-cursor/BelowHp/Escort recipes remain deferred, not implemented by guessed indices/protocols. A supported source-backed recipe/schema and independent raw-credit mapping would be needed to extend this scope. Successful native request and server acknowledgement remain open.

R04 remains incomplete: both equipment owners compare cursor entry/type and popup slot without proving physical cursor GUID or popup/request lifetime. Different-entry displaced cursor and same-entry foreign cursor can remain ambiguous; same-slot foreign popup ownership is not established. Required input is Build12340 source or a development-only trace identifying physical cursor GUID/lifetime and request-bound popup ownership. Pinned FrameXML slot data and Offsets335 symbol labels do not establish a usable ABI. Do not disable every healthy equip as a substitute for proof.

R06 W101 fixes known local refusal, while W100 protects after-capture continuity. A completed interaction can still be followed by a foreign same-NPC menu before first capture. Required input is supported original-client request/response or event-lifetime evidence tying that first response to the originating request, including concurrent same-NPC behavior. No assumed event order/count, modern API substitution, or client/server causality claim.

R07 original minimal compiler/construction/lifecycle/static-policy requirements have retained source coverage; keep docs/plugins/REFRESH.md limits. No speculative framework for immutable snapshots, concurrent/reentrant refresh, runtime dependency replacement/unloading or arbitrary activation rollback. R08, wider Wholesome/navigation/Singular, native/original-client/server, independent and supervised gates remain open. No merge or deployment.

Next: continue from this mapping and any genuinely new source finding or supported evidence. The inspected ownership gaps cannot be closed from the currently available entry/type/slot/menu observations. Preserve the verified repair; do not restart W80/W92 or repeatedly rerun unchanged CI. If no new actionable source work/evidence exists, report the exact hard blockers and apply the active Goal's blocking rules rather than invent another repair or declare completion.

## Publication constraints

Only approved-branch ordinary fast-forward publications; red/repair/docs each reconcile expected parent and retain exact path/blob/decoded bytes/tree/head readback outside the repo. Master b2324913e2499ba30b239dd67224ca2c655c05cc remains unchanged. Historical provider refusal is not globally resolved; no blocked combined request replay or new refusal. Numeric API-preparation budget3 historical/0 new/0 remaining. Process-scoped GIT_LFS_SKIP_PUSH applies only to checked non-LFS paths, preserving hooks/environment. No local project execution, production access, weakened assertions, force-push, master write, PR58 recreation, excluded PR25, merge or deployment. Preserve W100–W93, earlier repairs, R01/R02/R05 dispositions, W80 ledger, W91/W92 evidence, backupsaca2f1cd/f7a598ad and failed intermediates. Original3.3.5a/build12340; TrinityCore3.3.5 primary/AzerothCore WotLK secondary.