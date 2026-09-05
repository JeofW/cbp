# Balanced Runtime Latency Design

## Purpose

Reduce avoidable pauses in movement, combat, looting, and quest transitions while preserving the safety checks that prevent path thrashing, invalid casts, duplicate interactions, and unnecessary deaths.

The latest runtime log shows that the sluggishness is not one global timer. It is the combined effect of synchronous path regeneration, a destination throttle that can suppress movement, repeated Lua cooldown queries during rotation evaluation, rest behavior preempting useful work, and mounting for short ground moves.

## Scope and sequencing

Implementation is deliberately staged so every latency source can be measured and verified independently:

1. Correct `MoveTo` scheduling and post-unstick recovery.
2. Reduce combat-decision overhead and low-level Paladin hard-cast stalls.
3. Prioritize loot appropriately and avoid short-distance mounting.
4. Tune rest behavior around explicit start and resume thresholds.
5. Compare a fresh runtime log against the baseline.

The work does not replace the navigator, run live bot decisions concurrently, bypass game cooldowns, or broadly refactor unrelated bot code.

## Baseline evidence

The current log establishes the following baseline:

- A post-unstick path regeneration beginning near 11:29:39 did not reach the next kill point of interest until about 11:30:27. Hundreds of navigation tiles were loaded during this interval.
- `MeshNavigator.MoveTo` uses a 500 millisecond path-regeneration throttle and can return `Moved` without issuing movement when a new destination arrives during that interval.
- Paladin rotation candidates call `SpellManager.CanCast`, which obtains cooldown data through synchronous Lua for each candidate spell.
- Rest starts at the current 80 percent health threshold before pending loot and can hold the pulse for several seconds.
- Ground travel between nearby corpses sometimes mounts and immediately dismounts even though the normal configured mount distance is 75 yards.
- The native pathfinder is protected by one mesh lock. Concurrent path requests would serialize at the lock while introducing shared-state races around points of interest and movement commands.

## Chosen architecture

Use a single serialized control path for live navigation, combat, points of interest, and interactions. Remove blocking work and inappropriate policy pauses from that path instead of adding concurrency to mutable game state.

Each subsystem remains independently testable:

- Navigation owns destination acceptance, route regeneration, and post-unstick recovery.
- Combat cooldown lookup owns the fast cooldown snapshot used by rotations.
- The Paladin routine owns when a cast-time Exorcism is tactically acceptable.
- Loot/rest scheduling owns priority between a pending corpse and recovery.
- Mount policy owns whether a ground destination is far enough to mount.

Background execution is restricted to work that consumes immutable snapshots and does not mutate the active point of interest, navigator, object manager, or executor. Existing quest-data scanning may continue to use that pattern. Native pathfinding and live decisions remain serialized.

## Movement behavior

### Destination handling

A genuinely new destination must be accepted immediately. The 500 millisecond regeneration throttle may suppress repeated calculations for the same destination after a failure, but it must not suppress the first movement command for a changed destination or falsely report useful progress when no command was issued.

Destination equality follows the navigator's existing tolerance rather than exact floating-point equality. This avoids rebuilding a route for harmless coordinate jitter.

### Post-unstick recovery

After an unstick action changes the character's position, the navigator receives a two-second recovery window. During that window it should resume or rejoin the existing route when the remaining path is usable. It must not immediately classify the deliberate displacement as route drift and launch a full synchronous regeneration.

The grace period ends early when the current route is unusable, the destination materially changes, or movement reaches the destination. When the grace expires and the character is still genuinely off route, normal regeneration resumes.

### Path diagnostics

Measure complete path-generation duration. Emit one warning only when generation exceeds one second, including duration, start, destination, result status, and the number of tiles loaded when that value is available. Existing per-tile informational output should be summarized so diagnostics do not become another hot-path cost.

Path failure retains the current safe behavior: do not invent a straight-line route through unknown terrain and do not repeatedly regenerate faster than the existing same-destination throttle.

## Combat behavior

### Cooldown lookup

`SpellManager.CanCast` should use the existing memory-backed cooldown lookup instead of invoking Lua separately for every candidate spell. The result must preserve these semantics:

- A missing cooldown record means the spell is not on cooldown.
- A present record returns zero when expired and a positive remaining duration otherwise.
- Global cooldown and spell-specific cooldown legality remain enforced.
- Lua is not used as a per-candidate fallback in the normal rotation hot path.

The change should be made at the shared cooldown boundary so routines benefit without duplicating logic.

### Low-level Paladin Exorcism

For characters without `The Art of War`, cast-time Exorcism is allowed as a ranged opener when the target is not yet in melee engagement. Once the character is in melee and auto-attacking, the routine should not stop combat flow to hard-cast it.

Instant Exorcism enabled by `The Art of War` remains eligible according to the routine's existing target rules. This change does not reorder unrelated abilities or add abilities unavailable in the WotLK spellbook.

