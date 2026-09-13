# Combat Bot group engagement — recovered and promoted source

Baseline d62f2c7b73305087dd23f45d8e881ae38d3ab3e7. This slice implements the owner's no-unrelated-pulls requirement for Combat Bot in dungeon maps. It is independent of Paladin support PR #29. Neither source snapshot nor offline CI certifies all dungeon encounters.

## Root cause and repair ownership

The old Singular Unit helper and CombatBot allowed the target selected by a fighting tank/leader, even when that enemy was not fighting our group. Selection is not engagement. Shared casts, auto-attack, pet Attack and wand dispatch also needed a fresh check after setup yields.

GroupCombatSafety is the single host-level owner for current enemy combat plus a link to self/group: current aggression/targeting/tag evidence or positive observed party/raid threat. It rejects invalid/dead/friendly/unattackable/reset targets and ignores nonpositive/wrapped threat. A leader's selection, unrelated combat or stale post-reset evidence alone is insufficient. Botbase and routine eligibility now delegate to this owner. The restriction remains scoped to Combat Bot in dungeon maps; solo Wholesome pulls and ordinary outdoor behavior are retained.

Shared string/ID casts recheck target and known area-effect geometry at dispatch; auto-attack, pet attack and wand actions recheck the current enemy. Beneficial self/group support remains available. Existing AoE checks reject unengaged neighbors within the modeled effect range. This is not a complete encounter-specific cone/chain/rank-radius model and does not retract an already active persistent AoE.

LegacySpellManager ground placement now delegates the requested coordinates to the existing original-client SpellManager.ClickRemoteLocation implementation instead of substituting cursor/player commands. The nine compiled wiring checks include this handoff, not a live collision/cast acceptance test.

## Failing-before / passing-after evidence recovered in this continuation

- Saved source c9323664e070800114ef6fce5c5ad526178aeca6, unpromoted adapter run 34751110965, artifact 10316130494: actual linked source builds, 12/25 engagement scenarios fail; 13 controls pass. SHA-256 4d9b4e8c826a5122eb160c14a6dc57101d7f3df9b8ae304fb31049fe38d558a7. Complete artifact downloaded and inspected.
- Exact-source preflight 34751110956 at that same saved source: all twelve required build/run entries exit 0, 25/25 engagement cases, 9/9 compiled boundary checks, and 98 tracked Singular files compiled. Artifact 10316545123, SHA-256 068f23f205ef6fad836cff375bec0a5aef9e4711f10943b48250dbcfe957ba5f. All run logs, results.json and verified-blobs.json inspected; promoted source hashes match that manifest.
- Earlier preflight exposed eager client-map access outside the restricted bot. The scope check was reordered rather than relaxing the existing unattached compatibility tests.
- This commit promotes the already verified blobs and removes both temporary write-token preparation files. The final committed-source workflow must still be checked before claiming its result; preflight success is not substituted for that result.

## Limits / rollback

The policy depends on the correctness/freshness of existing client combat/roster/threat observations. Full multi-tick target identity, direct plugin/native commands outside the shared boundaries, pets already attacking, persistent AoE, all class-specific spell geometry, and attached 3.3.5a server acceptance remain separate gates. The dormant FollowMe UnitGroupRolesAssigned signature requires its own compatibility repair and is not silently declared fixed here. No new Lua/modern client API is introduced by this source slice.

No native binary, mesh, raw log, master or installed file is changed. Do not merge/deploy automatically. Independent review pending; rollback is a reviewed revert of this focused source set while retaining the regression evidence.
