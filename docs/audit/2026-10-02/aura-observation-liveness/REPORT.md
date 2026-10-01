# Aura observation and worker liveness

## Incident and deployed identity

The October 1 21:11 session terminated its worker six times without a logged user Stop. A valid actor's active aura 61988 could not resolve Spell metadata. `GetAllAuras` correctly refused to publish incomplete coverage, but that exception escaped Wholesome's pre-death attribution and progress-sampling calls through `BotBase.Pulse` and terminated `TreeRoot.WorkerThread`. Cleanup cleared the POI and released quest10161's attempt, explaining repeated manual Starts and some apparent collection stalls.

Recovery found master/origin at `59041ddea9bf0dae68589c2aaf492b9d2962d985` and no open PRs. Every one of the 434 production payload files matched the PR87 deployment manifest for `d949ff16d6396fdda788268648799ac04349b89d`. The relevant aura, Spell-table and worker code was identical in that deployed source and recovered master. Production was not silently assigned the newer master revision.

The older 16:25 session precedes PR87's 18:57 local deployment. Its 27,383 direct aura61988 exceptions, four wrapped occurrences, and 2,040 aura56817 exceptions establish the shared observation storm, but its three worker exits follow user Stop. It is not evidence of three additional autonomous worker deaths.

Full logs, current deployment hashes, protected settings/state hashes, IDA captures, and original recovery state are retained in `D:/Dev/CopilotBuddy-Evidence/aura-liveness-20261002`. The seven unpublished collision files and both Git patches from `D:/Dev/CB-CollisionRecovery-20261001` were preserved there with verified hashes. That worktree and PR51's pre-existing documentation changes remain separate work.

## Repair

`ObservationUnavailableException` preserves the legacy `InvalidOperationException` contract while identifying incomplete inputs. `GetAllAuras` still requires complete active coverage. The new `TryGetAllAuras` returns an explicit unavailable result with no usable partial or empty collection and checks actor/memory ownership before and after observation. It preserves cancellation and unrelated exceptions.

Wholesome's rest, pickup, rescan, vendor, progress and pre-death consumers defer decisions requiring unavailable aura coverage. An unknown rest observation suspends active quest-time accounting and clears stale death attribution; it cannot charge a failure episode or become proof that the character is not eating/drinking. A later complete observation resumes sampling. Existing grounded GameObject, quest progress and recovery ownership contracts remain in place.

The worker contains understood optional observation failures from the bot pulse and allows independent root/death/recovery decisions to run. It rechecks run and bot ownership before subsequent root actions. Explicit Stop and direct or reflection-wrapped cancellation/interruption propagate. Cleanup uses the bot owned at worker entry, so changing bots cannot stop the replacement bot.

Target filtering now publishes only after every required filter completes. Readers reject a not-yet-observed, in-flight, failed or missing-world publication instead of using an old, partially filtered or manufactured-empty target list. A filter failure aborts subsequent filters. Clearing candidates does not convert UNKNOWN into a complete observation. Shared targeting/healing pulses and individual event checkers contain understood unavailable observations while allowing independent later observation owners to run. Stop fences prevent later pulse/event work after run ownership is revoked.

The shared diagnostic path retains owner, first and last occurrence, count, session and reason. Repeats are summarized at a monotonic 30-second interval and session end. Distinct keys are bounded to 64 plus one overflow bucket, which retains the last owner and cumulative overflow count. Ordinary log-subscriber failures cannot turn a handled observation failure into worker termination; cancellation from a sink still propagates. Diagnostic text is bounded and single-line.

## Why the old decoder change was not repeated

