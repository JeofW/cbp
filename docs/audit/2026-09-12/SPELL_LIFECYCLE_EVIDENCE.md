# Spell lifecycle ownership — L01 verified repair

Baseline: `498257b58cb93470ce2c940bbdaab98b908a20d7`. Production repair: `2cfe1e5a1103e3bce2550fc9ffe00b4152670150`. Review: PR #7, `audit/06-spell-lifecycle-20260912`, based on reproducible core/Singular validation in PR #5. Independent of the lift and quest-hotspot production changes.

## Directed evidence chain and counterevidence

`TreeRoot.Start -> BotEvents.RaiseBotStart -> TreeRoot.OnBotStart -> SpellManager.Initialize -> BotEvents.OnBotStart += OnBotStarted_RefreshSpells` appended another future callback each run. The multicast invocation snapshot excludes the just-appended callback until a subsequent dispatch. Thus the first run missed that callback's Lua binding, and later starts combined direct refresh with prior callbacks. The audit's 91 rebuilds and 78 subscription messages are cumulative across 13 starts, not totals at the final start. Per-start captured counts were 1..13 and 0..12 respectively.

Source: `Styx/Logic/BehaviorTree/TreeRoot.cs:131–144,571–580`; baseline `Styx/Logic/Combat/SpellManager.cs:1083–1121`, blob `7cabfe968dfdac041b6ea7e95bafebbcce91e6a8`. Source inspection found TreeRoot's direct call and no ordinary caller of SpellManager.Shutdown. A remove-then-add BotStart subscription alone would still leave two refresh owners; that alternative was rejected.

A separate boundary defect was `LuaEvents.GetEventTableValue -> Lua.State.Globals.GetField`: absent client globals are a valid state, but the call dereferenced null. `Lua.DoString` already returns without a client executor. Source: `Styx/WoWInternals/LuaEvents.cs:344–355`, baseline blob `ceee476c450b302e0ae25d96700d51d44a9f4295`. This defect is not attributed to every historical NullReferenceException.

## Narrow production repair

TreeRoot's existing direct Initialize call remains the single per-start refresh owner. Initialize immediately refreshes and binds one owned handler for each spellbook Lua event without appending a BotStart subscriber. Explicit Shutdown removes only SpellManager's own Lua handlers. Missing globals leave LuaEvents uninitialized rather than throwing; managed registrations survive for later initialization.

Verified source blobs and SHA-256:

| File | Git blob | SHA-256 |
|---|---|---|
| SpellManager.cs | `3ef4ab75239fbc89004c310b832c578a80b57d30` | `0bb14599e7ecc02fa20a3e1cf15906d3e5449dd30c6c384a61a7b4ca11cdbabb` |
| LuaEvents.cs | `d41e022cb50ecbb2d0f7e1efeaaa98bd2cba28b8` | `250ce0861c582582778c3748f0f473ac6ab02d5732903dcec16b778412a5ee86` |

Repair source locations: SpellManager Initialize 1082–1087, Shutdown 1093–1100, binding helper 1106–1118, notification callback 1124–1129; LuaEvents missing-globals guard 345–359.

## Actual red → green evidence

1. **Test-only** `9d4b9fd8105404c0ff954e726b3452187fb3577a`, run **34696476656**: build succeeded, **9/10 new scenarios failed**. The unrelated-subscriber control passed. Absent globals interrupted the baseline event-dispatch fixture; the captured 1..13 sequence above comes from the runtime logs, not a completed red fixture. Artifact 10299261143, SHA-256 `d45794757703f05b68e80d2bd083deec2604964ede3934dcb2a12f5786087116`.
2. **Exact-patch preflight** at `735310a6b5efb71b8e2afb0de2e4c2cdf0e1ddca`, run **34697013461**: all ten new cases, the existing quest-core suite and explicit Singular checks passed. Both postimage blobs were verified before promotion. Artifact 10299360708, SHA-256 `f653a840bb8aeeee45c4424e3d53e60228ca7bea845c979018b0e125de9c3d4d`.
3. **Committed repair** `2cfe1e5a1103e3bce2550fc9ffe00b4152670150`, run **34697232345**: build/core/routine exit codes all 0. Ten cases passed again; thirteen synthetic start dispatches each invoked exactly one Refresh; 98 tracked Singular source files compiled. Complete artifact 10298952796 was downloaded and inspected, SHA-256 `44833bd95bf7e4858af61bb28a92ae310fd9ef571a76e064c44b129fae4e6ba8`. The verified x86 core and desktop frameworks were 10.0.12.
4. Independent Windows host-build job **103562696423**, run **34697232291**, completed successfully at the same production commit. Subsequent evidence-only commits do not change source or test bytes.

The fixture executes real SpellManager/BotEvents/LuaEvents methods in an unattached process. It isolates only TreeRoot's unrelated client setup. Ten scenarios cover first-run binding, repeated initialization and start dispatch, explicit teardown/restart, notification cardinality, unrelated subscribers, Lua reset and absent globals. These are not thirteen live client sessions or evidence of an in-game latency/DPS improvement. The ten cases run in two fresh processes; they are ten unique scenarios, not twenty.

## Remaining gates, rollout and rollback

No TreeRoot thread lifecycle, ordinary-stop policy, native executor, public cast API, movement behavior or unrelated subscriber was changed. Ordinary stop still does not invoke SpellManager.Shutdown. Generation-safe cancellation, all-owner teardown and live stop responsiveness remain separate work.

Before deployment, verify attached start/stop repetition, logout/reconnect, learning a spell, talent-group changes and explicit teardown with unrelated subscribers. Confirm one actual rebuild and one owned subscription set per start, and no native commands after cancellation. The latter is an acceptance gate for the broader lifecycle work, not a promise made by this PR.

Rollback is a reviewed revert of the two focused production changes; retain the regression and captured evidence. Temporary patch/blob-preflight files were removed when verified source was committed. No raw runtime capture, binary, mesh asset or account data is added. Independent review and live acceptance remain pending; no merge or deployment was performed.
