# W42 owner-boundary continuation implementation plan

## Goal and inherited work

Continue PR #40 from `877de5cd9d332d10d3d1586a78be6b6cff2c504b` on isolated branch `audit/next-42-ready-owner-20260914`, draft PR #41. Preserve all saved fixtures and the #39 completion-owner repair. The exhaustive audit remains open; this is a bounded diagnostic/owner slice, not full W42 closure.

Read `GITHUB_CONNECTION_RECOVERY.md` before treating missing plugin actions as a permanent limitation. The user specifically requests a reconnect reminder and one retry after unexpected loss of access. Native branch/file writes and PR #41 creation succeeded in this continuation. Do not infer the cause of earlier connection loss.

## Task 1 — resolve the inherited scheduler error before production changes

Files: `Tools/WholesomeQuestRecoveryRegressionTests/QuestLogCompletenessRegressionTests.cs`, `.github/workflows/audit-w42-normalized.yml`.

- Preserve all five existing assertions/control bodies and their aggregation.
- Change only exception reporting from `ex.GetType().Name + ex.Message` to `ex.ToString()` so the actual unavailable-owner call stack survives.
- Extend the branch-specific normalized push trigger to this branch; retain baseline/current matrix, read-only token, Windows x86 .NET10 runtime, immutable production source and identical fixture overlay.
- The inherited integrated workflow already triggers `audit/next-*` C# changes and retains all 17 entries. Publish diagnostics atomically, then inspect focused and integrated artifacts at that commit.
- Classify the unavailable-owner error only after identifying whether the stack reaches `ScanAndRefresh`, a fixture constructor/reflection setup, or a separate host owner. A fixture failure must be repaired as test infrastructure, never counted as a reproduced production defect.

## Task 2 — ready-observation advisory refresh boundary

Owners: `runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs`, actual `QuestLog` / `PlayerQuest`, and actual memory/player lifecycle owners. Tests: retain `Tools/QuestLogObservationRegressionTests/ReadyObservationRegressionTests.cs` and extend the existing extractor only when necessary to continue executing the actual checkout code.

The recovered complete normalized artifact at `cd82129ec81f8e51fa12620f95b307e2ca5b34b6` executes the original five ready cases: four assertion failures and one departure control. Its SHA-256 is `3ee7a49e02c7cf5bc1ff1b9d3143c11f7e96ca7b940943b3b4c1fecb5241b81a`; archive CRC and all 41 internal hash entries were independently verified in this continuation. These are saved controlled results, not new execution or actual turn-ins.

Before any repair, expand owner/read-failure controls where the actual external boundary can represent them, then run the unchanged production baseline. Missing metadata or readiness regression while still accepted must not be treated as departure. Distinguish raw identity read success from materialization. Reset advisory history on observed owner/lifecycle change or unknown reads. Preserve the genuine raw departure rescan control but do not label a disappearance as confirmed turn-in/abandonment. Do not export advisory history as completion or destructive-action permission.

Important counterevidence: `GreenMagic.Memory.ReadInternal<T>` returns default when ReadBytes fails, and ReadStructArray can return default-filled arrays. Therefore nonthrowing zeros are not evidence of empty slots. Any raw-read contract must check byte-read success explicitly; equal scans are not atomicity or ABA evidence. Default AcquireFrame is not a hard native capture lease. PID, names and counts are not a reliable session revision.

## Task 3 — verification, review and checkpoint

Run affected and actual integrated Windows x86 .NET10 verification on the same repaired commit. Inspect complete artifacts, source hashes, generated fixture identities, every suite exit and each new case. Keep the original 23-case sale suite and all 14 historical entries; unresolved existing W42 failures stay visible rather than forcing a green badge. Retain baseline/repaired comparisons with identical fixtures.

Update `AUDIT_RESUME.md`, `NEXT_CHAT_PROMPT.md`, a focused evidence ledger and directed graph. Link the reconnect runbook from both handover files. Distinguish source-confirmed, controlled-reproduced, regression-verified and live-unverified claims. No merges or deployment.

## Frontier that this plan does not silently close

Successful occupied raw slots through actual ScanAndRefresh, MaterializeSchedule, ProfileBuilder, profile publication and already-executing plan invalidation; trustworthy same-character reconnect/session and observation provenance; ABA and side-effect revalidation; sale/native acknowledgment. Continue those dependencies before owner-scoped protection, atomic FILE reload (not runtime/profile clearing), merchant/mail/discard/consumption and the saved broader backlog. Original WoW3.3.5a build12340 only. No native replacement, installed-bot update, capture mutation or mesh download for managed tests.
