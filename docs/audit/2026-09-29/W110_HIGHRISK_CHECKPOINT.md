# W110 high-risk completion: control, roaming and fixture memory safety

Tested source **dcd3c838ee3be470e15e9363cd055ac1aa514c5a**, tree **fc556211dd8c4a4f2d691df2eeb970803f27cc31**, parent **6da1c368ed84f3365a4a9eed336d19148a1a4d84**. Repository JeofW/cbp, PR51, branch `audit/next-55-equipment-observation-20260917` only. The documentation commit is separate; obtain its exact identity from the final external handoff and publication receipt. This extends and preserves 14950e99/9a9458db and all W80-W110 work.

## Changes and demonstrated behavior

The recovered 6da1c368 batch repaired target selection, chase continuation, shared dense-pull containment, waypoint arrival and parsed movement-mode materialization. It also repaired JIT-sensitive vendor stack attribution in the test harness while preserving exact exceptions, observed reads, call-chain ordering and lease assertions. All five production intervals and its earlier full local/optimized/hosted acceptance are retained.

The additional roaming pass binds target selection and its yielded acknowledgement to one actor, selected unit, map, profile and POI. A foreign displayed target cannot become this operation's Kill POI. Hotspot movement retains actor, route/provider/profile/area/point ownership through mount and diagnostic callbacks, honors navigation failures, preserves service/taxi work and stops when the player or living pet enters combat. Two separate unchanged-test intervals reached 98/98 and then 111/111.

The subsequent full local run reproduced a fatal CLR error in the isolated constructor child. Investigation demonstrated an unsafe harness capability: the real Interact path calls ResetAfk before refusing a missing executor, while the shared fixture supplied a writable self-process pseudo handle. A safe allocated-canary test proved actual writes succeeded. The fixture now uses an owned read-only process handle and closes it on disposal; all production interaction/refusal assertions remain. Five intended failures became 8/8 passing cases. The limited dumps do not prove the exact historical corrupting instruction.

## Final acceptance

The repaired source passes **17/17 ordinary local suites**, host compilation and the **whole optimized-runtime Wholesome run**. All **1,900** source inputs are identical before/after each final interval and between current checkout and hosted artifacts.

Hosted integrated run **36491479613**, artifact **11001815678**: **17/17**, **259 members / 258 internal hashes**. SHA256 `8dcb571d5420d343587699f3ac161d3a7ea41c39641d8299de74f4a30e548479`.

Hosted host run **36491479581**, artifact **11001565374**: **3434 Warning(s); 0 Error(s)**; compilation only. SHA256 `b9bcd5e7b7a7d177cf7894bfd73fbad3ef6d02f74d542af926818b50de7f2ef9`.

The failed 16/17 package, PID10572 dump and captured binaries remain preserved. A later green does not erase the failure or establish independent/live acceptance. Evidence, case summaries and exact receipt hashes are in W110_HIGHRISK_EVIDENCE.json.

## Scope and remaining acceptance

The 55-row map explicitly classifies all client, quest, navigation, Ret and support requirements. HoJ's live target-switch/run-away report is not certified resolved; the managed ownership defects are repaired. DensePullIsolationValidated remains false in Singular and the shared coordinator. Original-client atomicity, actual realm/server outcomes, causal menu/cursor/cast protocols, admitted special recipes, physical routes and independent supervised review remain D1-D6. No new speculative PvP planner, native ABI or seal policy was introduced. PR51 remains draft/unmerged; no production/native mutation or deployment is claimed.
