# Wholesome relation identity across scheduler, profile and host

Owner-approved full Wholesome continuation from e674798b87ca792b5faab72dc324ea531e278153. No default-branch merge, mesh/DLL changes or client deployment.

Confirmed source chain: AddRelationWork groups relations by numeric Entry alone; GetRelationSpawns searches creature first and then object regardless of declared GiverType/EnderType; CanRequestPickup uses the same ambiguous query; ProfileBuilder emits neither GiverType nor TurnInType. The host already parses and uses both typed attributes. Changing only coordinates cannot preserve the contract through dispatch.

The checked-in global dataset has 25 entry-number intersections between creature and object spawn maps. This is namespace-overlap evidence, not proof that all intersecting quests fail. Retain original dataset and no speculative locations.

Test first: actual MaterializeSchedule -> ProfileBuilder -> PickUpNode/TurnInNode parsers. Cover both stages, both namespaces, missing sources, deduplication, ranges, invalid enum values, ancestry checks and normal controls. Repair must key relation deduplication by declared type plus entry, select only that type's map, and emit host-compatible Npc/GameObject attributes. Persistent recovery key format and shared endpoint budgets are separate legacy contracts and are not silently migrated here.

Independent review, live quest dialog/object interaction and general data reconciliation remain open. No blanket all-quest or transport success claim.
