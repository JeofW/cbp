# Wholesome Task 3 Implementer Report

## Outcome

Implemented guarded Wholesome profile generation against the reviewed Task 1/2 scheduling contract. `ProfileBuilder.BuildProfileXml` now consumes `IReadOnlyList<QuestPlanEntry>` directly, and the production `QuestScheduler` caller passes `LastSchedule.Plan` without rebuilding a quest list or consulting database-global relations/spawns.

Each ordered plan entry emits one `If` group with the exact live-state condition required by the binding spec:

- pickup: `!HasQuest(id) && !IsQuestCompleted(id)`;
- objective and ancestor correction: `HasQuest(id) && !IsQuestCompleted(id)`;
- turn-in: `HasQuest(id) && IsQuestCompleted(id)`.

Conditions, names, and all other XML content are constructed with LINQ to XML and `XAttribute`. Objective definitions contain only the entry-approved hotspots. Pickup and turn-in groups preserve each approved relation endpoint as an explicit `X/Y/Z` node in deterministic plan order. No global giver, ender, creature-spawn, or game-object-spawn fallback remains in `ProfileBuilder`.

An objective or relation entry with no approved endpoint is omitted. The scheduler records exact objective omissions in the existing `QuestScheduleResult.Status`/`QuestScheduler.LastStatus` contract before profile generation; the builder does not invent a second status. No empty `<Hotspots>` collection is emitted.

Task 4 lifecycle behavior was deliberately not changed.

## TDD Evidence

### RED 1 — guarded shape/order/filter contract

The production-linked regressions were added before the implementation. The existing three-global-loop builder compiled, then the DLL exited 1 with the intended behavioral failure:

```text
Build succeeded.
3317 Warning(s)
0 Error(s)
System.InvalidOperationException: accepted objective and turn-in work must retain reviewed schedule order instead of waiting behind pickups
TEST_EXIT=1
```

This RED exercised the real external `ProfileBuilder.cs` and proved that the former implementation emitted global pickup/objective/turn-in loops rather than ordered guarded plan entries.

### RED 2 — exact objective identity and relation endpoint preservation

After the first minimal guarded implementation, the endpoint/index regression was added before its production change. The linked build succeeded, then the DLL exited 1 at the intended assertion:

```text
Build succeeded.
3302 Warning(s)
0 Error(s)
System.InvalidOperationException: objective work must use the exact accepted-incomplete live-state guard
TEST_EXIT=1
```

The failure was caused by the absent objective `Index` and explicit relation coordinates, not by test setup or compilation.

### GREEN

After the minimal implementation:

```text
Build succeeded.
3302 Warning(s)
0 Error(s)
BUILD_EXIT=0
Wholesome scheduler recovery regression tests passed.
TEST_EXIT=0
```

The regressions parse the generated XML with `XDocument`, assert decoded exact guard conditions and serialized `&amp;&amp;`, verify accepted objective/turn-in ordering ahead of a pickup, prove a cooled giver and ender are absent while approved alternatives remain, prove only plan hotspots are emitted, preserve distinct approved endpoints, and verify endpointless objective omission plus status.

## Fresh Verification

All commands used the bundled x86 `dotnet.exe`, `UseAppHost=false`, `--no-restore`, and direct DLL execution. No x86 apphost was launched.

```text
[Wholesome] BUILD_EXIT=0 SCOPED_DIAGNOSTICS=0
Build succeeded.
3302 Warning(s)
0 Error(s)
Wholesome scheduler recovery regression tests passed.
[Wholesome] TEST_EXIT=0

[QuestRecoveryCore] BUILD_EXIT=0
Build succeeded.
3246 Warning(s)
0 Error(s)
Quest recovery regression tests passed.
[QuestRecoveryCore] TEST_EXIT=0

[QuestPickupCore] BUILD_EXIT=0
Build succeeded.
3246 Warning(s)
0 Error(s)
Quest pickup policy regression tests passed.
[QuestPickupCore] TEST_EXIT=0

[FullReleaseX86] BUILD_EXIT=0
Build succeeded.
3250 Warning(s)
0 Error(s)
```

Scoped diagnostics counted warning/error lines naming `ProfileBuilder.cs`, `QuestScheduler.cs`, or the Wholesome regression `Program.cs`; the count was zero. The remaining warnings are repository baseline compiler/package noise.

Static and integrity checks:

```text
BACKUP_HASH_MISMATCHES=0
PROFILE_DATABASE_FALLBACK_MATCHES=0
RAW_XML_CONCATENATION_MATCHES=0
BUILD_PROFILE_XML_MATCHES=3
DIFF_CHECK_EXIT=0
UseAppHost=false
```

The three build-profile matches are exactly the method declaration, the production scheduler caller, and the production-linked regression caller.

## Files Changed

Repo-local files to commit:

- `Tools/WholesomeQuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-wholesome-quest-recovery-integration-plan/task-3-implementer-report.md`

External/local Wholesome runtime files intentionally left outside Git:

- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\ProfileBuilder.cs`
- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestScheduler.cs`

No core source changed for Task 3.

## Preservation and Deferred Concerns

- The rollback backup at `D:\World of Warcraft 3.3.5a\CB\Backups\quest-recovery-wholesome-20260903-080658` was not modified; every manifest entry re-hashed successfully.
- `WholesomeAutoQuest` still contains the existing stop/start refresh lifecycle and temporary compatibility adapters. Those are explicitly deferred to Task 4 and were not changed here.
- Multiple approved coordinates for one giver/ender remain inside that ordered entry's single guard. After one succeeds, the live-state guard suppresses later alternatives; failure reporting/rebuild ownership remains Task 4 work.
- Existing unrelated vendor/grind working-tree changes remain untouched and will not be staged.
- No push, deployment, live binary replacement, backup mutation, or apphost execution occurred.

