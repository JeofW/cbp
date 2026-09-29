# W84 — pending deletion observations distinguish unknown from absence

20 September 2026. Repository `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`.

## Verified state

Tested head **9f2064769b177eef2ffd43536f6eec35eb2470e4**, tree **36604b85337e112447c06b65ca57eb88a0502d17**. Integrated **35497665080/art10601597315** is17/17, new deletion-observation group18/18 (0 assertions,0 unexpected). Host **35497665006/art10601202604** exits0 with3344 warnings and0 errors; compile only. No game attached.

The continuation entered at W83 documentation f399fb81. The interrupted attempts had already saved W81–W83; they were reconciled rather than recreated. W80's140-commit retained-source review remains complete, with remediation still open.

## Reproduced and repaired

Test-only **1ab19b625d85a8295b6ae825f821c169f6fc984a**, integrated **35497245564/art10600602214**, executes the exact tracked MIR `TickPendingDelete` and `PendingDeleteItemStillObserved` method bodies with controlled inventory/cursor/lookup boundaries. Actual result: **7/18,11 intended assertions,0 unexpected**; the other16 integrated entries pass.

Previously missing/invalid/unreadable inventory could be interpreted as absence and logged as confirmed removal, even without a successful local request. An observation could also reset a replacement pending item after a callback changed it.

Production **cfca67f3f0945e0cc0fdadc81c96a5e3c63fc61b** changes the existing observer to nullable Boolean: present, absent or unknown. It samples one inventory collection, rejects null/invalid observations and checks that player reference/GUID and pending GUID/entry still match. The pending caller leaves unknown state alone until a later observation or the existing ten-second timeout. Known disappearance without a local request revokes intent without a success claim. Even requested disappearance is described as locally no longer observed, not server-certified deletion.

A one-word unrelated vendor diagnostic edit was caught in diff review and restored verbatim at **9f206476**. The final difference from the test-only red is only the two intended pending-observation methods in `runtime-snapshot/Plugins/MrItemRemover2/Methods.cs`. Pickup, Lua scripts, deletion selection/protection, request/confirmation gates, selling/opening/combining and all tests are unchanged.

Across red to final green, all **150 normalized members are byte-identical**. The1832-entry working-input indices differ only at Methods.cs. Both integrated archives pass CRC and all194 inner-manifest hashes. W83's baseline archive was also reopened and verified:17/17,193 inner hashes.

## Scope limits and next gates

This verifies C# observation/control flow, not native inventory completeness, physical deletion acknowledgement, or the full plugin lifecycle. A non-null materialized collection is not a new guarantee that every native inventory record was read. Reentrant state changes outside the tested observation boundary remain separate work.

R05 remains open for plugin/player admission at every mutation boundary and bounded multi-item scan continuation. Do not repeat W81's successful-request confirmation guard. R04 still needs EquipItem actor/quest lifetime and physical displaced/foreign cursor/popup ownership. R06 still needs same-NPC changed-menu identity in the final request and cleanup. R07 still needs actual compiler dependencies/static reload/full refresh. R03's unproven CAST and objective-index mapping remain deferred; W83's unsupported/ambiguous recipe rejection stays intact.

Keep W81–W83 and W77–W79 fixes, original3.3.5a/build12340 policies, TC3.3.5 primary/AC WotLK secondary, backups/exclusions and auction withdrawal. No new Gordunni/escort recipe, coordinates, native offset, Lua API or desktop mouse automation was added. The user conditionally authorises merge when all remaining gates are green; they are not green yet. No merge or deployment is claimed.
