@GitHub

Continue jeofwong/CopilotBuddy-private, draft PR51 on audit/next-55-equipment-observation-20260917. Reconcile live refs and read W68 checkpoint/evidence.

Verified code **0efaa29cdae649b2fb3c0eba2c2e31f5a3dce310**, tree **1eb88058a060339b30f01b6dc91738c49cf4c2cf**. W68 authoritative UseItemOn red a0074d13 -> production5a3fb22d + ordering485ff12 -> final 0efaa29cdae649b2fb3c0eba2c2e31f5a3dce310; integrated35413466475/art10574816771 and host35413466374/art10575620041 pass.

First next slice: reproduce and bound post-submission acknowledgement liveness. In authoritative mode, a consumed/missing item with unchanged quest progress must not wait forever. Preserve a finite window for delayed server progress, cancellation/lifetime checks, and never turn timeout into quest success. Legacy InvocationCount profiles must remain unchanged.

Only after that is green, wire source-bound UseItemOn recipes from QuestStrategyPack into actual scheduler/ProfileBuilder execution. Strategy bytes must participate in executable recovery identity, but absence of a pack must preserve legacy DatasetFingerprint. Generated profiles must explicitly set SuccessEvidence, ObjectiveIndex, MaxAttempts and all source-backed target/state/range/LOS facts. GossipEvent/Escort remain separate.

Keep every broader W67/W68 open requirement explicit.
