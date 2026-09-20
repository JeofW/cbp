# W85 — EquipItem actor/runtime/quest admission verified; merge remains blocked

20 September 2026. Repository `jeofwong/CopilotBuddy-private` (1367174964), draft PR51 branch `audit/next-55-equipment-observation-20260917`.

## Exact verified code

**9e6e62c56b1a5df74f971d07f3bcb8246af91cee**, tree **41e046af88920adc535881a7afaad2995413d009**.

Integrated **35498796034/art10601244213** passes17/17. New EquipItem context67/67, assertions0, unexpected0. Host **35498796053/art10601753229** exits0 with3344 warnings/0 errors; compile only. Both are offline Windows results with no game attached. Documentation after this tested head is not another tested production revision.

## Defect and test-first repair

Before this repair, EquipItem's pending tick and private submit/confirm methods did not retain the player or recheck running/world/quest admission. Stopping, losing the player, dying, replacing the actor or revoking the profile's quest requirements could leave the pending request usable. The initial path could also dereference a missing player. An expired transaction could attempt cursor restoration after the runtime had already stopped.

Test-only **59994e62a09977f64b9385331317abcd87766e76**, tree120bb771ce9b870cc5636df7b689d0f0f4ee8f8f, adds `EquipItemPendingContextRegressionTests.cs`. It compiles the exact tracked TickPendingEquip, SubmitOwnedCursorEquip, ConfirmOwnedEquipPopup and ResetPendingEquip bodies. It includes the actual new admission helpers when present. Player, quest observation, pickup, acknowledgement/return and Lua boundaries are controlled.

Actual red **35498532789/art10601792076**: **16/67,51 intended assertions,0 unexpected**; other16 integrated entries pass. The existing timeout10/10 and popup-submission18/18 cases remain green. No fixture compilation failure is classified as a gameplay assertion.

The two older isolated fixtures gained controlled player fields/Guid and an always-valid CanEquipNow stub before production so their original timeout/submission cases continue testing the same contracts. Their cases and assertions were not changed. This preparatory fixture change is part of the test-only commit, not a claim that the entire W84-to-W85 span is an unchanged-fixture pair.

Production **9e6e62c5** changes only `runtime-snapshot/Quest Behaviors/EquipItem.cs`:
- Capture the admitted player reference and GUID with pending intent.
- Reuse the existing profile quest requirements with valid/current/alive/non-ghost player, in-game and TreeRoot.IsRunning checks.
- Reject new admission and pending tick/submit/confirm entry after context loss.
- Revoke only managed intent before stale pending cleanup. A transient rejected tick does not set permanent behavior completion, so the same instance can admit fresh work after it is running again.
- Preserve explicit-profile combat use. AutoEquip's automatic combat exclusion is not copied into this quest behavior.

Lua strings, slot/type/submission checks, equipment selection, active-context timeout, acknowledgement/return bodies, constructor, OnStart, Dispose, IsDone, tests and workflows are unchanged. The native compare shows no unrelated edits.

The67 cases cover active/combat controls; stopped, dead, ghost, invalid, absent/replaced player, changed GUID, lost world, disposed/finished owner and revoked quest; both by-name and explicit-slot admission; replacement/quest revocation during pickup; refused pickup; expired stopped versus expired active work; and a same-instance restart after a tick observed Stop.

## Archived proof and scope

Red ZIP SHA256 **0eeeee657ced56d9581b1e3c1147711dc50177081560defbf333114875c749c2**.
Green ZIP SHA256 **0768dc1e9a531b3f6b9b8b82eeb852782ecd046d524e0704f63303bb028f1d49**.
Host ZIP SHA256 **475bcb9ce444f8f32281b72bd67f484d07f5fa903f34db012d0ad7f8281b2038**.

Both integrated archives pass CRC and all195 inner-manifest hashes. All151 normalized test/fixture members are byte-identical across59994e62 ->9e6e62c5. Both working-input indices have1833 entries, with only EquipItem.cs changed. The host has no inner manifest; none is invented.

This proves the named C# admission paths with controlled observations, plus retained complete-source compilation. It is not a supervised Stop/Start session, Lua execution, physical cursor ownership or a complete client lifecycle test. A stop/restart with no intervening behavior call, pause semantics, disposal reentrancy and context changes inside later Lua/logging/acknowledgement/cleanup callbacks are not newly certified. In particular the existing displaced-item return accepts a different item entry rather than proving the physical item GUID; that remains an R04 blocker. Do not label R04 fully closed.

## W84 retained in the same continuation

W84 tested9f206476/tree36604b85: integrated35497665080/art10601597315 is17/17, deletion observations18/18; host35497665006/art10601202604 exit0 warnings3344 errors0. Its real red1ab19b62 was7/18,11 assertions,0unexpected. Unknown inventory now remains unknown instead of being called confirmed deletion; actor/pending replacement during that observation is fenced. All150 normalized members are unchanged across the repair, and only two Methods.cs methods differ finally. The unrelated vendor-log word was restored explicitly before final green. W84 docs are atdc1478d4. Do not repeat this fix or infer complete MIR deletion lifecycle.

## Retained suite results

At9e6e62c5: EquipItem context67/67; deletion observation18/18; AutoEquip context46/46; reusable item10/10; delete-popup request14/14; gossip lifetime17/17; reward-selection lifetime18/18; strict strategy Kind9/9; strategy execution16/16; generated collection34/34; normal objective restart37/37; Ret registration9/9; acknowledged-equip timeout10/10; popup-submission18/18. Analyzer tests remain89. Different groups have different documented evidence levels; compilation/source/pure checks are not native acceptance.

Seven original archives were verified for this continuation: W83 baseline, W84 red/green/host and W85 red/green/host. A standalone standard-Python verifier checks their outer hashes, CRCs, exact source identities, inner manifests, expected results and the two unchanged-fixture pairs. It executes no bot/test binary and performs no network calls.

## Remaining blockers and merge decision

W80's fixed140-commit retained-source review is complete. Do not restart it. W81–W83 were already saved during earlier interrupted attempts and remain retained: disabled automatic dense-pull provider, reusable-item deadline, AutoEquip admission, successful-delete-request confirmation, gossip actor/NPC checks, final reward identity, strict cached/fresh plugin construction, strict Kind parsing and unsupported/ambiguous/anchor-mismatched recipe rejection.

Remaining gates: R03 CAST recipe scheduling and dataset/collection/raw-counter mapping; R04 physical displaced/foreign cursor/popup and remaining full lifecycle; R05 MIR plugin/player mutation admission and bounded scan continuation; R06 same-NPC changed-menu final selection/cleanup; R07 compiler dependency fingerprints, static-state/reload and complete refresh publication. No complete Gordunni/escort strategy or guessed coordinates have been added.

The user already conditionally authorises merging after all blockers and final gates are green. **They are not all green; no merge was performed.** Preserve that authority without asking again. When genuinely ready, reconcile direct refs, preserve backup, review the exact merge preview and integrated/host evidence, then use an exact-head conditional merge. Merge does not imply deployment or live gameplay acceptance.

Direct master rechecked at publication: **b2324913e2499ba30b239dd67224ca2c655c05cc**. No master write, force push, deployment or installed-file modification occurred here. W77's disclosed document add/revert history remains. Original WoW3.3.5a/build12340, TC3.3.5 primary/AC WotLK secondary, no guessed Lua/native offsets, no desktop mouse automation, no auction/ProfessionBuddy expansion. No independent reviewer is claimed.
