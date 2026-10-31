# ADR-0023: Agent access is a first-class, scoped, audited feature; MCP is a local bridge

- **Status:** Accepted (the Owner approved the direction on 2026-10-31; implementation started the same day).
- **Date:** 2026-10-31

## Context

- The Owner runs OpenCode on the Linux development box. That box is on the same VPN as the Windows VM, so an Agent can reach Core without exposing the panel to the internet.
- The Owner wants an Agent to operate and debug GameWarden directly: read logs, inspect processes, start/stop servers, diagnose failures, and later add its own endpoints and release them.
- The MVP has one Owner and no permission enforcement in code. Handing the CLI token to an Agent would give it the whole panel, no scope, no separate identity and no audit trail.
- The current docs promise permission names and audit, but the code does not enforce them yet. The Agent feature must not depend on the full M6 role system.
- MCP is a tool protocol, not an authorization system. Its HTTP authorization guidance expects OAuth 2.1; a local stdio bridge can use a credential from the environment.
- GameWarden is HTTP-only over the VPN ([ADR-0005](0005-http-only-over-vpn.md)); a bearer token inside WireGuard/Tailscale is acceptable for this single-owner deployment.

## Decision

1. **Agent is a first-class feature**, not a generic PAT. The audited identity is an **Agent client**; a client has zero or more **Agent tokens**. Tokens are credentials and can be rotated or revoked without losing the client's history.
2. **Two surfaces.**
   - `/api/v1/agents` is the Owner's management API (session cookie or loopback CLI only): clients, tokens, settings, audit.
   - `/api/v1/agent` is the runtime API. An Agent token is accepted **only** there; the middleware rejects it on every other API path.
3. **Agent tokens** use the prefix `gw_agent_`, are stored as SHA-256 hashes, are shown once, expire (1–90 days, default 30), can be revoked or rotated, and can be restricted to CIDR ranges.
4. **Permissions are explicit.** Each Agent endpoint declares one permission; a token holds a set of permissions, selected by preset or individually. `TrustedFull` is the only wildcard and is an explicit, warned choice. It lets a newly added Agent endpoint work without a permission edit, which the Owner wants for self-development.
5. **Three switches.**
   - `Agents.Enabled`: the global kill switch. When off, every Agent token gets 403 `agent_access_disabled`.
   - `Diagnostics`: a time-boxed window (default off; 1–480 minutes) required by the `diagnose` permissions. It unlocks core logs, processes, Event Log, network, storage, files and bundles.
   - `Admin`: a time-boxed window (default off; 1–60 minutes) required for destructive/admin permissions when `RequireAdminMode` is on.
   An Agent cannot enable its own windows; only the Owner can.
6. **The authoritative audit is server-side.** Every request to `/api/v1/agent` — reads, writes, denials, failures and auth failures — writes an `AgentAudit` row with client, token, action, target, outcome, status, source IP, request id, duration and redacted details. Audit rows are immutable through the API and kept 180 days.
7. **MCP is a local stdio bridge (`gamewarden-mcp`)** on the machine where the Agent runs. It reads the Agent token from the environment or a `0600` file and calls the REST API over the VPN. No MCP listener is added to Core in v1, avoiding remote MCP auth/token-passthrough complexity and a new internet-facing surface.
8. **No arbitrary shell in Core.** Core runs as SYSTEM; a generic command endpoint in a public web application is an unacceptable blast radius. Break-glass is an optional SSH/JEA channel on the VM, with a dedicated key and firewall rule, outside GameWarden. The Agent uses the REST surface for routine work.
9. **Network.** The Agent route is VPN-only and additionally restricted by the client's `allowedNetworks` and optional global networks. Tailscale or WireGuard is recommended for this channel; Radmin remains acceptable for friends but its encryption is unverified, so it is not the preferred Agent path.
10. **Self-development loop.** The Agent may add an endpoint under `/api/v1/agent`, declare a permission in the catalog, build and test on Linux, release, and call the endpoint. With `TrustedFull` the new permission is immediately usable; otherwise the Owner grants it. MCP tools are curated; one-off endpoints can be called with `curl` and the token file.

## Consequences

- The panel, CLI and full API stay Owner-only; no M6 role refactor is needed for this feature.
- An Agent token cannot accidentally call `/setup`, `/owner/password`, `/system/uninstall-cleanup` or the management API.
- The Owner can pause one client, revoke one token, or kill the whole feature without restarting Core.
- The feature adds a new SQLite table set, a migration, a permission catalog, an audit pipeline, a local MCP binary and documentation.
- Redaction is a prerequisite: process command lines (Valheim `-password`), Palworld INI secrets, logs and config files must pass through the redactor before they reach an Agent.
- `TrustedFull` is powerful. It is deliberately visible and warned about; a stolen trusted token can reach new endpoints without a new consent step.
- A remote MCP endpoint is deferred. If it is added later, it belongs inside Core (same token audience) or behind OAuth 2.1; a separate proxy that forwards the Core token is not acceptable.

## Alternatives considered

- **Give the Agent the CLI token.** Full panel access, no scope, no separate identity, no audit. Rejected.
- **Put a PAT on the whole API with full RBAC now.** Pulls M6 forward, touches every endpoint, and gives a token far more surface than the Agent needs. Rejected for v1.
- **Remote MCP endpoint in Core from day one.** Needs MCP auth, TLS/HTTP decisions, token audience validation and a new listener. The local bridge already works on the dev box. Deferred.
- **A generic `run_command`/PowerShell endpoint.** Convenient, but RCE as SYSTEM in a public repository, with prompt-injection exposure. Rejected; optional SSH/JEA is the break-glass path.
- **Browser automation of the panel.** Brittle, slow, hard to audit and impossible to scope. Rejected.
- **OAuth 2.1 + dynamic client registration now.** Correct for multi-user/cloud, overkill for one Owner, one client and a private VPN. Revisit when the Agent runs outside the VPN.
