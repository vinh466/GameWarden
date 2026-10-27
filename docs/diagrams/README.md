# Diagrams

The pictures of this spec, as [Excalidraw](https://excalidraw.com) files. Open one in excalidraw.com (menu → *Open*) or in VS Code with the *Excalidraw* extension (`pomdtr.excalidraw-editor`), edit it, and save it back to the same file. The files are the source; there are no exported images to keep in sync.

| File | Shows | Spec |
|---|---|---|
| [01-system-context](01-system-context.excalidraw) | The VM, VPN, firewall, Core, Runners, games, CLI, data root and external services | [02 §1](../02-architecture.md#1-context), ADR-0005, ADR-0008, ADR-0009 |
| [02-core-modules](02-core-modules.excalidraw) | Core modules by layer, infrastructure interfaces, event bus and subscribers | [02 §3](../02-architecture.md#3-core-modules) |
| [03-process-and-identity](03-process-and-identity.excalidraw) | Launch chain, Runner console rules, Job Object, identities, stop ladder | [02 §4, §6](../02-architecture.md#4-process-model-and-the-runner), ADR-0003, ADR-0008 |
| [04-filesystem-and-acl](04-filesystem-and-acl.excalidraw) | Folder layout and who may read, write or execute what | [08 §1](../08-operations.md#1-filesystem-layout), ADR-0009 |
| [05-instance-state-machine](05-instance-state-machine.excalidraw) | Instance states, transitions, crash policy | [03 §2–4](../03-domain-model.md#2-instance-states) |
| [06-flow-start-stop](06-flow-start-stop.excalidraw) | Start and graceful stop, end to end | [02 §4](../02-architecture.md#4-process-model-and-the-runner), [05](../05-api.md) |
| [07-flow-crash-and-reattach](07-flow-crash-and-reattach.excalidraw) | Crash and restart, Runner loss, Core restart or upgrade, VM shutdown | [02 §4–5](../02-architecture.md#5-desired-state-and-reconciliation) |
| [08-flow-update-backup-restore](08-flow-update-backup-restore.excalidraw) | Update check, update as one composite job, restore | [03 §5](../03-domain-model.md#5-jobs), [08 §4](../08-operations.md#4-backups) |
| [09-flow-access](09-flow-access.excalidraw) | First run, invite, login, permission check, audit | [06 §4–5](../06-security-and-access.md#4-authentication) |

## Conventions

- **Colours = identity or role.** Violet: LocalSystem (Core). Orange: `gamewarden-run`. Yellow: data and notes. Blue: people. Green: game traffic and event subscribers. Grey: OS and external services. Red: untrusted code or failure states.
- **Dashed boxes** are boundaries: machine, identity, console, Job Object.
- **Sequence diagrams:** solid arrow = call or command, dashed arrow = reply or OS side effect; tinted boxes on a lifeline are steps inside that participant.
- Open points are labelled with their id from [09-open-questions](../09-open-questions.md) (e.g. "OQ-20 (open)").
- When a diagram and the text disagree, fix one of them in the same change.
