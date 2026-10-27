# 06 · Security and access

## 1. Threat model (short)

| Asset | Threat | Main control |
|---|---|---|
| The VM | A panel user or a malicious mod/hook gains code execution as SYSTEM | Owner-only adapters; games, SteamCMD and hooks run as the non-admin user `gamewarden-run` ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)); explicit data-root ACL ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)); no file manager |
| Worlds / saves | Accidental or malicious deletion, bad restore | Role limits, pre-operation backups, audit |
| Panel accounts | Password guessing, session theft on the wire | VPN-only binding, rate limiting, strong hashing, HttpOnly/SameSite cookies |
| Secrets (server passwords, webhook) | Leak through API, logs, UI | DPAPI at rest, never returned by API, log redaction |
| Game ports | Exposure to the internet | Firewall rules scoped to VPN ranges |

## 2. Network exposure

- The panel listens only on configured addresses. Defaults: `127.0.0.1` + detected VPN interface addresses (Tailscale `100.64.0.0/10`, Radmin `26.0.0.0/8` **[Unverified: Radmin range]**).
- Binding to `0.0.0.0` or a LAN/public address requires an explicit Owner setting and shows a permanent warning banner.
- Game-port firewall rules are created per instance and scoped to the configured **remote VPN CIDRs**. Rule name: `GameWarden:<instanceId>:<port>/<proto>`. Removed when the instance is deleted.
- Tailscale users MAY additionally restrict friends with Tailscale ACLs (game ports + panel port only).

## 3. HTTP-only decision and mitigations

Decision: [ADR-0005](../decisions/0005-http-only-over-vpn.md). Traffic is not TLS-encrypted by GameWarden; confidentiality relies on the VPN tunnel (Tailscale uses WireGuard; Radmin VPN states it encrypts traffic **[Unverified]**).

Mitigations:

1. VPN-only binding (above).
2. Cookies `HttpOnly` + `SameSite=Strict`; `Secure` cannot be set without HTTPS.
3. CSRF header token on all state-changing requests.
4. Session lifetime: 7 days sliding, 30 days absolute; logout everywhere on password change.
5. Consequences accepted: no PWA install prompt, no web push, browsers mark the site "Not secure".

## 4. Authentication

| Item | Rule |
|---|---|
| Accounts | Local accounts (username + password). Discord login is a later option (OQ-04). |
| Password storage | ASP.NET Core Identity `PasswordHasher` (PBKDF2) |
| Password policy | ≥ 10 characters; no other composition rules |
| Throttling | 5 failed attempts per username per 15 min → 15 min lock; per-IP limit as well |
| Owner creation | Only during first-run setup and **only from loopback** (on the VM) |
| Owner lock-out recovery | `gamewarden owner reset-password` on the VM, as local Administrator |

## 5. Authorization

### 5.1 Permissions

| Permission | Meaning |
|---|---|
| `system.read` / `system.manage` | View / change system settings, prerequisites, notifications |
| `users.manage` | Users, invites, role bindings |
| `audit.read` | Read the audit log |
| `adapters.read` / `adapters.manage` | View / add, change, reload adapters |
| `instances.create` | Create new instances (global scope only) |
| `instance.view` | See the instance, its status, jobs, backups list, activity |
| `instance.connectInfo` | Use the Share button (address, port, password) |
| `instance.control` | Start / stop / restart |
| `instance.console.read` | Read console output |
| `instance.console.write` | Send allow-listed console commands |
| `instance.console.writeAny` | Send any console command |
| `instance.settings.read` / `instance.settings.write` | View / change settings |
| `instance.update` | Install / update game files |
| `instance.backup.create` | Take a backup |
| `instance.backup.restore` | Restore or download a backup |
| `instance.mods.manage` | Change mods |
| `instance.schedule.manage` | Change schedules |
| `instance.delete` | Archive or purge the instance |

### 5.2 Roles

| Permission | Owner | Admin | Operator | Player |
|---|:-:|:-:|:-:|:-:|
| `system.*`, `users.manage`, `adapters.manage` | ✓ | – | – | – |
| `audit.read` | ✓ | ✓ (own scope) | – | – |
| `adapters.read` | ✓ | ✓ | ✓ | ✓ |
| `instances.create` | ✓ | ✓ (global binding only) | – | – |
| `instance.view`, `instance.connectInfo` | ✓ | ✓ | ✓ | ✓ |
| `instance.control` | ✓ | ✓ | ✓ | – |
| `instance.console.read`, `instance.console.write` | ✓ | ✓ | ✓ | – |
| `instance.console.writeAny` | ✓ | ✓ | – | – |
| `instance.backup.create` | ✓ | ✓ | ✓ | – |
| `instance.settings.read` | ✓ | ✓ | ✓ | – |
| `instance.settings.write`, `instance.update`, `instance.backup.restore`, `instance.mods.manage`, `instance.schedule.manage`, `instance.delete` | ✓ | ✓ | – | – |

### 5.3 Rules

- Code checks **permissions**, never role names. Roles are named permission sets; adding a role needs no code change.
- A role binding has a scope: `global` or `instance:<id>`. Effective permissions = union of all bindings that cover the target.
- `Owner` is a flag on the user, always global, and cannot be removed from the last Owner.
- Only Owners manage users and invites in v1.
- The panel hides actions the user lacks; the API enforces regardless.
- `404` (not `403`) for instances the user cannot view, to avoid revealing them.

### 5.4 Invites

1. Owner creates an invite: role, scope, expiry (default 24 h).
2. Panel shows a link `http://<panel>/invite/<token>` to send privately.
3. Friend opens it over the VPN, chooses username + password; the token is consumed.
4. Owner can revoke unused invites, disable users, and change bindings at any time.

## 6. Secrets

- Stored under `secrets\`, encrypted with DPAPI in the `CurrentUser` scope of LocalSystem, so only SYSTEM can decrypt them ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)). The `LocalMachine` scope is not used: it lets any local process decrypt.
- Secrets that a game keeps in its own config file (e.g. Palworld `AdminPassword`) are plaintext in that file and in backups of it (OQ-14).
- API returns only `{ "set": true|false }`; values can be replaced, never read back.
- Exception: `instance.connectInfo` MAY include the game password if the instance setting `sharePasswordWithPlayers` is on (default **on** for friends-only use; Owner can turn it off).
- All logs (Core, job, console capture of the launch line) pass through a redactor that replaces known secret values with `***`.

## 7. Adapters, hooks and mods are code

- **Hooks** are PowerShell scripts; **mods** (e.g. BepInEx plugins) are DLLs loaded by the game. Both execute code on the VM.
- Therefore:
  - Only Owners can add or change adapters.
  - Anyone with `instance.mods.manage` can effectively run code with the **game process identity**. Games, SteamCMD and hooks therefore run as the non-admin user `gamewarden-run` ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)), which can write only to instance folders and `tools\steamcmd` ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)). SYSTEM never executes a file from a path that this user can write.
  - All instances share `gamewarden-run`, so a mod in one instance can change another instance's files. Accepted for friends-only use.
  - The panel shows a warning when granting Admin to a user: "Admins can install mods, which run code on this machine."

## 8. Audit

- Every state-changing API call creates an `AuditEvent`: who, what, target, result, timestamp, source IP.
- Shown in the activity feed in friendly form ("An restarted valheim-main"), and in full on the Audit page.
- Retention: 180 days (configurable).
