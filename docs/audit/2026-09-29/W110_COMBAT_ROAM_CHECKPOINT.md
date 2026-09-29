# W110 combat and roaming continuation

The coherent source batch is **dc545b045af585dedbc02c18b8b39c8cb041f9e6**, tree **5633b8900fd5e8f4212ed7b32e155aed319e3f00**, parent **e48988a9d4e1ed9cbec06f59a849ddd4c1ca4530**, on JeofW/cbp PR51 branch audit/next-55-equipment-observation-20260917. The later containing documentation commit is separate; its exact identity is in the external final verification and LATEST_CONTINUATION. Earlier W80-W110 and dcd3c838/e48988a9 work are preserved.

The batch finishes the recovered pre-pull changes and repairs three further caller/cleanup boundaries. Pre-pull rest/buffs, better-target publication, exact displayed-target acknowledgement and final LOS/range admission are retained. Ground combat now revokes yielded Heal/buff/offense when actor/GUID/map/life/combat ownership changes; offensive continuation also retains its selected candidate, while self-healing remains available through target gaps. Roaming's final chase/cleanup branch now excludes service/taxi/other noncombat POIs and rechecks ownership around callbacks and yields. RoutineAdmissionGuard publishes the old failure before Stop so cleanup cannot overwrite a replacement lifetime.

| Interval | Behavioral red | Green |
|---|---|---|
| Recovered pre-pull | 33/109; 76 intended assertions; 0 unexpected | 109/109 |
| Recovered final pull admission | 119/123; 4 intended assertions; 0 unexpected | 123/123 |
| Ground-combat continuation | 126/173; 47 intended assertions; 0 unexpected | 173/173 |
| Roaming chase/cleanup tail | 119/186; 67 intended assertions; 0 unexpected | 186/186 |
| Guard reentrant cleanup | 173/176; 3 intended assertions; 0 unexpected | 176/176 |

All fixture sources remain identical within each frozen production repair interval. Later case extensions are separate. GUARDREENTRANTRED was a missing-import fixture compilation failure and is not counted as behavioral red. The earlier COMBATROAM ordinary17/17 result is preserved at its own source; the final cleanup change received a fresh complete gate.

Final ordinary local Windows/x86: **17/17**, host compilation exit0, **1900 unchanged inputs**. Whole optimized Wholesome also passes, with identical executable inputs and group membership; runtime settings apply only to its child process. Hosted integrated run **36497800550**, artifact **11004650834**, passes **17/17** with **259 members / 258 inner hashes / 1900 matched local, optimized, hosted and current inputs**. Integrated SHA256: `8ec7b0fd3e2154e17dc131db476373b07d7b0bb5875fe84685030808c6c4f181`. Host run **36497800524**, artifact **11004550647**: 3486 Warning(s), 0 Error(s); compilation only. Host SHA256: `1d8d2c5e29affee017da01d93074e86bf01534ed2f3cbdbf55649d9375ec4845`.

The earlier CLR recurrence, limited dump and demonstrated writable-fixture defect remain in W110_HIGHRISK_CLR_DIAGNOSTIC.md. Its read-only handle and eight safe-canary controls remain; current whole-run non-reproduction does not identify the exact historical corrupting instruction. The shared dense coordinator49 and Singular capability9 remain fail-closed with DensePullIsolationValidated=false. No physical dense-pull safety or live HoJ resolution is claimed.

All55 requirement rows are retained and updated. Ret automates Freedom; automatic Protection/Sacrifice/Salvation policies are explicitly unsupported. Original seal/Judgement behavior, blessings/auras/defensives/dispels and all native/provenance gates remain. D1-D6 and synchronized live HoJ/native/server/route evidence remain exact acceptance inputs. PR51 remains draft/unmerged; no production/native mutation, master write, force push, deployment, subagent or numeric-budget renewal occurred.
