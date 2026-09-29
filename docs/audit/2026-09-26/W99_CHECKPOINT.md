# W99 — container use and quest-info Lua receipts verified offline

26 September 2026. JeofW/cbp ID1367174964, open/draft PR51, branch audit/next-55-equipment-observation-20260917. Active Goal/full audit remain in progress. No subagents.

## Repair and evidence

WoWItem container use emitted a raw Lua boolean through lua_tolstring, losing successful receipts; the managed bool converter also accepted arbitrary nonempty malformed text. Use now emits numeric1/0 and requires managed integer1. Quest-info validation and flags also use numeric1/0. Its consumer requires exactly four values, validation1, binary flags and a nonnegative invariant integer quest ID. It commits outputs only after complete validation. Unknown/malformed observations preserve defaults and MrItemRemover's conservative quest protection. Existing GUID/location/entry checks remain. Only WoWItem.cs changed in production; no global converter change.

The new58-case fixture compiles exact tracked item methods, the full UseItemOn owner and MrItemRemover's actual quest protection predicate. Generated requests execute with stock Lua5.1 C API/lua_tolstring and tracked managed converters/load-size logic. Controlled world/backpack/Lua API observations exercise healthy use, wrong source, missing/nil/raw-boolean/malformed/error receipts, healthy quest flags/IDs and invalid tuple shape/flags/negative/fractional/overflow IDs. It checks mutation count, item-use result, full dispatch counter/blacklist, complete info outputs and conservative protection. Dispatch uses legacy InvocationCount mode, not authoritative quest credit.

Initial test-only3a682a7121e8d18133f6a77dc9d7c7a2584dbd34 failed normalization before execution: three namespace lines inside an embedded raw string matched the outer-namespace guard. Test-only correction9af25395d376590c0a3011ee5719310789536678 adds the retained comment pattern on exactly those lines. Normalizer, cases and assertions unchanged. Initial failure is retained as a harness failure, not assertion-level red. Its integrated archive has51 members/50 verified inner hashes and no Wholesome run log.

## Exact source and hosted validation

Prior W98 docs8a6f2507c1fa887dcf40ae6620e7cc53e98ab3cb. Corrected red9af25395d376590c0a3011ee5719310789536678, tree9aa40204242db18223e3794b64d500d82ff9235d. Tested production83d0f6e1bea2b5e31f8778ce40d002cb2d89cf37, treeace6e56c336122b47acfe6ffe7924bc63a9e7a33. Containing checkpoint publication is documentation-only.

- Initial integrated36228022061/art10902075040: Wholesome normalization/build failure before execution; other16 groups passed. Host36228022073/art10901630916 passed.
- Corrected red integrated36228193099/job108366146297/art10901284309:30/58,28 intended assertion failures,0 unexpected;16/17 groups, all builds passed. Host36228193063/job108366145943/art10901875636 passed.
- Green integrated36228681261/job108367502458/art10902146074:58/58,0 assertions/0 unexpected;17/17 groups. Host36228681280/job108367502634/art10901795866 passed.

Corrected red/green integrated archives each contain208 members/207 verified inner hashes and1845 source inputs. All163 normalized fixture members are identical; only WoWItem.cs differs. All1845 green hosted input hashes match local files. Retained equipment Lua83/83, reward Lua33/33 and earlier owner/lifecycle/compiler regressions remain green. Both paired host builds are Release/x86,0 errors/3344 warnings, tests_run=false/game_attached=false. All project execution was GitHub-hosted Windows/x86. All six downloaded archive digests match GitHub metadata. Host ZIPs have4 members/no inner manifest; no separate CRC check claimed. Exact receipts/comparison in W99_EVIDENCE.json and external W99_RED_GREEN_COMPARISON_20260926.json.

Direct source/fixture self-review found the repair preserves conservative unknown handling and existing identity predicates. This is not independent acceptance. Stock Lua plus controlled APIs is not original-client executor, recipient effect, matching server acknowledgement, physical identity or quest credit evidence.

## Next task and retained limits

Next R06: reproduce same-content gossip close/reopen through actual GossipEvent generated scripts and full owner. Read external R06_GENERATION_INVESTIGATION_20260926.md and retained W88/GossipEventLifetimeRegressionTests. Preserve complete typed/ordered menu content and actor/NPC checks. Evaluate a Lua-side event generation/observer identity captured with content and checked at mutation; missing/replaced state must refuse stale capture, including during final observation. Healthy same-generation selection/cleanup must continue. This direction is not implemented. Pinned Build12340 event source is not original-client event delivery or native concurrency proof.

R03 strict v1 objective metadata/raw-slot/recipient distinctions, ObjectiveProgress deferral, QuestComplete, CAST refusal, independent collection and W91/W92 remain. R04 physical displaced/foreign/same-entry cursor and same-slot foreign popup ownership/full client lifetime remain unresolved; entry equality and queued events are insufficient. Keep original native/client/server and all wider Wholesome/navigation/Singular/independent/supervised acceptance gates explicit.

R07 original W80 minimal requirements are mapped to W89/W94/W95/W96 in external R07_REQUIREMENT_MAPPING_20260926.md. Actual compiler inputs, required construction/preparation preservation, lifecycle cleanup and explicit static policy have coverage. Keep docs/plugins/REFRESH.md limitations: no immutable snapshot, runtime dependency replacement, arbitrary activation rollback or concurrent/reentrant guarantee. This mapping is not independent closure and must be checked against the eventual final source. Avoid speculative plugin-framework expansion.

## Authority and preservation

Preserve W98/W97/W96/W95/W94/W93 and earlier repairs/evidence, including failed intermediates and initial W99 harness failure. Ordinary fast-forward publication only to approved PR51 branch after expected-parent checks; remote parent/paths/blob/decoded bytes/tree/head verified. Checked non-LFS paths before process-scoped GIT_LFS_SKIP_PUSH, hooks retained/environment restored. Historical refusal is not globally resolved; no blocked combined request replay or new refusal. Numeric preparation3 historical/0 new/0 remaining. Direct masterb2324913e2499ba30b239dd67224ca2c655c05cc unchanged.

No local project execution, production access, weakened assertions, W80/W92 restart, PR58 recreation, excluded PR25, guessed offsets/recipes, force-push, master write, merge or deployment. Original3.3.5a/build12340, TrinityCore3.3.5 primary/AzerothCore WotLK secondary. Active Goal remains incomplete; checkpoint every verified slice.
