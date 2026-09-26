# W89 continuation record — R03 scheduler reconciliation and managed lifecycle coverage

20 September 2026, Asia/Kuala_Lumpur. This checkpoint records exact-source offline evidence; merge gates remain open.

## Final exact-source verification

Tested source and last production commit **67cdec20d5ac7b7bc9ae64e9de633252f1ad248a**, tree **1b73cba2afaa5ddb9efe58964a3bc302f8f65827**. The production change here is only goal text; the functional scheduler repair remains040a274d. Final integrated run35512279345/art10605223925: **17/17 entries**, scheduler28/28 and constructor/lifecycle12/12, zero named assertion failures or unexpected errors. Final host run35512279263/art10606002826:exit0,3344 warnings/0 errors, compile only. Both exact-source archives were inspected, not inferred from green badges.

The goal-text red7f344c81-to-green67cdec20 pair keeps all155 normalized members byte-identical. Among1837 recorded source inputs, only GossipEvent.cs differs. This is separate from the071d/040 scheduler pair and the766/716 fixture-isolation correction. No assertion was weakened to obtain green.

Direct master last reconciled **b2324913e2499ba30b239dd67224ca2c655c05cc**, not the cached PR base. PR51 remains draft/unmerged. The following Markdown/JSON pointer commit is documentation only and is not claimed as another C# run. Integrated/host workflow path filters do not select Markdown/JSON-only pointer updates. W89 supersedes W88 only as the current frontier, not its retained history.

## Reconciliation and authorship

The supplied 071d3d8b handoff was stale. Initial live head was 76673b22, then moved to 716e7f6b during review. Retained, rather than recreated, the production scheduler repair 040a274d and the constructor/diagnostic/isolation commits 3bf9c9a2, 76673b22 and 716e7f6b. Those four commits were not authored by this continuation. W88 root pointers remained stale while those changes arrived.

This continuation recovered and verified the existing red/green artifacts, reviewed the scheduler and fixture paths, and published the test-only extension ae82b6de7ce31b68d3564fbce02f1ae21f143c77 on the explicitly approved PR51 branch. Its parent is 716e7f6b3976c9ba09726a78aee5a489f4367970 and tree is 45b6f03544c607bd32c795b178d6508659d75e9f. Only QuestStrategyConstructorDispatchRegressionTests.cs changes (+120/-9). Its published blob 4166cc5f6fccfbb57daaa354ed778f1c401dc343 matches the locally reviewed UTF-8 bytes. No scheduler repair was duplicated. A later, separately reproduced one-line GossipEvent goal-text correction is described below. No force update, master write, merge, deployment, installed-file replacement or Work/Codex switch occurred.

## Recovered evidence — separate the boundaries

The original Windows x86 R03 run35509383498/art10604384676 at071d3d8b records scheduler12/28,16 intended assertions,0 unexpected; other16 integrated entries pass. The navigation-mutation cases had not reached navigation in that red run.

The scheduler-only repair040a274d has integrated run35510085553/art10604774990:17/17 and scheduler28/28. All154 normalized members are byte-identical between red071d3d8b and green040a274d. Among1836 recorded input paths, only QuestScheduler.cs differs. The unchanged navigation assertions now reach their callback and reject stale publication. This is the matched scheduler red/green pair, not a claim that the original red had exercised navigation.

The added constructor fixture at76673b22 exposed a different problem: run35510632685/art10605321443 has constructor0/6,6 assertions,0 unexpected, while scheduler28/28 and other16 integrated entries pass. Actual compiler diagnostics name duplicate host types from earlier dynamically loaded shadow fixture assemblies. The production compiler enumerates loaded assemblies. The716e7f6b test-only child-process isolation does not alter production compiler reference selection. Its run35510941191/art10605616495 records constructor6/6 and17/17 integrated entries. This is an isolation correction, not a production compiler fix. The716 fixture stops at construction: no OnStart, branch tick or native action.

Each recovered ZIP was checked against the published outer SHA256, CRC and every inner manifest hash:198 hashes each at071d/040,199 each at766/716. Integrity verification is not runtime certification. Source identities and result counts are retained in W89_EVIDENCE.json; complete integrated result tables remain in the original archives in the downloadable bundle.

## R03 production review

040a274d captures the loaded strategy pack for the scheduler and XML builder, preserves the public no-pack materializer API, and admits only a dataset-bound, implemented whole-quest strategy. Dataset objective identity is retained; it is not renumbered into a raw-counter slot. Unsupported or unmapped work is deferred at objective level, preserving independently valid work. CAST without a supported recipe never becomes ordinary killing.

