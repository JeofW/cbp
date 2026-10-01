# Collision-recovery continuation plan

Goal: finish the exact preserved collision work on current master, preserving bounded temporary ownership and conservative observations through reentrant callbacks and retries.

Spec: the user's continuous CopilotBuddy audit request, the preserved seven-file implementation/tests, and original-build12340 research policy. Source baseline: PR90 merge c2f70e4c; original work is retained in a2c07d21 and reconciliation merge75cdf422. Stack: C#/.NET10 Windows x86, existing regression runner and controlled native-boundary tests.

## 1. Recover remaining failures

- Recover the already-applied fixture correction to the actual three-argument `AddGlobalBlackspot` API; preserve its permanent-registration assertion and existing receipts instead of repeating the edit.
- Run complete blackspot-manager and actual collision/route-owner tests. Record behavioral failures separately from fixture/compilation errors and retain prior receipts.
- Inspect manager registration, expiry, polygon marking/restoration and every external callback between observation and mutation. Include overlap, permanent promotion, map changes, zero-map validity, provider/session changes, failed restoration and UNKNOWN original area/flags.

## 2. Repair the smallest ownership gaps

- Extend only the existing manager/route ownership mechanisms needed by failing cases; do not manufacture original polygon area/flags, silently drop a failed restoration or allow a stale callback to mark/restore another owner's region.
- A temporary lease must expire without deleting independently registered permanent/global avoidance. Repeated observations must not slide its deadline forever.
- Preserve cancellation and explicit Stop; native submission is not proof of successful restoration. Add positive and adversarial cases where the new failure family requires them, including callback boundaries introduced by a fix.
- Keep collision connectivity, original-client terrain authority and controlled native test observations separate in evidence.

## 3. Validate and deliver independently

- Run focused then relevant full local/runtime-source tests and current 4,335-quest accounting without promoting quest classifications.
- Review final source diff, publish a reasonably scoped PR, verify exact-SHA Windows/x86 artifacts and only merge when green.
- Build/smoke the merged revision and deploy changed runtime payload through the verified backup/hash/protected-state process. Do not interfere with PR90's separate release transaction or unrelated user state.
- Record live-only collision/route limits and continue the next highest-impact live incident, including action acknowledgements, debris travel, death and recovery quarantine.

Review focus: reentrant native/logging callbacks, owner replacement during mark/restore, overlapping permanent and temporary owners, map/provider identity and actual pre/post native acknowledgements. No subsystem or PR ends the overall audit.