## Review Round 1

Base commit: `51ec3eccce9fb75c6dad46611cf3e24c8272d5f5`.

### Root-cause verification

The live profile parser maps `Type="UseObject"` to `UseObjectObjectiveInfo`, keyed by `ObjectId` and `UseCount`. `UseGameObjectObjective` calls `QuestInfo.FindUseGameObject(gameObjectId)` and consumes `OverridedHotspots`; when that lookup fails, it falls back to client quest-step locations. Task 3 had serialized every `CollectFromGameObject` as `CollectItem`, so an `ItemId=0` objective created a mismatched `CollectItemObjectiveInfo` and could not supply the scheduler-approved hotspots to `UseGameObjectObjective`.

The shipped aggregate data confirms the behavior is material:

```text
SHIPPED_ITEM_ZERO_GAMEOBJECT_OBJECTIVES=143
SHIPPED_ITEM_POSITIVE_GAMEOBJECT_OBJECTIVES=634
QUEST_498_ITEM_ZERO_GAMEOBJECT_OBJECTIVES=2
QUEST_498_GAMEOBJECT_IDS=1721,1722
```

The scheduler also silently skipped three distinct objective conditions: no known in-range clusters, no clusters surviving navigation/safety assessment, and no endpoints surviving recovery selection. Finally, duplicate giver/ender rows were evaluated and materialized independently, while exact duplicate coordinates survived inside a quantized cluster.

### Strict TDD evidence

Game-object schema/resolution RED:

```text
Build succeeded.
3302 Warning(s)
0 Error(s)
System.InvalidOperationException: an ItemId-zero game-object objective must use the live UseObject override schema
TEST_EXIT=1
```

After the focused schema fix, the suite advanced to the scheduler-omission RED:

```text
Build succeeded.
3302 Warning(s)
0 Error(s)
System.InvalidOperationException: an objective with no known in-range endpoint must report its exact scheduler omission
TEST_EXIT=1
```

After the scheduler status fix, the suite advanced to the relation-identity RED:

```text
Build succeeded.
3302 Warning(s)
0 Error(s)
System.InvalidOperationException: duplicate giver rows and exact endpoints must collapse while genuine alternate spawns remain
TEST_EXIT=1
```

Review-round GREEN:

```text
Build succeeded.
3302 Warning(s)
0 Error(s)
BUILD_EXIT=0 SCOPED_DIAGNOSTICS=0
Wholesome scheduler recovery regression tests passed.
TEST_EXIT=0
```

### Focused implementation

- `CollectFromGameObject` with `ItemId == 0` now serializes both definition and guarded order nodes as `UseObject` with `ObjectId`/`UseCount`. The regression parses the real XML through `QuestInfo.FromXML`, resolves `FindUseGameObject(1721)`, and confirms the approved override hotspot.
- `CollectFromGameObject` with `ItemId > 0` remains `CollectItem` with its `CollectFrom/GameObject` definition. The regression resolves `FindCollectItem(1206)`, verifies the game-object source, and checks the approved hotspot.
- `QuestScheduler.MaterializeSchedule` now reports `no-known-hotspots`, `no-assessed-hotspots`, or `no-selected-hotspots` with quest ID, objective index, and retry/context-change. This extends the existing status contract instead of adding a parallel builder-owned status DTO.
- Giver and ender relations are deterministically grouped by NPC entry within the already-separated quest/stage scheduling call. Exact duplicate map/X/Y/Z points are removed inside each quantized cluster; genuine alternate NPCs and coordinates remain ordered and available.
- The old direct malformed-builder status assertion was removed. The builder retains defense-in-depth omission of endpointless nodes, while the diagnostic regression now runs through actual scheduler materialization.

### Fresh review-round verification

All builds used the bundled x86 `dotnet.exe`, `UseAppHost=false`, `--no-restore`, and direct DLL execution only.

```text
[Wholesome] BUILD_EXIT=0 SCOPED_DIAGNOSTICS=0
Build succeeded.
3302 Warning(s)
0 Error(s)
Wholesome scheduler recovery regression tests passed.
[Wholesome] TEST_EXIT=0

[QuestRecoveryCore] BUILD_EXIT=0
Build succeeded.
3246 Warning(s)
0 Error(s)
Quest recovery regression tests passed.
[QuestRecoveryCore] TEST_EXIT=0

[QuestPickupCore] BUILD_EXIT=0
Build succeeded.
3246 Warning(s)
0 Error(s)
Quest pickup policy regression tests passed.
[QuestPickupCore] TEST_EXIT=0

[FullReleaseX86] BUILD_EXIT=0
Build succeeded.
3250 Warning(s)
0 Error(s)
```

Static/integrity results:

```text
BACKUP_HASH_MISMATCHES=0
PROFILE_DATABASE_FALLBACK_MATCHES=0
PROFILE_BUILDER_STATUS_MATCHES=0
RAW_XML_BUILDER_MATCHES=0
RAW_XML_LITERAL_MATCHES=0
SCHEDULER_OMISSION_REASON_MATCHES=3
DIFF_CHECK_EXIT=0
```

Review round 1 changes only the same two external runtime files:

- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\ProfileBuilder.cs`
- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestScheduler.cs`

Repo-local changes remain limited to the linked regression program and this report. `DataModels.cs` and core source did not require modification. Task 4 lifecycle work remains deferred. No push, deployment, live replacement, backup mutation, or apphost execution occurred.
