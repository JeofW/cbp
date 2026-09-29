# W107 — actionable source repairs verified; acceptance gates remain

27 September 2026. Recovered the interrupted request from durable W104–W106 work and completed the remaining identified native spell-reader and immediate consumer repairs. The currently identified evidence-backed implementation scope is complete, with hosted Windows/x86 evidence and explicit dispositions for remaining work. This is not full PR51 live/independent acceptance or merge readiness.

## Exact identity

Repository `JeofW/cbp`, id1367174964; PR51; approved branch `audit/next-55-equipment-observation-20260917` only. Workspace `D:\Dev\CopilotBuddy-PR51`; external evidence `D:\Dev\CopilotBuddy-Evidence`. Master remains `b2324913e2499ba30b239dd67224ca2c655c05cc` at each successful publication readback. No merge or PR-state mutation was requested.

| Stage | Commit | Tree |
|---|---|---|
| Recovered W106 documentation | `3d54d4172e40b06682a0ca3ef1768aa287ffd3ab` | `993119faff47d7329debb82f72e43dadfa0c7d1a` |
| W107 native behavioral red | `37ebf59f295f3216c2000bd791bef96fd099ba5e` | `ae7cdb28e53b3e856fe9811de1b982a64d5534ad` |
| Native reader production | `df3bbf83481e3b4c537d57f05d9aea2ee133ecd4` | `85e213451e5dbdda1cfe09f8cbb9f186e7e99efa` |
| Consumer behavioral red | `9f23968d24bf3b512187cb3ef6063e727978f04e` | `4646ea7a9189d0fc02119d5c1bb5131281d0de55` |
| Final validated production/source | **`0e5148366a9329b6f14c840bfd7df1edc1f4a5e1`** | **`9b0e804927aa9d10fa7a2229f83e9ebf822a6450`** |

Every source publication verified the expected parent, exact changed paths, Git blobs, decoded remote bytes, tree and direct refs. Receipts retain the historical publisher's W104 label prefix: `W104_REDNATIVESPELL`, `W104_GREENNATIVESPELL`, `W104_REDPENDINGWAIT`, `W104_GREENPENDINGWAIT`, each suffixed `_PUBLICATION_20260927.json`. These are W107 receipts, not repeated W104 work.

This checkpoint's containing commit is documentation only. Its exact SHA/tree are recorded externally in `W104_NATIVESPELLDOCS_PUBLICATION_20260927.json` and `LATEST_CONTINUATION.md`; never substitute that documentation revision for the tested source above.

## Repairs and native evidence

