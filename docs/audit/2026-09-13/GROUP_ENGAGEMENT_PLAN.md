# Group combat engagement ownership

Owner request: Combat Bot must not start unrelated dungeon/raid pulls. Baseline d62f2c7b73305087dd23f45d8e881ae38d3ab3e7. This branch is independent of the Paladin support slice.

The Singular Unit helper and CombatBot both authorize an enemy selected by a fighting leader/tank without evidence that this enemy is engaged. Shared cast dispatch and auto-attack also lack a fresh hostile-target guard. Target selection, an old tag, unrelated combat and actual group threat are different observations. Solo questing and battleground policies must not inherit the restriction.

Test-first: link actual Unit.cs and DungeonEngagementPolicy.cs with controlled world observations. Cases distinguish selection from party/raid threat, reset/dead/invalid states, AoE neighbors and unrestricted controls. Add dispatch/auto-attack and botbase integration evidence before claiming all attack boundaries closed.

Repair design: a shared host-level group engagement owner using existing 3.3.5 object and threat observations; runtime botbase and routine adapters delegate to it. Require live enemy combat plus evidence linking that enemy to self/group, not merely a selected target. Re-evaluate at harmful dispatch and auto-attack boundaries, and keep beneficial self/group casts available without a hostile target. Known AoE radii must be checked again after setup yields. Unknown encounter-specific hit geometry is not certified by this list.

The dormant CombatBot FollowMe path also expects a modern string from UnitGroupRolesAssigned. Original 3.3.5a returns tank/healer/damage booleans. Verify the original FrameXML signature and repair/test separately; do not assert the entire legacy command set compatible from a successful .NET build. No merge/deploy or native binary change.
