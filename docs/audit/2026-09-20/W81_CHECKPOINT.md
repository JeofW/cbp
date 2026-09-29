# W81 — recovered remediation and verified reward-selection repair

20 September 2026. Repository `jeofwong/CopilotBuddy-private` (1367174964), PR51, branch `audit/next-55-equipment-observation-20260917`.

## Coordinate and merge decision

Verified production/test head: **d72c557691c66563f791f3b9e848a9a1e3355a72**.
Verified tree: **bf65dce7dde08e2fd3dade05bafbff18639a2044**.
W81 documentation is a successor, not a separately tested production revision.

The user now conditionally authorises merging when all review blockers and verification gates are green. This supersedes older prose requiring another approval, but **the condition is not yet met**. PR51 remains draft/unmerged. Do not treat green CI as closure of untested findings. Direct master remains b2324913e2499ba30b239dd67224ca2c655c05cc, restored approved tree552eeab1233c7c282897dca0ed9f4334c5e8ed43. No master write, force push, deployment or installed-file replacement occurred in W81.

Original WoW3.3.5a/build12340 only; TrinityCore3.3.5 primary, AzerothCore WotLK secondary. Read the four governing policies, W80_CHECKPOINT/COMMIT_LEDGER/EVIDENCE and W77–W79. Scope remains questing, navigation, Singular and directly related safety. Do not resume AuctionHouse/ProfessionBuddy or invent Gordunni coordinates/recipes.

## Recovery rather than duplicate implementation

The live entry head94c1f0c71226b81fefe2438ea3e58a4057b4ad6e was16 commits beyond W80 documentation6cc0c13d. Root pointers and the PR description were still W80. The intervening work was reconciled before this repair.

| Finding | Recovered production | Evidence rechecked in original red archive and final integrated archive |
|---|---|---|
| R05, request admission only | a46f4d20f37206cff6622da3f7e912725cafa0a2 | Delete popup7/14,7 assertions ->14/14; no unexpected errors |
| R02, reusable-item acknowledgement liveness | bffef4bdacb6d47d25e7370217f19f44c3f9ecfb plus unrelated-label restoration1cb478c83631b6e20ef30a43b8a68422ef6ab087 | Retained-tree reusable item5/10,5 assertions ->10/10 |
| R01, automatic provider containment | c3a229eccc74b30f0956228f24d1c39671d1b059 | Provider containment5/9,4 assertions ->9/9; normal Ret remains registered |
| R06, actor/NPC lifetime and cleanup only | 6fd1b69d33ec7a4274c5164f780f9ee596def2ae plus exact finite-anchor restoratione91a08b79cb7bcc606c2fd853df847ecb8841686 | Complete tracked gossip4/17,13 assertions ->17/17 |
| R04, AutoEquip actor/context admission only | 346f133bc6e0851ac6da617460ea025f78c54df5 | Captured pending context4/46,42 assertions ->46/46 |

All five targeted normalized fixture files are byte-identical between their individual clean reds and the final W81 archive. This is **not** a claim that the entire suite stayed unchanged across16 commits: additional groups were added and the retained popup boundary was corrected at9d09cb2c4236def9d3dadaa4ed2c4370418fb932 to model the real HasPendingEquip prerequisite. Fixture-normalization corrections at e6a1c2c2 and d77507bf precede their assertion-level red evidence. Full-file transcription corrections1cb478c8 and e91a08b7 remain in history and are not separate gameplay improvements.

R01's provider has a private readonly false gate. It is contained, not a validated route implementation. R02 yields acknowledgement pulses so the deadline is re-evaluated and defers after an expired submission without an eligible recipient/tool; it does not count deferral as quest success. R04 captures the AutoEquip player reference/GUID and rejects invalid run/world/combat/disposed contexts without restoring an unowned cursor. R05's recovered change adds only the successful local-request flag. R06's recovered change is not same-NPC menu-generation or final atomic selection proof.

## R08 — complete reward owner and final choice-set admission

Clean test-only red94c1f0c7: integrated35488434087/art10597869062, **2/18;16 intended assertions;0 unexpected**, other16 integrated entries pass.

