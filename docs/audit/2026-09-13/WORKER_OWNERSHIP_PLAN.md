# Worker interruption and restart ownership — continuation

Baseline: 25993efe4a83455012e36339879d8bd26a8e5f02, containing the ten-suite integration plus the separately reproduced cast-dispatch repair.

## Observed and source-supported boundaries

The captured 2026-09-12_1701_45740.log at 18:22:01.228 records Stop, followed by a native strafe attempt at .575 and a ThreadInterruptedException whose stack reaches TreeSharp.Composite.Tick. The current Composite.Tick converts every exception into Failure, allowing a PrioritySelector to try another child. TreeRoot.SafeAction catches the signal again. This source contract is independently reproducible; it is not proof of an additional post-interruption movement in that exact capture.

TreeRoot.Start has another concrete ownership hazard: after Join(5000) times out, it forces Stopped despite the previous worker still being alive. A self-restart also falls through to that assignment. Releasing the state monitor around Join without a finally leaves it unbalanced if Join is interrupted. After reacquiring the monitor, the code does not recheck worker identity or lifecycle state, so a delayed request can overwrite a newer owner's state. RaiseBotStarted is outside WorkerThread's try/finally, so a throwing subscriber bypasses normal cleanup.

## Focused repair contract

Keep existing public APIs and the five-second maximum waiting behavior. Never admit startup while the previous worker is alive. A stopping worker cannot restart itself. Reacquire the state monitor in finally, and after waiting verify the same worker and an admissible state before any transition. Dispatch started callbacks inside worker cleanup ownership.

ThreadInterruptedException is a run-stop signal, not a behavior failure. Composite.Tick must preserve it while releasing owned cleanup; a secondary cleanup exception cannot replace it and authorize fallback. Start and SafeAction must propagate it without ordinary-error logging. Ordinary exceptions retain existing diagnostics and failure/fallback behavior. Do not blanket-treat OperationCanceledException as whole-run cancellation: existing per-behavior cancellation has a different owner.

## Test-first verification

WorkerOwnershipRegressionTests calls the actual public Start method, private SafeAction/WorkerThread delegates and real TreeSharp objects. Real managed threads verify timeout refusal, self-start refusal, interrupted joins, and identity changes while the state lock is released. A real Thread.Interrupt interrupts an actual managed wait. Event callbacks and BotBase are isolated fixtures; no client is attached or simulated as attached. Tests restore captured static state and join every fixture thread before restoration. The full core run executes these cases once; the separate routine-compatibility process does not repeat the five-second timeout.

Run the test-only commit, inspect failures and passing ordinary-error controls, then change only TreeRoot.cs and Composite.cs. Rerun the targeted cases and all ten combined validation entries. Record exact source hashes, run/artifact IDs and output. No new native worker, task scheduler, binary or public signature is introduced.

## Retained boundaries

This does not cancel a command already inside native injection, establish generation ownership for asynchronous/plugin commands, or repair every Lua/spell wrapper that can swallow an interruption. It prevents the reproduced restart and central-tree fallback hazards, not all possible stop races. It does not change the selected BotManager instance or solve a mid-tick bot-selection ownership change. These remain separate cross-layer work rather than being hidden by this regression set.
