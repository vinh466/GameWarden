# ADR-0002: Core is a C# / .NET 10 Windows Service

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

The Core must run without anyone logged in, survive reboots, manage Windows processes and consoles, edit firewall rules, and serve an API + web panel.

## Decision

Implement Core in C# on .NET 10 (LTS), hosted with `Microsoft.Extensions.Hosting.WindowsServices`, using ASP.NET Core (minimal APIs + SignalR) and SQLite via EF Core. Publish self-contained so the VM needs no separate .NET install.

## Consequences

- First-class Windows APIs: services, Job Objects, consoles, named pipes, DPAPI, firewall.
- One process hosts API, realtime, scheduler and panel files.
- Self-contained publish is larger on disk (tens of MB) — acceptable.

## Alternatives considered

- **Go** — small binary, low RAM; more hand-written Windows interop.
- **Node / Python** — packaging as a Windows service and Windows interop are weaker.
- **PowerShell module** — fine for scripts, not for a long-running supervisor with an API.
