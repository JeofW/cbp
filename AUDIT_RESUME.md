# CopilotBuddy audit — W51 focused-verified spell lookup and Paladin roles

Continue draft PR47 in `jeofwong/CopilotBuddy-private` on `audit/next-47-flight-owner-boundaries-20260916`. Read current refs before writing, then `docs/audit/2026-09-16/W51_SPELL_ROLE_CHECKPOINT.md`, `W51_EVIDENCE.json`, and `NEXT_CHAT_PROMPT.md`.

Latest focused-verified code is **c97d699ae4019549619facb925743bd48ca060a5**, tree45697de646100c98d8794b813013d4284d262ecf. Two new actual unchanged-fixture Windows repairs: spell-row lookup11/24->24/24; Paladin tank fallback16/28->28/28, all reds intended assertions with zero unexpected errors. All4 focused projects pass;66 Wholesome+3QuestLog groups,1750 input hashes,81 normalized members and104 source exports verified. No pending unexecuted test remains at this code revision.

Correction: the earlier three upstream ports057a153b already had a passing focused run35105135595, including24/24 contract cases. W49/W50's blanket no-post-port-pass statement was too broad. Integrated/host were skipped, not the already-enabled focused workflow. W51 recovered that evidence and preserved it separately from new execution.

Full integrated and host checks at c97 remain skipped under their existing private-only guards on this now-public repository. Public-CI adaptation was requested, not authorized by assumption. No workflow, guard, permission or event was changed, and no guarded suite was moved into another runner. Last fully focused/integrated passing checkpoint remains3c0b6744. Do not report current host warnings, full integration, independent approval or live acceptance without actual evidence.

Master71c79d1c/backups c43c50d8 and8382a7ec remain protected; exclude25/43/45 and do not remerge old PR44. No force push, merge, deployment or installed-binary/mesh replacement. Archive previous root pointers under `docs/audit/2026-09-16/pre-w51/`.

Read W50 for the remaining database/packed-row, utility/blessing, actual role API, safe aquatic escape, merchant/upstream and original-client requirements. W51 fixes lookup and inference boundaries, not maximum DPS, automatic Fury removal, complete tank permission, or full underwater recovery.
