# Wholesome Task 3 Implementer Report

## Outcome

Implemented guarded Wholesome profile generation against the reviewed Task 1/2 scheduling contract. `ProfileBuilder.BuildProfileXml` now consumes `IReadOnlyList<QuestPlanEntry>` directly, and the production `QuestScheduler` caller passes `LastSchedule.Plan` without rebuilding a quest list or consulting database-global relations/spawns.

Each ordered plan entry emits one `If` group with the exact live-state condition required by the binding spec:

- pickup: `!HasQuest(id) && !IsQuestCompleted(id)`;
- objective and ancestor correction: `HasQuest(id) && !IsQuestCompleted(id)`;
- turn-in: `HasQuest(id) && IsQuestCompleted(id)`.

Conditions, names, and all other XML content are constructed with LINQ to XML and `XAttribute`. Objective definitions contain only the entry-approved hotspots. Pickup and turn-in groups preserve each approved relation endpoint as an explicit `X/Y/Z` node in deterministic plan order. No global giver, ender, creature-spawn, or game-object-spawn fallback remains in `ProfileBuilder`.

An objective or relation entry with no approved endpoint is omitted. `ProfileBuilder.LastStatus` records the exact exclusion, and the production caller appends that status to the existing `QuestScheduleResult.Status`/`QuestScheduler.LastStatus` contract. No empty `<Hotspots>` collection is emitted.

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
