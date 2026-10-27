# ADR-0009: Explicit ACL on the data root; secrets readable only by SYSTEM

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

- Measured 2026-10-27 on the Windows 11 dev machine: `icacls C:\` shows `NT AUTHORITY\Authenticated Users:(OI)(CI)(IO)(M)`. Every folder created under `C:\` inherits *Modify* for every local user, so `C:\GameWarden` would be writable by `gamewarden-run` and by the mods running as it **[to re-check on the VM]**.
- With that default, a mod could replace `tools\steamcmd\steamcmd.exe` or a hook and wait for Core to run it, or edit `db\gamewarden.db` to make a user Owner.
- Core (SYSTEM) also writes into instance folders (rendered configs, restore, purge). If `gamewarden-run` can write there, a planted junction can redirect a SYSTEM write or delete to another path.
- DPAPI with `DataProtectionScope.LocalMachine` lets *any process on the computer* decrypt the data (Microsoft Learn, `DataProtectionScope`).

## Decision

1. **Explicit ACL on the data root.** The installer creates the data root with inheritance disabled and only these entries:

   | Path | SYSTEM, Administrators | `gamewarden-run` |
   |---|---|---|
   | Data root, and everything not listed below | Full control | none |
   | `adapters\` | Full control | Read & execute |
   | `tools\steamcmd\` | Full control | Modify |
   | `instances\<id>\` (the folder itself and `instance.json`) | Full control | Read |
   | `instances\<id>\{server, config, data, mods, logs}\` | Full control | Modify |

   Core sets the instance entries when it creates an instance and re-applies them on start-up, the same way it reconciles firewall rules.
2. **SYSTEM never executes or loads a file from a path that `gamewarden-run` can write.** SteamCMD and hooks therefore run as `gamewarden-run` ([ADR-0008](0008-run-as-user-and-launch-chain.md)).
3. **Core touches instance folders only while impersonating `gamewarden-run`** (`WindowsIdentity.RunImpersonated` with the ADR-0008 token), behind one interface, `IInstanceFiles`. Backups read instance files as `gamewarden-run` and write the zip as SYSTEM into `backups\`. Restores read the zip as SYSTEM and write the files as `gamewarden-run`.
4. **Secrets use DPAPI in the `CurrentUser` scope** of LocalSystem, so only SYSTEM can decrypt them. `secrets\` is SYSTEM + Administrators only. This includes the `gamewarden-run` password. ASP.NET Core Data Protection keys (session cookies, antiforgery) are persisted in `keys\` and protected with DPAPI the same way.

## Consequences

- A mod or hook cannot reach SYSTEM through files that Core executes or writes.
- Local Administrators can still read everything; the Owner is an Administrator on the VM anyway.
- Moving to another VM still needs secrets re-entered ([08 §8](../docs/08-operations.md#8-disaster-recovery)), and sessions become invalid.
- Installer, upgrade and instance creation must set ACLs; Windows integration tests check them.
- Secrets that a game keeps in its own config file (e.g. Palworld `AdminPassword` in an INI) are still plaintext in that file and in backups of it — see OQ-14.

## Alternatives considered

- **Keep the inherited ACL** — any local user, including mods running as `gamewarden-run`, can modify Core's files.
- **DPAPI `LocalMachine` scope** — any local process, including mods, can decrypt.
- **Do file operations as SYSTEM but refuse reparse points** — easy to miss on one code path; impersonation covers all of them.
