# Shared cooldown observations and cancellation

## Problem and scope

The October 1 live session submitted Divine Protection at 21:12:52.889 and again at 21:12:55.897, without retaining an authoritative cast/aura/cooldown acknowledgement. That incident led to inspection of the shared observation APIs before implementing an action reservation. The previous numeric readers converted missing Lua observations to zero, used `TimeSpan.MaxValue` for unavailable data, or returned the local 250-ms submission hold as a measured cooldown. Both positive and negative comparisons could therefore act on fabricated evidence. No retained log proves two successful Divine Protection casts or an acknowledged healing-potion use.

This scope repairs the observation foundation. It does not yet implement the pending heal/potion/defensive arbitration, declare quest10161 complete, change rotation priorities, or claim live acceptance. PR90 aura/liveness and PR91 collision work remain integrated. The source baseline is PR91 merge `c106a721612c4729d8a7b2d0e88c468c0ffa0be2`.

## Shared observation contract

`SpellManager` now owns a single marked cooldown query. It validates localized spell-name availability, finite start/duration/clock values, the enabled flag, protocol shape and representable durations before publishing a number. Actual Lua5.1 executes the query in the regression fixture, including malformed API inputs before arithmetic. Known zero, positive cooldown, known unusable state and unavailable observations remain distinct. Existing lag-tolerance and admission-backoff settings are retained.

The host, WoWSpell, Singular and legacy readers share that query. A numeric reader either returns the observed duration or raises the existing controlled `ObservationUnavailableException`; it never returns an admission hold or an unavailable sentinel that can satisfy a numeric predicate. The legacy millisecond API rounds a positive sub-millisecond observation upward and rejects an unrepresentable integer result. Invalid spell IDs cannot reach admission or native dispatch.

An observation retains actor reference/GUID/address, map, memory and executor identities, bot identity, actual TreeRoot worker identity, running state, spellbook/session epoch and monotonic clock. Replies and cached deadlines cannot cross a changed owner or supersede a later observation. Reusing the same bot/actor after Stop/Start still replaces the worker identity. Initialize, shutdown and spellbook changes retire the old deadlines. A known ready observation clears an old measured cooldown; the separate local submission hold remains an admission guard, never successful-cast evidence.

Deadlines begin when the query result is accepted. Starting a returned remaining duration before a delayed query could make it expire too early. Clock rollback relative to that completed observation invalidates the context. Cancellation and explicit Stop propagate through the observation checks.

Final dispatch review reproduced a gap between those readers and the actual native wrapper: a first observation could clear a just-submitted 250-ms hold because the wrapper had never established its observation owner. The wrapper now captures that owner before preparation, rechecks it before execution and publication, and preserves cancellation. A replaced actor/world/executor/run cannot receive the old dispatch's success or hold. A later observation by the same owner is retained when dispatch finishes; the older request does not clear its measured cooldowns. These checks still establish local dispatch only, not successful server action or a landed heal.

## Original-client evidence

Read-only IDA inspected the exact original 32-bit client SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. Retained decompilation of `0x807980` shows the existing five-argument thiscall cooldown reader: positive cooldown evidence returns true, but failed Spell metadata lookup through `0x4CFD20` also returns zero. A native zero therefore does not independently prove readiness.

The native ABI and original 680-byte Spell-row decoder are unchanged. Positive native cooldown evidence is retained. A native zero is confirmed by the complete localized marked query; unavailable confirmation stays UNKNOWN. Native cancellation or revoked context cannot fall through to a second query. This adds a query to the native-zero path; it intentionally avoids caching an unacknowledged zero. Runtime query cost remains a live acceptance measurement.

Primary client evidence is retained in `D:/Dev/CopilotBuddy-Evidence/action-acknowledgement-20261002/ida-spell-event-tables.json`, with the request text, client identity assertion and complete raw reply. Event string/registration evidence is also retained, but registration alone does not establish UNIT_SPELLCAST payload fields or prove a cast/heal landed. A further read-only investigation-script write was blocked twice before execution; no result is claimed for it.

## Cancellation and independent recovery

The actual `Lua.GetReturnValuesCore` and numeric conversion catches now preserve direct and reflection-wrapped cancellation/interruption via the existing shared helper. Ordinary unavailable transport retains its prior empty/default compatibility at that low-level API; cooldown readers use the explicit protocol to prevent that fallback from becoming zero.

The transport tests execute the complete tracked request/conversion owners with controlled allocation, memory, executor and cleanup leaves. They do not attach to the game or execute emitted native code. The existing equipment Lua bridge was updated only to reference the shared exception helper; its copied production conversion methods and assertions remain intact.

