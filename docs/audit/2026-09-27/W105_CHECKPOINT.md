# W105 — attack startup and cast facing verified

27 September 2026. Continued beyond the completed W104 equipment repair without subagents. Three proven shared combat defects are repaired, with hosted Windows/x86 behavioral red and green evidence. Interrupted-response recovery found the existing commits and results; it did not recreate them. The wider remaining questing/navigation and acceptance assessment continues.

## Exact revisions

Repository `JeofW/cbp`, id `1367174964`, draft/open PR51, only branch `audit/next-55-equipment-observation-20260917`. Workspace `D:\Dev\CopilotBuddy-PR51`; external evidence `D:\Dev\CopilotBuddy-Evidence`. Master remains `b2324913e2499ba30b239dd67224ca2c655c05cc` at current direct-ref reconciliation.

Validated source/test revision: **`500251fc20904b803aed40028c26edc1c8c5da94`**, tree `e8ef9959a01f39106bd295fa1e229e88a548e997`. Its parent is the production repair **`19ac34d3566f94408296b6a1196b02f27f5da943`**, tree `2b932189b9ca0adfba574d4ec06ce44d09d155cf`; the final commit changes two test files only. Behavioral red: **`538a46c130dda9c37d9c90090e92183042b2f8cf`**, tree `d057906e60b15974ad1719fdc01863d4089ab8df`.

W104 validated equipment source `6cbabba99ee523cbf2e0902f5b6964027edc410e` and documentation `5ce1faa4251540027d77d055648d6b2d99c2a8ec` are retained. Read `W104_VERIFIED_CHECKPOINT.md` for its exact history; earlier validation holds are historical, not the present implementation status.

## Production changes and evidence

**Autoattack startup:** `Common.CreateAutoAttack` previously observed inactive managed state then called `LocalPlayer.ToggleAttack`, whose Lua request was `AttackTarget()`. If attack became active before the client request, the startup action could stop it. The owner now requests `StartAttack()` while retaining group eligibility, Auto Shot exclusion, pet dispatch and fall-through behavior. The explicit legacy toggle method is unchanged for other callers.

Live read-only build-12340 IDA analysis found `AttackTarget` body `0x51A650` calls `0x72C2B0`; its active-state branch calls stop helper `0x6E1660`, also used by `StopAttack` body `0x51D0B0`. `StartAttack` body `0x523090` checks native active/auto-repeat state before invoking the attack path. Entrypoint locators come from the retained original-build map, with names/xrefs and bodies corroborated. Registration-table byte and further function-xref requests were provider-refused and were not replayed; do not claim those missing results or infer a new ABI from Hex-Rays prototypes.

**Target then face:** `Movement.NeedsOffTargetCastSetup` returned false as soon as the intended recipient became CurrentTarget. Its setup action returned failure immediately after retargeting, making the later facing branch unreachable under ordinary stable observations. Admission now includes an unfaced stationary hostile current target when movement is enabled. Selection is evaluated once by the action; target selection, facing and readiness remain separate steps. Existing friendly/self/casting/manual-movement exceptions remain.

**Face during cast:** `Spell.WaitForCast(bool,bool)` created `Movement.CreateFaceTargetBehavior()` inside an Action and discarded the returned Composite. The repaired Sequence owns and ticks that child after the existing casting, wand, channel and latency checks. A no-turn result still keeps the active cast wait successful; normal fall-through after casting/latency remains. This fixes the shared call path, including its retained 57 `WaitForCast(true)` callers, without rewriting class rotations or spell priorities.

Only three production paths changed: `runtime-snapshot/Routines/Singular wotlk/Helpers/Common.cs`, `Movement.cs`, and `Spell.cs`. W103's synchronous pending-index guard/physical GUID comparison/return-buffer lock and W104's cursor ownership/timeout changes remain untouched. The audit reviewed retained Ret rotation, shared Spell/Common/Movement, LevelBot combat and Targeting; no unsupported DPS coefficients, threat model or target-priority change was added.

## Actual hosted validation

