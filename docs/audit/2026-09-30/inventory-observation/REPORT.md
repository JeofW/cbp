# Complete carried-inventory observations for quest execution

The legacy carried-item helpers can return empty collections after failed reads and skip occupied slots whose objects are unresolved. A quest must not interpret that outcome as a complete item count. This independent continuation from PR66 adds a complete owned inventory observation and rechecks stock-sensitive work at publication and during continued execution.

## Resulting behavior

`QuestInventorySnapshot` publishes counts only after every supported carried slot and equipped bag, nonzero item GUID, owner/container identity, entry and quantity is read completely and checked again. Unknown results contain null counts. Bank and buyback slots cannot donate stock. The observer restores cache state, propagates stop/cancellation, bounds the supported layout, and rejects duplicate, partial, changed or foreign observations. A separate memory-owner regression fixed a race between selecting the reader and binding its Memory wrapper.

The source review found that the original client's trade item slots remain populated after closure. A first implementation using their nonzero GUIDs would have blocked valid inventory indefinitely. The corrected observer reads the dialog and server-session partner states separately: both must be closed. The server-status handler clears its partner only after terminal trade processing; closing the UI alone is insufficient. Stale closed-trade item GUIDs no longer block quantity observations. The retained failing tests prove both the stale-slot and premature-close defects.

Wholesome's live scan now passes these complete counts to its existing policy. A generated delivery pickup that needs carried stock, or a delivery/supplemental turn-in, receives a fresh-stock check at publication and continued execution. Losing a required item revokes permission; a fresh valid scan can recover. An acceptance source promise can still admit the appropriate pickup, but never becomes an inventory receipt at turn-in. Ordinary objectives and unrelated work do not acquire an unnecessary quantity requirement.

The debug-only, bounded, rate-limited rejection diagnostics include the inventory observation status. Existing relation, eligibility, history, navigation, recovery and execution ownership gates remain in force. Samples are not native transactions or proof against an intervening change-and-reversion.

## Evidence and tests

Enabled read-only IDA reverified the exact32-bit build12340 binary. GetItemCount, its enumerator and quantity reader, GetTradePlayerItemLink, CloseTrade, trade UI state transitions and the server-status callback establish the inspected client semantics. Pinned TC335/TDB335.25101 establishes the server reference behavior separately; it is not evidence that a customized realm completed a quest. Exact addresses, source hashes and retained receipt hashes are in source-contracts.json.

The final focused run passes59 inventory snapshot cases and14 inventory admission/publication cases, plus23 raw-publication,9 profile-acceptance,29 delivery and19 supplemental cases. Tests consume real allocated item/bag memory and real scheduler/profile/behavior owners; only fixed client globals are mapped into allocated test buffers. One pipeline runs source-bound load, scheduler pickup, generated profile, actual acceptance acknowledgement, inventory presence/loss/recovery, independent server-ready state, turn-in acknowledgement and next scheduling. No game was attached.

The initial API red had52 intended failures; the reader-owner red had1; the corrected admission red had14. Trade lifecycle tests exposed five snapshot failures and one admission failure before their correction. Earlier fixture compilation/stale-output failures remain explicitly excluded from behavioral proof. All300 analyzers pass with one expected Windows permission skip.

The precommit four-stage quest runner reproduces all258493 dataset and39 strategy checks with exactly the previous output hashes. The six classes remain2964generic/2strategy/932data/364source/16live/57scripted=4335, with1369 exact remaining IDs. No quest ledger membership, knowledge bytes, strategy recipe or classification changed, so the existing verified ledger is referenced rather than duplicated. Only9066/9447 remain vetted strategies. Final clean-commit local34-command and hosted Windows/x86 acceptance must name the actual containing commit; those receipts are external and are not replaced by this precommit comparison.

## Scope and continuation

This branch contains PR66's four reputation repairs. The separate58-contract/115-predicate condition worktree was recovered through its original supported commit request at09:14UTC: implementation814a01b47c554fff3e74ae10086a83ddf643e2aa and documentation candidate0722b9c4d7e2d958a23c0986e05864f2f6b68a0a. Its final local/hosted gates remain separate. It is not copied into this PR, and its1334-ID ledger must not be combined arithmetically with this branch's1369-ID ledger. Production remains deployed PR61 master3bc97e1e0b446aede269f7414c0c7c6358fdc192.

Continue the remaining availability inputs and exact source/data frontier after this scope's acceptance. The complete inventory observer supplies the needed foundation for carried-item conditions; bank, repeatable/seasonal, spell, daily, area and source-conflict families still require their own validated contracts. Simulation proves behavior for supplied observations, not live travel, inventory acquisition, interaction or completion.