The original-client input remains `Build12340-IDA\WoW.exe`, SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`, base0x400000, with `WoW-12340.i64`. A fresh harmless live health/path/hash check succeeded before relying on the seven retained W106 native receipts; their hashes still match W106_EVIDENCE.json. The copied input identity is not independently verified pristine-release provenance.

1. **Pending-spell observation.** Native getter0x7FD630 loads the pointer at0xD3F4E4 and, when present, reads the spell ID at offset0x20. Its UI caller0x5198A0 passes the result to spell lookup. LocalPlayer previously used two unrelated direct fields0xCEC1CC/0xCEC1D0. The property now follows the evidenced pointer-relative layout, rejects a null pointer and arithmetic wrap, and preserves null-memory/nonpositive-ID/FromId behavior. The three existing HasPendingSpell overloads remain unchanged.
2. **Terrain result width.** The complete instructions at0x80C340 define only AL on success/refusal; higher EAX bits can still contain a nonzero pointer when AL is zero. ClickRemoteLocation now reads a byte rather than int from the executor result. Native call address, arguments, instruction sequence, lock/cache handling, allocation and exception cleanup remain unchanged. This adds no native ABI or function call.
3. **Immediate caller consistency.** CastOnGround checked one pending observation for null and dereferenced a second observation's Name. Disappearance caused a null dereference; replacement mixed two observations. Its predicate now uses the existing single-read HasPendingSpell(string) helper. This is one added/two removed lines and changes no cast, timeout, location, area-safety or terrain-request policy. A matching current spell remains an observation, not proof of request ownership.

Only three production paths change in W107: `Styx/WoWInternals/WoWObjects/LocalPlayer.cs`, `Styx/Logic/Combat/SpellManager.cs`, and `runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs`. The recovered165-line native regression fixture was preserved rather than recreated. The new93-line consumer fixture executes the exact wait predicate and actual existing name-match helper under controlled successive observations.

## Actual hosted red/green

| Source | Integrated run | Actual W107 cases | Aggregate | Host run |
|---|---|---|---|---|
| Native red37ebf59f | 36309824667 | **12/23;11 intended assertions;0 unexpected** | 16/17; all builds succeeded | 36309824725 |
| Reader green df3bbf83 | 36310352504 | **23/23;0 assertions;0 unexpected** | 17/17 | 36310352527 |
| Consumer red9f23968d | 36310694489 | Native23/23; consumer **4/6;2 intended assertions;0 unexpected** | 16/17; all builds succeeded | 36310694482 |
| Final0e514836 | **36311065568** | Native **23/23**; consumer **6/6**;0 assertions/0 unexpected | **17/17** | **36311065567** |

All four host runs compiled Release/x86 with **0 errors and3344 warnings**, `tests_run=false`, `game_attached=false`. The final integrated job108596961480 uses the GitHub-hosted windows-2022 runner and the verified x86 runtime. These are executed regression fixtures and compile evidence, not attached-game acceptance.

The native red failures were eight pending-reader cases and three false terrain results with nonzero high EAX bits. The consumer red failures were exactly disappearing and replaced observations. Both fixtures executed their full case counts; no harness error was treated as behavioral red. Both successful repairs retain every assertion from their corresponding red baseline.

Final integrated artifact **10928958282**,938241bytes:

```text
SHA256 922936a4abc247e3506e25e4d1ba50225ecc7fa65df99fac3aedc7b1757bf71a
```

All **217 members,216 inner hashes and1854 captured source input hashes** were verified against the final checkout. The actual full suite-log SHA256 is `38073c35d00960fc625221c76601eb6e0f64717b097f47588bd0f94d7e70322f`. Final host job108596961528/artifact **10928573791**,82334bytes:

```text
SHA256 7681e2ba3bc3a38ae597ac1650538ad07b9ba8fbb75337f57814161e855e6f65
```

Both archive hashes match GitHub metadata. W107_EVIDENCE.json inventories all eight archives, jobs, case logs, comparisons, publication receipts and native evidence, including the failed baselines.

The reader red/green pair has exactly two production-input changes and all **171 normalized fixture members identical**. The consumer pair has exactly one production-input change and all **172 normalized members identical**. The second pair adds a new fixture compared with the earlier stage, so the whole four-stage sequence is not described as one identical fixture set.

## Earlier work retained in the final run

| Retained coverage | Final result | Limit |
|---|---|---|
| W104 equipment cleanup/lifetime | 57/57 | Actual owners/Lua5.1; controlled cursor observations |
| W103 synchronous pending index / physical guard | 24/24 and8/8 | Lua events and emitted-instruction contract; no assembled client execution |
| Equipment Lua boundary / continuation | 87/87 and46/46 | Actual generated scripts/managed ownership under controlled boundaries |
| AutoEquip / EquipItem context | 37/37 and57/57 | Captured actor/runtime/quest controls |
| W105 autoattack / movement-facing / cast-wait | 26/26,85/85,16/16 | Actual shared owners/TreeSharp and controlled client effects |
| W106 already-equipped acknowledgement | 18/18 | Both tracked owners; controlled equipment observations |
| Gossip lifetime / Lua generation | 59/59 and63/63 | Post-capture continuity; no first-response origin proof |
| Quest strategy scheduler / restart | 53/53 and37/37 | Controlled supported recipes and observations |
| Mesh clear / move lifetime / move request / transit reset | 45/45,29/29,64/64,36/36 | Actual managed owners; no live terrain traversal |
| Public flight owner / aquatic shoreline | 83/83 and35/35 | Controlled route/world observations |
| Publication / isolation containment | 21/21 and9/9 | No speculative publication repair or enabling deferred retreat |

The original W10449-case baseline was recovered and actually executed; W104's final57-case suite added observer continuity coverage. W104–W106 were already completed during interrupted turns and were not recreated in this recovery. Their failed intermediates and the exact historical provider refusals remain retained.

## Review, completion and restrictions

Direct self-review inspected the complete eight-file production diff from W103 through this final source,191added/91removed lines including comments. Workflows remain unchanged. Rechecked W103's unchanged GUID guard and return-buffer lock, synchronous bind-index authority, equipment context/deadline guards, combat safety/settings and native allocation cleanup. This is not an independent review; no subagent was used.

`W107_SCOPE_COMPLETION.md` is the final requirement/disposition map. The currently identified actionable source repairs are implemented, hosted-validated and checkpointed. Remaining work has named live/native/client/server/independent acceptance dependencies or explicit bounded-scope deferrals: R06 first-response provenance, full ground-request ownership, by-name duplicate-copy/broader transfer semantics, disabled dense-pack retreat, unsupported recipe/raw-slot materializers, and general concurrent/transactional reload extensions. The earlier intermittent W105 publication assertion remains undiagnosed pending actual recurrence evidence; a later passing test is not a repair claim.

PR51 remains draft/unmerged, and all acceptance/merge gates remain in force. No local project build/test, production CB access, IDB/executable mutation, force-push, master write, W80/W92 restart, PR58 recreation, excluded PR25 work or deployment. Numeric preparation remains **3 historical/0 new/0 remaining**. Final session-finish and documentation readback outcomes are recorded externally after this containing commit, without altering the hashed execution ledger.
