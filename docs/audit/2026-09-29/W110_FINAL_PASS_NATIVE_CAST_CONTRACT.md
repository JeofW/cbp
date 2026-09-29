# W110 build12340 cast observation contract

The existing read-only cast investigation used the approved 32-bit WoW.exe input, image base `0x400000`, SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`, with the development database `Build12340-IDA/WoW-12340.i64`. This documentation preserves that scoped investigation; it does not rerun or reconstruct any older refused IDA health/combined-map operation. No game executable or IDB was modified. Raw receipt hashes and the retained identity evidence are listed in `W110_FINAL_PASS_EVIDENCE.json`.

## Registered native callers and return contracts

| API | Registration / function | Observed contract relevant to the repair |
|---|---|---|
| UnitCastingInfo | table `0xAD2560`, function `0x611DF0` | Resolves the supplied unit token through `0x60C1F0`; absent unit, missing spell record or expired cast has no successful tuple. Four strings precede start timestamp value 5 and end timestamp value 6. Trade flag is value 7, unsigned byte cast counter is numeric value 8, and the interruptibility-helper result is boolean value 9. Successful return count is 9. |
| UnitChannelInfo | table `0xAD2568`, function `0x612090` | Uses the same unit-token resolver and channel record/expiry checks. Start/end timestamps are values 5/6, trade flag is 7 and the boolean interruption field is 8. Successful return count is 8; this tuple has no cast-counter slot. |
| SpellStopCasting | table `0xAF5200`, function `0x809EA0` | Accepts no cast-instance argument. Its current-state dispatch can act through current spell, auto-repeat or pending-action paths; it is not an owned managed-request cancellation API. |

The native cast record reads spell at unit+2668, start/end at +2680/+2684 and an unsigned byte at +2652. The channel reads spell at +2688 and start/end at +2692/+2696. The supporting `0x84E2A0` number push uses Lua number tag 3 and a 16-byte stack advance. The `0x71AB20` helper supplies the interruption boolean separately from the numeric counter. These are traced facts from this image, not new general-purpose memory APIs or runtime offsets added to production.

The raw string references, registration bytes, complete returned function bodies and supporting helpers are retained as `w110-cast-api-strings.json`, `w110-cast-api-registration.json`, `w110-cast-api-pointers.json`, `w110-cast-unitcastinginfo.json`, `w110-cast-unitchannelinfo.json`, `w110-cast-interruptibility-helper.json`, `w110-cast-lua-number-push.json` and `w110-cast-stopcasting.json` in the evidence directory.

## Managed result and failure handling

`WoWUnit.CurrentCastTimeLeft` and `CurrentChannelTimeLeft` now use tuple value 6 and require numeric end time. Missing/expired/failed observations return zero under the existing policy. `CanInterruptCurrentSpellCast` reads cast value 9, falls back to channel value 8 when no cast is returned, and grants permission only when a nonempty name and explicitly false noninterruptibility field produce the affirmative numeric result. Lua5.1 treats zero as truthy; treating the numeric cast-counter slot as the boolean is incorrect.

`CastLuaObservationRegressionTests` executes the actual getters/conversion and generated requests under stock Lua5.1 with controlled native tuples. Corrected behavioral red is 33/52 with 19 intended failures and zero unexpected errors; final is 52/52 with unchanged fixtures in that interval. The earlier missing-import harness failure is separately retained.

## Lifetime and acceptance limits

The native byte counter is bounded and reusable. A spell name, ID, counter, timestamp or short delay does not prove which managed submission caused a later observation. The investigation establishes these tuple/getter/caller contracts, not a complete writer-lifetime or atomic dispatch-to-response contract. No new cancellation token, signature, offset or ABI was invented.

The managed Cast/Heal/ground/duplicate-cast changes therefore bind known actor/recipient/selection observations and reject observable replacements. They do not establish original native cast-instance ownership, original cast recipient, generic ground-cursor request provenance or live cancellation acknowledgement. These remain D1/D3 in the requirement map. Server immunity/DR/proc effects remain D2; physical range/placement remains D5; independent supervised acceptance remains D6.
