# ADR-0034: A bounded stop, with the Runner kill as the last resort

- **Status:** Accepted — the Owner approved the proposal on 2026-11-20; the stop ladder is the agent's design.
- **Date:** 2026-11-20

## Context

On 2026-11-20 a restart of `valheim-main` stayed in `Stopping` for 4 m 39 s. The evidence:

- The game received `CTRL+C` at 09:44:54, ran `OnApplicationQuit`, saved the world at 09:44:55 and shut every
  subsystem down by 09:45:08 — then it never reported an exit.
- At 09:45:54 the Runner wrote `Killed the game and every process it started.` but **no `Game exited`** and no
  kill error. The Runner stayed alive until the Owner killed it at 09:52:42.
- Only then did Core see the pipe close, finish the stop and start a new game. The Runner's Job Object
  (`KILL_ON_JOB_CLOSE`) is what finally ended the game tree.
- Core had no deadline after its kill request; `RunnerClient` had no request timeout. The instance could stay in
  `Stopping` forever, and the only recovery was Task Manager on the VM.
- A controlled stop the same day exited normally in 19 s (`Game exited with code 0`), so the hang is intermittent
  and comes from the game side, not from a Runner regression (the Runner code is unchanged since dev.21).

The game is Valheim with BepInEx/Doorstop. BepInEx has long-standing reports of a stop hanging after the Unity
shutdown (BepInEx #587, #343, #185); a modded server can finish its save and still not end its process. On Windows,
`Process.Kill` is asynchronous: it requests termination, but a process can remain while pending I/O completes, and
`Exited`/`WaitForExit` are only signalled once it has actually ended. `Process.Kill(entireProcessTree: true)` can
also fail on a descendant (`AggregateException`). The .NET runtime plans to use `TerminateJobObject` for the tree
kill ([dotnet/runtime #126273](https://github.com/dotnet/runtime/issues/126273)); the process-object event is not a
reliable completion signal ([#63328](https://github.com/dotnet/runtime/issues/63328)).

Two smaller findings made the incident harder to debug:

- `AgentFileService.Read` used `File.ReadAllBytes`, which cannot open a log the Runner is still writing; the live
  `runner.log` was unreadable.
- `source=instance` in `AgentLogService` picked the newest `*.log`, usually the game's own log
  (`valheim_server.log`), not the dated Runner console that carries the `GW` lines; the diagnostics bundle did not
  include `runner.log` or the instance's system log.

The Owner asked for a clean root fix that never blocks and that leaves a trace for the next incident, and approved
this proposal on 2026-11-20.

## Decision

1. **A stop is a bounded ladder, not a single request.**
   - **Graceful:** the adapter's stop method, budgeted by its `stop.timeoutSec` (60 s for Valheim).
   - **Hard:** when the budget expires, Core asks the Runner to kill the game tree; ten seconds later the
     **Core kills the Runner process itself** (PID plus start time are checked against the stored values). The
     Runner's Job Object then kills the game and its children; the pipe close finishes the stop.
   - The next state is deterministic: `Stopped` for a stop, a new Runner for a restart. A start has the same
     shape: readiness timeout plus ten seconds, then the Runner is killed.
2. **A watchdog outside the actor mailbox enforces the deadline.** A 5 s `PeriodicTimer` reads the published
   snapshot; when `Starting` or `Stopping` is past `hardKillAt`, it kills the Runner even if the actor is blocked
   in an await. `RunnerClient` also times out every request after 10 s and surfaces it as a timeout, so one hung
   pipe cannot hold the mailbox.
3. **A force stop skips the graceful phase.** `POST /instances/{id}/stop?force=true` (Owner) kills the Runner at
   once from the running or stopping state; the panel offers it only while the instance is `Stopping`, behind a
   confirmation that warns about unsaved progress. The agent's stop accepts the same `force`.
4. **The Runner verifies the kill and records the exit in a `finally`.** `KillGameTree` checks `HasExited` after
   5 s and returns a rejection if the process is still alive; if the process is dead but its `Exited` event was
   missed, the Runner records the exit itself. `OnGameExitedAsync` bounds the log tail and records the exit even
   when a descendant or the tail misbehaves. A stop heartbeat is written every 5 s.
5. **One operation id threads the trace.** Every start, stop, restart and force stop gets `op_<8 hex>`, carried in
   the Runner request, the Core system log, the Runner log and the console `GW` lines. The snapshot exposes
   `operationId`, `stoppingSince`, `hardKillAt` and `stopPhase`; the panel shows the elapsed stopping time and
   the hard-kill time.
6. **The diagnostics read the right, live files.**
   - `AgentFileService.Read` opens with `FileShare.ReadWrite | FileShare.Delete`.
   - `source=instance` is the dated Runner console (`yyyy-MM-dd.log`, the `GW` lines), falling back to the newest
     `*.log`; `source=system` remains the structured system log.
   - The bundle includes `instances/<id>/console.log`, `runner.log`, `runner.previous.log` and `system.log`.
   - `runner.log` rotates at 10 MB and keeps one previous file; the core/console/system-log retention in
     [08 §5](../docs/08-operations.md#5-logging) is unchanged.

## Consequences

- An instance can no longer sit in `Starting` or `Stopping` indefinitely; the user sees the phase and the
  deadline, and can force the stop from the panel. The recovery the Owner had to do by hand becomes automatic.
- The Runner is treated as disposable. Killing it costs a new Runner process on the next start and may lose the
  last unsaved seconds of game progress; the graceful budget is long enough for Valheim's save (about 2 s), and
  the game's own world backups remain.
- The game-side hang itself is not fixed; it is an external BepInEx/Valheim issue. The guarantee is bounded and
  auditable recovery.
- Every stop now writes a little more (one line per phase, a 5 s heartbeat while stopping). The 10 MB rotation
  and the one previous file bound it.
- The pipe protocol gains an optional `operationId` on `SendCtrlC` and `KillGame`; the version negotiation stays
  at 1 because the field is additive and Core and its Runner ship together.
- Tests: a FakeGame that ignores `CTRL+C`, a Core force stop, the timeout ladder, live-file reads, the dated
  console selection and the bundle contents. The Windows-only process tests remain in the Windows tier.

## Alternatives considered

- **Only raise the stop timeout** — a hung process does not exit at any timeout; the state still never settles.
- **Only a Force stop button** — a manual workaround for an unattended server; the watchdog is needed anyway.
- **`TerminateJobObject` on a dedicated game job, keeping the Runner alive** — cleaner in theory but needs nested
  jobs and a second job handle; the Core kill of the Runner already uses the proven `KILL_ON_JOB_CLOSE` boundary
  and is far smaller. It remains a possible later refinement.
- **Kill the game PID from Core** — the game PID has no stored start time, so PID reuse is a risk; the Runner
  PID/start time pair is identity-checked and the Job Object kills the tree.
- **`taskkill`/WMI** — a shell or WMI dependency with the same asynchronous termination semantics and no better
  verification.
