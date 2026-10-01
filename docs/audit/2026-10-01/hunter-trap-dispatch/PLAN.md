# Hunter trap dispatch implementation plan

**Goal:** Make the five existing WotLK traps use the observed learned rank, shared cast admission and a completed local dispatch receipt across the existing Hunter callers.

**Architecture:** Retain the public trap overloads and the existing pre-placement target-selection policy. Route the selected trap through the real named Singular cast helper and SpellManager; retain a target identity through any setup yield. The terminal executor is a controlled test boundary, not a server acknowledgement. Trap placement/arming/trigger geometry is a separate source obligation; Data2 is a diameter in the pinned server and is not silently treated as radius.

**Constraints:** Original build12340; pinned TrinityCore335/client records. No native offsets or client ABI changes, no Trap Launcher, no changes to Paladin/DK/Rogue or quest knowledge. Preserve real cast receipt/cooldown protections and all existing assertions.

## Work

- [x] Add HunterTrapDispatchRegressionTests to the existing actual-owner aggregate. Extract complete trap methods, complete named Cast/admission methods and complete SpellManager dispatch methods; execute real TreeSharp and inspect the executor's actual generated arguments.
- [x] Reproduce wrong ranks, ignored GCD/resource/readiness, stale targets and success without a completed dispatch. Include every original learned trap rank, unknown/cooldown controls, all compatibility overloads, add selection, setup/log callbacks and recovery.
- [x] Replace the raw ID switches with the shared named cast path. Capture the selected target once per admission and revalidate it, actor ownership, candidate eligibility and combat safety before dispatch.
- [x] Couple the three complete specialization callers to the same action test boundary; keep unrelated pet/movement/world observations explicit.
- [x] Preserve baseline/green logs and hashes. Inspect adjacent raw/void-dispatch consumers and record separate obligations; do not claim untested adjacent rotations are fixed.
- [ ] Run focused x86 regressions, the existing aggregate and quest checks, then exact-SHA hosted validation before publication/integration.

## Review focus

The five controls requiring explicit tests are lower learned ranks, failure before any executor request, failure after an attempted executor request, target replacement during yielded dismount/setup, and a stale add disappearing while the current target remains valid. None may produce a fabricated successful dispatch or transfer the admitted action to a new target.
