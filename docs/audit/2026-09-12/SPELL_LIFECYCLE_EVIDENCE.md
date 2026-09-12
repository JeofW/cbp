# Spell lifecycle ownership — L01 continuation

Baseline: `498257b58cb93470ce2c940bbdaab98b908a20d7`. This is independent of the lift and quest-hotspot patches and stacked on reproducible core/Singular validation.

## Directed evidence chain and falsification

`TreeRoot.Start -> BotEvents.RaiseBotStart -> TreeRoot.OnBotStart -> SpellManager.Initialize -> BotEvents.OnBotStart += OnBotStarted_RefreshSpells` creates another future callback each run. The multicast invocation snapshot does not include the newly appended callback until a subsequent dispatch. Thus the first run misses the Lua subscription handler; later runs perform both the direct refresh and every previously installed callback. The audit's 91 spellbook rebuilds and 78 subscription messages are cumulative across 13 starts, not 91/78 on the last start.

Source: `Styx/Logic/BehaviorTree/TreeRoot.cs` lines 131–150 and 571–580; `Styx/Logic/Combat/SpellManager.cs` lines 1083–1121. SpellManager baseline blob: `7cabfe968dfdac041b6ea7e95bafebbcce91e6a8`.

A second code-supported boundary failure is `LuaEvents.GetEventTableValue -> Lua.State.Globals.GetField`: absent client globals are a valid state, but the call dereferences null. `Lua.DoString` already returns without a client executor. Source: `Styx/WoWInternals/LuaEvents.cs` lines 344–355, baseline blob `ceee476c450b302e0ae25d96700d51d44a9f4295`. This absence-state defect is not automatically attributed to every historical NullReferenceException.

## Narrow design

Keep TreeRoot's existing direct Initialize call as the single per-start refresh owner. Initialize should immediately refresh and bind exactly one owned handler for each spellbook Lua event, without appending another BotStart subscriber. Explicit Shutdown should remove only SpellManager's own Lua handlers. Missing Lua globals should remain uninitialized rather than throwing; managed registrations must survive for later live initialization.

Do not change TreeRoot's thread lifecycle, force-stop behavior, native executor, ordinary-stop policy, public casting API or unrelated Lua subscribers in this slice. Ordinary stop currently does not call SpellManager.Shutdown; this repair does not claim generation-safe cancellation or complete stop teardown.

The test-only commit precedes the repair. Ten scenarios execute real SpellManager/BotEvents/LuaEvents methods in an unattached process, isolating only TreeRoot's unrelated client setup. They cover repeated starts, first-run subscriptions, explicit teardown/restart, notification cardinality, unrelated subscribers, reset and missing globals. Red/green results are pending, not inferred from source alone.
