# W42 bounded continuation: accepted quest observations

Base: post-merge handover 0e2c092b23514ffd530a9d14d0270a9b1dcca0d7; master remains 8382a7ec05a64212ea0a237159dca427a0767425. Preserve PR35/36 and their combined c14bd264 evidence; no merge/deployment authorization.

## Evidence and intended contract

QuestLog.GetAllQuests drops null PlayerQuest.FromId materializations. The raw occupied descriptor remains nonzero. GetQuestCompletionSnapshot also uses materialization as acceptance authority, allowing historical completions to override an accepted but unhydrated quest. Wholesome SellByQuality and QuestScheduler.ScanAndRefresh consume the lossy list; ready-quest tracking can mistake its shrinkage for a turn-in.

Keep the legacy list API compatible, explicitly documented as materialized-only. Add a shared snapshot exposing raw occupied IDs, optional materialized quests and separate identity/metadata completeness. Capture raw original-12340 descriptor state before and after materialization with read caching disabled, pin player/memory/descriptor/GUID, reject changes, duplicate or invalid IDs, and propagate cancellation. No sleep or invented metadata. Revalidate observations at caller publication/dispatch boundaries. Missing metadata is unknown, never abandonment or permission to sell.

## Ordered work and gates

1. Test-only commit: linked full production QuestLog/PlayerQuest and exact existing sale method, controlled memory/cache/merchant. Confirm raw-slot loss, incorrect completion fallback and sale authorization; distinguish absent-new-API assertions from defect reproductions.
2. Implement the owner snapshot, completion acceptance guard, sale and scheduling adapters; add actual scheduler guard tests and adversarial state sequences. Preserve ordinary empty/hydrated behavior.
3. Run affected Windows x86 and combined suite on committed repaired source. Inspect artifacts, hashes and embedded commits. Lua unchanged requires no new Lua semantic claim; retain previous executable 5.1 evidence.
4. Self-review and record scope/limitations, graph links, focused stacked PR, durable resume and fresh-chat prompt. No merges, native promotion, meshes, installed changes or full-audit-completion claim.

Snapshot comparison is a bounded observation/revalidation contract, not atomic server acceptance or proof against undetectable ABA between observations. Native sale batch acknowledgment, full profile execution identity, session lifecycle, owner protection and remaining matrix rows stay separate unless specifically verified here. The original full census and corrected hypotheses are retained, not regenerated.
