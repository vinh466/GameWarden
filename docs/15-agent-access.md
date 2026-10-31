# 15 · Agents

Status: **Phase 1a implemented 2026-10-31** — clients, tokens, scopes, audit, the runtime API, the Owner
management API, the panel Agents screen, the local MCP bridge and the first diagnostics. Event Log, network, storage and diagnostic bundles are built; runbooks, instance files and the write/admin surface are the next phases. The decision is
[ADR-0023](../decisions/0023-agent-clients.md).

## 1. What it is

An Agent is a named client that the Owner creates in GameWarden. It has its own permissions, tokens and audit
trail. The Agent runs on the development box (OpenCode, Claude Code or a script), reaches Core over the VPN and
uses a scoped REST surface. MCP is a local stdio bridge over that REST API; it is not a second server.

Non-goals:

- No Agent on the public internet; no MCP listener in Core in v1.
- No arbitrary PowerShell/shell endpoint in Core. Break-glass SSH/JEA is outside GameWarden.
- No secrets in Agent responses, not even in diagnostics mode.
- No Agent management by an Agent: only the Owner session or the loopback CLI manages clients and tokens.

## 2. Model

| Table | Purpose |
|---|---|
| `agent_clients` | Identity: name, description, enabled, `TrustedFull`, permissions JSON, allowed networks JSON, last seen |
| `agent_tokens` | Credential: client id, SHA-256 hash, prefix, expiry, revoked, last used |
| `agent_settings` | One row: feature switch, diagnostics/admin windows, `RequireAdminMode`, default networks |
| `agent_audit` | One row per Agent request: client, token, action, target, outcome, status, IP, request id, duration |

A client has zero or more tokens. Rotating a token creates a new one and revokes the old one; the client's
permissions and audit history stay.

### Token format

```text
gw_agent_<10-character id>_<43-character base64url secret>
```

- Stored as SHA-256; the plaintext is shown once.
- Prefix `gw_agent_` makes leaks recognizable and prevents reuse of the loopback CLI token.
- Expiry 1–90 days, default 30.
- Optional allowed CIDR ranges; empty uses the feature-wide ranges, then any address the panel accepts.
- Revocation is checked on every request; no restart or cache flush.

## 3. Permissions

The catalog is served by `GET /api/v1/agents/permissions`.

| Group | Permissions |
|---|---|
| Observe | `agent.status.read`, `agent.instances.read`, `agent.jobs.read`, `agent.activity.read`, `agent.logs.read`, `agent.metrics.read`, `agent.settings.read`, `agent.mods.read` |
| Operate | `agent.instances.control`, `agent.instances.console.write`, `agent.instances.install`, `agent.instances.update` |
| Write | `agent.instances.settings.write`, `agent.instances.mods.write`, `agent.instances.config.write` |
| Diagnose | `agent.diagnostics.system`, `.processes`, `.network`, `.storage`, `.events`, `.files`, `.bundles`, `agent.runbooks.read`, `agent.runbooks.run` |
| Admin | `agent.instances.delete`, `agent.backups.restore`, `agent.system.update`, `agent.system.settings`, `agent.adapters.manage` |

Presets: **Observer**, **Operator**, **Debugger**, **Full**. The UI/CLI may also select individual permissions.

`TrustedFull` is the only wildcard. It is for the Owner's own development Agent and is shown with a warning: a
newly added permission is automatically available. Without it, a new endpoint needs an explicit permission
grant. The CLI can create a trusted client with `--trusted-full`.

## 4. Switches and windows

| Switch | Default | Effect |
|---|---|---|
| `Agents.Enabled` | off | Every Agent token gets 403 `agent_access_disabled` |
| `Diagnostics` | off, 1–480 min | Required by `diagnose` permissions and core/setup logs |
| `Admin` | off, 1–60 min | Required for destructive/admin permissions while `RequireAdminMode` is on |

The Owner changes them with `PUT /api/v1/agents/settings` or `gamewarden agents settings`. Turning the feature
off also closes both windows. An expired window is off on the next request; a restart does not extend it.

