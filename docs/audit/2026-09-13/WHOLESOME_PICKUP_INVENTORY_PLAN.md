# Wholesome pickup and carried inventory edge continuation

The owner uses Wholesome as the primary quester. Baseline d62f2c7b73305087dd23f45d8e881ae38d3ab3e7. Existing collection planning already checks carried stack totals by item ID, including alternative creature/object sources; the existing pickup policy waits for unknown dialogs and excludes a loaded missing offer after three distinct confirmations. Do not replace these working contracts or claim they are absent.

New source defects: the missing-offer evidence string depends on the order/duplication of the same offered IDs, preventing a stable rejection episode under reordered observations. Both the pickup tracker and Wholesome monitor reject only the immediately repeated cycle; an older cycle replay can count again. A stale different dialog may also reset current evidence.

Test-first: ten actual policy/tracker cases plus sixteen Wholesome scheduler/monitor cases. Canonicalize only the offered-ID evidence representation; retain the original immutable ordered UI snapshot. Ignore older interaction cycles before mutating a tracker; explicit lifecycle reset and new Wholesome attempt generations still permit restarted numbering. Preserve unknown-dialog waits, current target appearance, real changed evidence and inactive-work ownership.

Inventory cases are expanded verification of existing behavior, not twelve newly fixed collection bugs. Server quest completion, scripted objectives, stale inventory snapshots, item protection and every live quest are not certified by these cases. An independent inventory-protection ownership issue (one CollectItemObjective disposing a shared runtime item protection) requires a separate lease/refcount design without changing existing Add/Remove set semantics. It is recorded, not silently marked fixed here.

No new Lua, client offset or native dependency. Keep original 3.3.5a adapters and staged review. No merge/deploy.
