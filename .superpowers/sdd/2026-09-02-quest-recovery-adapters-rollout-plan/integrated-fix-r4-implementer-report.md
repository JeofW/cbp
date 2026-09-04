# Integrated Quest Recovery Fix Round 4 — Implementer Report

Date: 2026-09-04

## Outcome

The remaining death-quarantine equipment-reset gap was reproduced and fixed locally. No deployment, process inspection, client launch, push, external runtime-source edit, installed-binary mutation, or x86 apphost execution occurred.

## Fix

Equipment-based death recovery now uses one closed comparability gate for both repair and replacement. Both the failure-time and current equipped-entry snapshots must be nonempty, and the current equipped-entry count must be at least the prior count. Only then may either a lower critical-durability count qualify as a repair or a changed same/greater-count entry multiset qualify as a non-degrading replacement.

This prevents the specific persisted transition from `[100 critical, 200 healthy]` with critical count 1 to `[200 healthy]` with critical count 0 from reopening quarantine merely because the damaged item was unequipped. Existing repair, same-count replacement, equipment addition, and level-up reset behavior remains covered.

## Test-first evidence

- Added the exact direct-policy and persisted-manager regressions before changing production code.
- The correct x86 direct DLL failed before the production change with `unequipping the only critical item must not masquerade as an equipment repair`.
- After the production change, the same suite passed.

## Tracked files

- `Styx/Logic/Questing/Recovery/QuestRecoveryPolicy.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`

## Verification

- `QuestRecoveryRegressionTests`: Release x86/no-apphost build passed with 3,253 warnings and 0 errors; direct DLL passed.
- `QuestPickupPolicyRegressionTests`: Release x86/no-apphost build passed; direct DLL passed.
- `WholesomeQuestRecoveryRegressionTests`: Release x86/no-apphost build passed; direct DLL passed.
- `QuestRecoveryAdapterRegressionTests`: Release x86/no-apphost build passed; direct DLL passed.
- `CopilotBuddy.csproj`: Release x86/no-apphost build passed with 0 errors.
- All five generated DLLs are `0x014C` (`I386`), and all five output trees contain zero `.exe` files.
- `git diff --check` passed for the two changed tracked sources. The production-file focused legacy-writer/restart/empty-catch scan returned zero hits.
- The five refreshed DLL hashes are recorded in `D:/World of Warcraft 3.3.5a/CB/docs/superpowers/verification/2026-09-02-quest-recovery-live-smoke.md`.
- External adapter live-source hashes remained `b6f6c6b0...`, `cb402613...`, and `38744ca2...`; no external runtime source changed.
- Wholesome installed manifest remained 7/7 valid with SHA-256 `3c16bc197aa9fc841a29e9badcf2178c0e2c0a4d972ded8d87813d57d90a37a8`.
- Adapter original backup remained 3/3 valid with manifest SHA-256 `24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`.
- Wholesome original backup remained 7/7 valid with manifest SHA-256 `c5787dd61f477ecb40d3366f1834b3cd61123806436bf96109b611855d106d56`.

## Residual limitations

- Structured equipment comparison intentionally uses entry multisets and critical-durability counts, not item stats or enchants.
- Legacy or otherwise incomplete equipment snapshots remain fail-closed and require a level-up or scheduled quarantine probe.
- Deployment, runtime compilation, and live acceptance remain deferred by the local-only boundary.
