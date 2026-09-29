# W94 — plugin refresh preparation verified offline

26 September 2026. JeofW/cbp ID1367174964, existing open/draft PR51, approved branch audit/next-55-equipment-observation-20260917. Direct work without subagents. Full audit and the active Goal remain in progress.

## Change and exact source

Test-only red ca750bb535e8785fe6ea7391e3d3ed54c712a38c follows W93 documentation46c5b27e. Repair01bd1eeb1d545da9436cdb3bab155b1183a84b6d, tree08c2c56959f6911be3c898dde8ef66ca7c174109, changes only Styx/Plugins/PluginManager.cs. The containing documentation publication is a successor, not another tested source.

Refresh previously returned early for empty/missing external plugin directories, keeping stale plugins and discarding newly constructed built-ins. It also called replacement Name getters after retiring the old set and publishing the replacement. A throwing getter therefore left an altered active set and leaked rejected candidates.

The repair lets valid empty discovery complete, evaluates names and enable decisions before old lifecycle callbacks, reuses those values, and disposes unpublished candidates on preparation failure. Previous enabled instances remain untouched when preparation rejects a candidate. Existing compile-error/initial-load policy and source-cache behavior are retained.

Six new regressions call public RefreshPlugins with its actual SourceCompiler/cache and generated plugins with lifecycle counters. The fixture requires a previously absent Plugins path in hosted test output, does not overwrite an installation, and restores manager state. Cases cover missing/empty discovery, metadata rejection, retry of unchanged cached types, compiler rejection, and healthy case-insensitive selection. No substitute compiler or game process is used.

## Observed red and green

- Red integrated36221752016/job108348192411/art10899805076: all builds succeeded, aggregate16/17; new cases2/6,4 intended assertions,0 unexpected. Host36221752017/job108348192529/art10898713293:exit0,3344 warnings,0 errors.
- Green integrated36222082415/job108349108903/art10899970169: aggregate17/17; new cases6/6,0 assertions,0 unexpected. Host36222082537/job108349109412/art10899269794:exit0,3344 warnings,0 errors.
- Existing cached construction7/7, fresh compilation8/8 and source reuse9/9 passed on both. W93 Lua33/33 and retained reward lifetime18/18 passed on both.
- Runs actually executed on GitHub-hosted Windows/x86. Host is compile-only; tests_run=false. All game_attached=false. No local project build/test.

Both integrated archives were downloaded and their outer digests matched GitHub metadata. Each has202 members,201 verified inner hashes and1839 source inputs. The only changed source input is PluginManager.cs; all157 normalized fixture members are byte-identical. Host archives have4 members and no inner manifest. No separate ZIP-CRC verification claimed. Full hashes, sizes, receipts, source identities and results are in W94_EVIDENCE.json and D:\Dev\CopilotBuddy-Evidence\W94_RED_GREEN_COMPARISON_20260926.json.

## Review, remaining work and next task

Direct source/fixture self-review found no additional defect in this preparation delta. It is not independent acceptance review. This proves preparation rollback for the named managed failures; it does not prove arbitrary plugin side-effect rollback, successful native integration, dependency invalidation, static reset or concurrent lifecycle atomicity.

Next exact task: reproduce actual PluginContainer activation failure leaving Enabled=true and OnDisable failure skipping Dispose. Exercise its public setter and RefreshPlugins integration with lifecycle counters before choosing a focused repair. Preserve W94's six cases and W89/W93 fixtures. Default-context type reuse currently creates fresh instances while retaining static state; do not promise dependency reload or reset merely from a source fingerprint.

R03 W90 mapping obligations were reconciled with W91/W92. Ordinary matched-credit/publication and interaction-lifetime repairs remain completed. Strict v1 has no declared raw-counter mapping; source-backed credit identity independent of action recipient plus native request/ack evidence is still required before progress recipes may be enabled. Keep ObjectiveProgress deferred, QuestComplete admission, CAST refusal and independent collection. No guessed schema/slot/recipe.

R04 physical displaced/foreign cursor, same-slot popup and lifecycle; R06 same-content menu generation/native concurrency; R07 actual compiler dependencies/options/static reload/full refresh; R08 shared native/client/server and all retained Wholesome/navigation/Singular/independent/supervised gates remain open. No claim that all remaining items are blocked or complete.

## Publication and retained constraints

Ordinary fast-forward Git publication used only the approved PR51 branch after identity/parent reconciliation. Remote parents, changed paths/blobs, whole trees and direct head were read back. Source-only pushes used documented process-scoped GIT_LFS_SKIP_PUSH=1 after checking no LFS/attribute changes, with prior environment restored. Historical provider refusal records are unchanged; no blocked combined request was replayed and no new refusal occurred. Numeric API-preparation budget remains3 historical/0 new/0 remaining.

Retain W93 (R08 numeric receipt/UTF-8 length plus repository-name guard fixes), W92/W91/W90/W89/W88/W87/W86 and W80 evidence/ledger. Do not restart W80's completed140-commit review or W92. Read HANDOFF_POLICY.md and WOTLK_335A_RESEARCH_POLICY.md, TRINITYCORE_335_COMPATIBILITY.md, QUEST_DATA_PROVENANCE_335.md, ADDON_EVIDENCE_335.md. Original WoW3.3.5a/build12340; TrinityCore3.3.5 primary/AzerothCore WotLK secondary. Preserve R01/R02/R05 dispositions, navigation mutation, MIR lifetime, dense-pull containment, auction withdrawal and prior failed intermediates.

No local project execution, production access, weakened assertions, PR58 recreation, excluded PR25, force-push, master write, merge or deployment. Direct master remains b2324913e2499ba30b239dd67224ca2c655c05cc; stale PR base metadata is not authoritative. Checkpoint every verified slice and retain an external dated UTF-8 handoff.