Additional actual Cast/Buff-region tests use real TreeSharp to prove unavailable admission can reach independent recovery before and after yielded setup, without dispatch or success bookkeeping. A later complete observation recovers. Cancellation must propagate without selecting that recovery branch. The existing TreeSharp behavior already satisfies this contract and was not rewritten.

## Deterministic evidence

All receipt directories below are under `D:/Dev/CopilotBuddy-Evidence/action-acknowledgement-20261002`. The final expanded cooldown/dispatch set contains 280 cases. `spell-observation-review-final-c106a721` passes all 280, all 152 Lua transport/cancellation cases, all 97 existing dispatch-receipt controls and all nine selected groups with stable source inputs. `test-receipts.json` retains 22 earlier and final receipts, their raw log hashes and exact reported assertions. These include overlapping evolving fixtures and are not additive counts of independent defects. Full local and exact-SHA hosted validation remain separate gates.

| Family | Failing-before evidence | Repair evidence retained so far |
| --- | --- | --- |
| Numeric/marked/Lua cooldown validation | `cooldown-observation-red2-c106a721`: 34/138 pass, 104 assertions, zero unexpected errors | `cooldown-observation-green1-c106a721`: all 138 and adjacent groups pass |
| Actor/world/executor/session/cache ownership | `cooldown-context-red2-c106a721`: 138/229 pass, 91 assertions, zero unexpected errors | `cooldown-context-green1-c106a721`: all 229 and 97 dispatch-receipt cases pass |
| Lua transport and numeric cancellation | `lua-cancellation-red2-c106a721`: 32/152 pass, 120 assertions, zero unexpected errors | `lua-cancellation-green1-c106a721`: all five selected groups pass |
| Delayed reply and clock rollback | `cooldown-delay-red-c106a721`: 229/232 pass, three assertions, zero unexpected errors | `cooldown-delay-green1-c106a721`: all 232 plus 152 Lua cases pass |
| Native-zero ambiguity, invalid IDs and legacy conversion | `cooldown-native-zero-red-resume-c106a721`: 234/261 pass, 27 assertions, zero unexpected errors | `cooldown-native-zero-green1-c106a721`: all five selected groups pass, including 261 cooldown cases and recovery integration |
| Same-bot worker replacement | `cooldown-worker-generation-red-c106a721`: 261/268 pass, seven assertions, zero unexpected errors | `spell-observation-final-focused-c106a721`: nine groups pass; preserved in the later final gate |
| Actual submitted hold and changed dispatch owner | `spell-dispatch-context-review-red-c106a721`: 268/279 pass, 11 assertions, zero unexpected errors | `spell-dispatch-context-review-green1-c106a721`: 279/279 pass |
| Newer observation during the same native dispatch | `spell-dispatch-nested-review-red-c106a721`: one expected cooldown assertion; all 97 independent dispatch controls pass | `spell-observation-review-final-c106a721`: all nine groups pass, including 280/280 cooldown/dispatch cases |

The real host lifecycle suite also passed with four additional initialization/shutdown/spellbook reset controls. The existing dispatch fixture now extracts the full added observation helper chain and supplies actor/map/memory/worker leaves; its original 97 assertions remain unchanged. Fixture compilation errors, the initial Lua-fixture normalizer error and an interrupted build whose terminal disappeared are preserved separately and are not counted as behavioral red evidence. All accepted focused runs retain stable source snapshots. Final source review was performed inline without a separate reviewer agent; no independent review is claimed.

## Deployment and continuing incidents

PR91 is now installed in production: `ReleaseData/Deployment-PR91-c106a721-20261002.json` records 434 verified payload files, 2,013 protected files preserved, and all 13 deployed runtime components compiled. This new uncommitted spell scope is not part of that deployment.

The earlier merged Wholesome aggregate timed out during Restoration Shaman construction. An unchanged aggregate replay, all 189 registration cases, a subsequent complete 34-stage local gate, exact quest closure and merged hosted checks passed. The original timeout and unusable-architecture dump remain an unexplained intermittent validation incident; no increased timeout, skipped test or speculative runtime fix was used.

Continue the actual shared action-in-flight contract and the enabled independent DrinkPotions plugin. Preserve its observed configuration and the separate existing emergency policy; do not infer a healing amount or potion-use acknowledgement. Source review also leaves the legacy raw global-cooldown list readers for a separate bounded-observation/liveness pass; this numeric spell-specific repair is not proof of their safety. Full quest10161 progress/turn-in, successor-node flight/ground choice, rest admission, death/resurrection and quarantine scope remain live/replay incidents. The exact 4,335-quest execution ledger receives no promotions from this work.
