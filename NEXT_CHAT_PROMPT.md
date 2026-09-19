@GitHub

Continue `jeofwong/CopilotBuddy-private`, draft PR51 on `audit/next-55-equipment-observation-20260917`. Reconcile live refs and read W69 checkpoint/evidence first.

Verified checkpoint: **0640174f5b128ea466059c30493702a00307a478**, tree **6e2fca4209af9be109907e1ce5d902d303359040**; integrated **35423774281/art10578017430** and host **35423774279/art10578222288** pass.

Retain, do not recreate: bounded authoritative UseItemOn + source-bound strategy execution; dense-pack pull isolation (12/12) with normal Ret Exorcism opener and no taunt/melee close; aura-count allocation guard (8/8); CollectThings breath-budget fix (7/7); existing PallyPower read-only v3.2.21 Wrath bridge; quarantined addon hints + WorldMapArea candidate XY conversion; existing equipment observation/hand/reward identity tests.

First next slice: investigate the log-backed Roslyn OOM in repeated `PluginManager.RefreshPlugins`. `2026-09-12_1232_48388.log` has multiple successful refreshes followed by OOMs in `MetadataReference.CreateFromFile`. Current source recompiles plugins into unique `Assembly.LoadFrom` default-context assemblies that are not unloadable. Obtain a clean intended red before repair; prefer unchanged-source compilation reuse/content fingerprinting over speculative collectible-context changes unless type/lifetime evidence justifies them.

After that, design authoritative GossipEvent submission/acknowledgement lifetime before wiring it into Wholesome strategy execution. Do not wire Escort yet: current recipe lacks explicit start interaction, completion mode/destination, item/timer start semantics and bounded reacquisition facts.

Keep all broader W69 open requirements explicit. Do not merge PR51 without user approval.

For GitHub polling/stream failures, recover automatically from exact head SHA + run ID with short one-shot reads and bounded retries; do not wait on long polling and do not ask the user to say “retry” for ordinary connector timeouts.
