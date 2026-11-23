# Documentation

This folder is the specification of GameWarden: what the product is, how it works, what is built, and what comes
next. It is written for the Owner, for contributors who find the repository, and for coding agents that work in it.

Three rules keep it usable:

- **Each document opens with its status** (accepted, built, proposal) and, when it describes something that is not
  built, a **Not built yet** list before the design.
- **The code is what exists.** A doc or ROADMAP item says "built" only for behaviour the code has, with the pull
  request number. If a doc and the code disagree, fix the doc in the same change.
- **Decisions are ADRs.** A choice that changes direction becomes a new file in [`decisions/`](../decisions/), not
  an edit to an accepted one.

The plan and exit criteria live in [ROADMAP.md](../ROADMAP.md). Working rules for coding agents live in
[CLAUDE.md](../CLAUDE.md); this page is the map, not a second rulebook.

## Start here

| I want to… | Read in this order |
|---|---|
| Understand the product and its boundaries | [01](01-vision-and-scope.md) → [02](02-architecture.md) → [07](07-ux-and-user-flows.md) → [ROADMAP](../ROADMAP.md) |
| Install it or operate the VM | [08](08-operations.md) → [10 §5–8](10-build-test-release.md#5-first-install-on-the-vm) → [06 §2–4](06-security-and-access.md#2-network-exposure) |
| Change Core, the API or the database | [02](02-architecture.md) → [03](03-domain-model.md) → [05](05-api.md) → [06](06-security-and-access.md) → [10 §2–3](10-build-test-release.md#2-test-strategy) |
| Change the panel | [11](11-web-panel.md) → [07](07-ux-and-user-flows.md) → [22](22-panel-ux-review.md) → [12](12-explanations.md) → [panel/CONTRIBUTING](../panel/CONTRIBUTING.md) |
| Add or fix a game adapter | [04](04-adapter-spec.md) → [16](16-game-integrations.md) → [13](13-mods.md) / [14](14-palworld.md) / [17](17-valheim-console-integration.md) |
| Work on agents, Discord or notifications | [15](15-agent-access.md) → [20](20-operations-agent.md) → [21](21-operations-kernel.md) → [18](18-discord-notifications.md) → [19](19-system-log.md) |
| Work on users, roles or access (M6) | [06 §4–5](06-security-and-access.md#4-authentication) → [ADR-0006](../decisions/0006-rbac-with-separate-owner.md) → [23](23-users-and-roles.md) |
| Cut a release or fix CI | [10](10-build-test-release.md) → [ADR-0012](../decisions/0012-ci-and-tests-on-github-hosted-runners.md) → [docs/releases](releases/README.md) |
| Decide what to build next | [09](09-open-questions.md) → [ROADMAP](../ROADMAP.md) → the ADRs below |

## The specification

| Doc | What it answers | Read it when |
|---|---|---|
| [01 · Vision and scope](01-vision-and-scope.md) | Problem, vision, goals, non-goals, personas, constraints, success criteria, glossary | First, before any change |
| [02 · Architecture](02-architecture.md) | Context, components, Core modules, process model, platform ports, tech stack, environments | Before changing Core or the process model |
| [03 · Domain model](03-domain-model.md) | Entities, instance state machine, jobs, events, activity rules | Before changing state or persistence |
| [04 · Adapter specification](04-adapter-spec.md) | Adapter format, settings schema, hooks, support levels L0–L3, examples | Before adding or changing a game |
| [05 · API](05-api.md) | REST and realtime API, authentication, permission per endpoint, errors | Before adding an endpoint or calling one |
| [06 · Security and access](06-security-and-access.md) | Threat model, network exposure, authentication, RBAC, secrets, audit | Before touching auth, files, processes or user data |
| [07 · UX and user flows](07-ux-and-user-flows.md) | Lifecycle, navigation, screens, flows, "is it running" visibility | Before designing a screen or a flow |
| [08 · Operations](08-operations.md) | Filesystem layout, prerequisites, install/upgrade, backups, logs | Before changing install, storage or logging |
| [09 · Open questions](09-open-questions.md) | Open and closed questions, spikes, answers and dates | Before assuming an unverified fact |
| [10 · Build, test and release](10-build-test-release.md) | CI, test strategy, versioning, release assets, install, self-update, manual VM tests | Before changing CI, packaging or a release |
| [11 · Web panel](11-web-panel.md) | Panel scope, functions, layout, visual style, stack, working rules | Before changing `panel/` |
| [12 · Explanations](12-explanations.md) | The help drawer: one pattern, topics and sources | Before adding a setting or a choice |
| [13 · Mods](13-mods.md) | Mod sets, Thunderstore, BepInEx, r2modman codes, config revisions | Before changing mods |
| [14 · Palworld](14-palworld.md) | Palworld settings into its INI, REST readiness and stop, phases | Before changing Palworld |
| [15 · Agents](15-agent-access.md) | Agent clients, tokens, permissions, diagnostics windows, audit, MCP bridge | Before changing the Agent API or MCP |
| [16 · Game integrations](16-game-integrations.md) | One provider registry, one panel tab, per-game providers | Before adding a game API |
| [17 · Valheim console integration](17-valheim-console-integration.md) | Players from console events, admins from `adminlist.txt` | Before changing Valheim players or admins |
| [18 · Notifications and Discord](18-discord-notifications.md) | Provider-neutral events and outbox, Discord adapter, modes, delivery log | Before changing notifications |
| [19 · System log](19-system-log.md) | Sources, schema, query surface, retention, redaction | Before adding logging or diagnostics |
| [20 · Operations Agent](20-operations-agent.md) | The in-Core Agent, Discord surface, tools, approvals, audit | Before changing the bot or its tools |
| [21 · Operations Kernel](21-operations-kernel.md) | Capabilities, policy, incidents, runbooks, traces (target design) | Before extending agent capabilities |
| [22 · Panel UX review](22-panel-ux-review.md) | Screenshot review, findings, research, delivery batches | Before a panel redesign or a UX pull request |
| [23 · Users and roles](23-users-and-roles.md) | Research and options for M6: identity, invites, roles, scopes, panel UX | Before starting M6 or changing the RBAC design |

## Decisions (ADRs)

An ADR records **why** a choice was made, who made it, and what was rejected. Accepted ADRs are superseded, not
rewritten. New decisions take the next number from a fresh `origin/main`. Start with the template:
[decisions/0000-template.md](../decisions/0000-template.md).

**Product and scope**

- [0001](../decisions/0001-build-own-platform.md) — build our own platform instead of adopting an existing manager
- [0004](../decisions/0004-web-panel-only-no-tray.md) — web panel only, no tray app
- [0011](../decisions/0011-mvp-scope-and-direction.md) — the MVP is a single-Owner tool; the project is open source
- [0018](../decisions/0018-backups-leave-the-mvp.md) — backups, schedules and safe updates leave the MVP
- [0019](../decisions/0019-discord-joins-the-backlog-mods-next.md) — Discord joins the backlog; mods, then Palworld
- [0027](../decisions/0027-development-releases-move-forward.md) — development releases move forward only

**Runtime, Windows and release**

- [0002](../decisions/0002-core-as-csharp-windows-service.md) — Core is a C# / .NET 10 Windows Service
- [0003](../decisions/0003-runner-process-per-instance.md) — one Runner process per running instance
- [0007](../decisions/0007-declarative-adapters-with-hooks.md) — declarative adapters with optional PowerShell hooks
- [0008](../decisions/0008-run-as-user-and-launch-chain.md) — games, SteamCMD and hooks run as `gamewarden-run`
- [0009](../decisions/0009-data-root-acl-and-secrets.md) — explicit ACL; secrets readable only by SYSTEM
- [0012](../decisions/0012-ci-and-tests-on-github-hosted-runners.md) — CI tests what hosted runners can; Windows by hand on the VM
- [0013](../decisions/0013-own-setup-and-update-engine.md) — own setup/update engine, signed manifest
- [0014](../decisions/0014-service-host-with-preshutdown.md) — service host that handles pre-shutdown
- [0016](../decisions/0016-windows-as-an-integration-layer.md) — Windows behind ports around a portable Core
- [0017](../decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md) — develop on Linux; test Windows on CI and the VM
- [0025](../decisions/0025-steamcmd-repairs-a-stuck-manifest.md) — SteamCMD repairs a stuck manifest after `0x6`
- [0034](../decisions/0034-bounded-stop-and-a-runner-kill.md) — a bounded stop, with the Runner kill as the last resort

**Panel**

- [0010](../decisions/0010-panel-react-typescript.md) — a React + TypeScript SPA built with Vite
- [0015](../decisions/0015-panel-mantine-and-toolchain.md) — Mantine 9, TanStack Router and a generated API client
- [0029](../decisions/0029-panel-ux-from-the-ux-playbook-skill.md) — panel UX follows the pinned `ux-playbook`
- [0035](../decisions/0035-task-first-navigation-activity-and-phone-bottom-bar.md) — Activity at the top level; four-item phone bar

**Security, users and agents**

- [0005](../decisions/0005-http-only-over-vpn.md) — HTTP only, reachable only over the VPN
- [0006](../decisions/0006-rbac-with-separate-owner.md) — scoped roles; Owner is separate from Admin
- [0023](../decisions/0023-agent-clients.md) — Agent access is a first-class, scoped, audited feature

**Games, mods and integrations**

- [0020](../decisions/0020-mods-in-set-folders-as-pinned-sets.md) — mods in set folders, exchanged as r2modman codes
- [0021](../decisions/0021-mod-configs-edited-with-revisions.md) — mod config files edited in the panel, with revisions
- [0022](../decisions/0022-config-files-rendered-by-core-servers-controlled-over-http.md) — settings reach config files through renderers in Core
- [0024](../decisions/0024-game-apis-through-a-provider-registry.md) — game APIs through one provider registry
- [0026](../decisions/0026-valheim-console-events-and-adminlist.md) — Valheim players from the console; admins from `adminlist.txt`

**Operations, notifications and logs**

- [0028](../decisions/0028-one-queryable-system-log.md) — one queryable system log for the Owner and the Agent
- [0030](../decisions/0030-operations-agent-in-core.md) — the Owner's Operations Agent runs inside Core
- [0031](../decisions/0031-discord-gateway-bot.md) — Discord bot over the Gateway, one channel per server
- [0032](../decisions/0032-notifications-through-a-provider-neutral-outbox.md) — provider-neutral events and one durable outbox
- [0033](../decisions/0033-operations-kernel.md) — the Operations Kernel between surfaces and domain services

## Other sources

| Folder or file | What is in it |
|---|---|
| [diagrams/](diagrams/README.md) | Ten Excalidraw diagrams: context, modules, processes, ACL, states, flows, release. The files are the source. |
| [panel/](panel/) | Screenshots and evidence behind [22](22-panel-ux-review.md) and [12](12-explanations.md); organised by phase and feature, not a spec |
| [releases/](releases/README.md) | Release notes, the note template and how the development line is tagged |
| [../spikes/](../spikes/) | Small experiments behind decisions (Ctrl+C, Doorstop, Palworld); not shipped |
| [../panel/CONTRIBUTING.md](../panel/CONTRIBUTING.md) | Panel patterns, commands, tests and pull-request screenshots |
| [../panel/openapi/gamewarden-api.json](../panel/openapi/gamewarden-api.json) | Generated OpenAPI document; the panel client is generated from it |
| [../SECURITY.md](../SECURITY.md) | How to report a vulnerability; supported versions |
| [../ROADMAP.md](../ROADMAP.md) | Milestones, exit criteria, backlog and "other ideas" |

## For coding agents

The canonical working rules are [CLAUDE.md](../CLAUDE.md); read it first. This section only routes you to the right
spec. A repository's own guide or `.claude/agents/` definitions win where they differ.

Before you edit:

1. Read [01](01-vision-and-scope.md) and [02](02-architecture.md) once per session, then the task row.
2. Read [06](06-security-and-access.md) before anything that touches auth, permissions, secrets, files, processes
   or network access.
3. Read [10](10-build-test-release.md) before changing tests, CI, packaging or a release.

| Task | Spec to read | Decisions to respect |
|---|---|---|
| Core service, state, jobs, storage | [02](02-architecture.md), [03](03-domain-model.md), [05](05-api.md), [08](08-operations.md) | 0002, 0003, 0014, 0016, 0017, 0025, 0034 |
| API endpoint or generated client | [05](05-api.md), [11 §9](11-web-panel.md#9-data-flow) | 0015 |
| Panel screen or flow | [11](11-web-panel.md), [07](07-ux-and-user-flows.md), [22](22-panel-ux-review.md), [12](12-explanations.md), [panel/CONTRIBUTING](../panel/CONTRIBUTING.md) | 0010, 0015, 0029, 0035 |
| Game adapter or provider | [04](04-adapter-spec.md), [16](16-game-integrations.md), [14](14-palworld.md), [17](17-valheim-console-integration.md) | 0007, 0022, 0024, 0026 |
| Mods | [13](13-mods.md), [04 §7](04-adapter-spec.md#7-mods) | 0020, 0021 |
| Users, roles, invites (M6) | [06 §4–5](06-security-and-access.md#4-authentication), [23](23-users-and-roles.md), [07 §9](07-ux-and-user-flows.md#9-role-aware-examples) | 0006 |
| Agent access or MCP | [15](15-agent-access.md), [19](19-system-log.md) | 0023, 0028 |
| Operations Agent, Discord, notifications | [20](20-operations-agent.md), [21](21-operations-kernel.md), [18](18-discord-notifications.md), [19](19-system-log.md) | 0030, 0031, 0032, 0033 |
| Install, update, release, CI | [10](10-build-test-release.md), [08](08-operations.md), [releases/](releases/README.md) | 0012, 0013, 0027 |

Hard rules that matter in every task:

- **Never commit secrets** (passwords, webhook URLs, tokens, signing keys). The repository is public.
- **Nothing that changes the machine runs on the development machine**: no `Setup.exe`, no service install, no
  user or firewall changes, no admin-only tests. Those run on the VM.
- **Work on a branch and open a pull request**, docs-only changes too; never push to `main`, never force-push.
- **Follow the published pattern** of a library or framework. A deviation needs a reason in
  [11](11-web-panel.md) (panel) or an ADR (architecture).
- **Docs match the code.** Update the status, add the PR number when something becomes built, and list what is not
  built at the top. New direction → new ADR.

Before you open the pull request:

```bash
node build/check-docs.mjs                     # links, diagrams, ADR numbers
dotnet test --solution GameWarden.slnx        # if you touched .NET
cd panel && npm test                          # if you touched the panel
```

Panel changes also need the screenshots described in [11 §10](11-web-panel.md#10-working-rules), and every string
in both `en.ts` and `vi.ts`. After an API change, rebuild Core, run `npm run gen:api` and commit the generated
document and client.
