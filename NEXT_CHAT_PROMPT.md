@GitHub

Continue PR51 on `audit/next-55-equipment-observation-20260917` from W70. Reconcile live refs and read W70 checkpoint/evidence first.

Verified checkpoint: **2456369563478b1f0c11c902649d7b308169ddfc**, tree **90501a9a3233d9bd043621abf2ae5671c658cf05**. Integrated **35425658391/art10578078516**, host **35425658309/art10578774562**, quest-log **35425658361/art10578159821** pass.

Retain: source-bound UseItemOn, dense-pack isolation, aura-count guard, breath recovery, plugin refresh reuse (9/9), authoritative single-option GossipEvent (14/14), PallyPower bridge, quarantined addon hints, equipment observation/hand/reward identity. Escort remains unwired.

First next slice: native container slot identity in WoWItem.UseContainerItem. Current BagIndex=-1 means both backpack and unresolved container, and BagSlot reads BagIndex again. Publish the prepared test-only contract first and require a clean assertion red. Prepared test blob: **0970290ab69cc54bc8a39f98e8814f7f6c081420**. Prepared production blob **ffc12d34d2971c944ff32196f3fd1ede83361f5e** is NOT yet authorized. After core repair, audit UseItemOn and other authoritative/destructive callers so a refused safe submission does not become local success.

Then continue all-class stronger/exclusive buff policy, cap/loadout/reward evaluation, native cursor/LOS, GatherBuddy/full underwater, remaining nav/performance signatures and live acceptance. Do not merge PR51 without explicit approval.

For normal GitHub connector timeouts self-recover by exact SHA/run IDs with short reads and bounded retries; do not ask the user to say retry.
