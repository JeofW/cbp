# W42 completion owner — bounded full-source regression

This runner links the full production QuestLog and PlayerQuest classes, including actual completed-history ownership, to controlled external memory, cache, native-executor and world boundaries. It does not replace the quest-log implementation. The shared BoundaryFixture declares a sale caller, so the existing sale extractor supplies that method for compilation; these 15 cases do not execute sale or replace the original 23-case sale suite.

The executable requires Windows x86 and writes all 15 case outcomes plus immutable commit/source provenance. Exit 0 means all cases passed, 1 means assertion failure, and 2 means fixture/environment/unexpected failure. A new output path and actual-owner-manifest.json are required. Native completion dispatch, current live client/server behavior, scheduler publication, execution-plan validity, whole-log identity completeness and ABA immunity are NOT established by this runner.

Run after building Release/x86:

```powershell
& <x86-dotnet.exe> Tools/QuestCompletionOwnerRegressionTests/bin/Release/net10.0/QuestCompletionOwnerRegressionTests.dll <new-results.json> <actual-owner-manifest.json>
```

The initial 14 cases are test-first. The source-confirmed cache-miss/history bug was already reproduced by the uploaded 24-case fixture on de10ebc1. This runner additionally exercises that path through the full production owners rather than only extracted methods. Nine compatibility/control cases and five cache-miss/sequence assertions are expected; inspect actual artifacts before assigning result counts. No new public snapshot API or atomicity guarantee is specified by these tests.

PR #39 adds one test-first compatibility control: quest ID zero is not accepted merely because raw lookup matches an empty slot. The original fourteen assertions are unchanged. At 29644fec the full owner executes 10/15 with five intended assertion failures and zero unexpected errors; at 1dccead8 the same fifteen assertions execute 15/15. See the completion-owner checkpoint for exact artifact identities and remaining W42 failures.
