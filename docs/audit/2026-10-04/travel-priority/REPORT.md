# Flight priority and mounted quest continuation

This change addresses both reported Outland journeys and the mounted quest stall while preserving the earlier correction: incidental aggro does not authorize voluntary combat dismount. The selected travel owner continues until client-observed mount loss; combat then waits for positive ground support. Explicit quest combat and destination interactions retain their own landing authority.

## Production evidence

Source baseline is `aa6f1fa4dd90ae45c8596bcf94702cc8c8a38d65`, whose tree matches PR97 merge `5e993c98cf3a54e35321f1a616de9daa199bdb5c`. The installed baseline host SHA-256 is `1c5d9152aeb0f4bf4d158b810e9a96953b658724105ab208a33ebf6549456bf7`.

`D:/World of Warcraft 3.3.5a/CB/Logs/2026-10-03_2356_21624.log` was copied unchanged into `D:/Dev/CopilotBuddy-Evidence/travel-priority-20261004`. Its SHA-256 is `f2836bc1a31b16b8b4977cead7da816d509ba499430feba7fdaeae41a55c4783`. The installation selects ground mount35022/Black Hawkstrider and flying mount Tawny Wind Rider, with UseMount enabled. These settings were inspected and hashed; they are not changed by this repair.

The repair trip starts at `(-989.65204,3218.9448,45.25728)` and targets Hagash at `(-1329.01,2397.58,89.1584)`. After the vendor visit, the turn-in trip starts at `(-1328.8708,2398.1316,89.101585)` and targets Aledis at `(-689.583,4167.8,58.5228)`. Both flight searches exhausted their budget on remote collision misses and selected ground travel. At the vendor departure, IsOutdoors is true but the rays also record an overhead surface above the supported actor. The log then records the mount spot blacklist at00:00:52.539 and ground mount request at00:01:23.747.

## Root causes and repair

### Remote geometry and permanent ground choice

`GroundApproachSearch` originally required a final landing and complete onward mesh path before admitting an entire long flight. The native collision world did not provide those distant surfaces. `GroundTransitionRuntime` then retained its ground choice indefinitely, even after the actor moved away from the original obstruction.

Long journeys now search bounded local candidates,64/48/32yards ahead in forward directions. Each candidate still needs positive collision support, known mesh area, dry and unblocked body footprint, and clear ascent space. These plans have an explicit `ProgressOnly` role and provide no final arrival or descent authority. Observed arrival renews the next local leg. Only the existing ordinary final approach can descend and release the ground interaction path. Actual displacement keeps a productive long flight from expiring at the landing deadline; rejected movement still reaches the existing no-progress recovery.

Ground departure examines the actor's overhead body columns after stopping, so an outdoor flag cannot authorize takeoff through an awning. A blocked departure walks clear. Ground fallback reconsiders flight after bounded time and observed displacement; optional unavailable flight observations retain useful ground travel. Coordinate transit uses the same preference and planning path. Once a final exterior landing has been selected, a distant indoor endpoint cannot trigger repeated takeoff/landing oscillation.

The review also reproduced a moving-departure failure: an exact-origin cost observation can be rejected while the actor continues walking, so repeated moving reviews can keep selecting ground. Flight-capable moving departures now request a bounded stop and wait for its observation before the cost decision. A three-second missing stop acknowledgement yields ground progress; incidental combat immediately revokes this optional pause. No-flight ground journeys and already selected final ground approaches retain their owner.

### Mount preparation was charged as an actual request

`GroundMountRequest` started its30-second retry budget when it selected a mount, before knowing whether any command was admitted. A rejected preparation therefore caused the observed long walk before retry.

A rejected preparation now yields walking and may retry after one second. Only a submitted request retains the longer retry budget and eight-second observation window. A positive mounted observation clears the old preparation delay so a later interaction's remount is promptly eligible. The request remains bounded, owned and separate from its acknowledgement.

When a useful ground journey later becomes eligible for flight, the owner also distinguishes a ground mount from an already observed flying mount. It requests one supported, out-of-combat removal, waits for a separate unmounted observation and then releases flight preparation. Late combat revokes that optional removal. A missing acknowledgement yields bounded ground fallback instead of repeated removals. An already observed flying mount is retained for takeoff.

