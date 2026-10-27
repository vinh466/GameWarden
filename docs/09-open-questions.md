# 09 · Open questions

Close each question by recording the answer here; if it changes architecture, also write an ADR.

| ID | Question | Options | Recommendation | Status |
|---|---|---|---|---|
| OQ-01 | Panel framework | **A.** Blazor WebAssembly + component library (e.g. MudBlazor): one language, DTOs shared with Core. **B.** React + TypeScript + Vite: biggest ecosystem, smaller bundles. | A if you prefer staying in C#; B if you're comfortable with React. Both fit the architecture. | Open |
| OQ-02 | Identity of Runner + game processes | **A.** Same as Core (LocalSystem): simplest. **B.** Dedicated non-admin user `gamewarden-run`: Core starts Runners with that user's token; password generated at install, stored with DPAPI. | **B** — mods and hooks are code; friends with Admin role must not get SYSTEM. Validate in M0 that target games run fine under it. | Open |
| OQ-03 | Default panel port | 8080 · 8765 · other | A less common port (e.g. 8765) to avoid clashing with other tools | Open |
| OQ-04 | Discord login (OAuth) in addition to local accounts | Later / never | Later (after M3), only if friends ask; needs checking whether a VPN-only redirect URL is accepted **[Unverified]** | Open |
| OQ-05 | Can Admins invite users? | Owner only · Admins can invite Players/Operators for their own scope | Owner only in v1 | Open |
| OQ-06 | Installer technology | Inno Setup · WiX | Inno Setup (simpler script, enough for one service + files) | Open |
| OQ-07 | Resource guardrails | None · warn when starting would exceed free RAM · hard block | Warn (show estimated RAM from adapter vs free RAM) | Open |
| OQ-08 | Off-VM copy of backups | None in v1 · copy to a network share / cloud folder | Plan for v1.x; until then backups die with the VM disk | Open |
| OQ-09 | Remote repo hosting and visibility | Local only · private GitHub · public + license | Private remote first; decide license if it goes public | Open |
| OQ-10 | Player "request restart" | No · button that notifies Operators/Discord · vote | Notify-only button, after M3 | Open |
| OQ-11 | Show game password to Players via Share | On by default · off by default | On (friends-only); Owner can turn off per instance | Proposed in spec |
| OQ-12 | Games that need a Steam login in SteamCMD | Not supported · supported with stored credentials (Steam Guard issues) | Not supported in v1; adapter declares `requiresLogin` and falls back to a guide | Proposed in spec |
| OQ-13 | Relationship with `gamehost-infra` (Pelican/Linux plan in the same workspace) | Keep both · archive one | Decide which plan is active to avoid two diverging designs | Open |

## Spikes needed before M1 (answers feed back into the spec)

| Spike | Question to answer |
|---|---|
| S1 Ctrl+C delivery | Does the Runner console trick deliver Ctrl+C to Valheim and save the world? |
| S2 Session 0 | Do Valheim, Palworld, Terraria servers run correctly when started from a service (no desktop)? |
| S3 Run-as user | Can Core (LocalSystem) start a Runner as `gamewarden-run` and does the game work under it (OQ-02)? |
| S4 SteamCMD | Output format for progress parsing; build-id check for "update available"; behavior when two SteamCMD processes run. |
| S5 Prerequisites | Exact silent-install commands and sources for VC++ and DirectX runtime; does Valheim start after installing them on the template VM? |
| S6 Discord | Can a webhook edit its own pinned message for the live status? |
