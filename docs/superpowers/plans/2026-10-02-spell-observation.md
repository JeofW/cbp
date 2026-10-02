# Shared spell observation implementation plan

> Execute inline with the retained systematic-debugging, test-driven-development and verification workflow. The user's continuous-audit authorization covers these reversible changes and gated publication.

**Goal:** Missing or malformed cooldown observations cannot authorize casting or positive-cooldown decisions, cause an uncontrolled exception, or masquerade as cast acknowledgement.

**Architecture:** Reuse SpellManager's marked observation protocol and the existing controlled UNKNOWN exception. Route host/Singular/legacy cooldown readers through the same validated query; keep local submission holds separate from observed cooldown. Add context ownership only after its failing-before cases are captured.

**Tech stack:** Existing C# host/runtime sources, Windows/x86 .NET test owners, stock Lua5.1 controlled boundaries; no new dependency or client ABI.

**Spec:** `docs/superpowers/specs/2026-10-02-spell-observation.md`.

## Constraints and review focus

Exact original WoW3.3.5a build12340 only. Preserve PR90 UNKNOWN/liveness, PR91 collision ownership, all 4,335 execution classifications, user settings and existing lag policy. Test missing and nonfinite observations, valid ready/positive controls, positive as well as negative comparisons, direct/wrapped cancellation, local submission holds, and changed actor/session ownership. No unverified potion-use or spell-landed claim.

## Task 1: Observe cooldown conservatively

Files: `Styx/Logic/Combat/SpellManager.cs`, `WoWSpell.cs`, `LegacySpellManager.cs`, `runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs`; new `Tools/WholesomeQuestRecoveryRegressionTests/SpellCooldownObservationRegressionTests.cs`.

- [x] Run the actual readers, parser and admission query against malformed marked vectors and stock Lua5.1 API inputs; preserve a clean behavioral-red receipt. Execute the actual managed conversion path for legacy raw readers.
- [x] Tighten `TryParseAvailability(IReadOnlyList<string>, out double)`; add one shared `CreateCooldownQuery(int spellId, bool requireUsable)` with validated timing inputs. Keep valid unavailable `-1` distinct from malformed values and preserve original localization and lag behavior.
- [x] Make `GetSpellCooldownTimeLeft(int)` expose an observed result or controlled UNKNOWN, never a local dispatch hold; route WoWSpell, Singular and the legacy integer API through that boundary. Propagate cancellation from the existing native fallback without altering its ABI.
- [ ] Run the focused new group plus existing dispatch/buff/metadata owners; inspect callers, especially Feral positive comparisons. Run complete relevant local regression and source compilation before commit.

## Task 2: Fence cached observations and in-flight submissions

- [x] Reproduce actor/map/memory/executor, clock and Start/Stop changes around cooldown reads/cache writes; retain failing-before evidence before implementing ownership changes.
- [x] Scope cache/deadline state to its actual observing context, revoke stale callbacks, and preserve explicit cancellation. Verify a submission does not prove cast/aura/cooldown success.
- [ ] Build a bounded acknowledgement contract on the actual shared cast owner; test repeated defensive requests with known/unknown observations, rejection, interruption and replacement generation.

## Task 3: Coordinate the actual recovery consumers

- [ ] Capture pending self-heal versus enabled DrinkPotions/healthstone cases using the actual host/routine/plugin owners, preserving saved 50/30-percent policy distinctions.
- [ ] Reserve submitted healing until known failure/interruption/landing; require explicit existing emergency policy for an override. Do not fabricate an expected healing amount when metadata is unavailable.
- [ ] Cover health/mana shared potion identity, duplicate requests and authoritative item/cast acknowledgement. Extend package/smoke coverage for the changed DrinkPotions runtime component.

## Publication and next incidents

Each independently reviewable scope requires exact-SHA 34-stage local validation, exact quest closure, remote diff/head and hosted receipts before merge, then merged build/smoke and guarded deployment. Keep live-only acceptance and the remaining quest10161/travel/rest/death/quarantine incidents explicit; continue the next highest-impact stream after delivery.
