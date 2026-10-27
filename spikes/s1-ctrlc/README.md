# Spike S1, part 1: CTRL+C delivery through a Runner console

- **Date:** 2026-10-27
- **Machine:** Windows 11 (10.0.26200) dev machine
- **Not covered yet:** the template VM, the service context (Session 0), a different user, a real game

Three small programs mimic the planned process chain ([02 §4](../../docs/02-architecture.md#4-process-model-and-the-runner)):

- `core.exe` starts `runner.exe` with `CREATE_NO_WINDOW` (own console, no window), as Core will.
- `runner.exe` starts `game.exe` on its own console with stdin/stdout redirected to pipes, then stops it in one of five ways.
- `game.exe` prints `ready`; on CTRL+C it "saves" and exits with 42; on the stdin line `exit` it exits with 7.

## Results

| Mode | What the Runner does | Result |
|---|---|---|
| `handler` | Registers a handler that returns TRUE, then `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)` | ✗ Neither the game nor the Runner's handler saw CTRL+C: the ignore flag was inherited from the parent process chain |
| `handler-reset` | `SetConsoleCtrlHandler(NULL, FALSE)` first, then as above | ✓ The game saved and exited with 42; the Runner survived; output arrived through the pipe |
| `stdin` | Writes `exit` to the game's stdin | ✓ The game exited with 7 |
| `ignore-null` | `SetConsoleCtrlHandler(NULL, TRUE)` before starting the game | ✗ The game ignores CTRL+C (the flag is inherited) |
| `newgroup` | Starts the game with `CREATE_NEW_PROCESS_GROUP` | ✗ The Runner gets the event, the game does not |

The `handler` result depends on the parent: started from a plain PowerShell window it may pass. That is the reason the Runner resets the flag itself instead of trusting whoever started it.

Conclusion, recorded in [02 §4.3](../../docs/02-architecture.md#43-graceful-stop): the Runner MUST call `SetConsoleCtrlHandler(NULL, FALSE)` before starting the game, MUST NOT call `SetConsoleCtrlHandler(NULL, TRUE)` before starting it, and MUST NOT start it with `CREATE_NEW_PROCESS_GROUP` or `CREATE_NEW_CONSOLE`.

References: Microsoft Learn — [SetConsoleCtrlHandler](https://learn.microsoft.com/en-us/windows/console/setconsolectrlhandler), [GenerateConsoleCtrlEvent](https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent), [Process Creation Flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags).

## Run it

Only the .NET Framework C# compiler that ships with Windows is needed:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
foreach ($n in "game", "runner", "core") { & $csc /nologo /out:"$n.exe" "$n.cs" }
.\core.exe "$PWD\runner.exe" "$PWD\game.exe"
```

`core.exe` gives each Runner its own console, so the CTRL+C never reaches the window you run it from. The `.exe` files are ignored by git.

## Still open

- **S1 part 2:** the same chain on the template VM, with Core as a service in Session 0, and real Valheim. The official Valheim guide says to stop the server with CTRL+C; check that the world is saved.
- **S3:** the Runner started as `gamewarden-run` through the [ADR-0008](../../decisions/0008-run-as-user-and-launch-chain.md) chain.