### Mandatory quest work contradicted mounted combat policy

The combat root deliberately yielded to incidental mounted travel, but `QuestLootHandoff.AllowLoot` classified all player combat as blocking mandatory quest work. Both owners could therefore yield. `ForcedQuestTurnIn.SearchCurrent` separately rejected combat, which also revoked the moving-NPC search while the character remained mounted.

The selected mandatory pickup/turn-in stage now remains scheduled during mounted incidental combat. Exact selected-stage, actor, run, profile, destination and POI checks remain. The patrol predicate uses the same mounted policy. Unmounted combat keeps priority, unrelated loot is not drained, replacement work cannot borrow the old owner, and interaction/attack admission still depends on its own later observations.

### Native terminal marker was mistaken for an unknown traversed polygon

Retained real map530 native replays contain complete paths whose last vertex has `End`, a zero polygon reference and area0. `GroundApproachQueries` passed that marker through as an unknown area, potentially rejecting a valid final onward path.

Only a complete path with matching arrays and the exact terminal End/zero-reference shape now triggers a separate nearest-polygon and area query. A positive endpoint snap within half a yard supplies that single vertex's observed area. Interior unknown areas, partial paths, missing markers, nonzero references, unavailable polygons, wrong floors and replaced providers remain rejected or unknown. The native provider's buffers are copied and never rewritten. The fresh `TravelIncidentReplay` captures actual native path/endpoint observations for both reported regions and labels its nearby synthetic controls separately.

## Deterministic validation

All causal failures and subsequent receipts are retained under the evidence directory. The original failing assertions were inspected before the corresponding production edits. Preparation/setup errors are not counted as behavioral failures.

| Boundary | Focused result before final release gate |
| --- | --- |
| Actual GroundApproach search and query adapter |42/42|
| Ground mount request lifetime |12/12|
| Ground-transition state machine |48/48|
| Bounded relation search |10/10|
| Complete controlled flight journeys, including moving review, upgrade and long flight |17/17|
| Actual indoor/action integration |100/100|
| Actual mandatory gate, published quest root and patrol admission |14/14|
| Mounted combat production branches |110/110|
| Actual mounted-ground integration |19/19|
| Actual combat-POI replacement acceptance |12/12|
| Ground dismount recovery and ownership |34/34|
| Actual Flightor ownership and movement waits |359/359|
| Flight/ground travel cost decisions |12/12|

The host builds successfully for Windows/x86. `focused-integrated-travel1` contains16successful build/run stages; `focused-mount-upgrade1` reruns the four affected runtime integrations after the final upgrade repair, with8successful stages. These are scoped focused receipts, not a substitute for final whole-source acceptance.

`focused-moving-flight-review1` adds12successful build/run stages across six affected suites after four retained causal failures. The final clean-source host build is repeated by the complete gate.

The full canonical local gate and hosted workflows must run against the final clean candidate. Exact commit/tree, complete gate, native engine/mesh hashes, publication and installed payload belong to the subsequent machine-readable receipts, rather than an invented precommit identity in this report. Native replay uses the installed engine's independently checked SHA-256, not the different tracked library merely because it shares the same filename.

## Review and proof limits

The review covered final versus progress-only landing, indoor departure, ground-to-flight switching, late combat, successful versus rejected preparation, selected quest stage, moving NPC search, target/POI replacement, unknown geometry and endpoint metadata. Existing cancellation, executor, actor/session and ground-support assertions remain enabled. No workers were available in this session; this is an implementation and test review, not a claim of an independent reviewer.

Controlled client/native leaves make scheduling and action/acknowledgement regressions deterministic. Actual native mesh queries establish their recorded connectivity and endpoint metadata only. Neither proves collision geometry everywhere in the live world, physical flight through every route, server mount acceptance, quest completion or a measured travel speedup. Those distinctions remain explicit in diagnostics and receipts. The original3.3.5a build12340 policies and existing quest dataset/classification limits remain unchanged.
