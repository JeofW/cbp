# Explicit offline native assembler boundary

This is test infrastructure, not a production/native repair. The host still compiles against the unchanged repository `Lib/fasmdll_managed.dll`. Only the Wholesome regression executable's private output directory receives a clearly marked managed type-identity boundary after compilation. Other executables, the host output, original library, installed bot, Navigation.dll and meshes are not replaced.

The boundary contains only the `Fasm.ManagedFasm` type required by the actual `Memory` owner's private assembler field. It intentionally has no public constructors or assembler API. Any attempted native dispatch fails; no assembly, injection, client memory operation or native result is simulated. A separately reported runtime guard verifies the metadata marker, lack of dispatch API and loaded assembly hash. This guard is an environment assertion, not another production bug.

The new scan fixture still executes the actual Memory.Read primitives, QuestLog, ScanAndRefresh, DoScan, RefreshGate and WholesomeExecutionGate. Only external handle/cache values and optional player observations are controlled. No client is attached. Existing assertions and production method bodies remain unchanged.

Why isolation is necessary: at eb236c45 and 6877382d, all fourteen new cases and later retained groups execute, but process exit aborts with a garbage-collected System.EventHandler callback error. At6877382d, merely avoiding Memory's constructor still loads fasmdll_managed version1.0.3262.20709. A verified historical source export confirms the linked C++/CLI library contains CRT module-uninitialization callbacks. This establishes an additional native-dependency/lifetime investigation, not its complete root cause. Do not call the old terminal crash a scheduler assertion. Do not label this test-only boundary as fixing the production library or validating native shutdown.

Baseline and repaired managed tests must use identical boundary SOURCE/build-target bytes and original assertions. Full host compilation and the other retained suites remain independently required. Save native-lifetime counterevidence for the later compatible-native-packaging investigation rather than suppressing exceptions or forcing a successful process exit.

## Verified7bf54c1e addendum

All25 generated normalization files and archived fixture/target source bytes are identical from clean baselinec3d455d0 to repaired7bf54c1e; only two production inputs differ among1680 tracked input hashes. New scan cases progress4/14 to14/14, with0unexpected case errors and normal process exit. The remaining W42 assertions still leave the executable exit1; nothing forces a green badge.

Rebuilt boundary binaries are not asserted byte-identical: clean red loaded SHA256096d08bbfa982622e4fe09442e6e72b36af5d3b3ae52fd0537bbcb019cbc4857; repaired focused and combined both loaded SHA2569c95b8c11ba7a15107b0f67bd672cd2a68f5d9de704ddf045ec82858cf52fb05. The exact binary difference reason was not independently established. Runtime guards passed for the same explicit deny-dispatch metadata and absence of a public assembler API. This corrects any interpretation of earlier 'identical boundary bytes' wording as a binary-equivalence claim.
