# Recovered quest fixtures: entry and ownership reconciliation

Baseline for this continuation: 96652a10a0950721ab91d6b8957c9be57cf58ed9. Its run 34753740187 compiled all eleven owners; Pickup observation was 5/10 and Wholesome inventory/pickup was 14/16. Later module-initializer groups did not execute because the earlier groups threw. The previous 42ed7c6d Wholesome run had a missing-using build error; it is NOT behavioral reproduction evidence.

This test-only preparation calls every recovered group through one aggregate entry per executable. Each group's assertions and error reporting remain; a failing group cannot hide the following group. Two existing saved groups from d9bba18e54d1fba9bfe220ec456a80ca958887ad are retained (missing-offer evidence and full inventory materialization).

One draft PickupObservation assertion conflicted with the saved MissingOfferEvidence design: it expected an unrelated offer-list change to reset confirmation while the requested quest remained absent. That recreates the reported endless-retry problem. The intended key is requested quest + NPC + failure reason; order, duplicates and unrelated offerings remain diagnostics, not a new absence. The draft assertion is changed explicitly before repair. Wrong-quest-shown evidence keeps its distinct existing contract; no loaded-list assumption is made for an unknown dialog.

The temporary preparation workflow may be green only when exactly the two expected test owners compile and fail, all other owners pass, and all five group markers are present. It stages only hash-verified test-source blobs, not production fixes or references. Inspect actual failures before source repair; do not describe its expected-red success as a passing bot.