The v1 schema supplies dataset ObjectiveIndex but no validated raw-counter mapping. Therefore ObjectiveProgress strategies remain conservatively deferred; this change does not implement or prove a raw-counter mapper. Creature UseItemOn with supported state and GossipEvent with a bounded declared option can use QuestComplete. GameObject item protocols, unimplemented Escort and BelowHp without a source threshold remain unsupported. No recipe, coordinate, offset or success condition was guessed or silently changed.

## Newly added managed lifecycle coverage

The original262-line scheduler fixture remains byte-identical, blob c49325edb941ea1f187c80c8cfe60b39a6942822. The existing isolated constructor fixture was extended, not duplicated: six original constructor scenarios plus six lifecycle scenarios for UseItemOn/GossipEvent at dataset indexes0,3,17.

Lifecycle scenarios use the actual emitted XML, CodeNode, runtime compiler, ForcedCodeBehavior wrapper, real OnStart/OnTick and real Composite branch. Existing allocated descriptor observations supply the player/quest log; player health uses the repository's WoWUnitFields enum, not invented offsets or a replacement getter. They assert one start, missing-recipient wait without an attempt, unrelated packed counters not completing whole-quest work, a raw ready transition stopping the body, and a fresh wrapper not repeating completed quest work. Compiler references, behavior properties and generated XML are not rewritten to make assertions pass. Batch/text state is restored and wrappers disposed.

This does not exercise a matching recipient, actual item/gossip submission, native acknowledgment, full QuestBot driver execution or original-client acceptance. A native executor is explicitly absent. The first new lifecycle run35511732664/art10606091788 atae82b6de records12/12,0 assertions/0 unexpected and17/17 integrated entries. Host35511732675/art10605142008 exits0 with3344 warnings/0 errors; compile only. These results are separate from716's constructor-only evidence.

## Additional goal-text regression and narrow repair

The actual ae82b6de lifecycle logs exposed a UI-only defect: GossipEvent displayed ` + quest.Name + ` literally. Its triple-quoted C# string did not concatenate the quest name. The initial starts-with assertion was insufficient for that defect. Test-only7f344c819890ba96318a0fccc401ca44101c732f strengthens goal-text equality without changing production or the twelve scenarios. Run35511972000/art10605118585 reproduces9/12,three intended GossipEvent assertions and zero unexpected errors. Scheduler28/28 and the other16 integrated entries remain green. The three red cases stop at the title assertion; their later lifecycle behavior is not thereby reproved by that red run.

Production67cdec20d5ac7b7bc9ae64e9de633252f1ad248a, parent7f344c81, changes only that quote-concatenation expression plus a terminal newline in GossipEvent.cs (+2/-2 in Git statistics). The strengthened fixture is not changed. All menu/actor/lifetime/acknowledgment code remains byte-for-byte outside that expression/EOF. The new blob9ec3346672a5d6c42a3e35284241e7b585e8808a matches the reviewed bytes; an initial local comparison differed solely because the original file lacked an EOF newline, which was resolved before any ref update. This is not another gameplay/scheduler repair. The controlled quest cache supplies an empty name; the exact-title test catches the literal-expression bug, not a localized/nonempty-name matrix.

## Remaining gates and next task

R03 remains open for source-proved dataset/collection/raw-counter mapping and actual recipient/request/acknowledgment integration beyond missing-recipient managed dispatch. Do not enable ObjectiveProgress from a coincidentally matching index; prove the mapping through loader, scheduler, XML and actual behavior boundary. Preserve supported whole-quest work and isolated unsupported work.

R04 physical displaced/foreign cursor, same-slot popup and full lifecycle; R07 actual compiler dependencies/static-state reload/atomic refresh; and R06 native/original-client acceptance remain. The named W88 changed-menu repair is not proof of same-content close/reopen generation identity or bridge concurrency. All W77-W88 fixes, W87 separate unchanged-fixture pairs and limited R05 scope, W77 documentation add/revert, W87 duplicate-fixture add/remove history, provenance and prior quest/buff/gear/navigation/native acceptance remain retained.

Keep W88's failed intermediate restart compile and boundary-only adaptation, matched8cc504ca-to-f6ee23de evidence, and W87/W86/W80 records. Do not restart W80's completed140-commit review. Never merge the intentionally red W88 reference branch or PR57 capability-test branch.

Original WoW3.3.5a/build12340; TrinityCore3.3.5 primary/AzerothCore WotLK secondary. Scope Wholesome questing/navigation/Singular and related safety. No AuctionHouse/ProfessionBuddy expansion, desktop mouse simulation, guessed offsets or installed-file replacement. All four governing policies and HANDOFF_POLICY were read from pinned live source.

Conditional merge authority remains, but all final gates are not cleared. Before an eligible merge reconcile direct refs, retain backup, inspect an exact merge preview and exact-source integrated/host results, then use an exact-head conditional merge. Do not ask for authorization again or equate merge with deployment. Every mutation needs the explicit nonempty approved branch, expected parent/blob and reviewed content/message, force=false.
