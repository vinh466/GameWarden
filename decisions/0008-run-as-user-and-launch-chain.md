# ADR-0008: Games, SteamCMD and hooks run as `gamewarden-run`, started with CreateProcessAsUser

- **Status:** Accepted (closes OQ-02; game behaviour under this user is validated in spike S3)
- **Date:** 2026-10-27

## Context

- Mods (e.g. BepInEx DLLs) and adapter hooks are code. Friends with the Admin role can install mods, so whatever runs the game must not be SYSTEM ([06 §7](../docs/06-security-and-access.md#7-adapters-hooks-and-mods-are-code)).
- Core runs as `LocalSystem` ([ADR-0002](0002-core-as-csharp-windows-service.md)).
- With `ProcessStartInfo.UserName` set, .NET calls `CreateProcessWithLogonW` (dotnet/runtime `SafeProcessHandle.Windows.cs`, read 2026-10-27). Microsoft documents that this function cannot be called from a process running as LocalSystem and points to `LogonUser` + `CreateProcessAsUser` instead.
- `CreateProcessAsUser` neither loads the user profile nor builds the user's environment block. The new process also needs a window station and desktop it may use: with `STARTUPINFO.lpDesktop = ""`, a process in a non-interactive logon session connects to a window station named after its logon session, which the system creates if missing (Microsoft Learn, "Process Connection to a Window Station").
- `SeBatchLogonRight` ("Log on as a batch job") is granted only to Administrators on client editions by default (Microsoft Learn).
- Some games write to the user profile unless told otherwise. Valheim's default save path is `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim`; `-savedir` overrides it (official dedicated server guide).

## Decision

1. The installer creates a local user `gamewarden-run`: not in Administrators, a random password of 32+ characters stored as a secret ([ADR-0009](0009-data-root-acl-and-secrets.md)), password never expires, `SeBatchLogonRight` granted, interactive and Remote Desktop logon denied.
2. Runners (and therefore games), SteamCMD and adapter hooks run as `gamewarden-run`. Core keeps only the work that needs admin rights: firewall rules, prerequisite installers, service control.
3. Core starts each of these processes through `IProcessHost` with this chain:
   `LogonUser(LOGON32_LOGON_BATCH)` → `LoadUserProfile` (kept loaded while Core runs) → `CreateEnvironmentBlock` → `CreateProcessAsUser` with `lpDesktop = ""` and `CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT`.
   `Process.Start` with `UserName` is not used.
4. Adapters MUST point save data at `{paths.data}` with a launch argument where the game supports one (Valheim: `-savedir`). Spike S3 lists any other profile paths a game writes; they go into `savesRedirect` or `backup.paths`.
5. Development mode starts Runners as the current user with a plain `CreateProcess` ([02 §11](../docs/02-architecture.md#11-development-environment)).

## Consequences

- A malicious mod or hook gets a non-admin user, not SYSTEM, provided the data-root ACL of ADR-0009 is in place.
- We write and test the Win32 interop ourselves (P/Invoke; the Microsoft.Windows.CsWin32 source generator is a candidate).
- The installer manages a user account and a user right; uninstall removes both.
- All instances share one identity: a mod in one instance can modify another instance's files. Accepted for friends-only use; per-instance users would need a new ADR.

## Alternatives considered

- **Everything as LocalSystem** (OQ-02 option A) — simplest; any mod or hook becomes SYSTEM.
- **`Process.Start` with `UserName` / `Password`** — uses `CreateProcessWithLogonW`, which does not work from LocalSystem.
- **One user per instance** — real isolation between instances; more accounts, ACLs and passwords to manage. Revisit if people outside the friend group ever get Admin.
- **S4U logon (no stored password)** — avoids storing a password; more complex LSA interop **[Unverified]** for this use.
