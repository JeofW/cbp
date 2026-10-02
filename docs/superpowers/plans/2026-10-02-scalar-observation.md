# Native scalar observation continuation

The user's shared observation/liveness requirement applies below the newly repaired Lua boundaries. Continue from preserved PR93 candidatedaf1afd9 in the isolated scalar worktree. Before publishing, reconcile the exact newer merged master and retain PR93's complete receipts and source.

1. Reproduce the actual Memory.Read/ReadBytes default path through LocalPlayer.MapId/CurrentMap and WoWObject.ObjectFlags/IsValid/IsDisabled. Controlled leaves are native byte transfer, process/world references and object addresses. Cover complete map0, missing/short/failed/overreported transfers, object/memory replacement, direct/wrapped cancellation, complete invalid types and genuine fatal process-handle loss.
2. Keep generic Memory.Read compatibility intact. Introduce a narrow complete-byte scalar boundary for these actual getters; unknown transfers cannot publish map0 or enabled/valid object flags. Preserve direct/wrapped cancellation, invalid-object controls, known zero, and the current map cache's correct ownership across construction callbacks. Inspect nearby scalar consumers for the same family without claiming all raw getters are repaired.
3. Run focused, full local/runtime compilation and exact4335 accounting, publish a scoped PR with exact-SHA Windows/x86 checks, merge only when green and validate/package/deploy exact merged master. Retain any fixture/behavioral failures and physical/live proof limits separately.

The source values and offsets are retained from the current original build12340 implementation; no native ABI or gameplay policy is introduced. This is a prerequisite to reliable action arbitration, not a claim that pending healing, consumable use, aura landing or quest execution is acknowledged.
