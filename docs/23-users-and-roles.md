# 23 · Users, roles and access: options for M6

**Status: research and proposal by the agent on 2026-11-23; the direction is accepted in
[ADR-0036](../decisions/0036-users-roles-and-per-server-access.md) (2026-11-24). The panel still has exactly one
account, the Owner; the phases in §9 are the implementation plan.** The accepted base is
[ADR-0006](../decisions/0006-rbac-with-separate-owner.md) and the permission list in
[06 §5](06-security-and-access.md#5-authorization); this page researched how to deliver it and proposes the shape
for M6. The questions the Owner still owns are in §10.

**In one minute:** local accounts with **one-time invite and reset links** (no email); four fixed roles with
**global or per-server bindings**; **Owner-only user management**; server creation and deletion stay Owner-only
until backups exist; Players get the roster, connect info and a notification-only restart request; one permission
registry with a test that every endpoint declares a permission. The panel adds **System → Users & Access** and a
**Server → Access** tab, with a "what this role can do here" preview.

**Not built yet:**

- User accounts other than the Owner, invites, the Users & Access screen and the per-server Access tab. Phase 0a
  adds the `role_bindings` table and the permission service, but no API or UI writes a binding yet.
- Endpoint enforcement: the API names permissions in [05](05-api.md), and Phase 0a resolves a user's effective
  permissions, but every endpoint still accepts any signed-in user (Phase 0b adds the per-endpoint checks).
- Player-facing capabilities (player roster, connect info, request restart) and audit actors with a user id.
  Disabling a user and demoting an Owner already end the session on the next request.
- The permission catalog predates the game integrations and backups. [ADR-0036](../decisions/0036-users-roles-and-per-server-access.md)
  adds the missing player/admin permissions and keeps `instance.delete` Owner-only until backups exist; the
  endpoint checks that use them are Phase 0b.

## 1. What exists today

| Fact | Where |
|---|---|
| One `users` row: id, user name, password hash, security stamp, `IsOwner`, timestamps | `src/GameWarden.Core/Data/UserRecord.cs` |
| Owner creation only on loopback, with the one-time setup token | [06 §4](06-security-and-access.md#4-authentication), `AccessEndpoints.cs` |
| Cookie session with a 7-day sliding / 30-day absolute limit; CSRF header; login throttling | [06 §3–4](06-security-and-access.md#3-http-only-decision-and-mitigations) |
| The CLI is a separate loopback identity with a local token, not a panel user | [02 §8](02-architecture.md#8-cli), `CliToken.cs` |
| Agent clients are separate identities with their own tokens and permissions | [15](15-agent-access.md), [ADR-0023](../decisions/0023-agent-clients.md) |
| Activity rows store a display name (`User`) but no user id; there is no separate audit table yet | `ActivityRecord.cs`, [19](19-system-log.md) |
| The permission names and role matrix are specified, not enforced | [06 §5](06-security-and-access.md#5-authorization), [05](05-api.md) |
| Users and invites endpoints are specified | [05 §2](05-api.md#auth-and-session) (`/users`, `/invites`) |

### 1.1 Constraints that shape the design

| Constraint | Consequence |
|---|---|
| **No email server.** The panel is a private tool on a VPN. | Invites and password resets must work as **links the Owner copies** into Discord or another chat. Do not depend on SMTP. |
| **HTTP over the VPN only** ([ADR-0005](../decisions/0005-http-only-over-vpn.md)). | Passkeys / WebAuthn are unavailable outside `localhost`. Passwords and optional TOTP are the realistic factors. |
| **3–10 friends, one shared Windows host.** | Optimise for one Owner doing the administration, not for a help desk. Fixed roles and presets beat a 40-checkbox matrix. |
| **One `gamewarden-run` identity for every server.** | An Admin who can change mods can run code that touches another instance's files. This is already accepted ([06 §7](06-security-and-access.md#7-adapters-hooks-and-mods-are-code)); roles must not pretend otherwise. |
| **The group already uses Discord**, and Core already runs a bot. | Discord is the natural delivery channel for invite and reset links. It can later be a second sign-in path, but not the source of truth. |
| **Backups are not built** ([ADR-0018](../decisions/0018-backups-leave-the-mvp.md)). | Destructive permissions (delete, purge, restore) need a higher bar than the current matrix. |
| **The Owner can recover with `gamewarden owner reset-password` on the VM.** | Friends have no equivalent; the Owner must be able to issue a reset link without email. |

## 2. What M6 has to solve

1. **Add a friend** without creating a password for them or relaying one out of band.
2. **Grant a role on a scope**: all servers, or one server. Change it later, and take it away immediately.
3. **Answer "what can this person do here?"** in the panel, without reading code or a permission table.
4. **Know who did what**: activity and audit carry a stable user id and a readable name.
5. **Keep the Owner safe**: no self-demotion, no last-Owner lockout, no privilege escalation through the UI.
6. **Stay small**: four roles, no custom role matrix in v1, no email infrastructure.

Success criterion 6 of [01 §8](01-vision-and-scope.md#8-success-criteria) is the acceptance test: *a friend with the
Operator role can restart a server but cannot change its settings or see the Owner's system pages.*

## 3. Research: how comparable tools do it

Checked 2026-11-23 against official documentation or source. Where a detail could not be confirmed, it is marked
**[Unverified]**.

| Product | How an account appears | Role model | Scope | UI granularity | Lesson |
|---|---|---|---|---|---|
| **Pterodactyl 1.x** ([permissions source](https://github.com/pterodactyl/panel/blob/1.0-develop/app/Models/Permission.php)) | Admin creates a user by email; adding a subuser also creates the account; a blank password triggers a reset email | No roles: a `root_admin` flag plus a per-server permission array | Global admin flag; permissions per server | Checkbox groups of about 40 keys (`control.*`, `file.*`, `backup.*`, …) | Cheap, but email is load-bearing and "admin" is not a role. |
| **Pelican** ([role source](https://github.com/pelican-dev/panel/blob/main/app/Models/Role.php), [comparison](https://pelican.dev/docs/comparison)) | Admin creates; reset-token email; passkeys and OAuth offered | Custom admin roles (`Root Admin` plus Spatie roles); subusers still use permission arrays **[Unverified: current subuser UI]** | Admin roles can be limited to nodes; server permissions per server | Per-model CRUD checkbox lists | Node-scoped roles are a useful idea; two permission systems are not. |
| **AMP** ([roles discussion](https://discourse.cubecoders.com/t/managing-user-permissions-in-amp/2301)) | Admin creates a user at ADS with a password; OIDC maps groups in paid editions **[Unverified]** | Custom roles, several per user; global and per-instance; template roles | ADS level and each instance | Deny / neutral / allow tri-state per function | Precise but easy to misconfigure; explicit ownership is better than implicit. |
| **Crafty Controller** ([docs](https://docs.craftycontrol.com/pages/user-guide/user-role-config/)) | Admin sets username and password; no email or invite | Custom roles plus a `Superuser` flag; per-user quotas | A role is a set of servers plus area flags | Server multi-select, then area checkboxes (commands, terminal, logs, schedules, backup, files, config, players) | Closest to GameWarden's "role + scope" binding; a Superuser warning is necessary. |
| **Grafana** ([users](https://grafana.com/docs/grafana/latest/administration/user-management/manage-org-users/)) | Invite by email **or username**; "send invite email" can be off, with a copyable link and pending-invite revocation | Fixed org roles (Admin / Editor / Viewer / none), teams, server admin separate | Org, team, folder, dashboard | Role dropdown; per-folder/dashboard View/Edit/Admin; highest permission wins | The no-email invite link is exactly the pattern GameWarden needs. |
| **Jellyfin** ([users](https://jellyfin.org/docs/general/server/users/adding-managing-users/)) | Admin creates a local user; no first-party invite (third-party tools fill the gap) | Admin boolean plus many per-user toggles | Global toggles, per-library and per-device access | Checkboxes with an "all libraries" master switch | A master switch plus per-item list is friendly; disabling a user terminates sessions immediately. |
| **Home Assistant** ([users](https://www.home-assistant.io/docs/configuration/user-configuration)) | Owner at onboarding; admins add users | Admin toggle only (internal groups not exposed) | Whole system; some views can be admin-only | Admin toggle; per-view visibility is cosmetic | Visibility is not permission; the owner principal is undeletable. |
| **Tailscale / Radmin** | Network identity, not application identity | ACLs at the network layer | Network ranges | Policy files | Useful as defence in depth, not as the panel's user model. Do not trust a VPN header as a login. |

### 3.1 What to borrow

- **Invite as a one-time link, email optional** (Grafana). Show it once, store only a hash, mark it single-use,
  expiring and revocable. This fits a Discord group with no SMTP.
- **One special Owner principal** (Pterodactyl root admin, Pelican Root Admin, Home Assistant owner). Keep Owner a
  flag, not a role; never let a role edit remove it, and never allow the last Owner to disappear.
- **A role bundle plus a binding** (Crafty, AMP). Edit the role in one place; bind `(role, scope)` on the user. This
  is already [ADR-0006](../decisions/0006-rbac-with-separate-owner.md).
- **Presets first, checkboxes second** (Jellyfin's "all libraries", Pterodactyl's groups). Four named roles cover
  95% of a friends group; a raw checkbox matrix is a support burden.
- **Disable and revoke are one action** (Jellyfin). Disabling a user, resetting a password or removing the last
  binding must end the sessions that could still act.
- **A master switch plus a per-item list** (Jellyfin, Grafana). "All servers" as a global binding, then per-server
  overrides, is easier to reason about than a list of every server for every friend.
- **An effective-permission preview** (implied by Grafana's role pages and Crafty's role summary). The Owner should
  see "Operator on valheim-main: restart, console, backup; not settings, mods or delete" before saving.

### 3.2 What to avoid

- **Email-dependent onboarding** (Pterodactyl, Pelican). Do not create an account with a password the Owner has to
  relay, and do not require a reset email.
- **Two permission systems** (Pelican's admin roles plus subuser arrays; Pterodactyl's flag plus arrays). One
  registry of permission names, used by every surface.
- **Tri-state or deny rules** (AMP). For 3–10 users, explicit grants and a union are enough. Deny rules make "why
  can't I?" unanswerable.
- **Cosmetic visibility** (Home Assistant views). Hiding a button is not authorization; the API must enforce.
- **A blank custom-role matrix** as the first version. It multiplies tests and support before the group needs it.
- **Weak passwords** (Crafty requires 6 characters with composition rules). GameWarden already requires 10; keep
  that and add a length check only.

## 4. Design axes and the proposed choices

| Axis | Options | Proposed for M6 | Why |
|---|---|---|---|
| **Account creation** | Owner sets a temporary password · one-time invite link · Discord OAuth · Steam OpenID | **One-time invite link** | No SMTP; the link is the secret; the friend chooses their own password. Steam/Discord can come later as identity proof, not as the account. |
| **Role model** | Fixed roles · custom roles · direct permission grants | **Fixed roles as presets**, permissions underneath | Four roles cover the group; the API stays permission-based so a later role is additive. |
| **Scope** | Global only · global or per instance · per feature | **Global or per instance**, union of bindings | Matches [ADR-0006](../decisions/0006-rbac-with-separate-owner.md); one friend can operate one server and only see another. |
| **Granularity in the UI** | Raw permission checkboxes · role cards with a "what this can do" summary | **Role cards + preview**, no raw matrix | The Owner thinks in people and servers, not a few dozen permission strings. |
| **Owner** | One fixed Owner · multiple Owners · Owner as a role | **A flag, multiple allowed, last-Owner guard** | A second Owner is sometimes needed; a role would make it grantable by accident. |
| **User management** | Owner only · Admins invite for their scope | **Owner only in v1** (§10 Q2) | The group is small; delegation can be added with `instance.access.manage` later. |
| **Invite transport** | Copy link · Discord DM from the bot · QR code | **Copy link first**, Discord share and QR later | Copy-paste works even if the bot is off; QR helps phone onboarding. |
| **Second factor** | None · TOTP · passkeys · Discord code | **TOTP optional later**; passkeys blocked by HTTP | TOTP works over HTTP and protects replay; WebAuthn needs a secure context. |
| **Session control** | Cookie only · revoke-all · per-device list | **Revoke-all via security stamp**; per-device list later | Solves disable/reset/password change with little schema; a session table is a later nicety. |
| **Account removal** | Delete · disable | **Disable by default**, keep the name in history | Audit and activity must stay readable; a deleted user makes old rows ambiguous. |

## 5. The proposed model

### 5.1 Identities

| Identity | What it is | Can it use the panel? | Can it be managed here? |
|---|---|---|---|
| **Owner** | A `users` row with `IsOwner = true`. System, users, adapters, every server. | Yes | Yes, but the last Owner is protected |
| **Admin** | A friend trusted with one server or all servers. Can change settings, mods, updates and backups; no delete in v1 (see §5.2). | Yes | Yes |
| **Operator** | A friend who keeps the server running: start/stop/restart, console, read settings, create a backup, kick/announce. | Yes | Yes |
| **Player** | A friend who plays: see status, connect info, player roster, request a restart. | Yes | Yes |
| **CLI** | The loopback token on the VM. Root-equivalent locally. | No (CLI only) | No |
| **Agent client** | A scoped machine identity for `/api/v1/agent` and MCP. | No | Separate screen ([15](15-agent-access.md)) |

The Agent and CLI identities are **not** panel users. An Agent token must never manage users, and a panel session
must never call `/api/v1/agent`. This separation already exists; keep it.

### 5.2 Permission catalog

The catalog in [06 §5](06-security-and-access.md#5-authorization) is the base. The game integrations and the
missing backup work exposed gaps; **this table is the proposed M6 catalog**. Rows marked **new** are not in 06 yet.

| Permission | Meaning | Owner | Admin | Operator | Player |
|---|---|:-:|:-:|:-:|:-:|
| `system.read` / `system.manage` | System pages, network, prerequisites, updates, notifications | ✓ | – | – | – |
| `users.manage` | Users, invites, bindings | ✓ | – | – | – |
| `audit.read` | The full audit log | ✓ | – | – | – |
| `adapters.read` | See adapters and their guides | ✓ | ✓ | ✓ | – **changed** |
| `adapters.manage` | Add, change, reload adapters | ✓ | – | – | – |
| `instances.create` | Create a server | ✓ | – **changed** | – | – |
| `instance.view` | See the server, status, overview, its activity | ✓ | ✓ | ✓ | ✓ |
| `instance.details.read` | Jobs and logs, mod list, config file list, schedules | ✓ | ✓ | ✓ | – |
| `instance.connectInfo` | The Share button: address, port, password if allowed | ✓ | ✓ | ✓ | ✓ |
| `instance.control` | Start / stop / restart | ✓ | ✓ | ✓ | – |
| `instance.console.read` | Read the console | ✓ | ✓ | ✓ | – |
| `instance.console.write` | Send allow-listed console commands | ✓ | ✓ | ✓ | – |
| `instance.console.writeAny` | Send any console command | ✓ | ✓ | – | – |
| `instance.settings.read` | View settings and whether secrets are set | ✓ | ✓ | ✓ | – |
| `instance.settings.write` | Change settings and secrets | ✓ | ✓ | – | – |
| `instance.update` | Install / update game files | ✓ | ✓ | – | – |
| `instance.backup.create` | Take a backup (when built) | ✓ | ✓ | ✓ | – |
| `instance.backup.restore` | Restore or download a backup (when built) | ✓ | ✓ | – | – |
| `instance.mods.manage` | Change mods, *Mods on/off*, configs | ✓ | ✓ | – | – |
| `instance.schedule.manage` | Change schedules (when built) | ✓ | ✓ | – | – |
| `instance.delete` | Archive or purge the server | ✓ | – **changed** | – | – |
| `instance.access.manage` **new** | Invite or remove users on this server | ✓ | – | – | – |
| `instance.players.read` **new** | See the player roster and game info | ✓ | ✓ | ✓ | ✓ |
| `instance.players.kick` **new** | Kick a player | ✓ | ✓ | ✓ | – |
| `instance.players.ban` **new** | Ban / unban a player | ✓ | ✓ | – | – |
| `instance.admins.manage` **new** | Make / remove a game admin (`adminlist.txt`) | ✓ | ✓ | – | – |
| `instance.announce` **new** | Send an in-game announcement | ✓ | ✓ | ✓ | – |
| `instance.save` **new** | Ask the game to save now | ✓ | ✓ | ✓ | – |
| `instance.restart.request` **new** | Ask for a restart (notification only, no control) | ✓ | ✓ | ✓ | ✓ |

**Why the changes:**

- **`instances.create` and `instance.delete` become Owner-only by default.** Backups are not built, and one shared
  VM means a new server is a disk/CPU commitment for everyone. An Admin can still do everything inside a server.
  If the Owner wants a co-admin who creates servers, promote them to Owner.
- **Player gets the roster and a restart request.** "Is it up, who is on, how do I join" is the Player persona
  ([01 §5](01-vision-and-scope.md#5-personas)). The request only notifies; it does not restart.
- **Kick is Operator, ban and game-admin are Admin.** A kick is reversible; a ban and `adminlist.txt` are not.
- **Mods stay Admin** because [ADR-0006](../decisions/0006-rbac-with-separate-owner.md) trusts Admins with the
  server, and the run-as user bounds the damage. The grant screen must say, in plain words, that mods run code.

The role matrix is a **default**, not a hard-coded truth: the code checks permissions, the role is a named set. A
future "Custom" role can be a copy of a preset with a few toggles, not a blank matrix.

### 5.3 Bindings and effective permissions

A binding is `(user, role, scope)`, where scope is `global` or `instance:<id>`. Effective permissions on a server are
the union of every binding that covers it, plus the Owner flag.

```
Vinh  ── Player   (global)             → sees every server, plays
      ── Operator (valheim-main)       → restarts valheim-main
      ── Admin    (palworld)           → changes palworld settings and mods
```

Rules:

- **No deny in v1.** If a friend should not see one server, do not give a global binding; bind the servers they may
  see. The UI must say this plainly ("All servers" means every current and future server).
- **One binding per (user, role, scope).** Duplicate grants are a UI mistake; the database enforces uniqueness.
- **Owner is implicit.** No binding rows; `IsOwner` answers every permission check. Do not let a role edit remove it.
- **Role changes take effect on the next request.** Permissions are resolved from the database, not copied into the
  cookie, so an Operator who loses a binding cannot act with a stale session. The panel refreshes `/auth/me` on
  focus and shows "Your access changed" instead of a generic error.
- **404 for invisible servers, 403 for a known server.** This is already the rule in [06 §5.3](06-security-and-access.md#53-rules);
  keep it. A Player who is not bound to a server must not learn that its id exists.

### 5.4 Invites and resets: one mechanism, two purposes

One table, one token format, two purposes:

| Purpose | Created by | Lifetime | What the link does |
|---|---|---|---|
| **Join** | Owner, with a role and a scope | 7 days (changeable) | Friend chooses a user name and password; the account and the binding are created; the invite is consumed. |
| **Reset** | Owner, for an existing user | 24 hours | Friend chooses a new password; the security stamp changes; every session ends; the link is consumed. |

Rules:

- The URL contains a random 256-bit token. The database stores **only its SHA-256 hash**, plus the inviter, role,
  scope, expiry and use. The accept URL necessarily carries the token; request logs, audit and analytics must
  redact `/invites/*` paths, and the global `Referrer-Policy: no-referrer` stays on.
- The link is shown **once**, with *Copy*; the Owner can revoke a pending invite from the list. Do not store the
  plaintext token for a later "show again".
- The accept endpoint is anonymous and rate-limited; errors are generic ("This link is invalid or has expired").
  A used, revoked or expired link is indistinguishable.
- Accepting is atomic: one conditional update consumes the invite; a race cannot create two accounts.
- **No temporary passwords.** The Owner never knows the friend's password. This is the main reason to prefer a link
  over "create user with password".
- The same link mechanism covers a friend who forgot their password; the Owner does not need SMTP.

### 5.5 Owner guard

- At least one Owner must always exist.
- The last Owner cannot be disabled, demoted or deleted, by another Owner or by themselves.
- Promoting a user to Owner is a separate, explicitly confirmed action; an invite can never grant Owner.
- The Owner's own password recovery stays `gamewarden owner reset-password` on the VM ([06 §4](06-security-and-access.md#4-authentication)).
- If there is more than one Owner, any Owner may demote another, but never the last one.

### 5.6 Sessions, disable and revoke

- Permissions are resolved per request from the user id and bindings; **do not put the role list or the permission
  list in the cookie**.
- `IsOwner`, disabled state and security stamp are checked on every request. Phase 0a adds the disabled check and
  rejects a session whose `gw:owner` claim no longer matches the user; a demoted Owner cannot keep Owner rights
  until the cookie expires. Permissions themselves stay out of the cookie and are resolved from bindings.
- Disable, password reset and "sign out everywhere" bump the security stamp; every session ends on the next request.
- A role or binding change does **not** bump the stamp (that would log the friend out for a routine change); the
  per-request permission check is enough, and the panel refreshes its view.
- A per-device session list is a later nicety; revoke-all covers the real cases.

### 5.7 Audit and activity

- Add a stable actor to every state change: `actor_kind` (`user`, `cli`, `agent`, `system`), `actor_user_id`,
  `actor_name`, source IP.
- Log invite created / accepted / revoked, binding added / removed, user disabled / enabled, password reset,
  Owner promoted / demoted, login failure and permission denial. Never log a password or a token.
- The activity feed shows friendly text ("An restarted valheim-main"); the audit page shows the full record. Both
  read the same rows; the audit page is Owner-only ([06 §8](06-security-and-access.md#8-audit)).
- Keep the old `User` display column for existing rows; backfill `actor_user_id` where the name matches, leave it
  null otherwise. Never rewrite history.

## 6. Panel UX

### 6.1 Where it lives

The Owner needs one place for people, and each server needs a short access list.

```
System
├─ Overview
├─ Jobs
├─ Logs
├─ Notifications
├─ Agents
├─ Operations
├─ Users & Access        ← new, Owner only
└─ Adapters

Servers → valheim-main
├─ Overview
├─ Console
├─ Settings
├─ Jobs
├─ Mods
├─ Game API
├─ Access                ← new, Owner and instance.access.manage
└─ Info
```

The phone bottom bar stays four items ([ADR-0035](../decisions/0035-task-first-navigation-activity-and-phone-bottom-bar.md));
Users is a System tab, and the server Access tab joins the section picker.

### 6.2 Users & Access

```
Users & Access                                  [Invite a friend]
┌────────────────────────────────────────────────────────────────────────┐
│ An Nguyen        Owner                          all servers   ● Active  │
│ Minh Tran        Operator · valheim-main       1 server       ● Active  │
│ Lan Pham         Player · all servers          3 servers      ● Invited │
└────────────────────────────────────────────────────────────────────────┘

Pending invites
┌────────────────────────────────────────────────────────────────────────┐
│ Player · all servers · expires in 6 days     [Copy link] [Revoke]      │
└────────────────────────────────────────────────────────────────────────┘
```

- **List**: name, role chips with scope, status (active / disabled / invite pending), last seen; search when the
  group grows.
- **Empty state**: "Just you so far. Invite a friend to watch or run a server." with the invite button.
- **Invite**: one modal, four short steps or one scroll: role cards (Player recommended, Operator, Admin with the
  code warning) → scope ("All servers" or specific servers) → expiry → the link with *Copy*. Do not show a raw
  permission matrix.
- **User drawer**: identity, status, last seen; access list with *Add access* and remove; **Can do here** preview
  by server; *Reset password link*, *Disable*, *Make Owner* (with confirmation). Disable is the destructive-looking
  action; delete/remove account is not offered in v1.
- **Pending invites**: visible only when non-empty; copy and revoke. Expired invites move to a collapsed "Expired"
  list or disappear after a day; do not clutter the main list.

### 6.3 Server → Access

- Members with their role on **this** server; add an existing user or invite a new one.
- "What this role can do here" summary, generated from the same permission catalog.
- The Owner sees every binding; an Admin with `instance.access.manage` (if that is ever granted) sees only this
  server and cannot grant Admin or Owner.
- Removing the last binding on a server is allowed; the user stops seeing that server unless a global binding still
  covers it.

### 6.4 Invite accept page

A standalone page, no app shell:

```
Vinh invited you to GameWarden

You will be an Operator on valheim-main.
You can start, stop and restart it, read the console and take a backup.
You cannot change settings, mods or delete it.

[ Choose a user name        ]
[ Choose a password         ]
[ Confirm password          ]

[ Join ]
```

- Show the inviter, the role and the server in words, not permission keys.
- If the invite is for all servers, say "all current and future servers".
- The page explains the password rule (10+ characters) and that the Owner can reset it with a new link.
- On success, sign in and land on the Dashboard or the server they were invited to.
- A used/expired link shows the same neutral message and "Ask the Owner for a new link".

### 6.5 Role-aware UI

- **Player**: Dashboard (their servers), Servers (status, players, *Connect*), Activity (their servers). No System
  (the read-only Adapters tab is hidden too — this narrows `adapters.read` from [06 §5.2](06-security-and-access.md#52-roles)),
  no console, no settings, no mods, no jobs/logs. A *Request restart* button posts a notification, not a command.
- **Operator**: everything a Player sees, plus Console, Jobs, Settings read-only (secrets show "set"), Backups
  (create), Game API actions (announce, save, kick). No settings write, mods, update, restore or delete.
- **Admin**: the full server, including settings, mods, update and restore. No delete in v1 (Owner only until
  backups exist), no System, no Users, no adapters.
- **Owner**: everything.
- Hide what a role cannot use; do not show a disabled button for a page a Player will never have. A read-only
  Settings tab for an Operator is the exception: it is useful and clearly labelled.
- A 401 sends the user to sign-in; a 403 shows "Your access changed — ask the Owner" and a link back to the
  servers, not a raw error.
- All strings in `en.ts` and `vi.ts`; a phone pass for the list, the invite modal and the accept page.

### 6.6 Dashboard and first run

- After the first server exists, show a dismissible **Invite your friends** card next to *Getting started*.
- The card explains the three roles in one line each and opens the invite modal.
- The panel should never suggest sharing the panel outside the VPN; the Accept page is reachable only inside it.

## 7. API and data model

### 7.1 Tables

```text
users
  id, user_name, display_name?, password_hash, security_stamp,
  is_owner, disabled_at?, created_at, password_changed_at, last_seen_at?

role_bindings
  id, user_id, role (admin|operator|player),
  scope_type (global|instance), scope_id?, created_at, created_by
  unique (user_id, role, scope_type, scope_id)
  -- scope_id is NULL for global: use a partial unique index, or a sentinel,
  -- because SQLite treats NULLs as distinct in a plain unique index

invites
  id, purpose (join|reset), token_hash unique, user_id? (reset),
  role?, scope_type?, scope_id?, created_by,
  created_at, expires_at, used_at?, used_by?, revoked_at?

activity (existing)
  + actor_kind, actor_user_id?, actor_name
```

- `user_name` is already `COLLATE NOCASE` with a unique index, so ASCII case variants are one account today
  (`OWNER` signs in as `owner`). `NOCASE` is ASCII-only; if Unicode names must case-fold, add a normalised column.
  No change is needed for v1.
- `scope_id` is the instance id. A global binding has `scope_id = null`.
- No `sessions` table in v1; the security stamp is the revoke-all mechanism.
- The Owner has no binding rows. `IsOwner` is the only special case.
- Agent clients stay in their own tables; do not reuse `users` for machine identities.

### 7.2 Endpoints

| Method | Path | Who | Notes |
|---|---|---|---|
| `GET` | `/auth/me` | signed-in | Adds the user's **global** permissions and identity; the panel uses it for the shell |
| `GET` | `/instances` | signed-in | Each item carries the caller's `permissions` for that server (small string array), so a list can hide actions without N+1 requests |
| `GET` | `/users` | `users.manage` | List with bindings, status, last seen |
| `PATCH` | `/users/{id}` | `users.manage` | `{ displayName?, disabled? }`; disable bumps the stamp |
| `PUT` | `/users/{id}/roles` | `users.manage` | Replace bindings atomically; last-Owner guard |
| `POST` | `/users/{id}/owner` · `DELETE` | `users.manage` | Promote / demote; confirmation and last-Owner guard |
| `POST` | `/users/{id}/reset-link` | `users.manage` | Returns `{ url, expiresAt }` once |
| `GET` | `/users/{id}/permissions?instanceId=` | `users.manage` | The "Can do here" preview |
| `POST` | `/invites` | `users.manage` | `{ purpose, userId?, role?, scope?, expiresInHours }` → `{ id, url, expiresAt }` |
| `GET` | `/invites` | `users.manage` | Pending only |
| `DELETE` | `/invites/{id}` | `users.manage` | Revoke by id, **not** by token, so the token never reaches a log |
| `POST` | `/invites/{token}/accept` | anonymous, rate-limited | `{ userName, password }` → session; consumes atomically |
| `POST` | `/invites/{token}/reset` | anonymous, rate-limited | `{ password }` → session; bumps the stamp |
| `GET`/`PUT` | `/instances/{id}/access` | `instance.access.manage` | Members and bindings for one server |
| `POST` | `/auth/logout-everywhere` | signed-in | Bumps the stamp |

`/users` and `/invites` already appear in [05](05-api.md); this refines them. `DELETE /invites/{token}` in the
current API doc should become `DELETE /invites/{id}` (§10 Q10).

The per-instance `permissions` array on `GET /instances` is a deliberate duplication: it keeps the panel's list
render one request, and it is small (at most a few dozen strings). The server remains the source of truth; the
array is a view of the same check.

### 7.3 Enforcement

- One `PermissionCatalog` with constants; one `RoleCatalog` mapping each role to a set. No `if (role == "admin")`
  in endpoints.
- Endpoint metadata declares the permission and, for instance routes, the route parameter that carries the id. A
  policy handler resolves the user's effective permissions for that instance and returns 403/404.
- **A coverage test** enumerates every endpoint and fails if a non-anonymous one has no declared permission. This is
  the guard against a new endpoint that forgets authorization.
- A **snapshot test** asserts the role matrix in code matches [06 §5](06-security-and-access.md#5-authorization)
  (or this page, once accepted). When they drift, one of them is wrong.
- Owner is handled once, in the policy handler; not with scattered `IsOwner` checks.
- The CLI and Agent schemes keep their own policies; a panel permission never grants an Agent token anything.

### 7.4 Migration

1. Add nullable columns (`display_name`, `disabled_at`, `last_seen_at`) and the two tables. The existing Owner row
   needs no binding.
2. Backfill `actor_user_id` for activity rows where `User` matches a user name; leave the rest null.
3. Do not ship the checks in shadow mode: ship them with the Owner-only UI first. The Owner has every permission,
   so the change is behaviour-preserving; tests prove the matrix.
4. Turn on invites and the Users screen in the same release; there is no period with accounts but no roles.
5. The first non-Owner account is the real acceptance test: an Operator who can restart but not change settings.

## 8. Security and abuse cases

| Threat | Control |
|---|---|
| Invite link leaks in a public Discord channel or a screenshot | Single-use, 7-day expiry, revocable, hash at rest, audit; the Owner can see pending invites and revoke. Do not post links in a public channel; the bot DM/private channel is the intended path. |
| Token guessing | 256-bit random token, constant-time hash compare, rate-limited accept endpoint, generic errors. |
| Privilege escalation through the UI | Only `users.manage` changes bindings; an Admin with `instance.access.manage` (later) can grant only Player/Operator, only on that server, never Admin/Owner. |
| Last Owner lockout | Transactional last-Owner guard on demote, disable and delete; the CLI reset remains the break-glass path. |
| Disabled or demoted user keeps acting | Check disabled state and `IsOwner` on every request; bump the stamp on disable/reset; resolve permissions per request. |
| Owner flag stale in the cookie | Do not trust `gw:owner` alone; reload the user in `ValidateSessionAsync` or the policy handler and refresh/reject on change. This is a real gap in the current code. |
| Password reset by an attacker with panel access | Reset links are Owner-only, audited, single-use; resetting bumps the stamp and signs out every session. |
| Username enumeration | Generic login error; generic invite error. A taken user name on accept may need a clear message; accept that small leak or let the invite suggest a name. |
| Password sniffing over HTTP | Existing ADR-0005 accepts VPN encryption; do not add a false sense of TLS. TOTP protects replay, not a compromised VPN. |
| Mods are code | Keep the Admin warning; games run as `gamewarden-run`; one shared identity means a mod can touch another instance's files (accepted, [06 §7](06-security-and-access.md#7-adapters-hooks-and-mods-are-code)). |
| Shared server disk exhaustion | `instances.create` is Owner-only by default; resource checks already exist before a start. |
| Audit tampering | Append-only rows; no API to edit or delete audit records; retention job only. |
| Agent token manages users | The Agent surface must never expose `/users`/`/invites`; the Agent scheme is accepted only on `/api/v1/agent`. |
| CSRF on the accept form | The accept page is anonymous, so the signed-in CSRF token does not exist yet; give the page its own double-submit token or origin check. Do not create a session from a cross-site POST. |

## 9. Delivery plan

Sizes are the playbook's S/M/L, not calendar estimates. Each phase ends with tests and, for panel work, screenshots
at 1280/390 in light and dark.

### Phase 0a — foundation (S/M)

- `PanelPermissions`, `PanelRoleCatalog`, the `role_bindings` table and `PanelAccessService`.
- User columns (`DisplayName`, `DisabledAt`, `LastSeenAt`), the disabled check and the Owner-flag check in session
  validation, the last-Owner guard, and unit/API tests.
- Owner-only still; no visible change.

**Done when:** the role matrix and binding scopes have unit tests; a disabled user and a demoted Owner are refused
on the next request; the last Owner cannot be demoted or disabled.

### Phase 0b — endpoint enforcement (S/M)

- Per-endpoint permissions, instance-scope resolution, a coverage test that fails on an undeclared endpoint, list
  filtering for `GET /instances`, `/jobs` and `/activity`, and 404/403 behavior.
- Owner-only still; the tests create Operator/Player fixtures and prove the denials before the UI exists.

**Done when:** every `/api/v1` endpoint has a declared permission; an Operator fixture gets 403 on settings write
and 200 on restart; a per-instance Player sees only their server in the list.

### Phase 1 — accounts, invites, roles (M)

- `role_bindings` and `invites` tables and endpoints; one-time join and reset links.
- Users & Access screen: list, invite modal, pending invites, user drawer, disable, reset link, promote Owner.
- Accept and reset pages; auto sign-in; i18n; empty/loading/error states.

**Done when:** a friend accepts a link, signs in as Operator, restarts a server, and cannot open System; the Owner
revokes the invite before use and the link dies.

### Phase 2 — the server side (M)

- Server → Access tab; add/remove members; "Can do here" preview.
- Role-aware shell: hide System, console, settings write, mods, delete; show read-only Settings for Operator.
- Player view: connect info, roster, request restart (notification only).
- Activity with names; audit page for the Owner; 404/403 handling.

**Done when:** success criterion 6 passes on the VM; a Player sees only their servers and can copy connect info; a
demoted Operator loses the actions on the next request.

### Phase 3 — convenience and hardening (S/M)

- QR code for an invite; *Send in Discord* through the bot (DM or the server's private channel).
- TOTP optional for the Owner and any friend who wants it; recovery codes.
- Per-device sessions and *Sign out everywhere* UI.
- `instance.access.manage` for Admins, if the Owner wants delegation.

### Phase 4 — optional identity work (M/L)

- Discord sign-in: the bot issues a short code after a DM, the panel exchanges it for a session; link only from a
  signed-in panel first. Still a second path, not the account source.
- Steam OpenID or SteamID linking: identify a player in the roster as "this panel user"; useful for Player access,
  not for Owner/Admin.
- Custom roles: clone a preset and edit grouped toggles; never a blank matrix.
- Multiple Owners UI polish if the group actually uses it.

**Not in M6:** custom roles, Discord OAuth, passkeys, Steam login, group/tag scopes, per-user quotas, email. Those
need their own decision after the fixed-role version is in use.

## 10. Decisions for the Owner

Each question has the agent's recommendation; the Owner's answer decides the ADR. Questions already in
[09](09-open-questions.md) keep their ids.

| # | Question | Recommendation | Why |
|---|---|---|---|
| Q1 (OQ-05) | Can Admins invite users? | **No in v1.** Owner only. Revisit with `instance.access.manage`. | One administrator avoids escalation paths and support confusion in a 3–10 person group. |
| Q2 | Can Admins create servers? | **No.** Owner only. This narrows the Friend-Admin persona in [01 §5](01-vision-and-scope.md#5-personas); a co-admin who creates servers should be an Owner. | One shared VM, no backups, shared disk; a server is a commitment for everyone. |
| Q3 | Can Admins delete or purge servers? | **No until backups exist.** Owner only. | Purge is irreversible today; an Admin can still restart and repair. |
| Q4 | Can Admins manage mods? | **Yes, with the code warning.** | ADR-0006 already trusts Admins; the run-as user bounds the blast radius. |
| Q5 | Does a Player see the player roster? | **Yes**, with the game info. | It answers "who is on?" and is low risk; hide it with a permission if the group prefers. |
| Q6 (OQ-10) | Player "request restart"? | **Notify-only**, after M6. | Gives friends a voice without giving them control. |
| Q7 | More than one Owner? | **Allow, but make it rare.** Last-Owner guard. | A second Owner is a break-glass convenience, not a role to hand out. |
| Q8 | Custom roles? | **Defer.** Four presets first. | The group is small; a matrix multiplies tests and support. |
| Q9 | Discord sign-in / Steam login? | **Defer.** Invite links through Discord first. | Local accounts work without OAuth redirects or HTTPS; identity linking can come later. |
| Q10 | Invite API by token or id? | **Revoke by id.** | The revoke call should not repeat the token; the accept and reset URLs are the only places the plaintext token travels, and request logs must redact them. |
| Q11 | Invite lifetime? | **7 days**, changeable; reset 24 hours. | Friends may not read Discord immediately; a week is enough and revocable. |
| Q12 | TOTP? | **Optional later**, not required. | Works over HTTP; add it when the Owner wants a second factor, not as a blocker for M6. |
| Q13 | Where does user management live? | **System → Users & Access**, plus Server → Access. | Keeps the four-item top navigation and the phone bar. |
| Q14 | Usernames or display names? | **Username for login, optional display name** for activity. | "An restarted valheim-main" reads better than "an.nguyen"; login stays simple. |
| Q15 | What happens on a role change? | **Immediate**, on the next request; no forced sign-out. | Routine changes should not interrupt play; disable and reset still end sessions. |

## 11. Sources

Checked 2026-11-23.

- Pterodactyl 1.x permission source: <https://github.com/pterodactyl/panel/blob/1.0-develop/app/Models/Permission.php>
- Pelican role source: <https://github.com/pelican-dev/panel/blob/main/app/Models/Role.php>; comparison:
  <https://pelican.dev/docs/comparison>
- AMP user permissions: <https://discourse.cubecoders.com/t/managing-user-permissions-in-amp/2301>
- Crafty user/role configuration: <https://docs.craftycontrol.com/pages/user-guide/user-role-config/>
- Grafana organization users and invites:
  <https://grafana.com/docs/grafana/latest/administration/user-management/manage-org-users/>
- Jellyfin users: <https://jellyfin.org/docs/general/server/users/adding-managing-users/>
- Home Assistant users: <https://www.home-assistant.io/docs/configuration/user-configuration>
- Grafana last-admin guard: **[Unverified: the `ErrLastGrafanaAdmin` name came from the research, not from a page
  read here]**; the rule itself is common and worth copying.

Internal sources: [06](06-security-and-access.md), [05](05-api.md), [07 §9](07-ux-and-user-flows.md#9-role-aware-examples),
[01 §5](01-vision-and-scope.md#5-personas) and [§8](01-vision-and-scope.md#8-success-criteria),
[ADR-0006](../decisions/0006-rbac-with-separate-owner.md), [ADR-0018](../decisions/0018-backups-leave-the-mvp.md),
[ADR-0035](../decisions/0035-task-first-navigation-activity-and-phone-bottom-bar.md).
