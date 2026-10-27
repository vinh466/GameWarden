# ADR-0003: One Runner process per running instance

- **Status:** Accepted (mechanics to be validated in spike S1)
- **Date:** 2026-10-27

## Context

- Restarting or upgrading Core must not stop game servers.
- Many servers save only on Ctrl+C or a stdin command. Windows only delivers Ctrl+C to processes sharing a console; a service has none, and a process started by a dead parent loses its stdin pipe.

## Decision

Core launches a small **Runner** per instance (detached from Core). The Runner owns a hidden console, starts the game inside a Job Object (kill-on-close) on that console, relays stdin/stdout, and exposes a named pipe (`\\.\pipe\gamewarden.runner.<instanceId>`). Core reattaches to Runners on start. Runner binaries are versioned per install.

## Consequences

- Core restarts/upgrades keep games alive; graceful stop methods (Ctrl+C, stdin) keep working.
- No orphaned game processes: if a Runner dies, its Job Object kills the game.
- Extra moving part: a pipe protocol to version and test.

## Alternatives considered

- **Core starts games directly** — simplest, but Core restart = lost control or killed games.
- **Run each game as its own Windows service (e.g. NSSM/WinSW)** — survives Core, but Ctrl+C/stdin control and live console are awkward, and services clutter the system.