## 5. API

### Owner management (session or loopback CLI only)

```text
GET    /api/v1/agents
POST   /api/v1/agents
GET    /api/v1/agents/{id}
PATCH  /api/v1/agents/{id}
DELETE /api/v1/agents/{id}
GET    /api/v1/agents/permissions
GET    /api/v1/agents/settings
PUT    /api/v1/agents/settings
GET    /api/v1/agents/audit
GET    /api/v1/agents/{id}/audit
GET    /api/v1/agents/{id}/tokens
POST   /api/v1/agents/{id}/tokens
POST   /api/v1/agents/{id}/tokens/{tokenId}/rotate
DELETE /api/v1/agents/{id}/tokens/{tokenId}
```

### Runtime (`Authorization: Bearer gw_agent_...`, only this surface)

Implemented 2026-10-31:

```text
GET  /api/v1/agent/me
GET  /api/v1/agent/status
GET  /api/v1/agent/instances
GET  /api/v1/agent/instances/{id}
GET  /api/v1/agent/instances/{id}/console
GET  /api/v1/agent/instances/{id}/settings
POST /api/v1/agent/instances/{id}/start|stop|restart
POST /api/v1/agent/instances/{id}/console
GET  /api/v1/agent/logs
GET  /api/v1/agent/jobs
GET  /api/v1/agent/jobs/{jobId}
GET  /api/v1/agent/jobs/{jobId}/log
GET  /api/v1/agent/activity
GET  /api/v1/agent/metrics
GET  /api/v1/agent/diagnostics/system
GET  /api/v1/agent/diagnostics/processes
GET  /api/v1/agent/diagnostics/storage
GET  /api/v1/agent/diagnostics/network
GET  /api/v1/agent/diagnostics/events
POST /api/v1/agent/diagnostics/bundles
GET  /api/v1/agent/diagnostics/bundles/{id}
GET  /api/v1/agent/diagnostics/bundles/{id}/download
GET  /api/v1/agent/system/update
POST /api/v1/agent/system/update
```

Planned: install/update, settings/mods writes, firewall rules, instance-file reads and runbooks. Each new endpoint declares a permission; `TrustedFull` clients get it automatically.

### Errors

Stable codes: `unauthenticated`, `token_expired`, `token_revoked`, `agent_disabled`,
`agent_access_disabled`, `ip_not_allowed`, `diagnostics_mode_off`, `admin_mode_off`, `forbidden`,
`rate_limited`. The runtime API adds `X-GW-Request-Id`; the panel can use it to find the audit row.

## 6. Audit

Every request that carries an Agent credential — including a call to the wrong surface, a denied permission,
a 401 and a 429 — writes an `AgentAudit` row. The audit middleware wraps authentication, permission/mode checks,
authorization and the endpoint. Reads are audited too; the activity feed only shows state-changing actions.

Fields: `At`, `ClientId`, `ClientName`, `TokenId`, `Action`, `Target`, `Outcome` (`ok`/`denied`/`failed`),
`StatusCode`, `SourceIp`, `RequestId`, `DurationMs`, `Details` (method, path, query, user agent; redacted).
Retention 180 days. The API cannot edit or delete rows.

## 7. Redaction

`AgentRedactor` replaces known secret values from `ISecretStore` and common patterns:

- `password=`, `secret=`, `token=`, `authorization=` and similar assignments;
- `-password <value>` command-line arguments (Valheim);
- `Bearer <value>` and `gw_agent_...`;
- Discord webhook URLs;
- ANSI escapes and control characters;
- line length capped at 8 KB.

It is applied to core/job/instance logs, console tails, process command lines and diagnostic bundles. Secrets, `secrets/`, `keys/`, the database, `cli.token` and `setup.token` are never exposed.

## 8. MCP bridge

`src/GameWarden.Mcp` is a stdio MCP server on the Agent's machine. It holds the Agent token and calls
`/api/v1/agent`. Tools (2026-10-31):

