# ADR-0036: Users get one-time links, fixed roles and per-server access; the Owner keeps system control

- **Status:** Accepted — the Owner approved the direction, and the safe defaults in points 1, 3 and 5, on 2026-11-24
  ("ok làm đi"), after the agent's research in [23](../docs/23-users-and-roles.md). The invite-link mechanism, the
  fixed-role catalog and the permission registry are the agent's design from that note.
- **Date:** 2026-11-24

## Context

GameWarden has exactly one panel account, the Owner. M6 adds friends: an account, a role and, usually, one server.
The current API names permissions ([06 §5](../docs/06-security-and-access.md#5-authorization)) but the code only
distinguishes Owner, CLI and Agent. The panel runs on HTTP over a private VPN, with no email server, on one shared
Windows host; backups are not built ([ADR-0018](0018-backups-leave-the-mvp.md)); mods are code, and every game runs
as the same `gamewarden-run` user ([ADR-0006](0006-rbac-with-separate-owner.md),
[ADR-0008](0008-run-as-user-and-launch-chain.md), [ADR-0009](0009-data-root-acl-and-secrets.md)).

The research in [23](../docs/23-users-and-roles.md) compared Pterodactyl, Pelican, AMP, Crafty, Grafana, Jellyfin
and Home Assistant. Two patterns fit: a **one-time invite link** that works without email (Grafana), and a
**role bundle plus a scoped binding** (Crafty, AMP). Two patterns do not: email-dependent onboarding and a blank
permission matrix.

## Decision

1. **Accounts are local, and links replace email.** The Owner creates a one-time **join** link with a role and a
   scope; the friend chooses a user name and password. A **reset** link does the same for a forgotten password. The
   token is random, stored only as a hash, single-use, expiring (7 days join, 24 hours reset), revocable, and
   redacted from request logs. The Owner never creates or knows a friend's password. An invite link can never grant
   Owner.
2. **Permissions are the contract; roles are named sets.** One registry contains every permission. The fixed roles
   are **Admin**, **Operator** and **Player**; **Owner** stays a flag, not a role. A binding is
   `(user, role, global | instance:<id>)`; effective permissions are the union of the bindings that cover the
   target. There are no deny rules and no custom roles in v1.
3. **Safe defaults for a small shared host.** Admins do **not** create servers, delete/purge servers or invite users
   in v1; those stay Owner-only until backups exist. An Admin manages everything inside an assigned server,
   including mods, with the existing warning that mods run code. An Operator runs the server day to day and may kick
   a player; a ban or a game-admin change is Admin. A Player sees status, connect info, the roster and a
   notification-only **request restart**. The Owner may promote a second Owner; the last Owner cannot be demoted,
   disabled or deleted.
4. **The panel checks permissions, not role names.** Endpoints declare a permission; the policy resolves it against
   the bindings and the target instance. A user who cannot see a server gets `404`; a known server where the action
   is denied gets `403`. The panel hides what a role cannot use; the API enforces regardless. The CLI and Agent
   identities keep their own schemes and roots.
5. **Access changes take effect on the next request.** No permission list is copied into the cookie. A disabled
   user, a changed security stamp, or a changed Owner flag ends the session on the next request; a routine role
   change is resolved from the database and does not sign the friend out. Disable and password reset bump the
   stamp.
6. **Out of scope for M6:** custom roles, Discord OAuth or Steam login, passkeys (blocked by HTTP outside
   `localhost`), group/tag scopes, per-user quotas and email. They need their own decision after the fixed-role
   version is in use.

Delivery is phased: **0a** permission catalog, role bindings, user/session safety and tests; **0b** endpoint
enforcement and list filtering; **1** invite/reset links and the Users & Access screen; **2** server Access tab and
role-aware UI. The details, panel flows and open questions are in [23](../docs/23-users-and-roles.md).

## Consequences

- Friends can help at 1 a.m. without system access, and the Owner can revoke access without changing a game
  password or a Windows account.
- No email infrastructure; the Owner copies a link into the group's existing Discord. A leaked link is bounded by
  single use, expiry and revocation.
- More schema (`role_bindings`, `invites`, user status fields), one permission registry, endpoint coverage tests,
  and a new panel destination. The deployment footprint does not change.
- Admins can still run code through mods; the non-admin `gamewarden-run` identity bounds it. One shared run-as user
  means an Admin of one server can affect another server's files; this is accepted for friends-only use.
- The last-Owner guard and per-request resolution are more code than a role string in a cookie, but they close the
  stale-Owner and disabled-user gaps the current session code has.
- Rollback: remove the bindings and the new panel screens; the Owner-only path keeps working because Owner is an
  implicit allow. The schema is additive.

## Alternatives considered

- **One shared admin account** — no accountability, all-or-nothing; rejected in [ADR-0006](0006-rbac-with-separate-owner.md).
- **Email invites** — no SMTP on the VM; the link has to travel through Discord anyway.
- **Custom roles first** — a blank permission matrix multiplies tests and support before the group needs it.
- **Deny rules / tri-state** — AMP shows how precise they are and how easy to misconfigure; a union of grants is
  enough for 3–10 friends.
- **Discord-only login** — couples the panel to a third party and still needs a local recovery path; a later optional
  identity proof, not the account.
- **Implicit ownership of a created server** — AMP users create an instance and cannot see it; if server creation
  opens up later, the creator gets an explicit Admin binding, never an implicit one.
