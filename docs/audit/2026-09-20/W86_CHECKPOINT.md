# W86 — MIR deletion instance/run context verified; finite scan still open

20 September 2026. Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`.

## Exact verified state

Tested production **73c418210bdfa9a01e611d84a91a428584381c97**, tree **ee724ea1f71a8d36b3b25d4fa294733ef55a5ab4**. Integrated35501234594/art10602552093 passes17/17; MIR context82/82, assertions0/unexpected0. Host35501234606/art10602092819 exits0 with3344 warnings/0 errors; compile only. No game attached.

The user requires a copy-paste new-chat prompt and a downloadable UTF-8 handoff after every continuation, not repository pointers alone. That persistent requirement is now `docs/audit/HANDOFF_POLICY.md`, introduced at6d296189. Update root pointers and include the actual prompt/file in final replies, even for partial work.

## Evidence and repair

Test-only c17ad9ac initially stopped before group execution: the existing normalizer classified a namespace at the start of a raw-string line as a namespaced initializer. Artifact10602281559/run35500461891 is retained as a harness failure, not gameplay assertion red. Test-only8e5c9e96 moved that generated namespace onto the preceding fixture line, with no case/assertion/workflow change. Actual run35500720727/art10602042257 then produced4/82,78 intended assertions,0unexpected.

Test-only **ae3f1b1d604b839a308b7d1d6b24ad86b9402ecc** additionally lets one old source-contract locator accept an instance callback declaration. Actual red35500961319/art10602152292 is again4/82,78 assertions,0unexpected, with the other16 integrated entries passing. Existing deletion observation18/18, popup request14/14 and plugin compilation/source contract14/14 all pass. Earlier fixture preparation makes two isolated old wrappers accept instance methods and explicitly controls their runtime admission; their existing cases/assertions are preserved. Those preparation commits are not part of the unchanged-fixture production pair.

Production73c41821 changes only the plugin's Methods.cs and MrItemRemover2.cs. Private pending state and callbacks now belong to the plugin instance, rather than all instances sharing static mutable state. The operation records the admitted player reference/GUID, run/enable lifetime and a distinct local token. Existing BotEvents start/stop notifications revoke old intent even without an intervening pending tick. Disable revokes before external cleanup. Pickup/request/confirmation paths require the current initialized, running, unpaused, in-game, alive/non-ghost/noncombat/noncasting actor and the removal setting. Stale pickup/request/timeout callbacks cannot retire or mark a replacement operation submitted.

Deletion Lua, cursor predicates, the ten-second deadline, item selection/protection, selling/opening/combining rules and the scan body are unchanged. Instance state does not establish global native cursor arbitration. A callback result and observed inventory absence are not server-certified deletion.

The new fixture executes the actual state declarations, OnEnable/OnDisable and deletion method bodies with controlled world/player/event/inventory/recording-Lua boundaries. It covers admission/revocation across12 invalid states, replacement actors, multiple instances, stop/start notifications, stale successful returns and reentrant timeout logging. The full real plugin also compiles in the retained compilation group; its UI/file setup is not executed there.

Red->green: all152 normalized files byte-identical;1834 indexed inputs, only the two production files differ;196 inner-manifest hashes and ZIP CRC verified in each integrated archive. Host contains no inner manifest. Archive integrity verification is separate from the recorded Windows test execution.

## Next and remaining gates

Next: finite multi-candidate scan continuation with no additional timer/loot trigger. A refused or locally completed first request must not starve later candidates; keep a bounded pass, current actor identity and existing selection rules. Do not repeat the already-green context or nullable inventory repairs.

R05 is not wholly closed: scan progression and broader plugin/native lifecycle remain. R03 CAST scheduling/raw-index mapping, R04 physical displaced/foreign cursor/popup, R06 same-NPC changed-menu final selection/cleanup, R07 compiler dependency/static reload/full refresh remain. Retain W77–W85, W80's completed140-commit review, all source/core/addon policies, backups/exclusions and auction withdrawal. No new Gordunni/escort recipe or guessed coordinates.

Merge remains conditional and blocked, although the owner has already authorized it once the actual gates are green. No re-approval question is needed. A source merge is not deployment or live acceptance. Original WoW3.3.5a/build12340; TC3.3.5 primary/AC WotLK secondary. No desktop mouse automation, new Lua/native offsets, unrelated auction/ProfessionBuddy work or installed-file replacement. No independent reviewer or supervised gameplay result is claimed.
