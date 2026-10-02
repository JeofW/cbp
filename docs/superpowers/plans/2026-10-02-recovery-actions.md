# Recovery observation and action plan

Source: isolated D:/Dev/CB-RecoveryActions-20261002, branch audit/next-recovery-actions-20261002, fast-forwarded to PR92 merge bc45d423. Specification: docs/superpowers/specs/2026-10-02-recovery-actions.md and the user's continuing audit request. Existing PR90/91/92 fixes and source evidence remain intact.

1. Reproduce item cooldown numeric/boolean false-ready cases using actual WoWItem members and generated requests under stock Lua5.1. Cover malformed/empty/nonfinite/disabled input, actor/item/map/memory/executor/worker replacement and cancellation. Add an explicit observation API and conservative readiness predicate; preserve existing valid numeric units.
2. Audit actual self-heal, defensive and DrinkPotions/Singular submission paths and acknowledgement evidence. Add failing-before owner/state-machine tests before introducing shared pending health/action reservations. Use confirmed original-client tuple/event layouts only. Keep ambiguous or unavailable observations pending until an attributable bounded recovery, never silently success.
3. Exercise consumer admission and duplicate requests through actual callers, including rejected use, in-flight heals, shared health/mana items, interruption, death/Stop/restart and stale observations. Preserve configured thresholds and avoid invented expected-healing coefficients.
4. Run focused and full local tests, runtime-source compilation and exact4335 closure; publish a scoped candidate, verify exact-SHA Windows/x86 artifacts and remote diff, merge when green, validate/package/deploy merged master while protecting user state. Extend package/probe membership for any modified runtime plugin.

The first item observation step can be delivered as a smaller prerequisite when its proof is complete. Further acknowledgement work remains separately explicit; no completed PR or deployment ends the audit.
