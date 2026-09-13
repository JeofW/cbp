# Shared routine input boundary and yielding — test-first slice

Baseline 292e42add337a3dc2cdcda6a84910ea9c6cfe7f6. Shared Helpers/Spell.cs evaluates target-dependent requirements before rejecting a missing selected unit in both name/ID Cast paths, and Buff paths call missing selectors. TreeSharp.Composite.Tick catches/logs exceptions and reports Failure, so a Failure-only test would falsely pass. The fixture also asserts callback counts and absence of exception logs.

The Shadow Priest Normal, Battleground and Instance factories each reference a blocking Thread.Sleep(100) action. The fixture resolves those stable factories' IL references without compiler-lambda-name assumptions, executes the real delay component and checks yielding, cancellation, 500 pending ticks and single follow-on execution. This is not complete client-bound rotation composition or DPS validation.

Thirteen scenarios are added before production changes. The explicit --routine-compatibility entry compiles the tracked Singular source for these checks in addition to the existing unchanged compatibility checks. No production test hook or fake game connection is added.

Intended repair is narrow: reject invalid helper configuration/metadata before callbacks; capture and reject a missing selected target before dependent requirements; keep false requirements from reaching native work; make Buff selector guards explicit; replace all three Sleep actions with a shared existing WaitContinue-based 100ms delay. Selected target lifetime across multi-tick cast setup, dispatch-versus-server-acceptance, all-spec decisions and Retribution priority remain separate work, not claimed fixed here.
