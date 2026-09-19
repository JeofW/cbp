@GitHub

Continue `jeofwong/CopilotBuddy-private`, draft PR51 on `audit/next-55-equipment-observation-20260917`, from W71. Reconcile live refs and read `AUDIT_RESUME.md`, `docs/audit/2026-09-19/W71_CHECKPOINT.md` and `W71_EVIDENCE.json` first.

Verified head **5db0de566cea515a21f3706fd7835b82dda9737a**, tree **f9b8a5cbb14ebad0a419ac1a53ff3ab04dafbad0**. Integrated **35430867637/art10580233586** passes all17 entries; host **35430867668/art10580507599** is0 errors/3340 warnings. Production quest-log owners at prerequisite repair **16acaaac** pass **35430209827/art10580646500**.

Retain and do not recreate: safe GUID-based container use (9/9), bounded UseItemOn submission refusal, TC335 dependent-previous ordered OR (14/14), active-parent tests (32/32), negative-exclusive dependency tests (10/10), plugin refresh reuse, GossipEvent, PallyPower bridge, quarantined addon evidence and prior equipment/navigation/merchant work.

First next slice: pinned TrinityCore335 `8fda442f...` requires negative direct `PrevQuestID` parent status `QUEST_STATUS_INCOMPLETE`. Current scheduler merely checks accepted membership and therefore allows a ready/completed-but-unturned-in parent. Pinned AzerothCore WotLK `8337a378...` is broader. Existing snapshot already distinguishes accepted vs ready through `QuestSchedulerAcceptedQuest.IsCompleted`. Add a test-only TC-primary conservative contract first; require clean Windows assertion red, then repair without inventing core identity. Preserve the AC divergence as an explicit unresolved compatibility difference.

The live ForcedQuestPickUp already checks the actual gossip/native offered quest list and confirms target identity before AcceptQuest; missing class/reputation/condition data may cause travel but is not permission to accept an unoffered quest.

Then resume separate cursor ownership review (PickUp/Delete/Equip/AH/ProfessionBuddy); do not blanket-convert multi-step cursor transactions. Keep all W71 open frontiers explicit and do not merge PR51 without user approval.

For ordinary GitHub stalls recover from exact head/run IDs with short bounded reads; do not ask the user to repeat state or blindly rerun.
