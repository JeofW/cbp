# W110 — native aura lifetime repaired and hosted verified

28 September2026. Current available-evidence source work is verified. Frozen historical transactions and external acceptance are not declared completed by this checkpoint.

## Exact source and repair

Repository **JeofW/cbp**, ID1367174964, PR51; branch **audit/next-55-equipment-observation-20260917** only. Tested source and last production change: **67632376f4c75acd134b0e72e2c84ee1ea5ae90b**, tree **8a170978bc2c4e48878edb185a5f4a11e590b615**. Test-only baseline: **d8d236d7f90371767f5d7eaf6c4c72dba9d11bdb**, parent previous documentation **38ec2c2f4846a800b0cc6535b1c97863d71deb3f**. The earlier tested support source **a43c04a7** and all its repairs were preserved, not recreated.

The actual original-build UnitAura output uses the signed duration field at aura+0x10 and expiry at+0x14. The host instead used the legacy name `AuraFlags.NoDuration=32` to return infinite lifetime. Timed flagged buffs could therefore remain covered forever, and actual permanent coverage could look expired. The real Paladin blessing selector depends on that lifetime observation.

The repair changes **one expression** in `Styx/Logic/Combat/WoWAura.cs`: `HasNoDuration` now tests `unchecked((int)Duration) <= 0`. Two comments clarify the historical enum name and native field. No enum value, public interface, structure layout, timer/clock subtraction, blessing priority, PallyPower assignment, native dispatch or other source owner was changed. It is not an external effective-strength observer or an all-aura redesign.

## Full regression-first gate

| Stage | Actual result |
|---|---|
| Test-only baseline | **41/96 pass;55 intended assertions;0 unexpected**. All17projects compiled, only the new aura group failed, other16groups passed. |
| Minimal repair | Exact one-file publication from the verified test parent;3insertions/3deletions including comments. |
| Current targeted green | **96/96;0assertions;0unexpected** using complete actual aura properties and blessing selector, not a configured TimeLeft mock. |
| Current integrated | **17/17groups**, all builds/runs successful; game_attached=false. |
| Artifact/source integrity | **1865/1865checkoutsourceinputs,226innerhashes,227archive members** verified at exact tested source. |
| Frozen fixture comparison | **182normalizedmembers unchanged**; no added/removed inputs; only WoWAura.cs differs from behavioral red to green. |
| Release/x86 host | **0errors/3344warnings**, compile-only; tests_run=false,game_attached=false. |

The96cases cover duration-field versus flag independence, positive/zero/signed-nonpositive values, expiry and unknown clock, all four normal/Greater blessing families for own/external casters, active/expired Shout, same-snapshot expiry progression, and retained admission/layout/enum compatibility. No existing assertion was weakened, and no harness failure was counted as behavioral red.

| Hosted artifact | Exact identity |
|---|---|
| Red integrated | run36379186962,job108791185917,artifact10951354045,995173bytes; SHA256 `7e8a6a7ee865979d220385e15ce3c8716a625386ee1894059cdda10194a1f452` |
| Green integrated | run**36379680563**,job**108792657174**,artifact**10952595538**,995178bytes; SHA256 **`70b6af66fa4c7599cec81df65aeb4e9d135717ad98d479d270a4dd46e0fb8478`** |
| Green host | run**36379680530**,job**108792656817**,artifact**10951788373**,83283bytes; SHA256 **`d40459124aa2cb8eba6a14c22eaf18f7e39e9057d752271972136d3669925776`** |

Both new final workflow conclusions were returned, and downloaded archives matched GitHub artifact digests. Exact red/green receipts, inspections, all target case lines, retained summaries and comparison are in the accompanying evidence manifest. The red host also passed; its exact receipt is retained, not substituted for behavioral verification.

## Earlier W104–W110 work remains verified at this source

Current artifacts retain world-read68/68, address37/37, world8/8, metadata165/165, AutoEquip51/51, dispatch97/97, terrain46/46 and W109ground11/11. Support selection/revalidation63/63 and regularsupport44/44 pass. Default-equipment54/54, cleanup57/57 and publication21/21 remain, alongside the other physical-cursor, Lua, pending-owner, movement, navigation, quest and refresh controls listed in the complete actual summary set.

The old450f1fe1 integrated artifact was not obtained or retried. Its unchanged repair had already passed in a43c04a7 and is again exercised in this genuinely new source revision. That is independent current-source verification, not retroactive execution of the original refused watcher/download or normalized comparison.

## New native evidence and active Goal rule

Read **W110_AURA_NATIVE_CONTRACT.md**. Current IDA tools/list and hash-bound survey established a usable original-build read/analysis path without invoking the frozen server_health transaction. New API references, decompilation and actual duration instructions substantiate this narrow repair. Pinned TC335/AC AuraDefines sources corroborate duration/optional-amount flags but do not identify the realm or provide a generalized effective-amount field. No IDB/executable, client process or gameplay was mutated or launched.

Read **W110_ACTIVE_GOAL_AMENDMENT.md** before older generic refusal instructions. For NEW exact safety-status refusals, the user authorizes at most3identical total attempts, without rephrasing/fragmenting/rerouting, with ambiguous mutations reconciled first. Historical explicitly frozen operations stay frozen. Five new operations in this native/source cycle succeeded on an unchanged later attempt; six earlier refused attempts are preserved exactly. No causal diagnosis or execution of those refused attempts is inferred. Subsequent publication/finish outcomes belong to separate final records.

Core read/exec/edit/finish are exposed and operational. Initial calls were filed as Unattributed, which the connector explicitly did not equate with missing permissions. No project or connector-permission changes were made to restore tools. The current schema exposes no saved-Goal editor; the prospective amendment is durably overlaid and published, not falsely represented as an edit to the CoS UI task text.

## Full saved-goal disposition and finalization

**W110_AMENDED_GOAL_DISPOSITION.md** covers every original objective and references the existing full Ret/seal/ten-class/blessing/Greater/aura/PallyPower/racial/defensive, core-category and remaining-scope reports without restarting them. The newly demonstrated lifetime defect is fully repaired; no additional active-path defect is demonstrated by this bounded native review. Remaining exact inputs are current client proc/rank/usability and effective aura observations; actual realm/database recipes/raw-credit contracts; healthy ground/gossip origin/lifetime; supervised routes/physical inventory/server effects; and independent review. **DensePullIsolationValidated remains false.**

The historical combined native-map assembly and other specifically refused status/comparison/package/final-verifier/finish operations remain incomplete and unreplayed. This new scoped aura contract and publication do not replace those outputs. A successfully closed source/evidence task under the original Goal's outcomeB is not blanket gameplay support, independent acceptance, merge readiness or a claim that every requested frozen deliverable exists.

Only new aura/amendment/disposition documents are published. Both intentional root edits and all original policy/protection/exclusion gates are preserved. No subagents, local project build/test, productionCB access, native/IDB mutation, force-push, master write, merge/deploy, W80/W92 restart or PR58/25 recreation occurred. Numeric preparation remains **3historical/0new/0remaining**.

The containing documentation commit is not a new tested source. Its exact parent/path/blob/remote bytes and current PR/source/handoff verification are recorded after publication. Actual `session_finish` follows only the genuine new completed checkpoint; **HELD is not RELEASED**, and this document does not predeclare release or final delivery.