### Combat diagnostics

Add low-overhead elapsed-time logging only for unusually slow combat decision pulses. Do not log every candidate spell or every normal tick. The diagnostic must distinguish rotation evaluation time from legitimate waiting caused by the global cooldown, cast time, or network acknowledgement.

## Loot, mount, and recovery behavior

### Loot priority

When a valid loot point of interest is pending, routine rest must not preempt it if health is above 30 percent and there is no immediately threatening hostile unit. The bot should select and process the nearest valid pending corpse through the existing point-of-interest mechanism.

Critical survival still wins: at or below 30 percent health, or under immediate hostile threat when recovery is viable, rest or combat safety may preempt loot.

### Rest hysteresis

Use distinct thresholds to prevent oscillation and avoid beginning long recovery at minor damage:

- Begin routine rest at or below 45 percent health or 30 percent mana.
- Once resting begins, resume normal work after reaching at least 75 percent health and 60 percent mana, except classes without a meaningful mana requirement may resume based on health alone.
- A newly acquired hostile threat interrupts routine rest and returns control to combat safety.

The existing minimum rest-settle interval may remain only where it is required to confirm food or drink state. It must not become an unconditional delay after the recovery condition is already satisfied.

### Short-distance mount policy

Ground movement must consult the shared `Mount.ShouldMount(destination)` policy. A destination below the configured 75-yard mount distance remains on foot. This applies to loot, vendors, quests, and other `Flightor` ground fallbacks so a nearby corpse cannot cause a mount/dismount cycle.

Flying behavior is unchanged. Existing restrictions for combat, indoors, swimming, mount availability, and special movement states remain authoritative.

### Interaction waits

Loot keeps condition-based waits for stopping movement, receiving `LOOT_OPENED`, and allowing server acknowledgement. Fixed waits are removed or shortened only when a test or runtime trace demonstrates they are redundant. A timeout returns control to the normal retry/recovery path rather than blocking later corpses indefinitely.

## Quest transition behavior

The previously implemented turn-in scheduling corrections remain in force: kill points of interest may yield to a forced turn-in, and completed forced behaviors are not reacquired by the scheduler.

This pass does not add parallel quest execution. Quest compilation or scanning may operate from immutable snapshots, but applying a selected behavior and changing the active point of interest remain on the serialized pulse thread.

## Error handling and safety

- A failed path calculation is throttled by destination and retried through existing recovery logic.
- A corrupt or unavailable cooldown snapshot fails closed for casting rather than spamming Lua or repeatedly attempting an illegal spell.
- Invalid or vanished corpses are removed through the existing loot-point validation path.
- Rest thresholds never override combat survival logic.
- Runtime deployment occurs only after the WoW/CopilotBuddy process releases `CopilotBuddy.dll`; source builds and tests may run while the client is open.
- Existing unrelated dirty-worktree changes are preserved. Each implementation commit stages only files belonging to that subsystem.

## Verification strategy

Every stage follows a regression-first cycle: add a test that demonstrates the observed failure, confirm it fails, implement the smallest correction, and confirm the focused and full regression suites pass.

Required regression coverage:

- A changed destination issues movement without waiting for the old regeneration throttle.
- Repeated same-destination path failures remain throttled.
- Deliberate unstick displacement does not immediately force full regeneration, while persistent route drift regenerates after the recovery window.
- Cooldown evaluation uses the memory-backed value and correctly handles missing, active, and expired records.
- A low-level Paladin may open with cast-time Exorcism but does not hard-cast it while engaged in melee.
- Noncritical rest does not preempt pending loot; critical health still can.
- Ground destinations below 75 yards do not mount, while eligible longer destinations still can.
- Existing quest turn-in recovery tests continue to pass.

After deployment, collect a fresh runtime log and compare these operational targets:

- No artificial 500 millisecond pause when switching `MoveTo` destinations.
- No immediate full-route regeneration solely because an unstick action displaced the character.
- Outside legitimate game cooldowns and casts, target acquisition should normally produce the first eligible combat action within 500 milliseconds.
- After a kill, a reachable pending corpse should become the active loot point within 500 milliseconds unless critical survival, combat, or server acknowledgement blocks it.
- No mount cycle for ground destinations shorter than 75 yards.
- No routine rest before pending loot above 30 percent health.
- Slow path generation is visible as one timed diagnostic rather than an unexplained stall.

These are control-loop latency targets, not guarantees that network, server, terrain, or game mechanics complete the resulting action within the same interval.

## Rollout and rollback

Build and test after each subsystem stage. Deploy the resulting assembly only after the running process is closed, retaining a timestamped backup of the previous deployed binary and configuration.

If fresh logs show a regression, revert only the responsible subsystem's commit or restore the previous binary. Navigation, combat, and loot/rest changes must not depend on one another for correctness, so each stage remains independently removable.