| Tool | Scope | Kind |
|---|---|---|
| `gw_agent_get` | endpoint scope | read; advanced GET under `/api/v1/agent` |
| `gw_status` | status.read | read |
| `gw_me` | — | read |
| `gw_list_instances` | instances.read | read |
| `gw_get_instance` | instances.read | read |
| `gw_get_console` | instances.read | read |
| `gw_get_settings` | settings.read | read |
| `gw_tail_logs` | logs.read | read |
| `gw_get_jobs` | jobs.read | read |
| `gw_get_job_log` | jobs.read | read |
| `gw_get_activity` | activity.read | read |
| `gw_get_metrics` | metrics.read | read |
| `gw_get_diagnostics` | diagnostics.* + mode | read; system, processes, storage, network, events |
| `gw_collect_bundle` | diagnostics.bundles + mode | read; saves a redacted zip on the agent host |
| `gw_check_update` | system.update | read |
| `gw_update_gamewarden` | system.update + admin mode | write |
| `gw_control_instance` | instances.control | write |
| `gw_install_instance` | instances.install/update | write |
| `gw_send_console` | console.write | write |
| `gw_wait_job` | jobs.read | read |

Configuration:

```json
{
  "url": "http://<vm-vpn-ip>:8765",
  "tokenFile": "/home/vinh/.config/gamewarden/agent.token"
}
```

`~/.config/gamewarden/agent.json` (0600) and `~/.config/gamewarden/agent.token` (0600). Environment variables
`GAMEWARDEN_URL`, `GAMEWARDEN_TOKEN_FILE` and `GAMEWARDEN_API_TOKEN` override them. OpenCode:

```jsonc
{
  "mcp": {
    "gamewarden": {
      "type": "local",
      "command": ["/home/vinh/.local/bin/gamewarden-mcp"],
      "enabled": true,
      "timeout": 120000
    }
  }
}
```

A remote MCP endpoint is deliberately not built. If it is added, it must be inside Core (same token audience) or
behind OAuth 2.1; a separate proxy must not forward the Core token.

## 9. CLI bootstrap

```powershell
gamewarden agents settings --enabled true
gamewarden agents create opencode-devbox --permissions agent.status.read,agent.instances.read,agent.instances.control,agent.logs.read,agent.jobs.read,agent.diagnostics.system --network 100.64.0.10/32
gamewarden agents token <client-id> --days 7
gamewarden agents tokens <client-id>
gamewarden agents revoke <client-id> <token-id>
gamewarden agents delete <client-id>
gamewarden agents settings --diagnostics 30 --reason "debug Valheim crash"
gamewarden agents settings --diagnostics 0
```

The token is printed once. Store it in a 0600 file; never commit it.

## 10. Self-development loop

1. Add an endpoint under `/api/v1/agent` and declare a permission in `AgentPermissions`.
2. Add integration tests; build on Linux with the fake game.
3. Release a candidate; the Agent can check GitHub with `gw_check_update` and start the self-update with `gw_update_gamewarden` when `system.update` and a live admin window are granted. Core restarts, so the Agent reconnects and reads the update result.
4. Call the endpoint through MCP (`gw_agent_get` for a new read endpoint, or a dedicated tool) or with `curl` and the token file.
5. If the client is `TrustedFull`, the new permission works at once; otherwise grant it in the panel/CLI.
6. All calls and denials are in the audit trail.

For VM deployment, the Owner can install the candidate or grant a break-glass SSH key. The key is separate from
the Agent token, restricted to the dev box, and should be revocable independently.

## 11. Limits and next phases

- Log queries: tail ≤ 1000 lines, 4 MB scan; diagnostic bundles are capped at 25 MB uncompressed and 500 entries.
- Process list: 300 entries; command lines are redacted, including Windows WMI command lines.
- Firewall rules and instance-file reads are next; then runbooks, settings/mods writes and the panel's deeper diagnostics.
- A diagnostics-mode request/approval flow and per-action admin approval are later refinements.
