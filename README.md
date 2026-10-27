# GameWarden

Self-hosted control panel for running Steam dedicated game servers on a single Windows machine, for a small group of friends.

**Status (2026-10-27):** design / spec review. No code yet. Next step: review the spec, close the open questions, then start milestone M0 in [ROADMAP.md](ROADMAP.md).

## At a glance

| Concern | Choice | Details |
|---|---|---|
| Host | One Windows VM (template, scaled up as needed) | [01-vision-and-scope](docs/01-vision-and-scope.md) |
| Core | C# / .NET 10, runs as a Windows Service | [ADR-0002](decisions/0002-core-as-csharp-windows-service.md) |
| Process control | One small *Runner* process per game server | [ADR-0003](decisions/0003-runner-process-per-instance.md) |
| UI | Web panel served by Core; no tray app | [ADR-0004](decisions/0004-web-panel-only-no-tray.md) |
| Access | HTTP only, reachable only over VPN (Tailscale or Radmin) | [ADR-0005](decisions/0005-http-only-over-vpn.md) |
| Users | Owner + friends with roles (Admin / Operator / Player) | [ADR-0006](decisions/0006-rbac-with-separate-owner.md) |
| Game support | Per-game adapters: JSON + optional PowerShell hooks, levels L0–L3 | [ADR-0007](decisions/0007-declarative-adapters-with-hooks.md) |
| Why not WindowsGSM / AMP | Build own platform | [ADR-0001](decisions/0001-build-own-platform.md) |

## Names

| Thing | Name |
|---|---|
| Product | GameWarden |
| Windows service | `GameWardenCore` |
| CLI | `gamewarden` |
| Default data root | `C:\GameWarden` |
| Default panel URL | `http://<vpn-ip>:8080` (port is an open question) |

## Layout

```
GameWarden/
├─ README.md                      you are here
├─ ROADMAP.md                     milestones + exit criteria
├─ docs/
│  ├─ 01-vision-and-scope.md      problem, goals, non-goals, personas, constraints, glossary
│  ├─ 02-architecture.md          components, Core modules, runner, process model, tech stack
│  ├─ 03-domain-model.md          entities, instance state machine, jobs, events
│  ├─ 04-adapter-spec.md          adapter format, settings schema, hooks, mods, examples
│  ├─ 05-api.md                   REST + realtime API, auth, errors
│  ├─ 06-security-and-access.md   threat model, network, RBAC, secrets, audit
│  ├─ 07-ux-and-user-flows.md     lifecycle, screens, flows, "is it running" visibility
│  ├─ 08-operations.md            filesystem, prerequisites, install/upgrade, backups, logs
│  └─ 09-open-questions.md        decisions still needed
└─ decisions/                     ADRs (why things are the way they are)
```

Planned (not created yet): `src/`, `adapters/`, `tests/`, `build/` — see [02-architecture](docs/02-architecture.md#9-solution-layout-planned).

## Reading order

1. [01-vision-and-scope](docs/01-vision-and-scope.md)
2. [02-architecture](docs/02-architecture.md) and [decisions/](decisions/)
3. [07-ux-and-user-flows](docs/07-ux-and-user-flows.md)
4. [03-domain-model](docs/03-domain-model.md), [04-adapter-spec](docs/04-adapter-spec.md), [05-api](docs/05-api.md)
5. [06-security-and-access](docs/06-security-and-access.md), [08-operations](docs/08-operations.md)
6. [09-open-questions](docs/09-open-questions.md) and [ROADMAP](ROADMAP.md)

## Conventions

- **Facts vs assumptions.** Anything not verified is tagged **[Unverified]**. Measured facts carry the date they were measured.
- **Dates** are absolute (`YYYY-MM-DD`).
- **MUST / SHOULD / MAY** in specs follow RFC 2119 meaning.
- **Secrets are never committed** (passwords, webhook URLs, tokens).
- **New decision → new ADR** (copy [decisions/0000-template.md](decisions/0000-template.md)). Don't rewrite accepted ADRs; supersede them.
- Docs are written in English.
