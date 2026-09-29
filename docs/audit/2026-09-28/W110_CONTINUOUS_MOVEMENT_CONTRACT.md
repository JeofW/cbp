# W110 continuous audit: original-client movement observation

This is a new movement/landing/input-owner investigation under the expanded continuous Goal. It does not reconstruct the historically refused combined native map. Analysis used the copied, approved 32-bit executable, base `0x400000`, SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. `W110_MOVEMENT_FLAGS_REVIEW_IDENTITY_20260928.json` records a fresh matching IDA survey and disk hash. The copied input and native database were not edited, and no game process was attached or executed by these tests.

## Movement storage and flags

The retained current movement receipts trace the unit's movement pointer at object+216, primary flags at movement+68, and transport identity at movement+8. Raw object GUID at object+48 is separate from the managed wrapper's cached identity. The constructor/publication/destruction and raw-GUID writer receipts are retained as `w110-movement-*` JSON files in the external evidence directory. A complete read requires exact byte counts; an unsuccessful generic numeric read must not become a valid zero-state observation.

`WoWUnit.TryGetMovementState` reads raw GUID, movement pointer, flags and transport GUID with the existing thread-local cache temporarily disabled, then rechecks the pointer, raw identity, wrapper identity and memory owner. Outputs remain zero and the method returns false on unavailable/short data, invalid or changed ownership, or exceptions. The previous cache setting is restored. This is a bounded observation, not a pinned native frame or proof against undetected ABA reuse between reads.

The primary movement enum had been imported with the transport position missing. The original-client/core layout is:

| Field | Build12340 value |
|---|---:|
| OnTransport | `0x200` |
| Disable gravity / retained Levitating name | `0x400` |
| Root | `0x800` |
| Falling | `0x1000` |
| FallingFar | `0x2000` |
| Pending stop through pending root | `0x4000` through `0x100000` |
| Swimming | `0x200000` |
| Flying | `0x2000000` |

The pinned TC335 `UnitDefines.h` at `8fda442f6c30ca21a622638063ab8b28376f1b25` and AC WotLK header at `8337a378ac325e62a6a91e00c6a5e944205e8536` agree. Both decoded file bytes and Git blob identities were retained. Native `0x6E9920` adds far-falling `0x2000` to primary flags when ordinary falling `0x1000` is present; its actual callers were read. Native Lua `IsFalling` at `0x612430` tests falling while excluding root. Therefore the existing `WoWUnit.IsFalling` root exclusion remains; it must not be silently equated with the raw movement-info falling bit.

The enum and all five newly repaired landing/removal masks now use falling mask `0x3000`, or `0x02003000` when flying must also be excluded. Grounded root alone is allowed. `W110_FLAGS_RED_20260928.json` records 37 intended assertions and zero unexpected errors across six affected fixture families. `W110_FLAGS_GREEN_20260928.json` records the same corrected fixtures passing: layout39, actual gather34, Flightor52, host removal90, coroutine101 and shared dismount33. The older new fixture stimuli inherited the incorrect enum values; they were corrected before this separately frozen red/green interval. Their earlier hashes are not described as identical to this interval.

## The actual input owner is not the interaction target

The old `GlobalOffsets.ActiveMoverGuid = 0xBD07A8` was wrong for this binary. The complete 24-reference result and actual functions `0x51FCE0`, `0x518D50` and `0x512E60` show that this field belongs to interaction selection and frame cleanup. It can identify an NPC or game object independently of the actor receiving movement input.

The input dispatcher `0x5FBBC0`, called by input control `0x5FBE10`, resolves **`0xCA1238`** as a unit (type mask8) before applying movement. With no resolved unit it does not perform that dispatch. The local player's GUID getter `0x4D3790` instead reads a TLS-owned object-manager value. These are distinct contracts.

| Native evidence | Observed contract |
|---|---|
| `0x717C50` | Stores both words of `0xCA1238`, submits opcode618 with that GUID, updates input and resolves the selected unit. |
| `0x729010` | Changes the input owner, tears down prior control state, permits a zero owner, submits the new nonzero GUID, and updates input only after unit resolution. |
| `0x6E2880`, `0x72CCA0` | Real callers choose another controlled unit, the player, or zero based on current control eligibility. Missing control is not unconditional player ownership. |
| `0x734B50`, `0x734FD0` | Object teardown/detachment paths clear the global when the departing object's GUID matches it. |
| `0x742220` | Registers unit handlers and initializes the movement globals, including zeroing the input owner. The retained filename says `release-lifetime`; the returned code establishes initialization, not a claimed shutdown function. |
| `0x4D43C0` | Compares an object's descriptor GUID with the active input-owner global. |

The direct-MOV scan initially reached its two-million-instruction cap. The subsequent query resumed at the returned `0x9B88F5` boundary and scanned through `0x9DF000` without truncation, finding no additional direct MOV writes in that tail. Together the queries found five low/high writer pairs, whose containing functions were read. This is not a claim that an instruction-pattern query discovers every possible indirect memory write. The earlier capped xref result remains explicitly partial. The receipt named `input-owner-writer` at `0x4D6DA0` is a consumer candidate, not a proven writer.

The managed getter now uses `0xCA1238`, requires two complete matching uncached eight-byte reads under the same memory owner, and returns zero on failure. `ActiveMover` resolves that GUID, checks the unit and its raw object identity, rechecks ownership, and returns null for an unavailable, cleared, stale or unresolved actor. It no longer fabricates `ObjectManager.Me` as successful resolution. The existing object manager returns the existing typed wrapper, including the actual LocalPlayer instance, on the ordinary valid path.

`ActiveMoverObservationRegressionTests` compiles the complete tracked getters, the actual declared offset and the actual asynchronous StopMoving owner. Controlled native bytes distinguish interaction target999 from player123 and controlled unit456. The red has 27 intended assertions/zero unexpected; green28/28 and the surrounding coroutine101/101 pass with unchanged input hashes during each run. It covers complete/short/oversized reads, changed control/memory, stale cache, invalid/raw-mismatched units, lookup failure, no-owner rejection, valid player/vehicle stops and handoff during acknowledgement. Native code and actual vehicle movement are not executed by this fixture.

## Landing, form cancellation and practical limits

Shared Singular, Gatherbuddy, Flightor and coroutine owners retain captured actor/node identity, bounded descent, stop/timeout handling and cleanup. A descent timeout does not authorize removing an airborne mount. The shared helper additionally handles FlightForm/EpicFlightForm when MountDisplayId is zero: four intended red assertions become shared37/37. Pinned TC/AC shapeshift handlers set the actor's form/display independently of mount display. The form is cancelled only after the existing landing guard permits it.

The host's process admission checks exact version-resource components3.3.5.12340 before publishing memory or hooking. Version resources are not cryptographic runtime image attestation. This contract is evidence for the approved hash; a differently patched executable requires separate verification. Other fixed addresses, native reentrancy, atomic control-to-dispatch ownership, transport/vehicle subtleties, physical ground contact, safe landing terrain and supervised client/server outcomes remain acceptance inputs. `MoveStop` also contains intentional global input-release behavior; its best-effort stop is not an exclusive actor-bound native dispatch certificate. Existing callers that deliberately substitute a player for an unavailable mover are not made into native ownership proofs by this corrected API.

Local regression results are separate from the final hosted Windows/x86 artifact gate and from live acceptance. PR51 remains draft and unmerged. The checkpoint/evidence document records final source and publication identities after verification.
