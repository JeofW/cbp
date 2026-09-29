# W110 final source pass — retained execution and corrections

The approved branch was recovered at documentation `9e61f0328a8af610c4e3ff257860ec0578ac5d7f`, following tested source `f12c57267fa071305085915f4ec2a2bb9e52c144`. Interrupted work was inspected before changes. Its six existing regression families and four production owners were preserved rather than recreated. Current published source is `a3c01d48e887745d3b0e478e4018b01cad4f1edf`; only its 24 reviewed source/test paths were staged. The remote parent, tree, path set, blobs and full decoded bytes were verified, with master unchanged.

## Local implementation intervals

The eight red/green intervals and exact counts are in the checkpoint and evidence manifest. Existing phases are MOVEMENTRED/GREEN, CONTROLRED/GREEN, CASTLUARED2/GREEN, HEALRED/GREEN, CASTOWNERRED2/GREEN and ITEMOWNERRED/GREEN. The additional intervals are GROUNDOWNERRED/GREEN3 and SHAREDOBSRED/GREEN2. Each actual behavioral baseline compiled and failed on intended assertions, with no unexpected errors. All changes were validated locally before the single meaningful source publication.

The source and caller review covered the actual shared Movement/Spell implementations, WoWUnit casting getters, UseItemOn lifecycle, Ret Normal/Instance/PvP factories, Paladin support, interrupt/autoattack callers, generic Escort and Wholesome profile/scheduler admission. New defects triggered review of their adjacent decision boundaries. The 55-row map retains the broader quest/navigation/support evidence and explicitly limits unsupported materializers, native request ownership and physical-route claims.

## Compiler and fixture failures retained separately

`CASTLUARED` failed because of a missing logging import in the dynamic fixture; it is not behavioral red. `CASTOWNERRED` stopped in normalization because an embedded class literal collided with the normalizer's boundary; the corrected fixture preserves that normalizer contract before its actual red. `GROUNDOWNERGREEN` and `GROUNDOWNERGREEN2` passed the new controlled cases but failed full Singular compilation on the WoWPoint namespace. The correct existing `Styx.Logic.Pathing` import yielded GREEN3. `SHAREDOBSGREEN` passed all 109 new cases but full Singular compilation exposed the LocalPlayer-only IsWanding extension; retaining the concrete captured player type yielded GREEN2. None was reported as an all-green source.

The first broad local gate completed **15/17**, with the host compile passing. It remains a failed aggregate in `W110_FINAL_PASS_LOCAL_GATE_20260929`. Three extracted ground fixtures needed type/capture context for the actual changed factory; their assertions and real pending observer remained. The separate complete-factory suite supplies the newly required behavioral coverage. `GROUNDCOMPATGREEN` subsequently passed all six requested groups.

Two quest tests correctly refused a stale generated UseItemOn copy. Its SHA256 matched the retained pre-repair ITEMOWNERRED source exactly. The generated file was backed up, then only that development test layout was refreshed from current tracked source. Both fixture overwrite-refusal guards stayed intact. `QUESTLAYOUTGREEN` passed the five lifetime/constructor/item/publication groups; the source, backup and resulting byte identities are retained in the layout receipt.

The legacy Singular dispatch fixture previously constructed an uninitialized compiler-generated closure, which cannot supply initialized actor/recipient captures. It now runs the complete tracked factories with the same process-owned descriptor/cache pattern already used by the retained quest-publication fixture. It opens no game process and creates no executor, and it restores globals and frees its own allocation. All 16 cast assertions and all 13 routine-boundary assertions remain. Unknown-spell factory admission and separate real backend refusal are accurately distinguished; late dispatch/false-receipt continuity is covered by the 136-case complete-factory suite. `ROUTINE_ACTOR1` passed full Singular compatibility.

The first new pair verifier mistakenly wrapped the PowerShell 5.1 parsed JSON array in another array. It reported a source-mutation error although all four relevant before/after manifests were byte-identical. The correction assigns the parsed array before enumeration, validates scalar path/hash values and checks the expected input count. The corrected verifier confirms both later intervals, unchanged regression assembly digests and only Spell.cs changing. This is an evidence-helper failure, not production or test behavioral red. Its exact diagnostic is retained.

## Final validation and publication

The corrected full local Windows/x86 gate is **17/17** with host build exit 0 and all 1892 working source hashes stable and rechecked before publication. The retained boundary extraction was verified against all eight unchanged source inputs and generated hashes; its old extraction commit remains named rather than relabeled as a new extraction. The local source identity is the base commit plus exact dirty-file hashes until publication; the hosted identity is the actual published source commit.

Hosted integrated `36466136560` is **17/17**, artifact `10989179321`, with 251 members / 250 inner hashes / 1892 source inputs verified. Hosted Windows/x86 `36466136368`, artifact `10988963899`, compiles with zero errors. Exact archive digests, current source comparisons, retained case summaries and warning counts are in the evidence manifest. No unchanged CI was rerun merely for activity, and a documentation-only successor does not revalidate a different production source.

The source changes do not enable dense-pull retreat, new realm recipes or new native APIs. Seal/support/equipment/quest protections and all acceptance gates remain. No game/server/runtime/route/independent acceptance is inferred from controlled passes.

## Operation-specific refusals and protocol

One new source/test-memory search batch received the exact provider uncertainty refusal on all three identical attempts. It has no output and remains stopped/unverified in `W110_FINAL_PASS_SEARCH_REFUSAL_20260929.json`; it was not reconstructed. The independent test-layout operation received two identical refusals and then succeeded on the third identical call; the successful mutation was not replayed. `W110_FINAL_PASS_LAYOUT_RETRY_20260929.json` and its layout receipt retain those outcomes. These outcomes do not establish global read-only/disconnected status. Historical do-not-replay operations remain frozen at their original identities.

The prior incomplete CLR-crash evidence remains a separate historical runtime diagnostic in `../2026-09-28/W110_CONTINUOUS_CLR_CRASH.md`. The managed assertion/fixture failures in this pass do not diagnose that crash, and later green does not explain it.

The extended Goal was not closed by the earlier green/checkpoint/finish attempts. This pass continues it through the new source work and final validation. Actual completion-stage CoS results are recorded externally in `W110_FINAL_PASS_FINISH_STATUS_20260929.json`; a checkpoint does not predeclare RELEASED. New authorized unresolved work extends the same Goal. No subagents, merge, master write, force push, production CB access, native/IDB mutation or numeric-budget renewal occurred.
