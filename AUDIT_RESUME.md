# Resume at W68 — authoritative UseItemOn acknowledgement verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code **0efaa29cdae649b2fb3c0eba2c2e31f5a3dce310**, tree **1eb88058a060339b30f01b6dc91738c49cf4c2cf**. Read `docs/audit/2026-09-19/W68_CHECKPOINT.md` / `W68_EVIDENCE.json` plus W67 and the three 3.3.5 policy/provenance docs.

W68 adds opt-in authoritative UseItemOn semantics while preserving legacy InvocationCount default. ObjectiveProgress acknowledges only an observed descriptor count increase; QuestComplete only explicit quest completion. MaxAttempts bounds submissions but does not establish quest credit. Integrated35413466475/art10574816771 and host35413466374/art10575620041 are green.

NEXT: test/fix the post-submission liveness gap where a consumed/missing item plus no quest progress can leave authoritative mode waiting indefinitely. Add a bounded acknowledgement deadline distinct from MaxAttempts and success. Then wire source-bound UseItemOn strategy packs into scheduler/profile generation, with executable strategy bytes included in runtime/recovery identity while no-pack legacy DatasetFingerprint remains stable. Do not wire Gossip/Escort yet.

All-class buff strength, equipment caps/loadout/rewards, runtime Carbonite terrain, underwater escape, full GatherBuddy/rest/remount, native UI/slot/cursor/LOS and live acceptance remain open.
