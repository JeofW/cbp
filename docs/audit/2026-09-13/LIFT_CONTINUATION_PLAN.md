# Preserve supported navigation after a synthetic lift

Owner-approved audit follow-on; parent dedc25fe369b6a1d1c7e3e2ea8b032906848dcc8. This does not replace the unclosed acquisition/optimal-route problem.

Confirmed source contract: MeshNavigator.TryCreateSafeElevatorShortcut publishes waiting point, exit and requested destination, discarding every original onward mesh point. TryInstallNearbyElevatorShortcut then forces IsPartialPath=false and synthesizes Ground/no-ability metadata for the tail. Thus a valid boarding controller can hand off to an unproven straight chord, erase an onward gate/portal/jump, and upgrade a partial search prefix to complete. No live incident attribution is assumed.

Test first through the existing real point-selection helper and production MeshNavigator state. Cases include ascending/descending bends, partial tails, endpoint-at-landing, ordered landings, finite geometry, absent landing controls, metadata alignment/absence, original partial/resource status, rejection without mutation and a 2,048-point curved tail.

Repair sequence: retain the compatible helper signature but select an exit strictly after the source landing; return the waiting point plus the entire native suffix starting at that exit, never append an unproven requested destination. Extract TryInstallElevatorContinuation(WoWPoint[] shortcut, int sourceExitIndex) inside the same partial MeshNavigator type to validate exact suffix identity and aligned metadata before atomic replacement. Preserve original partial/native status and copy onward flags, area types and abilities at matching indices. Mark only the newly introduced first transition as Elevator. Reject missing evidence without changing route state. Caller corridor checks and live controller remain authoritative for physical approach/boarding/exit. No new native probes, geometry, radius or globally optimal travel-time claim.

Required verification: intended baseline failures; linked owner tests green; all existing combined suites green; clean committed sources; hash/run provenance and directed graph; supervised actual exit-following around an obstacle and through later transitions before deployment.
