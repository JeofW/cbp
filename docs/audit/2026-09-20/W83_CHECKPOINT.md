# W83 — strict strategy kinds and safe materialization; merge remains blocked

20 September 2026. Repository `jeofwong/CopilotBuddy-private` (1367174964), draft PR51, branch `audit/next-55-equipment-observation-20260917`.

## Current verified state

**Tested code/test head: e518f4a32b3f4517c81a9b7c26b35d39452a102b.**
**Tree: a66eb9543df2b85c4cf980db25f11807bacfd3e9.**
The last production change is3856b39c45dc19d6296c740e2f565ac0d2bf0989; e518f4a3 changes one retained test expectation only. Documentation successors are not additional tested production revisions.

Exact-head integrated **35495595055/art10599609800** passes **17/17**; strategy Kind9/9, materialization16/16, retained gossip strategy14/14, all assertions0/unexpected0. Host **35495595061/art10600498568** has build exit0, **3344 warnings/0 errors**, tests_run=false. All game_attached=false.

Integrated ZIP SHA256: `defc998e34e213cf7865e03d57a106cfa8892fda5be4bd802be8335febe72a38`.
Host ZIP SHA256: `38a3522f3e17dba65596eb6512777c9861ec103517c89b2bd6cbd53e0884b121`.

The user conditionally authorises merge after all blockers and final gates are green. **That condition is not met.** W83 repairs parts of R03, not all W80 findings. Keep PR51 draft/unmerged. No master write, force push, merge, workflow change, deployment, installed-file replacement, new Lua API or new gameplay recipe occurred in W83.

Read the four governing original-client/core/provenance policies under docs/audit, W80's completed fixed-range review, W81 and W82. Original WoW3.3.5a/build12340; TrinityCore3.3.5 primary/AzerothCore WotLK secondary. Focus Wholesome questing/navigation/Singular and directly related safety. No desktop mouse simulation or auction/ProfessionBuddy expansion.

## Reconciliation — do not recreate earlier repairs

The live entry was **a46369ed83f96eb075f91a163924b21fc0c93834**, W82 documentation, not the older W80 checkpoint. W81 already contains R01 provider containment, the R02 reusable-item deadline, R04 AutoEquip context, R05 successful delete-request admission, R06 actor/NPC lifetime and R08 final reward-selection identity. W82 already contains strict cached construction83190e18 and actual fresh compilationc46ede2c. Those narrow results and remaining limits are retained.

The W82 entry archive35493042467/art10600005968 was opened and reverified: exact c46ede2c identity,17/17 entries, CRC and192 inner-manifest hashes. Its SHA256 is `d25814c70c7253f8a4809512992513a7a036cd08d4eca25b6bf7e7c9dbe5a961`. Do not restart the140-commit post18 September21:58 Malaysia review; fix the recorded blockers.

## Pair A — undefined numeric Kind could impersonate Escort

`QuestStrategyPackLoader.ParseRecipe` used Enum.TryParse for Kind without Enum.IsDefined. A numeric value outside the enum could enter the final Escort-shaped branch. Other recipe enum fields already used a strict RequiredEnum helper.

Test-only **8efc6592718b7f47b3eafdb82595a6fe97b66e2d** adds QuestStrategyKindValidationRegressionTests. It invokes the actual pack loader and DataLoader on source-bound temporary JSON. Controls preserve all three declared kinds as readable data, missing optional packs and a repaired-input retry on the same loader. Parsing an Escort declaration is not execution permission.

Clean red **35494127843/art10599987550**: **5/9,4 intended assertions,0 unexpected**; all16 other integrated entries pass. SHA256 `d0751cd1204892a7566846a0448f3e4e5b4a76ee536b9ee52042e13cf386b5dc`.

Production **5242d74ff25c62bc900aa15df9ea9e4b2750ad8d** replaces the three-line unchecked parse with the existing `RequiredEnum<QuestStrategyKind>(node, "Kind")`. Only DataLoader.cs changes; no new schema, executor or Lua API.

Green **35494389527/art10600137655**: **9/9,17/17**. SHA256 `887c1cd09eb7576751ab91245187d462aedf9c1bfd43280a4003397d45d0db47`. Host35494389570/art10599938125 exits0 with3344 warnings/0 errors; SHA256 `07b548d9110e992aa38173fdc45bbee23cee8cacac0860353aeaeec3aecd1d72`.

Both integrated ZIPs pass CRC and193 inner hashes. Their1831 indexed input paths are identical; only DataLoader.cs differs. All149 normalized members are byte-identical. This is actual loader execution with controlled files, not live quest execution.

## Pair B — unsupported actions and unrelated anchors could produce executable XML

The previous materializer searched supported kinds separately. A declared Escort or undefined in-memory kind could miss both searches and fall through to ordinary KillMob XML. UseItemOn also accepted an unrelated target or a GameObject identifier while borrowing creature-source hotspots. Matching an integer is not proof that two object namespaces or item-target protocols are interchangeable.

Test-only **c8604cd7bb716253e59ea9b0334bf73c27d586ca** strengthens QuestStrategyExecutionRegressionTests to16 cases. Two prior expectations that allowed Escort-to-legacy fallback or GameObject emission from a creature anchor were corrected before the production repair. Added cases cover target mismatch, matching-but-unimplemented GameObject protocol, unknown programmatic Kind and duplicate cross-kind owners.

Three cases traverse the actual DataLoader -> MaterializeSchedule -> ProfileBuilder path: declared Escort must not become kill XML; no-pack ordinary work remains byte-identical; CAST-credit work remains excluded rather than becoming ordinary killing. The controlled normal objective reaches a real plan before the rejection. This does not enable CAST strategies or prove native action dispatch.

