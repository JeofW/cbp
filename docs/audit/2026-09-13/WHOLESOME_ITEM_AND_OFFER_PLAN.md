# Wholesome follow-up: evidence and inventory

Current owner request prioritizes the Wholesome quester, absent NPC offers, existing carried items and original 3.3.5a compatibility. Start from support repair 315929f7477a09d90c7a7ce9ca48799a9408ceed, retaining the previous checkpoint and all tests. The one-use source export is removed; ordinary tests do not need meshes.

Existing scheduler and CollectItemObjective already consult carried item identity/count. Do not claim that functionality was entirely missing. Add public materialization cases for alternate item sources, partial/absent/unrelated counts, live completion and inventory changes; distinguish regression coverage from a production repair.

New source-supported defect: QuestPickupMismatchTracker keys its confirmation lifecycle on the entire diagnostic Evidence string. An NPC can keep not offering the requested quest while unrelated offer order, membership or shown detail changes; every change resets the retry counter. Positively showing the target while its button is loading also leaves old absence evidence intact.

Test-first cases execute the actual policy and tracker. Stable confirmation identity for PickupTargetNotOffered should be requested quest + NPC + failure reason, not unrelated display text. Keep current raw evidence in the produced outcome. Preserve one count per distinct interaction, unloaded Wait semantics, changed-NPC/quest/reason isolation and existing strict wrong-dialog behavior. A positively identified target invalidates absence even before an Accept button arrives. No new client command, lower retry limit, unproved completion or automatic abandonment is introduced.

After intended failures are observed, implement the narrow tracker repair, execute existing/expanded suites and inspect the combined result. Record this source-reproduction separately from unproven attribution to old logs. Independent review and live acceptance remain pending; no merge/deploy.
