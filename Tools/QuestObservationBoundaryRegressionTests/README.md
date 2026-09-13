# W42 uploaded boundary fixture — baseline execution

Imported from the user's `copilotbuddy-w42-checkpoint.zip` in continuation PR #38. Actual attachment archive SHA-256: `d9b133bdab43117ff4ddc586baa5d5c04760b8e1db5cb092d57b891996c34be4`. The 33 files covered by its internal SHA-256 manifest were verified. This initial commit publishes the previously local 24-case fixture; it does not change production or the original sale suite.

The generator reads six source files from an explicit immutable Git commit. It extracts actual QuestLog lookup/completion, PlayerQuest materialization, and Wholesome SellByQuality methods and includes the actual ProtectedItemsManager, DualHashSet and ValuePair. Descriptor reads, metadata, current completion, historical cache refresh, player/profile objects, consumables and merchant dispatch are controlled external boundaries. Native completion and the scheduler are NOT executed by this fixture. Full-host compilation and scheduler/publication tests remain separate requirements.

The initial fixture must compile and execute before its assertions count as reproduction. Windows x86 .NET 10 is required and checked by the executable. Exit 0: all assertions pass; exit 1: behavioral assertions fail; exit 2: environment/fixture/unexpected failure. Build or extraction failure is not a behavioral red. Every case and the exact source manifest are retained.

From repository root, use a clean output directory:

```powershell
python Tools/QuestObservationBoundaryRegressionTests/extract_owners.py --repo . --ref <full-40-character-source-SHA> --out Tools/QuestObservationBoundaryRegressionTests/Generated
dotnet build Tools/QuestObservationBoundaryRegressionTests -c Release -p:PlatformTarget=x86
& <x86-dotnet.exe> Tools/QuestObservationBoundaryRegressionTests/bin/Release/net10.0/QuestObservationBoundaryRegressionTests.dll <new-results.json>
```

The legacy GetAllQuests omission is a characterization, not a demand to keep unsafe downstream behavior. Safety assertions must be preserved when adding an independent raw-identity snapshot owner. Repaired owners may require extending extraction and controlled dependencies; do not silently replace actual production decisions with a helper model. The original 23-case sale suite is unchanged. Two equal reads or counts do not prove atomicity or exclude ABA. No live client, installed bot, native binary replacement or deployment is involved.
