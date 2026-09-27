# W109 — ground-cast refusal propagation verified

27 September 2026. Continued the original PR51 request through the interrupted W104–W108 work and the additional W109 local receipt defect. The identified evidence-backed source repairs are implemented and hosted-verified. This checkpoint does not establish original-client/server/native, independent or supervised acceptance and does not authorize merge.

## Exact source and publication chain

Repository `JeofW/cbp`, id1367174964; PR51; only branch `audit/next-55-equipment-observation-20260917`. Workspace `D:\Dev\CopilotBuddy-PR51`; external evidence `D:\Dev\CopilotBuddy-Evidence`. Master remains `b2324913e2499ba30b239dd67224ca2c655c05cc` at successful source publication readbacks.

| Stage | Commit | Tree |
|---|---|---|
| Validated W108 predecessor | `917eacedb425c5a12dc7f1d5c7ab9ed0760f00bd` | `b021097dd4cf32a1f58ea9c78d3caf8a59f884f3` |
| W109 behavioral red/test-only | `f7c52cfd2b6face06ff82cbb190b15a739cd286d` | `24990eb8c80cf12157e173cfe108fd5e349a5bd7` |
| Final validated production/source | **`ae7e3f2325be14ea155f76167e31d25c9b2da51a`** | **`63ab966c90531d1e8c2cbb544259ada7b1074b74`** |

`W104_REDGROUNDRECEIPT_PUBLICATION_20260927.json` and `W104_GREENGROUNDRECEIPT_PUBLICATION_20260927.json` verify each expected parent, exact changed paths, Git blobs, full decoded remote bytes, tree and direct refs. The historical W104 prefix is required by the retained publication helper; these are W109 transactions. The existing119-line fixture was published once and was not recreated or changed through the production repair.

This containing commit publishes only the three new W109 checkpoint/evidence/scope paths. Its documentation SHA/tree are recorded externally by `W104_GROUNDRECEIPTDOCS_PUBLICATION_20260927.json` and LATEST_CONTINUATION.md, separately from tested source. The prior refused W108/root-document publication is not replayed or relabelled successful.

## Proven defect and repair

The complete CastOnGround factory in `runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs` placed the bool-returning SpellManager.Cast call inside TreeSharp's void ActionSucceedDelegate. That delegate discards the return value and reports Success. Consequently, even when the actual cast returned false after an earlier CanCast check, the sequence proceeded to pending-cursor observations and could reach a terrain click.

The repair changes one action to return `RunStatus.Success` or `RunStatus.Failure` from the actual Cast receipt. A refused submission now stops that sequence before further cursor or terrain authority is acquired. Healthy immediate/delayed submissions and existing admission/range/area checks remain. The W107 single-observation pending predicate, wait/timeout policy, legacy terrain call, native argument/instruction sequence and all other owners are unchanged.

This is exactly **one added/one removed production line**, with the original UTF8 BOM preserved. It introduces no client offset, ABI, new state machine, request nonce or speculative gameplay rule. A false receipt may follow partial native dispatch; stopping the sequence does not assert that no earlier side effect occurred. A true local receipt is still not proof of exclusive originating-request ownership, terrain effect or server credit.

## Actual hosted regression and build evidence

| Stage | Integrated run | Observed W109 cases | Aggregate | Host run |
|---|---|---|---|---|
| Behavioral red f7c52cfd | 36314190682 | **8/11;3 intended assertions;0 unexpected** | 16/17; all builds successful | 36314190619 |
| Final ae7e3f23 | **36314573862** | **11/11;0 assertions;0 unexpected** | **17/17** | **36314573881** |

All11 individual cases were inspected in both archives. The three red failures were exactly refused-ready, refused-empty and refused-foreign. Immediate/delayed success, cannot-cast, unmet requirements, unsafe area, out-of-range, missing location and zero-range behavior passed. The fixture copies the complete tracked factory and existing pending-name helper and uses real TreeSharp; only backend/world observations are controlled. No harness error or compilation failure was called behavioral red.

Both integrated archives have **219 members,218 inner hashes and1856 captured source input hashes**, verified against their respective checkout. The comparison proves exactly one production input changed and **all174 normalized fixture members are byte-identical**. No assertion was weakened or skipped.

Final integrated job108606734318/artifact **10930541966**,949065bytes:

```text
SHA256 afd21e0af4c30833fd4f2f95aab0074fae795430b21cf8515437626e028d029f
```

Final host job108606733948/artifact **10929769116**,82206bytes:

```text
SHA256 a0fcdc786fe435acda2e65766c3e6cb0498dd1f86ce3f5f2388d2af6adfaef20
```

The host runs are Release/x86, **0 errors/3344 warnings**, `tests_run=false`, `game_attached=false`. All archive hashes match GitHub metadata. W109_EVIDENCE.json retains the four run/artifact inspections, all case lines, comparison and exact publication receipts, including failed baselines.

## Prior repairs retained in the final run

| Coverage | Final result |
|---|---|
| W104 equipment cleanup/lifetime | 57/57 |
| W103 synchronous pending index and physical GUID guard | 24/24 and8/8 |
| Equipment Lua boundary and continuation | 87/87 and46/46 |
| AutoEquip and EquipItem context | 37/37 and57/57 |
| W106 pre-existing equipment acknowledgement | 18/18 |
| W108 default equipment physical-copy ownership | 54/54 |
| W105 autoattack, movement-facing and cast-facing | 26/26,85/85,16/16 |
| W107 native spell observations and pending predicate | 23/23 and6/6 |

The broader retained quest, mesh/flight/aquatic, gossip, protection and publication suites remain in the17-group integrated gate. The W103 same-dispatch full GUID guard, synchronous pending-index authority and return-buffer lock remain; W104 token/handler/registration continuity and timeout cleanup remain; automatic destinations retain W108's selected-copy and bind-checking route. These are controlled source/execution boundaries, not live all-class/all-quest acceptance.

## Remaining acceptance and documentation state

W109_SCOPE_STATUS.md records the remaining specific live/protocol/client/server/independent dependencies and deliberate bounded-scope exclusions. The earlier by-name limitation in the two pending owners was actually repaired in W108; W109's local Cast-refusal defect was also implemented rather than hidden under the broader ground-request limitation. No further evidence-backed source repair was identified in this recovered scope. This does not prove absence of every future defect or authorize speculative features to fill missing realm recipes or route evidence.

The four-path W108 checkpoint/root handoff publication received a provider safety-status refusal. Read-only reconciliation found no partial commit, intent receipt or staged changes; it remains unpublished and was not retried. W108's checkpoint and evidence exist locally, and this W109 manifest references their path/hash without embedding or republishing the refused document payload. The root handoff edits remain local. The independently authorized W109 test, production and new checkpoint transactions exclude those four paths.

Separate status reads were also refused without results; their exact outcomes remain recorded. Successful independent artifact operations supplied real validation evidence and do not rewrite those failed calls. No global permission/read-only diagnosis is made. The latest external handoff distinguishes tested source, published W109 documentation, retained local W108 documents and the exact historical refusal boundary.

PR51 stays draft/unmerged. No subagents, local project build/test, production CB access, IDB/executable mutation, force-push, master write, deployment, W80/W92 restart, PR58 recreation, excluded PR25 or weakened assertions/merge gates. Original3.3.5a/build12340, TC3.3.5 primary/AC secondary and all original provenance policies remain. Numeric preparation is **3historical/0new/0remaining**. Direct self-review is not independent acceptance. Final session-finish and readback outcomes are recorded externally after this commit without altering the hashed execution ledger.
