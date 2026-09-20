# Resume W85 — two more verified repairs; conditional merge still blocked

Repo `jeofwong/CopilotBuddy-private` (1367174964), draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs. Read `docs/audit/2026-09-20/W85_CHECKPOINT.md`, `W85_EVIDENCE.json`, W84/W83/W82/W81, W80's completed review/ledger and the four original3.3.5a/core/provenance policies. Do not restart the140-commit review or recreate saved interrupted work.

Tested head **9e6e62c56b1a5df74f971d07f3bcb8246af91cee**, tree **41e046af88920adc535881a7afaad2995413d009**. Integrated35498796034/art10601244213:17/17, EquipItem context67/67, deletion observation18/18. Host35498796053/art10601753229:exit0,warnings3344,errors0; compile only. No game attached. Documentation is newer than this tested production head.

## Do not recreate

**W85:** test-only59994e62 actual16/67,51 assertions,0unexpected ->9e6e62c5 actual67/67. EquipItem captures player reference/GUID and reuses existing quest requirements plus running/in-game/alive admission at the named tick/submit/confirm entry paths. Lost context revokes managed intent before stale timeout processing. Explicit profile combat use and a fresh admission after a tick observed Stop remain supported. Lua, physical cursor policy, item choice, timeout and other behavior methods unchanged.151 normalized members identical,1833 indexed inputs, only EquipItem.cs changed across the production pair;195 inner hashes verified per ZIP. Old timeout/popup fixtures gained only controlled context declarations before the red; original cases/assertions retained. Do not call the whole W84-to-W85 span unchanged-fixture.

**W84:**1ab19b62 red7/18 ->cfca67f3/9f206476 green18/18. MIR's pending inventory observer is tri-state and rejects stale player/pending identity. Unknown inventory cannot retire the operation or be logged as confirmed deletion.9f206476 restores one accidental vendor-log word; final change is only two Methods.cs methods.150 unchanged normalized members,1832 inputs,194 inner hashes. W84 docs dc1478d4. Lua/eligibility/request gates unchanged.

Retain W83 strict Kind5242d74f, unsupported/ambiguous/cross-kind/anchor fallback rejection3856b39c and corrected old Escort expectatione518f4a3. Retain W81 automatic dense-pull containment, reusable-item deadline, AutoEquip admission, delete-request gate, gossip actor/NPC checks and final reward identity; W82 strict cached/fresh construction; W77–W79 and auction withdrawal. W83's aggregate residual artifact and test correction remain explicitly separate from an unchanged-fixture pair.

## Still not ready to merge

R03: CAST recipe scheduling and source/dataset/collection/raw-counter mapping remain deferred. R04: physical displaced/foreign cursor and same-slot popup ownership, plus remaining complete EquipItem lifecycle. W85 does not prove a real Stop/Start session without an intervening behavior call, pause/disposal reentrancy or context changes inside later Lua/logging/acknowledgement/cleanup callbacks. R05: MIR plugin/player mutation admission and bounded multi-candidate scan continuation. R06: same-NPC changed-menu final request/cleanup. R07: actual compiler dependency fingerprints, static-state/reload and complete refresh publication.

Next priority: finish the destructive MIR actor/plugin and scan-continuation boundaries with real controlled lifecycle tests, then same-NPC gossip-menu identity. Keep physical cursor identity separate and do not infer GUID ownership from an item entry. No guessed offsets or complete Gordunni/escort recipes/coordinates have been shipped.

The user already authorises merge after all review blockers and final gates are green. They are not green yet; no merge is performed. Do not ask the same permission again. When justified, reconcile direct refs, preserve backup, inspect merge preview and exact-head integrated/host results, then use exact-head conditional merge. Merge is not deployment/live acceptance.

Original WoW3.3.5a/build12340; TC3.3.5 primary/AC WotLK secondary. Scope questing/navigation/Singular and directly related safety. No desktop mouse simulation, new unverified Lua/ABI, auction/ProfessionBuddy expansion or installed-file replacement. Writes require explicit nonempty approved branch, expected parent/blob, reviewed content/message and force=false. Direct master rechecked: **b2324913e2499ba30b239dd67224ca2c655c05cc**. W77's disclosed doc add/revert remains in history. No master write, deployment or independent review occurred in W84/W85.
