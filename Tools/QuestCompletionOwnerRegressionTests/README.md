# W42 completion owner — bounded full-source regression

This runner links the full production QuestLog and PlayerQuest classes, including actual completed-history ownership, to controlled external memory, cache, native-executor and world boundaries. It does not replace the quest-log implementation. The shared BoundaryFixture declares a sale caller, so the existing sale extractor supplies that method for compilation; these 14 cases do not execute sale or replace the original 23-case sale suite.

The executable requires Windows x86 and writes all 14 case outcomes plus immutable commit/source provenance. Exit 0 means all cases passed, 1 means assertion failure, and 2 means fixture/environment/unexpected failure. A new output path and actual-owner-manifest.json are required. Native completion dispatch, current live client/server behavior, scheduler publication, execution-plan validity, whole-log identity completeness and ABA immunity are NOT established by this runner.

Run after building Release/x86:

```powershell
& <x86-dotnet.exe> Tools/QuestCompletionOwnerRegressionTests/bin/Release/net10.0/QuestCompletionOwnerRegressionTests.dll <new-results.json> <actual-owner-manifest.json>
```

The initial 14 cases are test-first. The source-confirmed cache-miss/history bug was already reproduced by the uploaded 24-case fixture on de10ebc1. This runner additionally exercises that path through the full production owners rather than only extracted methods. Nine compatibility/control cases and five cache-miss/sequence assertions are expected; inspect actual artifacts before assigning result counts. No new public snapshot API or atomicity guarantee is specified by these tests.