Clean red **35494787101/art10600243144**: **9/16,7 intended assertions,0 unexpected**, all16 other integrated entries pass. SHA256 `241256cf154bd06b01f8f7ecf90600d95e9da0e8750741cf4164416ffa4fae8a`.

Production **3856b39c45dc19d6296c740e2f565ac0d2bf0989** changes only ProfileBuilder.cs:
- One exact quest/objective lookup establishes ownership before choosing an executor. Duplicate owners across kinds are rejected.
- Only the implemented UseItemOn and GossipEvent materializers are dispatched. A matched unsupported kind is rejected, not replaced by ordinary work.
- Generated UseItemOn is restricted to the implemented creature-target path. Its source objective must be a matching creature-source KillMob/CollectItem objective. GameObject/ground item recipes remain rejected until their protocol is established.
- Missing/unrelated recipes, valid matching creature XML, pickup/turn-in, ordinary GameObject collection, transport preambles and CAST exclusion are preserved. No handwritten behavior body changed.

The source diff is31 additions/43 deletions: duplicated kind-specific lookup is removed rather than adding a general strategy framework. Together the two production files have14 fewer lines than the W82 entry.

### Residual test contradiction was inspected, not hidden

At3856b39c, integrated **35495109983/art10600273632** has new materialization **16/16** and Kind **9/9**, but only **16/17 integrated entries**. The retained GossipEventStrategyRegressionTests group is13/14 with one unexpected InvalidDataException: its separate old Escort case explicitly expected legacy KillMob XML. SHA256 `2fdcf1eb86d89ae89cf0dfdfe853aa6b5306c862a6e5d6f2235f8bf8eb7ac2d4`. Host35495109990/art10600073782 passes compile; SHA256 `b42c4cd92c8623d792e654d97228ea0abd3f155fda0adc1a22bc6a95c05a3b46`.

Test-only **e518f4a32b3f4517c81a9b7c26b35d39452a102b** changes only that contradictory case to require InvalidDataException for unimplemented Escort. No production guard or valid gossip test is relaxed. Native diff confirms one case only; the count remains14. The exact final result is17/17 integrated,16/16 materialization,9/9 Kind and14/14 gossip strategy at the head above.

The c8604cd7 ->3856b39c production pair retains all149 normalized members byte-identical and only ProfileBuilder.cs changes among1831 inputs. It proves the targeted7 failures repaired but **is not an aggregate-green pair**. The3856b39c ->e518f4a3 correction changes one indexed test input and two normalized members (that test plus its normalization-manifest.json);147 normalized members remain identical, and all production inputs are unchanged. Do not mislabel the final whole c8604cd7 ->e518f4a3 span as unchanged-fixture verification.

## Retained final checks and evidence limits

The final archived run retains AutoEquip context46/46, reusable-item acknowledgement10/10, delete-popup request14/14, gossip lifetime17/17, reward-selection lifetime18/18, plugin cached construction7/7, plugin fresh compilation8/8, collection restart34/34, normal-objective restart37/37, Ret registration9/9 and acknowledged-equip timeout10/10. Python analyzers run89 tests,OK. The source-only/native-boundary limitations of those groups remain as recorded in W77–W82.

The evidence package verifies nine original CI ZIPs: outer digests/CRC, exact source identities, all inner hashes for six integrated archives (entry192, five others193), three host compile archives, two unchanged-fixture production comparisons and the explicitly changed-test correction transition. Host archives have no inner manifest and are not called test runs. The offline Python verifier additionally rejects wrong digest/commit/tree, an undisclosed failed group, an unlisted source change and a changed fixture mislabeled unchanged. Rechecking archives is not rerunning C# locally or running WoW.

## Remaining merge gates

**R03 remains partial.** Undefined Kind, unsupported materializer fallback and UseItemOn creature-anchor subcases are repaired/contained. The scheduler still does not admit CAST work through a fully verified recipe contract; raw normal-counter indices versus dataset/collection indices remain unresolved. GameObject/ground item protocols and Escort execution are explicitly not enabled. No Gordunni Cobalt recipe or guessed coordinates were added.

**R04 remains partial:** EquipItem actor/lifetime and physical displaced/foreign cursor/popup ownership. A different held entry at an empty source slot is still not physical ownership; preserve the existing timeout and successful-submission guards.

**R05 remains partial:** MIR plugin/player context, bounded scan continuation after refusal/acknowledgement, and unknown inventory versus observed requested removal. Do not redo the already-fixed successful-request gate or weaken quest-item protection.

**R06 remains partial:** same-NPC changed menu, final option selection and cleanup in the client request. W83 inspected current GossipEvent/GossipFrame/GossipEntry and retained lifetime fixtures but made no R06 code change. Existing actor/NPC checks do not prove a menu remained the same.

**R07 remains partial:** actual compiler dependency fingerprints, static-state/reload semantics and complete refresh publication/initialization lifecycle. W82's strict construction is retained, not a certificate for arbitrary plugin side effects.

R01 remains contained; R02/R08 retain their demonstrated local results and native-acceptance limits. Keep all prior prerequisites/provenance, addon/terrain, buff, gear/loadout, escort/event, aquatic, native UI/LOS/ABI and independent/live acceptance requirements. No new feature expansion before these blockers are resolved or explicitly contained with evidence.

Direct master was rechecked as **b2324913e2499ba30b239dd67224ca2c655c05cc**; W77's disclosed empty-doc add/revert remains historical. W83 did not write master. Every publication must use explicit nonempty approved PR branch, expected parent/blob, reviewed content/message and force=false. Before any authorised merge, reconcile direct refs, preserve a backup, inspect the merge preview and exact final validation, and use an exact-head conditional merge. A checkpoint is not deployment acceptance.
