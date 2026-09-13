# W42 slice A — repository-backed baseline established

This checkpoint supersedes the attached ZIP's execution limitation, not its safety requirements. It records actual work in the continuation that created draft PR #38. It does not claim a production repair, a green W42 result, a complete architecture audit, or live validation.

## Repository state and actual execution path

Authenticated GitHub branch creation, PR creation, tree creation, commit creation and fast-forward ref updates succeeded. New branch: `audit/next-42-observation-boundary-20260914`, draft PR #38 against #37's `audit/next-41-wholesome-postmerge-verification-20260913` branch. This is a stacked draft, not permission to merge any parent or deploy anything.

The existing `audit/next-42-quest-log-snapshots-20260914` branch was discovered at `2d3728feb22938428fe64d8ff8204d5779cb4e9e`. Its six test/tooling commits above `0e2c092b23514ffd530a9d14d0270a9b1dcca0d7` contained no production repair. They were preserved as #38's starting point. Master was read at `8382a7ec05a64212ea0a237159dca427a0767425`; the backup was read at `c43c50d8d5d6775055f19bf018b52930a264d4a4`. #35/#36 remain preserved through #37; do not recreate them. #25 remains excluded.

Commit `de10ebc1cfd6a48ba53fc561b445d8555ecd51b0` actually published the uploaded 24-case fixture, provenance README and Windows x86 workflow. No production file or original sale assertion changed. Push-triggered Windows workflows actually ran; local authenticated Git checkout or local .NET installation was not required and is not claimed. No workflow-dispatch action was used. No merge, installed-bot change, Navigation.dll replacement or mesh download occurred.

## Attachment and source verification

The attached `copilotbuddy-w42-checkpoint.zip` has SHA-256 `d9b133bdab43117ff4ddc586baa5d5c04760b8e1db5cb092d57b891996c34be4`. All 33 internally listed file hashes matched. The imported Program.cs, BoundaryFixture.cs, extract_owners.py and csproj matched the attachment byte-for-byte after the CI source export was downloaded. The attachment's 20 Python extraction-utility tests were rerun locally and passed; they remain synthetic utility tests, not C# or bot behavioral validation.

Existing source export run `34768560934`, artifact `10321331028`, source commit `229663baed9ef890c5de39ed75579ea3645ba65e`: ZIP SHA-256 `3786d648f699bf274091b55e4ef735e893466c84f9a711340118d4d51b332fe6`. All 3,945 exported files matched the export's SHA-256 and Git blob manifest. GitHub comparison from that source commit to `2d3728...` showed only eight added test/tooling files and no production delta. The handover documents, original audit instructions/context/hypotheses and uploaded W42 plans were read without restarting the broad audit.

## New observed red results — not historical green evidence

Run `34771588297`, artifact `10322406544`, source `de10ebc1...`: SHA-256 `b6061ba136d149d661f01a699f8edbc26208f22b206594e8731b8947aee1b913`. Build exit 0; run exit 1; Windows NT 10.0.20348, x86 pointer size 4, .NET 10.0.12. All 24 cases executed: 13 passed, 11 assertion failures, zero unexpected errors. Full logs, case JSON, source manifest, source archive and runtime information were inspected.

Reproduced under controlled external boundaries: cache-missing raw acceptance falls back to historical KnownComplete or KnownIncomplete (or loses acceptance with invalid history); an occupied cache-missing log permits unprotected sale; mixed/full uncached logs, hydration/eviction sequences, same-count replacement and new acceptance before sale dispatch are unsafe. Working hydrated/empty-log, player/merchant veto, quality-disable and interruption controls passed.

The first run's `files-sha256.json` was empty. Its archive was independently hash-verified; do not claim its internal hash listing was valid. The next runner change computes the hash list before creating its output file to avoid hashing a concurrently opened manifest. This is a verification-tooling correction, not a behavioral fix.

New combined run `34771588276`, artifact `10322535459`, same source `de10ebc1...`: ZIP SHA-256 `079eb541c2df2f4ac9242354b64efad78ac8411c25ef88d0d984fca582224d5e`. Downloaded and inspected. The original sale suite still reports 23/23. The combined run remains red at the existing scheduler module initializer; it is not W42 validation and not all of the later original Wholesome tests execute after that initializer aborts.

## Prior W42 evidence corrections retained

At `2d3728...`, owner run `34769629603` / artifact `10321233037` (SHA-256 `dbfd98611dd7b4587f069467e68e4b1688b05004c45fcc1acf144f698ce30018`) built, ran five ready-log cases with one pass, then threw from a module initializer. Its main 25 cases did NOT run. Do not call this 30 executed owner cases.

Prior integrated run `34769629484` / artifact `10321911349` (SHA-256 `6e60fd77a4d0c6853827033adf1e70e6988131d36e311fc788a791b3903aab40`) had all 14 builds at zero but one run abort. Scheduler cases included a genuine null-observation-to-pickup assertion failure, two missing-contract assertions, one working empty-log control and one unexpected NullReferenceException in the unavailable-owner scan. Those are not five clean behavioral reproductions.

Historical c14bd264/master/role artifacts in the uploaded VERIFIED_EVIDENCE.json remain historical only.

## Next bounded production-owner slice

Add and execute a focused 14-case runner linking the full QuestLog and PlayerQuest, including actual completed-history ownership, before trusting a narrow cache-miss guard. Keep the original 24 assertions and original sale suite unchanged. Build failures or unexpected exceptions do not count as red. Only after intended full-owner failures are inspected should the completion owner preserve raw acceptance independently of optional materialization, returning accepted/Unknown for the cache-miss branch instead of consulting history.

That narrow guard will NOT complete W42: raw whole-log snapshots, identity versus metadata completeness, true player/session/observation provenance, atomicity/ABA, scheduler capacity/pickup exclusion, historical recovery side effects, profile write/load publication, ready-log ownership and already-executing root cancellation remain open. No helper or equal-scan/count model may be presented as closing them. Ordinary cache/read failures and native completion uncertainty must be included in the remaining owner design. Do not add a fake observation epoch or claim a managed lock freezes the client.

Source inspection shows ScanAndRefresh materializes accepted work from GetAllQuests, MaterializeSchedule can mark history before validating acceptance, WriteProfile publishes later, DoScan subsequently calls ProfileManager.LoadNew under a lifecycle lease rather than a quest-observation lease, and the execution gate currently checks schedule shape rather than fresh quest provenance. Those actual boundaries must receive tests and repairs. The current fixture does not execute them.

Follow the saved post-W42 dependency order. FILE protection reload clears FILE protection before parsing; it does not clear runtime/profile protections. The exhaustive architecture/refactoring audit remains incomplete. No merge or deployment is authorized.
