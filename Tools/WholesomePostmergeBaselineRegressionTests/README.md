# Retained postmerge Wholesome baseline — supplemental, not W42 closure

This project compiles the thirteen unchanged test source files retained from the combined postmerge baseline against the current production host and Wholesome sources. The explicit source list is intentional: it is the old baseline, not a replacement for the full regression project. RuntimeGuard adds only an executable Windows/x86 check and runtime reporting.

The existing WholesomeQuestRecoveryRegressionTests project still includes QuestLogCompletenessRegressionTests.cs. That pending W42 module initializer aborts the executable before all retained tests can execute. It is NOT removed, disabled, weakened, or reclassified by this additional project; the original combined workflow remains required and currently red. The W42 owner/ready fixtures likewise remain enabled.

A green result here establishes only that the retained assertions execute successfully against the current source. It does not establish complete observation reads, current player/session ownership, atomicity or ABA immunity, safe profile publication, or safe already-executing plans. It does not reproduce a game client. No installed bot, native DLL, or mesh data is touched.

When changing the retained test source list, review it explicitly against the original baseline; do not silently include or exclude pending W42 tests to change aggregate status. The workflow archives source identities, all output, exit codes, and runtime information.
