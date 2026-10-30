# 12 · Explanations: a help drawer for settings and choices

**Status: Accepted (the Owner approved it on 2026-10-29: a small help icon; the length is a guide, not a limit; topics in English)**. The Owner asked for this before M3 (ROADMAP, M5). Panel rules: [11](11-web-panel.md). Adapter files: [04](04-adapter-spec.md).

## 1. Goal

When a setting or a choice is not clear, the user opens a longer explanation next to it, in a drawer, with the **sources at the end**. The sources serve two readers: the maintainer who updates the text, and the user who wants to check it. Requirements:

- One pattern for the whole panel: the server Settings tab, the new server wizard, System (network, updates), setup and account. Later it covers backups and schedules (backlog, ADR-0018).
- One component set: no per-screen variants.
- It works where the trigger already sits in a dialog: the wizard is a drawer, and the network settings form is a modal.
- The text is short, clear and useful: what the setting does, what to choose, what it costs, what we tested.

## 2. What exists (main, 2026-10-29)

- **Field renderer.** Server settings are rendered by one component, `SettingField`, through `useSettingSections`. The Settings tab and the wizard share it.
- **Text per setting.** Each setting has `label`, `help` and `warning`, each a string or `{ en, vi }`.
  - `help` is the input's one-line description: in Valheim's 18 settings, 7 have help, 32–100 characters each.
  - There is no field for a longer text or for sources.
  - The sources of the Valheim research ([04 §9](04-adapter-spec.md#9-example-valheim-l2)) live only in pull request #19.
- **Panel text.** The panel's own inputs take their label and one-line description from `en.ts` / `vi.ts`.
- **No rich text.** There is no markdown and no HTML rendering. Rule [11 §10.7](11-web-panel.md#10-working-rules): never render HTML from the server.
- **Dialogs.**
  - Drawers: the wizard (`Drawer.Root`, right, `xl`) and the job log (`Drawer`, right, `xl`).
  - Modals: the network settings form (`modals.open`) and the confirmations.
  - There is no help icon, popover or hover card yet.

## 3. User experience

- **Trigger.** A small icon button (Tabler `IconHelpCircle`, 16 px, subtle, dimmed) in line with the label of a field, a section title or a card title, so it sits with the label without competing with it. It appears only where a topic exists.
  - The button is outside the `<label>` element, so it does not change the input's accessible name.
  - Its name is "About ‹label›" ("Giải thích ‹label›").
  - Its hit area is at least 40 px on phones (11 §5).
  - The one-line description under the input stays: it is the short answer. The drawer is the long one.
- **Drawer.** It opens on the right on desktop (`md`) and from the bottom on phones (about 85 % of the height). It has five parts, top to bottom:
  1. **Title**, normally the setting's label.
  2. **In short**: one or two sentences.
  3. **Body**: paragraphs, a list, a *tip* or *warning* note. For a choice, a values table: each option and what it means, with the default marked.
  4. **What we tested**, when a check on the VM or a test covers it, e.g. "Crossplay: joined from outside the VPN, 2026-10-29".
  5. **Sources**, always last: title, publisher, link, and the date the source was last checked ("checked 2026-10-29").
  - A topic that relies on an unverified source carries a "Not verified yet" badge instead of hiding it, like **[Unverified]** in the docs.
- **Related topics** open in the same drawer, with a Back button. A drawer never opens a second help drawer.
- **Language.** Topics are written in English only (the Owner's choice, 2026-10-29). The drawer's own words ("In short", "Sources", "Back", "Not verified yet") and the one-line descriptions under the inputs stay in English and Vietnamese (11 D1).
- **Length.** About 80–200 words is a guide, not a limit: a topic that needs more information takes more.

## 4. Dialog over dialog

Read in `@mantine/core` 9.7.4:

- **Escape.** Every open `Modal` or `Drawer` listens for Escape on `window` (capture phase) and closes itself (`use-modal.mjs`). Only dialogs inside one `Drawer.Stack` or `Modal.Stack` share a set that lets the first one handle the key and stop the others. Without it, Escape over the wizard would close the help drawer **and the wizard**, losing what was typed.
- **Tab.** Every active focus trap listens for Tab on `document`, but it ignores the key while focus is outside its own node (`scopeTab` in `use-focus-trap`). The help drawer is rendered in a portal, so the wizard's trap leaves Tab in the help drawer alone. *Corrected 2026-10-29 during the first implementation: this section first said the wizard's trap would pull Tab back.*
- **Mantine's own solution, and why it does not fit.** `Drawer.Stack` / `Modal.Stack` disable the focus trap and Escape of every dialog but the active one. They take only plain `Drawer` / `Modal` from the same stack: not the wizard's `Drawer.Root`, not `modals.open`, and not a help drawer that can open over any of them.

**Decision: one help drawer; host dialogs give up Escape while it is open.**

- **One drawer for the whole panel.** A single `HelpDrawer` sits under `HelpProvider` in `App.tsx`. `useHelp().open(request)` opens it from anywhere. Its `zIndex` is one layer above Mantine's modal layer.
- **Hosts step aside.** A dialog that contains help triggers spreads `useHelpHost()` into its props. That gives `closeOnEscape` = `!help.opened`, the Escape half of what `Drawer.Stack` does for inactive drawers. The host keeps its focus trap: the trap leaves Tab alone while focus is in the help drawer, and turning it off and on would move focus inside the host.
  - Hosts: the wizard drawer, the job log drawer, and the network settings, switch back and change password dialogs.
  - Those three dialogs moved from `modals.open` to `Modal` components, so their props can change while they are open.
- **Focus.** Closing the help drawer returns focus to the button that opened it (Mantine's `returnFocus`).
- **Tests** (Vitest + Testing Library) check the behaviour inside the wizard and inside the network dialog:
  - Escape closes only the help drawer;
  - Tab stays in the help drawer;
  - focus comes back to the trigger.
- **Checked 2026-10-29** in the first implementation's screenshots (`docs/panel/explanations`): on a phone the bottom help drawer sits above the host modal and its overlay.

## 5. Content model

The same shape serves both owners of help text, adapters and the panel. All text in a topic is English (§3):

```ts
type HelpTopic = {
  title?: string;               // default: the setting's or field's label (localised like the form)
  summary: string;              // one or two sentences
  body: Block[];
  values?: { value: string; text: string }[]; // for enum settings and choices; the default is marked
  tested?: string;              // what a VM walk-through or a test showed, with its date
  verified: boolean;            // false while any statement rests on an unverified source
  sources: Source[];            // at least one
  related?: string[];           // topic ids
};
type Block = { p: string } | { list: string[] } | { tip: string } | { warning: string };
type Source = { title: string; url: string; publisher?: string; checked: string /* YYYY-MM-DD */ };
```

- **Rendering.** Mantine `Text`, `List`, `Alert`, `Table`, `Anchor` and `Badge`. There is no markdown library and no HTML (11 §10.1, §10.7). It also costs almost nothing against the 350 KB budget (11 §10.9).
- **Sources.** Links open in a new tab with `rel="noreferrer"`. Only `https` URLs are accepted. A repository document counts as a source through its GitHub link, e.g. 10 §4 for update channels.

### 5.1 Where the text lives

| Owner | File | Served |
|---|---|---|
| A game adapter | `adapters/<id>/help.json`: topics keyed by setting key, by `group:<id>`, or by `option:<key>=<value>` for one option | Core validates it with the adapter (04 §8: unknown keys, `https`, ISO dates, summary and sources present) and serves it **on demand** at `GET /api/v1/adapters/{id}/help`. `GET /adapters` only flags which settings and groups have a topic (`hasHelp`), so the forms stay light |
| The panel | `panel/src/help/topics/<area>.ts`, typed `HelpTopic` records with ids like `system.vpnRanges` | Bundled with the panel. A test checks that every topic has a summary, a body, at least one source and a date per source |

This keeps `settings.schema.json` about the form and puts the explanations in the adapter's own file, where they are updated with the adapter.

## 6. Topics to write (priority order)

What already exists stays as the short line under each field; these get a drawer. Candidate sources are listed for the research step; each is checked and dated when the topic is written.

### 6.1 Valheim (`adapters/valheim/help.json`)

| Topic | Why it needs more than a line | Candidate sources |
|---|---|---|
| `crossplay` | Security: PlayFab's relay bypasses the VPN and the firewall; join code; what the VM test showed (OQ-36) | Valheim Dedicated Server Manual (in `server/`), Iron Gate's crossplay patch notes, the VM walk-through 10 §8.4 |
| `public` (since adapter 0.3.2) | Listing: every Valheim player sees the name, while the firewall still scopes the game ports to the VPN ranges; what the list shows to friends on the VPN is not checked | Iron Gate's guide, a server host's guide, 06 §1 |
| `preset` + `values` | What each preset changes (casual, easy, normal, hard, hardcore, immersive, hammer) | Server Manual, Iron Gate's world-modifier patch notes, Valheim Wiki "World Modifiers" (community) |
| `group:modifiers` and each of combat, death penalty, resources, raids, portals | What each level does; settings that stay in the world after the argument is removed (OQ-35) | Same, plus the VM walk-through |
| `nobuildcost`, `playerevents`, `passivemobs`, `nomap` (keys) | A topic each (`values` belong to enum settings only); the group topic lists the four in a short list | Server Manual, Wiki |
| `world` | Another name loads or creates another world; where the files are; backups | Server Manual, 08 §4 |
| `password` | Rules (5+ characters, not inside the server name) and why | Server Manual |
| `saveInterval` | Why 600 s instead of 1800 s (OQ-32); cost of shorter intervals | Server Manual, 09 OQ-32 |
| `backups`, `backupShort`, `backupLong` | Valheim's own world backups compared with GameWarden's backups (backlog) | Server Manual, 08 §4 |

### 6.2 Panel (`panel/src/help/topics/`)

| Topic | Screen | Candidate sources |
|---|---|---|
| `system.vpnRanges` | System → network; setup step 3 | Radmin VPN's 26.0.0.0/8 range, Tailscale's IP address docs (100.64.0.0/10), RFC 6598, ADR-0005, 08 §7 |
| `system.panelNetworks` | System → network | 06 §2, OQ-28, ADR-0005 |
| `system.channel` | System → updates (today it has no description at all) | 10 §4–6, ADR-0013 |
| `system.restoreDb` | System → switch back | 10 §6, ADR-0013 |
| `wizard.id` / `wizard.name` | Wizard | 03 (instance id rules) |
| `wizard.portsAuto` | Wizard review | 08 §7 (ports and firewall) |
| `auth.password` | Setup, change password | NIST SP 800-63B (length over composition rules), 06 |
| `setup.token` | First-run setup | 06 §4 |

### 6.3 With backups (backlog)

Backup mode (`stop`, `saveThenCopy`, `hot`), retention and pinning, schedules in the VM's time zone, the low-disk guard. These topics are written in the same pull requests as the backup features.

## 7. Plan

Each step is one pull request with tests and screenshots (1280 px and 390 px, light and dark). *2026-10-29: the three steps were built together in one pull request.*

1. **Panel framework.**
   - Components: `HelpProvider`, `HelpDrawer`, `HelpButton`, `useHelpHost`, the block renderer and the panel topic registry with its test.
   - The network dialog becomes a `Modal` component.
   - The dialog-over-dialog tests of §4.
   - The first panel topics: `system.vpnRanges`, `system.panelNetworks`, `system.channel`.
2. **Adapter help.**
   - docs/04 gains §5.3 `help.json`.
   - Core: validation, `GET /adapters/{id}/help`, the `hasHelp` flags; the generated client is regenerated.
   - `SettingField` and the section titles get the trigger.
   - Valheim's topics of §6.1, researched with sources.
3. **The remaining panel topics** of §6.2.

Topics are written in English (§3); the Owner reads them in the pull request.
