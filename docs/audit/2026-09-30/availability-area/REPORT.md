# Checked current-area observations for quest availability

This slice starts from validated PR70 `034c75d9fe93f27103133341345647eaaefe92af`. Quest215 has a pinned primary condition excluding area99. The new complete area observation feeds condition23 without treating its numeric identity as a quest ID, parent zone, map ID or localized text. All61 prior contracts are unchanged; the bundle now has62 contracts and127 predicates.

## Behavior and evidence

QuestAreaSnapshot binds the current player/Memory wrapper and raw object/descriptor GUIDs, reads the original client's area/map fields completely, and checks the bytes and owners again. Zero, partial, changing or foreign observations publish null area. It restores the caller's cache setting and propagates cancellation/interruption. Existing LocalPlayer compatibility properties remain unchanged. The new API only reads memory; it adds no native function dispatch.

Type23 evaluates equality with unknown-preserving negation and participates in the existing complete OR-of-AND groups. A current area is sampled at live pickup admission and again at availability publication/execution. Unknown or changed observations cannot authorize a negated condition. Accepted objectives and turn-ins retain their separate ownership. Debug-only bounded diagnostics identify the required area, observed area and exact final rejection. Inventory, source promises, prerequisites, recovery, navigation and authoritative reward acknowledgement remain separate.

Pinned TC335 ConditionMgr.cpp:279-280 establishes the reference area predicate. Read-only IDA verified the exact32-bit build12340 binary and existing area0xBD0810/map0xBD088C fields, separately from parent zone0xBD080C. The GameUI updater0x5204C0 receives separate zone/area inputs; area lookup0x5167E0 and teardown0x529160 corroborate that identity and its unavailable state. Raw receipt hashes are in source-contracts.json. An initial IDA helper failed with a Python comprehension-scope NameError; the corrected read-only helper succeeded, and both receipts remain external.

## Validation and exact accounting

The retained observer red/green tests and26 current snapshot cases cover exact owners, zero/partial/changed reads, different map and parent-zone identities, currentness, cache restoration and cancellation. The area runtime test first produced18 intended failures out of23 scenarios with zero unexpected exceptions. All23 now pass, including source-bound loader, actual area transport, scheduler/generated profile, acceptance, inventory loss/restoration, independent ready state, turn-in reward acknowledgement and next scheduling. Seven focused runtime groups pass.

The source exporter originally produced3 intended failures; the area receipt validator produced1. The first full analyzer run additionally exposed a test setup referencing absent quest2 in a supposed valid mixed group. It was corrected to the fixture's actual quest201 and an explicit absent-reference rejection was added. All324 analyzer tests now complete with zero failures and one expected Windows permission skip. That test setup correction does not count as a production defect.

The actual dataset sweep passes259206 checks across4335 unique rows, plus39 strategy cases. Exactly5 cases were added for quest215, none removed: unknown/matching/other area and the two required availability-publication/fixture checks. All721 availability checks pass. Quest215 alone changes from SOURCE-UNCERTAIN to GENERIC-PROVEN. Exact classes are3003 generic,2 strategy,932 data,325 source,16 live and57 scripted =4335, with1330 remaining IDs. The same totals in a sibling ledger do not establish the same IDs; keep lineage and hashes distinct until reviewed integration.

The original dataset, every unrelated repair, and recipes9066/9447 are unchanged. The strategy repair hash is rebound to the new knowledge bytes. The retained dirty-tree run supplies comparison fixtures only; the final containing commit still needs full local and hosted Windows/x86 acceptance at its exact SHA.

## Continuation

There are63 remaining availability contracts, including subject source conflicts, carried/bank item, per-ID daily and spell evidence. The newly recovered combined integration is published separately as PR71 at3c0b6c7a19144946415871ef83b4509a4a506bfe; it includes inventory and four reputation repairs. This area lineage contains PR67/70 and has not imported that sibling. Integrate only validated scopes and regenerate combined ledgers, never add sibling totals. Canonical master remainsb91c548b until a later verified merge; production stays deployed PR61 3bc97e1e. No real realm completion or scripted recipe is inferred by this slice.
