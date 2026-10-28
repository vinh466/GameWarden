# ADR-0014: Own service host that handles SERVICE_CONTROL_PRESHUTDOWN

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

- When Windows shuts down, a service that does not accept pre-shutdown gets only the `SHUTDOWN` notification and a short time before it is ended. Valheim needs time to save its world when stopped.
- A service that accepts `SERVICE_CONTROL_PRESHUTDOWN` is notified earlier, and the service control manager waits until it stops or until its pre-shutdown timeout expires. Since Windows 10 1703 the default pre-shutdown timeout is 10 seconds; a service raises it with `ChangeServiceConfig2(SERVICE_CONFIG_PRESHUTDOWN_INFO)`. A service that handles pre-shutdown no longer receives `SHUTDOWN` (Microsoft Learn: service control handler function, `SERVICE_PRESHUTDOWN_INFO`).
- .NET's `ServiceBase` and `Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceLifetime` accept stop and shutdown but have no pre-shutdown support (dotnet/runtime sources, read 2026-10-27).
- The hypervisor that hosts the VM may also force the VM off after its own shutdown timeout **[Unverified: value of the VM host]**.

## Decision

1. Core uses its own `IHostLifetime` implementation for Windows services: `StartServiceCtrlDispatcher` + `RegisterServiceCtrlHandlerEx` via P/Invoke, accepting `STOP` and `PRESHUTDOWN`. Both trigger the normal host shutdown, which stops every instance gracefully and in parallel, keeping desired state.
2. The Setup engine sets the pre-shutdown timeout to 120 s **[Unverified: enough for Valheim; measured in the VM checklist]** with `ChangeServiceConfig2`.
3. While stopping, Core reports `SERVICE_STOP_PENDING` with increasing checkpoints and a wait hint.
4. Defence in depth: the Valheim adapter passes `-saveinterval` with a shorter value than the 1800 s default (OQ-32), and the operations doc requires the hypervisor's shutdown timeout to be longer than the pre-shutdown timeout.

## Consequences

- A VM shutdown or a Windows Update reboot saves the worlds, provided the hypervisor waits long enough.
- About a hundred lines of interop to write, unit-test (control code → host stop) and verify on the VM (checklist step 5 in [10 §8](../docs/10-build-test-release.md#8-manual-tests-on-the-vm)).
- Shutdown can be held up by up to the timeout; Microsoft asks services to use pre-shutdown only when needed — saving game worlds is such a case.

## Alternatives considered

- **`WindowsServiceLifetime` as is** — only `SHUTDOWN`, a few seconds: worlds may not be saved.
- **Rely on the game's autosave only** — loses up to the save interval on every reboot.
- **Group Policy shutdown scripts** — run outside the service, need machine policy changes, and are hard to test.

## Implementation notes (2026-10-28)

- Decision 1 read literally ("both stop every instance") would contradict [ADR-0003](0003-runner-process-per-instance.md) and the VM checklist ([10 §8](../docs/10-build-test-release.md#8-manual-tests-on-the-vm) step 4: a service restart leaves the game running and Core reattaches), and updates would stop every game. As built, both controls trigger the normal host shutdown, but only `PRESHUTDOWN` makes the supervisor stop the games: gracefully, in parallel, within 110 s, keeping their desired state so they start again after boot. `STOP` (service restart, update, uninstall's last step) detaches and leaves the games running. Uninstall stops the games itself through Core's clean-up call first.
- `src/GameWarden.Core/Hosting/WindowsServiceLifetime.cs`; the control-code mapping is unit-tested (`ProductionPartsTests`), the graceful stop on shutdown and the restart after "boot" by `SupervisorTests.Windows_shutdown_saves_the_world_and_the_game_comes_back_after_boot`. `HostOptions.ShutdownTimeout` is 150 s so the host never cuts the 110 s short.