| Stage | Integrated result | Interpretation |
|---|---|---|
| First test `675f787b`, run36303095404 | 16/17; movement80/85 | Autoattack extraction carried unmatched region trivia and did not execute. Not autoattack behavioral red. Retained without concealment. |
| Corrected red `538a46c1`, run36303455985 | 16/17; autoattack21/26, movement80/85, cast-wait7/16 | 19 intended assertions, zero unexpected errors in these three groups. All project builds succeeded. |
| Production candidate `19ac34d3`, run36303897365 | 15/17; all three new groups pass | An old compiled wiring test still searched for ToggleAttack; one unrelated publication stack-provenance assertion also failed. Not aggregate green. |
| Validated `500251fc`, run36304366416 | **17/17; autoattack26/26, movement85/85, cast-wait16/16** | Actual complete aggregate and unchanged new behavioral assertions pass on hosted Windows/x86. |

Final integrated run **36304366416**, job **108577948896**, artifact **10926403153**: archive926788 bytes, SHA256 `2310125276d58d288469b7b179a9f7769717998c5e68a87f2e669ae2e4b63d8f`, matching GitHub metadata. All214 members/213 inner hashes verified; all1851 source hashes match the current checkout. The controlled harness runs no attached game.

Final host run **36304366440**, job **108577949026**, artifact **10925679514**: Release/x86 build **0 errors,3344 warnings**, `tests_run=false`, `game_attached=false`. Archive82599 bytes, SHA256 `40e086d06bbcca6210c99805e856db77177057c3f66831b16f0f0300b0e4ebd5`, matching metadata.

Retained W104 groups in the final integrated artifact: cleanup57/57, equipment Lua87/87, pending-index24/24, physical GUID guard8/8, AutoEquip context37/37, EquipItem context57/57, continuation46/46, acknowledged timeout10/10 and popup18/18.

The final two test-only changes adapt the compiled group-safety wiring lookup to the actual startup dispatch and add the actual exception text to the publication failure diagnostic. The guard-before-dispatch predicate and negative controls remain. The publication assertion still requires `ProfileBuilder.WriteProfile` in the exception stack. **Its earlier intermittent failure is not claimed repaired:** the candidate did not retain the underlying exception and the later successful predicate does not diagnose why it failed. Further production changes are deferred unless actual failure evidence supports them; the improved diagnostic preserves the next failure's details.

All three new behavioral fixtures remain identical from corrected red through the production repair and final test adaptation. Across all169 normalized members, two changed in the final revision: the unrelated publication fixture's diagnostic and its normalization manifest. Across1851 recorded source inputs, exactly the three production files and two test-adapter/diagnostic files differ. Do not claim the entire red/green artifact or every fixture is identical.

## Custody and limitations

`W105_EVIDENCE.json` records fresh archive/hash/input verification, all eight retained stage archives, publication receipts and native receipt hashes. The historical W104 prefix of the publication helper is retained in `W104_REDCOMBAT_PUBLICATION_20260927.json`, `W104_REDCOMBATFIX_PUBLICATION_20260927.json`, `W104_GREENCOMBAT_PUBLICATION_20260927.json`, and `W104_COMBATWIRING_PUBLICATION_20260927.json`; they belong to this W105 work. Each successful publication has exact parent/path/blob/full decoded-byte/tree/direct-ref readback.

`W105_AUDIT_20260927.md` retains the investigation and exact provider refusals. A fresh health/hash read after interruption still identified `WoW.exe`, base0x400000, `WoW-12340.i64`, input SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. These are input/analysis facts, not proof of pristine release provenance, native execution, server acknowledgement or installed gameplay acceptance.

This is a direct self-review and hosted controlled-code result, not independent review or supervised acceptance. No project build/test, game launch, production CB access, executable/IDB mutation or subagents were used locally. No pending-index ownership, asynchronous packet correlation, maximum DPS, all-class live behavior or every-quest support is implied.

## Continuation

Continue from this validated source without repeating W104/W105 repairs. Assess remaining R06 first-menu attribution, explicit R03 recipe/raw-slot limitations, by-name equipment limits and unresolved quest/navigation acceptance against current evidence. Preserve the R01 containment/R02/R05/R08 repairs, R07 bounded refresh policy, all mandatory acceptance gates and original 3.3.5a/core/provenance policies. Do not restart W80/W92 or add speculative functionality. This checkpoint does not mark the wider goal complete.

Only ordinary approved-branch publication with expected-parent and exact readback; no master write, force-push, PR58 recreation, excluded PR25, merge or deployment. Numeric preparation remains3 historical/0 new/0 remaining. The containing documentation commit's final SHA is recorded externally in its publication receipt and `LATEST_CONTINUATION.md`; never substitute it for the tested source.