Read-only IDA against the original 32-bit client SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c` reconfirmed the native 680-byte Spell records at `0x4CFD20` and `0x61DC30`, and the existing packed decoding algorithm at `0x4CFBB0`. The managed decoder's record size and decoding algorithm were retained.

The historical logs did not record the actual Spell table header, sparse slot, compression mode, row identity or failed read for 61988/56817. The exact live lookup failure therefore remains unproven. New per-lookup evidence distinguishes unavailable tables/headers, invalid header geometry, out-of-range IDs, missing/unreadable sparse rows, unknown compression mode, raw/packed read failure, wrong decoded identity and publication changes. The reason travels through `WoWSpell` and `WoWAura` into the bounded diagnostic. There is no negative metadata cache, and both new and existing aura objects can recover when metadata becomes available.

## Deterministic evidence

Six permanent regression groups contain 96 scenarios: 12 actual worker cases, seven consumer-deferral cases, 16 metadata-diagnostic cases, 21 shared pulse/event cases, 27 targeting-publication cases, and 13 diagnostic-lifecycle cases. The worker and consumer tests use actual runtime owners with controlled allocated test-process memory; the target publication test executes the extracted production filter/publication/readers with a controlled frame/world boundary. No game process or native/game dispatch is installed by these tests.

Retained failing-before/repair receipts are under `D:/Dev/CopilotBuddy-Evidence/postmerge-20260930-pr61`:

| Failure family | Behavioral red receipt | Repair receipt |
| --- | --- | --- |
| Aura-driven worker exits, Stop and logging storm | `aura-worker-red3-20261002`: six assertions, zero unexpected errors | `aura-worker-green1-20261002`; extended worker cases also pass in the later shared/diagnostic receipts |
| Unknown work/death attribution | `aura-consumer-red-20261002`: six failures | `aura-consumer-green1-20261002`: eight focused groups pass |
| Missing metadata failure reasons | `aura-metadata-red-20261002`: 14 assertions, zero unexpected errors | `aura-metadata-green1-20261002`: eight focused groups pass, including 16/16 metadata cases |
| Partial target publication and shared cancellation/ownership | `aura-shared-red3-20261002`: actual shared owners fail before the repair | `aura-shared-green1-20261002`: eight focused groups pass; shared21/21, target27/27 and worker12/12 |
| Wrapped cancellation through a broken diagnostic sink | `aura-diagnostics-red-20261002`: two assertions, zero unexpected errors | `aura-diagnostics-green1-20261002`: four focused groups pass, including diagnostics13/13 |

Earlier fixture compilation/setup errors are retained but are not counted as behavioral red evidence. Existing collection read validity, duration, aura cancellation, Spell lookup, packed decoder, rest ownership, objective rescan, execution watchdog and mounted-hotspot protections were exercised during the focused passes. A complete local suite, runtime-source compilation, exact-SHA hosted receipts, reviewed PR and merged package/deployment gates are separate publication requirements; these focused receipts do not substitute for them.

## Continuing incidents and limits

The 21:11 timeline proves initial flight selection, debris183395 acquisition, landing/dismount attempts, combat with a Bonestripper Buzzard, a later `Looting Zeppelin Debris` submission, and selection of distinct debris183397 before another aura failure. It also records Holy Light at21:14:57.743 and eating Smoked Talbuk Venison at21:14:58.777. It does not by itself prove 30 authoritative debris increments or completion/turn-in. The two Divine Protection requests at21:12:52.889 and21:12:55.897 remain an action-acknowledgement/arbitration incident, not evidence that both casts succeeded.

The preserved recovery ledger has 369 records. Its 65 invalid-data quarantines have six-hour probe deadlines; 13 `LegacyUnknown` quest-stage quarantines come from imported `quest_blacklist.txt` evidence and have no probe deadline. No cross-quest `SourceKey` was found in the persisted evidence. These are distinct provenance families needing scope/context/retry review; the observations neither establish nor rule out scheduler starvation. No recovery state was deleted or rewritten during this pass.

The source dataset still has exactly 4,335 rows and 4,335 distinct quest IDs. The existing ledger/classification is not restarted or promoted by this fix. Exact quest execution, successor-node travel cost, post-combat reacquisition, heal/potion/defensive acknowledgement, rest admission, full death/resurrection, recovery quarantine, and the preserved collision work remain continuing audit lanes. Actual client/server behavior requires a subsequent live session with the deployed revision recorded. Worker survival in a controlled replay is not live quest completion or proof of a full resurrection lifecycle.
