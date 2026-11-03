# GameWarden {{version}}

> Copy this file to `docs/releases/<version>.md` before tagging. The release workflow uses it as the GitHub release
> body; the panel shows that body in the update confirmation.

## Data impact

Pick one and delete the other two.

- [ ] **Update only** — instances, worlds, mods and the database stay as they are.
- [ ] **Back up before updating** — the release changes data that may not survive:
  - `C:\GameWarden\instances\<id>\data` (worlds and saves)
  - `C:\GameWarden\db\gamewarden.db` (servers, settings, Owner account)
  - `C:\GameWarden\instances\<id>\mods` and `BepInEx\config` if you changed mods or configs
- [ ] **Clean start** — the database or the instance layout is incompatible; this release may need a fresh data
  folder. Back up first, install, then re-import the folders above.

## What changed

- …

## What to test on the VM

- …

## After updating

- [ ] Server list still shows the instances and they start.
- [ ] The worlds are the expected ones.
- [ ] If data was reset: re-import the backup and update the settings that were lost.
