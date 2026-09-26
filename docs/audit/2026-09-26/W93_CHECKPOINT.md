# W93 — R08 numeric reward receipt and UTF-8 load size verified offline

26 September 2026, Asia/Kuala_Lumpur. Canonical repository `JeofW/cbp`, ID `1367174964`; existing open/draft PR51, approved branch `audit/next-55-equipment-observation-20260917`.

## Tested source and publication

Tested production/source: `069c6e8c2fe06bddd5a6dd36c782d0519fcff69b`, tree `e2a2533d98b0cfec6ed556c0cef627d82e1fde84`. Workflow-only parent: `27f2d635ef151e6c65448b504cffa3322209d311`. This containing handoff commit is documentation only; resolve its SHA from the live branch and do not call it the tested source.

Repository ID, PR51 branch/head, source tree/parent, direct master and retained backups were reconciled before publication. Direct master was `b2324913e2499ba30b239dd67224ca2c655c05cc`; PR cached base metadata still named `f462a9bb4eb18acac9069f495177df35672286d5` and was not used as the direct ref. Preserve pre-W93 backup `audit/backup-pr51-before-w93-reward-20260921` at `aca2f1cd75e067aa14a91e3cb615b5c74814c403` and W92 lifetime backup at `f7a598ad3a15c2e9b216e965113c255c538e37fc`.

## Changes and cause

The already-present candidates were retained unchanged and committed together:

- `Bots/Quest/Actions/ActionSelectReward.cs:221`: after the guarded button click, return `(QuestInfoFrame.itemChoice=={0}) and 1 or 0`. The shared bridge transports through lua_tolstring, so the raw true receipt was not a managed success. Numeric 1/0 fits the existing managed conversion without changing shared raw-boolean semantics.
- `Styx/WoWInternals/Lua.cs:108`: pass `bytes.Length` to loadbuffer, matching the UTF-8 payload, allocation and filename placement. `lua.Length` counted UTF-16 code units and truncated non-ASCII requests. Sampled shared callers were reviewed; exhaustive shared/native acceptance remains open.

No scoring, quest/item identity, complete-choice observation, owner/lifetime guard or test assertion changed. A button selection receipt is not quest completion or server acknowledgment.

The separate workflow commit changes only the old repository-name literal to `JeofW/cbp` in `audit-integrated.yml` and `audit-validation.yml`. Repository-ID, event and branch gates remain intact. Without this correction the required jobs would skip after the repository move. Eighteen other legacy exact-name/private-repository guards were inventoried but not broadly modified.

## Preserved red and verified green

Red source `aca2f1cd75e067aa14a91e3cb615b5c74814c403`, run `35572599113`, job `106247242803`, artifact `10626487223`: original reward lifetime 18/18; stock Lua boundary 20/33 with 13 intended assertion failures and zero unexpected errors; integrated 16/17. Three direct non-ASCII size cases and ten positive generated-owner cases failed. Some truncated requests still parsed/clicked, so truncation is not always a syntax failure. Keep this archive.

Green integrated run `36220559526`, job `108344896910`, artifact `10899430321` executed on GitHub-hosted Windows 2022 with explicit x86 test runtime. Actual results: integrated 17/17 build/run entries; stock Lua boundary 33/33, zero assertions/unexpected errors; retained reward lifetime 18/18; reward observation 12/12. The receipt records `load-size=bytes.Length` and the expected production Lua source hash.

Green host run `36220559518`, job `108344896825`, artifact `10898448690`: build exit 0, 3344 warnings, 0 errors, Release/x86, `tests_run=false`, `game_attached=false`. This is compilation, not runtime acceptance.

Both downloaded outer archive digests match live GitHub artifact metadata. Red and green integrated archives each have 201 members and 200 verified inner hashes, with complete membership and no duplicate/missing/unlisted entries. Across their 1838 recorded input paths, only the two production files and two disclosed workflow files differ; no input added/removed. All 156 normalized fixture members are byte-identical. The fixture/provisioner assertions were not weakened. No separate ZIP-CRC check is claimed; host has no inner manifest.

Lua 5.1.5 source hash and provisioning script hash match the retained red; the separately rebuilt DLL hash differs and is explicitly recorded in W93_EVIDENCE.json. That is not fixture drift or a claim of a reproducible binary build.

## Evidence limits and review

The fixture executes actual generated reward Lua using stock Lua 5.1 C APIs, the extracted production load-size expression and four verbatim managed conversion methods with controlled UI observations. It does not execute the production native assembler/executor, remote-memory allocation/readback, native error handler/return-buffer lifecycle, original client or server. Raw boolean transport controls remain unchanged.

The final four-line source/workflow span was reviewed directly without subagents as requested. No additional actionable defect was found in that span; this self-review does not satisfy the project's independent acceptance-review gate. No local project build/test, original-client attachment, server receipt, merge or deployment occurred.

## Publication and retained transport history

The 23 September scoped capability-probe receipt plus fresh 26 September ordinary operations established usable GitHub access after the earlier operation-specific provider refusals. Historical blocked combined requests were not replayed or relabelled successful. Numeric API-publication preparation remains three historical attempts, zero new, zero remaining; this slice used local Git commits and an ordinary fast-forward push.

Origin was changed to canonical `https://github.com/JeofW/cbp.git` only after identity/history verification. Publication read-back checked both parents, exact changed paths, all four Git blob identities, full trees and the direct branch head. No replacement PR was created.

The earlier disposable probe had uploaded unrelated LFS objects. This source-only push used documented process-scoped `GIT_LFS_SKIP_PUSH=1` after checking every changed path was non-LFS and no attributes/assets changed. The prior environment value was restored and the hook retained. This does not provide navigation-map payloads or runtime readiness.

## Next exact task and remaining gates

Reconcile remaining R03 special-action credit-mapping obligations against `2026-09-20/W90_R03_MAPPING_REVIEW.md`, W91's completed ordinary-credit/publication repairs and W92's completed interaction-lifetime repair; identify the next source-backed special-action regression without reopening those completed fixes. Preserve strict v1 fields and ObjectiveProgress deferral until mapping is proven. Where source/native prerequisites block one item, document that exact dependency and continue independent recorded R04/R06/R07 work.

R03 successful native matching-recipient request/acknowledgment, R04 physical displaced/foreign cursor/same-slot popup/lifecycle, R06 same-content menu generation/native concurrency, R07 actual compiler inputs/static-state reload/full refresh, and broader Wholesome/navigation/Singular/shared/native/independent/supervised acceptance remain open. R08's named offline defects are verified; original-client/shared-native acceptance is not closed.

Read and retain W92/W91/W90/W89/W88/W87/W86 and W80 checkpoint/evidence/ledger plus all four original-client/core/provenance/addon policies. Do not restart W80's completed 140-commit review or W92. Preserve R01/R02/R05 dispositions, ordinary matched credit, QuestComplete admission, CAST refusal, independent collection, navigation mutation guards, MIR lifetime guards, dense-pull containment, auction withdrawal, failed intermediates and earlier red/green evidence.

Original WoW 3.3.5a/build 12340; TrinityCore 3.3.5 primary, AzerothCore WotLK secondary. No guessed mappings/recipes/offsets/coordinates, excluded PR25, PR58 recreation, capability/reference-branch merge, assertion weakening, force push, master write, merge, deployment or production access is authorized by this checkpoint. Full audit is not complete.
