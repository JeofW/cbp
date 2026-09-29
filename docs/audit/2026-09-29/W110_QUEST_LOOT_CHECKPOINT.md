# W110 combat and quest/loot continuation checkpoint

Tested source **b1b1b6a8c4434094e2632734c66c214dc1c5db15**, tree **2d9cb27d0c4168891a7a06d7296ef3874e68a210**, parent **c0bb35da84cdae2ba5f8f9e943b56ecf44e1793c**. Repository JeofW/cbp, branch audit/next-55-equipment-observation-20260917; PR51 remains OPEN/DRAFT/UNMERGED. The containing documentation commit is separate; its exact identity belongs in the external final handoff/publication receipt. All W80-W110 and later aura/taxi/control/roaming/CLR-fixture repairs are preserved.

## Changes

The batch binds shared POI travel to the observed actor, input owner, target/POI, map, provider and fresh finite destination. Quest and loot targeting now await their exact selected target instead of accepting any existing target. Loot acquisition, waiting, slot processing, counters and cleanup preserve their original work and reject service/taxi takeover or revoked actors. Raw player/living-pet combat blocks loot even if cached enemies disappear. The added combat-entry repair keeps a still-live Kill POI and self-healing available during an empty targeting observation while retaining idle/dead/missing cleanup.

## Behavioral intervals

| Interval | Actual corrected red | Actual green |
|---|---|---|
| POITRAVEL | POI travel ownership scenarios: 23/130; assertions=107; unexpected=0; | POI travel ownership scenarios: 130/130; assertions=0; unexpected=0; |
| HANDOFF | Quest loot handoff scenarios: 17/156; assertions=139; unexpected=0; | Quest loot handoff scenarios: 156/156; assertions=0; unexpected=0; |
| COMPLETION | Quest loot handoff scenarios: 161/214; assertions=53; unexpected=0; | Quest loot handoff scenarios: 214/214; assertions=0; unexpected=0; |
| ADMISSION | Quest loot handoff scenarios: 220/264; assertions=44; unexpected=0; | Quest loot handoff scenarios: 264/264; assertions=0; unexpected=0; |
| COMBATOBS | Quest loot handoff scenarios: 266/286; assertions=20; unexpected=0; | Quest loot handoff scenarios: 286/286; assertions=0; unexpected=0; |
| COMBATPOI | Combat target-gap scenarios: 179/185; assertions=6; unexpected=0; | Combat target-gap scenarios: 185/185; assertions=0; unexpected=0; |

All six intervals have zero unexpected errors and unchanged asserting fixture sources across their production repair. Early fixture compilation/observation corrections are retained separately. The LootLatency fixture still requires terminal clear to be the last action, now through the new guard. Later travel indentation and a corrected boundary comment do not change assertions.

## Final acceptance

Local Windows/x86 **17/17** plus host compilation; **1903** unchanged inputs. Entire optimized Wholesome **198 aggregate groups**, identical to ordinary and hosted membership, with unchanged immutable binaries, exit0 and no dump. Hosted integrated run **36503597472**, artifact **11006546354**: **17/17**, **261 archive members**, **260 internal hashes**, **1903 local/hosted/current inputs matched**. Hosted application compile run **36503597489**, artifact **11006039776**: 3492 Warning(s); 0 Error(s). Compilation is not behavioral/live acceptance.

Integrated SHA256: `14e5ce9374f222f8582892cb6f64a6e0dd6474c3c88f08ce228aa2062e1c2bf0`. Host SHA256: `73883ce01c4c996895d7dc7a3aad3f3ce6b7a570c5a45dc5179ebe10ea04ba23`. Full execution, pairs, source identities and receipts are in W110_QUEST_LOOT_EVIDENCE.json.

## Remaining boundaries

All55 rows retain explicit dispositions. Live HoJ attribution remains unverified. The previously demonstrated writable offline fixture handle remains repaired and the whole suites exercise the OS-enforced read-only canary; historical limited dumps still cannot identify the exact initiating invalid write. DensePullIsolationValidated remains false at coordinator and routine, independent of a foreign provider's positive range. Enabling retreat still requires proved actor/target/route ownership, Z/LOS/collision/hostile-envelope and supervised world evidence. D1-D6, causal menu/loot/cursor/cast protocols, realm recipes, physical routes and independent acceptance remain open. No merge, deployment or native-image/IDB change follows.