Production461bf601322e4f41260bb3da8dfba0d9023083f3 changes only `Bots/Quest/Actions/ActionSelectReward.cs`. It captures player reference/GUID and shown quest, rechecks the complete link/count choice set after scoring and diagnostic callbacks, and validates original-client reward panel, choice set, button type/ID and local selection in the final Lua request. Existing gear weights and vendor fallback calculations are unchanged. It does not complete the quest.

That first production commit failed host-dependent compilation: missing `using Styx` made StyxWoW unresolved (CS0103). Archived run35490089337/art10599106279 is retained as a compile failure, **not behavioural red or accepted code**. Correctiond72c5576 adds only that import; native diff verifies one line. Every regression fixture remains unchanged from94c1f0c7.

Final integrated35490438405/art10598957028: **17/17**, reward lifetime **18/18;0 assertions;0 unexpected**. Across1,828 indexed inputs, the only red-to-green changed input is ActionSelectReward.cs. All146 normalized members are byte-identical. Both complete reward red/green archives pass CRC and all190 inner-manifest hashes each.

Host35490438279/art10598314425: **build exit0;3,344 warnings;0 errors**; tests_run=false. Host ZIP passes CRC; it has four files and no inner manifest. Integrated and host both have game_attached=false.

Pinned original UI: wowgaming/3.3.5-interface-files@d0339b17b0221db76e6acd2dc2915d224a5b62ca, QuestInfo.lua blob8fe25c706d3123c431630b9e55557dc6ab2da383 and QuestFrame.lua bloba5802c3f2f1f56c22dd78b0b55916b3f9ce25491. QuestInfoItem_OnClick sets QuestInfoFrame.itemChoice; the reward template and panel distinguish choice selection from the later GetQuestReward request. No GetQuestID or later-client API was added. Lua button callback is not desktop mouse simulation.

Evidence limit: the18 tests compile the complete C# owner with controlled external observations and record its Lua requests. They do not execute the Lua in an original client. The shown-quest check in C# and final choice-set check in Lua are separate boundaries; same-identity ABA, native atomicity and a later quest-completion request are not certified.

## Retained final group results

AutoEquip context46/46; delete request14/14; gossip lifetime17/17; isolation containment9/9; reusable acknowledgement10/10; reward lifetime18/18; collection admission34/34; normal-objective restart37/37; Ret registration9/9; equip acknowledged timeout10/10; popup submission18/18; analyzer89 tests. These preserve each fixture's stated evidence level, not full gameplay acceptance.

## Remaining W80 gates

| Finding | Status after W81 | Remaining work before relevant approval |
|---|---|---|
| R01 | Contained | Keep provider disabled; actual route/deadline/context implementation remains deferred |
| R02 | Reviewed scenario repaired offline | Retain10-case continuation tests and server-acknowledgement limits |
| R03 | Open | Real loader/scheduler/materializer contract, explicit raw-slot/source mapping, target/location authority, reject undefined/unimplemented kinds, GameObject dispatch; never admit ordinary kills for cast credit |
| R04 | Partial | EquipItem lifetime plus physical displaced-item/foreign cursor and same-slot popup proof; no restoration based only on different entry |
| R05 | Partial | Current plugin/player context, scan continuation after refusal/ack, and unknown inventory versus confirmed deletion; preserve protection checks |
| R06 | Partial | Same-NPC menu generation and final option/cleanup ownership across the final request |
| R07 | Open | Actual compiler dependency/reload inputs and all-or-nothing construction/publication; avoid another broad plugin framework |
| R08 | Local selection scenario repaired offline | Native Lua/client acceptance and later completion ownership remain separate limits |

Next implementation should remain within these unclosed gates. Prefer bounded, actual-owner regression tests and narrow fixes or explicitly justified containment. Do not restart the already-completed140-commit review, recreate recovered fixes, remove valid guards to satisfy source-token tests, or treat dormant/unimplemented Gordunni/escort work as functional. Keep all prior source/provenance, terrain/Z/LOS, buffs, loadout/caps, underwater, remount and independent/live acceptance requirements.

Before any merge: reconcile direct master and PR head, close or explicitly contain all W80 blockers with meaningful evidence, inspect final integrated+host artifacts and merge preview, preserve a backup ref, and use exact-head conditional merge. A source checkpoint merge is not a live deployment. No merge action is authorised by W81's present partial status.
