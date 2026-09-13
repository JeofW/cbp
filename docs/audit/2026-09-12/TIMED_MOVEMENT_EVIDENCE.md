# Timed movement ownership — test-first investigation

Integrated baseline c19e730b774bb2cc982bce0c739b25515f86c6a9. Existing duration moves are already queued; this is NOT a duration-sleep rewrite. Source: Styx/WoWInternals/WoWMovement.cs TimedMovementSchedule and public Move/MoveStop/StopMovement entry points.

The scheduler replaces only identical bit masks. Renewing Forward after Forward|StrafeLeft leaves an older composite expiry able to stop the renewed direction. Conversely a composite renewal leaves old individual deadlines. Public untimed start and explicit stop do not remove earlier pending timers. The global stop's no-player early return also leaves timers alive. Duration overflow is detected only after start dispatch.

Eleven scenarios exercise the real schedule and public entry points without a client, including a 2,000-operation seeded reference-model test. Source side effects are observed via the actual movement event. Tests are added before implementation; no fixed-by claim before execution.

Proposed bounded repair: deadlines owned independently per direction bit, cancellation before public stop/untimed takeover, global cancellation before no-player early return, and deadline validation before start. This does not alone close the native command race after an expired mask has been removed from the queue, high-level quest/mount arbitration, or blocked injection cancellation. Those remain separate generation/owner and live-thread boundaries, not claims of this slice.
