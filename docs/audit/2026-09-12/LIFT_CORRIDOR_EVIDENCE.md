# N05 — separate approach and actual boarding authorization

Baseline 292e42add337a3dc2cdcda6a84910ea9c6cfe7f6 includes the previous continuous safety-revocation fix. MeshNavigator.HandleElevator still supplies a corridor-to-WaitPoint result as boardingPathSafe while MoveToBoard targets the live platform. Earlier dock and landing validation is relevant counterevidence but does not validate the current commanded segment.

The new fixture runs the actual old controller with the same two inputs as the baseline caller, then uses the distinct-permission overload once available. This is a deterministic boundary replay, not an attached-client simulation or proof of native ground support. Baseline source wiring is explicitly preserved in the compatibility bridge; it does not substitute a model of the controller.

Fourteen scenarios cover both travel directions, safe approach with unsafe boarding, active permission revocation and fresh dwell, approach while the lift is absent, unsafe approach, selected attachment, non-finite dock/landing/player/live inputs, 500 seeded permission checks, a complete safe crossing and reset. Existing eleven lift fixtures remain. This test-only commit precedes repair and must exhibit the expected failures before implementation.

Design: keep the nine-argument compatibility entry point for existing pure callers, delegating to a ten-argument policy with separate approach-to-wait, boarding-to-live-platform and exit-to-landing inputs. The real MeshNavigator caller must use the distinct inputs and validate the live target only when a platform is present at the source dock. Safe approach must remain possible with the lift absent. Reject non-finite configuration and fail closed on non-finite observations without treating a deliberately unavailable Empty platform as invalidating a safe approach.

No transport coordinates, selection radii, quest data or live observation are fabricated. The actual collision API's platform support and timing, bounded acquisition, efficient complete-route selection and live end-to-end acceptance remain distinct gates.
