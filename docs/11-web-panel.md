# 11 · Web panel (M2)

> **Status: approved by the Owner on 2026-10-28, with every proposal of §15.** This page and [ADR-0015](../decisions/0015-panel-mantine-and-toolchain.md) are the M2 contract; [07](07-ux-and-user-flows.md) stays the UX source for later milestones. Working method: for each part, research the official pattern for the version in use (Mantine's Vite template and docs, TanStack docs) and follow it; deviations are written down with their reason.

The panel is the Owner's daily tool: see at a glance whether every server is fine, act in one or two clicks, from a desktop or a phone, over the VPN. Everything the CLI does today is in the panel; nothing needs the command line any more.

## 1. Scope

| In M2 | Later |
|---|---|
| First-run setup, login, sessions, password change | Friends' accounts, roles, invites (M6) |
| Dashboard, server list and detail (Overview, Console, Settings, Jobs, Info) | Backups and Schedules tabs (M3), Mods tab (M7), settings forms from `settings.schema.json` (M5) |
| New server wizard with resource, prerequisite and DNS checks | Prerequisite installer jobs (links only in M2) |
| Jobs with live progress and log; Activity feed | Discord settings (M4) |
| System: version, update / rollback, prerequisites, network, panel access | Player counts (M4: `—` until then) |
| Adapters list and reload | Adapter editor |
| Realtime state, job progress and console; connection and Core-restart notices; favicon status | PWA, web push (need HTTPS, [ADR-0005](../decisions/0005-http-only-over-vpn.md)) |

## 2. Functions

| Area | Functions |
|---|---|
| **Access** | Setup wizard on `http://localhost:8765/setup?token=…` (loopback only): Owner account → prerequisites (status, download links) → panel access (which networks may open the panel) → done. Login with rate limiting; 7-day sliding session; change password (signs out everywhere); `gamewarden setup-url` and `gamewarden owner reset-password` on the VM. |
| **Dashboard** | Monitoring only (D7): overview numbers (servers running, memory, disk, jobs; CPU load and per-server CPU / RAM once Core reports them), then short summaries that link to their screens: servers (one line each: state, game, ports, running job), *Needs attention* (crashed or problem servers, jobs failed in the last 24 h, missing prerequisites, blocked hosts), recent activity. Actions stay on the Servers screen. |
| **Server detail** | Header: name, state badge, desired state, actions (Start, Stop, Restart; ⋯ Update game, Validate files, Delete). Tabs: **Overview** (state, ports with *Copy connect info*, resources, recent jobs, last problem), **Console** (live, searchable, pause on scroll, command box with history), **Settings** (settings and secrets the adapter needs; *restart required* banner), **Jobs** (this server's jobs with live log), **Info** (adapter, level, paths, launch summary with secrets hidden). |
| **New server** | Wizard in a wide drawer over the server list, opened by *New server* on the Servers screen (D8): game (adapter cards with level) → name and id (slug suggested) → settings and secrets → review (ports, min / recommended resources, prerequisites, DNS) → *Create & install* → live install progress → *Start*. |
| **Jobs** | Global list with filters, live progress, log viewer (follows while running). A header indicator shows running jobs from every page. |
| **Activity** | Feed of state changes, commands (who, what), jobs and updates; filter by server and type. |
| **System** | Version and *Check for updates* (stable / release candidates) → *Update* with progress and the result of the last update; *Switch back to previous version*; prerequisites with links; DNS check; panel access networks and VPN ranges for game ports; data root and disk; adapter problems. |
| **Adapters** | List with level, version, built-in / Owner, validation errors; *Reload*. |
| **Everywhere** | Command palette (Ctrl/⌘ K): go to a server or page, start / stop / restart, new server, check for updates. Toasts for job results and crashes. Red banner when the realtime link drops; notice after Core restarted. Favicon colour ([07 §8](07-ux-and-user-flows.md#8-is-it-running--visibility-without-a-tray-app)). Light / dark / follow the OS. |

## 3. Layout

One application shell (Mantine `AppShell`) on every page after login:

```
Desktop (≥ 992 px)
┌──────────────────────────────────────────────────────────────────────────────┐
│ ◆ GameWarden   ● Core connected · v0.2.0      [⌕ Search… Ctrl K]  ⟳2  ◐  An ▾ │  header 56 px
├────────────┬─────────────────────────────────────────────────────────────────┤
│ ▣ Dashboard│  Servers / valheim-main                                         │
│ ▤ Servers  │  valheim-main  ● Running · desired running   [Stop][Restart][⋯] │  page header
│   ● valheim│  ───────────────────────────────────────────────────────────── │
│   ○ palworld│  Overview  Console  Settings  Jobs  Info                       │  tabs
│ ⟳ Jobs     │                                                                 │
│ ≡ Activity │  (content, max width 1400 px)                                   │
│ ⬢ Adapters │                                                                 │
│ ⚙ System   │                                                                 │
│            │                                                                 │
│ v0.2.0     │                                                                 │
└────────────┴─────────────────────────────────────────────────────────────────┘
  navbar 240 px; a burger in the header hides it on desktop (AppShell `collapsed.desktop`) —
  Mantine has no built-in icon-only rail, so the standard collapse is used

Phone (< 768 px)
┌───────────────────────────────┐
│ ☰  ◆ GameWarden    ●  ⌕  An ▾ │  navbar becomes a drawer
├───────────────────────────────┤
│ valheim-main      ● Running   │
│ [Restart]            [⋯]      │  secondary actions in a menu
│ Overview Console Settings …   │  tabs scroll sideways
│ …                             │
└───────────────────────────────┘
```

Page pattern: breadcrumbs, then a page header (title, status, actions on the right; actions fold into a menu on phones), then content. Forms and wizards stay ≤ 720 px wide; tables and the console use the full width.

## 4. Screens

**Dashboard** (as first proposed; replaced after the Owner's review by the monitoring layout of D7, see the screenshots in `docs/panel/phase1`)

```
┌ Core ● connected · up 3 d 4 h · v0.2.0 · ⬆ 0.3.0 available ─────────────────┐
│ CPU ▓▓▓▓░░░ 42 %      RAM ▓▓▓▓▓░░ 5.1 / 8 GB      Disk ▓▓░░░░░ 24 / 63 GB    │
└──────────────────────────────────────────────────────────────────────────────┘
┌ valheim-main ─────────────── ● Running ┐ ┌ palworld-coop ─────── ○ Stopped ┐
│ Valheim · up 5 h 12 m · 2456–2457/udp  │ │ Palworld · last run 2 d ago      │
│ CPU 18 % ▁▂▃▂▁   RAM 2.3 GB ▁▁▂▂▂      │ │ ⚠ below recommended RAM          │
│ [Restart]  [Console]            [⋯]    │ │ [Start]                   [⋯]    │
└────────────────────────────────────────┘ └──────────────────────────────────┘
Recent activity                                                   [All activity]
 04:00  valheim-main   Game updated (job 20261028-040000)
 01:47  valheim-main   Crashed (exit 3) → restarted automatically
```

Cards: 1 column on phones, 2 on tablets, 3 on wide screens. Status colour, icon and word together; a yellow *Busy* badge with the job's stage and percentage while a job holds the lock.

**Console tab:** full-height, monospace, virtualised (thousands of lines without lag), stderr in red, GameWarden's own lines dimmed; search highlights matches; scrolling up pauses following and shows *Jump to latest*; command box with ↑/↓ history; *Copy* and *Download log*.

**Settings tab:** the settings and secrets the adapter's launch section uses ([04 §4](04-adapter-spec.md#4-templates)); secrets are write-only (*set* / *not set*, never shown). The same sections as the new server wizard (D10), all open and free to fold, with *Discard* and *Save* in a footer that stays in view and counts the unsaved changes; a save with errors opens their sections and focuses the first one. *Save* → *Restart required* banner with *[Restart now]*. With L2 adapters (M5) each `group` of `settings.schema.json` becomes a section, the `advanced` ones closed ([04 §5](04-adapter-spec.md#5-settingsschemajson-l2)).

**New server wizard:** Mantine `Drawer` (right, size `xl`, 780 px; full width on phones) opened by `/servers?new=true`, so links and Back work with it; a click outside does not close it. The steps are sections of one scrolling form (`FormSections` on Mantine's `Accordion`, D10): the open section is the current step; completed sections close to one line that sums up what was chosen and reopen with a click; later sections stay locked until reached. *Back*, the step count and *Next* (*Create & install* on the last section) sit in a footer that sticks to the bottom of the drawer, as Drawer.Header sticks to the top; the section that opens scrolls to the top and takes the focus. *Next* validates the open section and shows all its errors at once; the review section shows every check with ✓ / ⚠ / ✕ and the reason; the Owner may continue past a ⚠ and, for minimum resources, past ✕ with *Install anyway*.

**System:** sections in cards: *Version & updates* (current, latest, channel switch stable / release candidate, *Update*, last result, *Switch back to <previous>*), *Prerequisites*, *Network* (DNS check, panel access networks, VPN ranges), *Storage* (data root, free disk), *Adapters* (problems).

**Setup and login:** centred single card on a plain background, product mark on top; the setup wizard is a `Stepper` of 3 steps.

## 5. Visual style

| Topic | Choice |
|---|---|
| Character | Calm operations console: clean surfaces, generous spacing on overview pages, compact tables and console. Modern but not decorative. |
| Theme | Mantine theme, `defaultColorScheme: "auto"` (follows the OS) with a toggle; both schemes are designed, not inverted. |
| Primary colour | `violet` (proposed): distinct from every status colour, so a primary button never reads as a state. |
| Status colours | Running `green` · Starting / Stopping `blue` · Crashed / Faulted `red` · Stopped / Not installed `gray` · Busy / warning `yellow`. Always colour + icon + word. One `StatusBadge` component owns this mapping. |
| Typography | Inter Variable for UI, JetBrains Mono Variable for console, logs and ids; both self-hosted (`@fontsource-variable/*`): the VM may have no internet and the panel loads nothing from third parties. |
| Shape and density | Radius `md`; cards with border and small shadow in light mode, border only in dark; tables `verticalSpacing="xs"`; touch targets ≥ 40 px on phones. |
| Icons | Tabler icons (Mantine's own set), 18 px in buttons and nav, stroke 1.75. |
| Motion | Mantine transitions only (modals, drawers, notifications, skeletons); nothing loops except progress; respects `prefers-reduced-motion`. |
| Empty, loading, error | Skeletons in the shape of the content; empty states with one clear next action; errors show the API's `title` and a retry. |

## 6. Interaction rules

1. **Every action answers.** Buttons show their pending state; the result arrives as a toast and in the UI state (realtime); failures show the API's message and `code` ([05 §5](05-api.md#5-error-codes)).
2. **Unavailable actions say why.** Disabled buttons carry a tooltip ("Updating — job 20261028-…", "Not installed"). Nothing is hidden only because it is temporarily unavailable.
3. **Destructive actions confirm.** Stop asks once when players are online (M4); Delete opens a dialog where the id must be typed; an update shows what happens to running games.
4. **Below the minimum, the Owner decides.** `412 resources_insufficient` turns into a dialog listing what is missing with *[Start anyway]*.
5. **State is pushed, not polled.** SignalR events update the cached data; after a reconnect the panel re-fetches ([05 §3](05-api.md#3-realtime-signalr)).
6. **Keyboard.** Ctrl/⌘ K palette; `g d` dashboard, `g s` system; Enter submits the console box; Esc closes dialogs.
7. **Phone first for daily work.** Status, restart, console tail and job progress are checked at 390 px wide on every change.

## 7. Stack

Versions checked on npm on 2026-10-28; exact versions are pinned in `package-lock.json` and updated by Dependabot.

| Concern | Choice | Version | Why |
|---|---|---|---|
| UI kit | Mantine: `core`, `hooks`, `form`, `notifications`, `modals`, `spotlight`, `dates`, `nprogress` | 9.7 | Owner's choice ([ADR-0015](../decisions/0015-panel-mantine-and-toolchain.md)); complete set for dashboards; one design system for everything |
| Tables | `mantine-datatable` | 9.4 | Built for Mantine 9 (sorting, pagination, row expansion); `mantine-react-table` is stuck on Mantine 6 |
| Charts | Sparklines: a small SVG component (`MetricSparkline`) in theme colours; meters: Mantine `Progress` | – | `@mantine/charts` (Recharts) was tried and dropped on 2026-10-28: it added 85 KB gzip and put the dashboard at 396 KB, over the 350 KB budget (rule 9); without it 315 KB. It comes back if a real chart (axes, tooltips) is needed, loaded only on that page. |
| Framework | React, without the React Compiler | 19.3 | As in the template, which does not enable the compiler; its Vite 8 integration was still experimental when checked (2026-10-28). Revisit when it is stable. |
| Language | TypeScript, strict: `tsc` 7 for type checking (`@typescript/native` = `npm:typescript@7`), and the TypeScript team's compatibility package under the name `typescript` (`npm:@typescript/typescript6`) for tools that call the compiler API | 7.0 / 6.0 | 7 is the template's version. `@hey-api/openapi-ts` still calls the compiler API that 7 no longer ships (`ts.SyntaxKind` undefined, tried 2026-10-28); the alias is how TanStack Router's own examples handle it. Probe: type check, code generation and build pass. Drop the alias when the generator supports 7. |
| Toolchain | Vite+ (`vite-plus`: `vp dev`, `build`, `test`, `lint`, `fmt`, `check`) with Vite 8 core, Vitest 5, oxlint (type-aware) and oxfmt configured by `oxc-config-mantine`; stylelint for CSS modules; `@vitejs/plugin-react` | 1.4 / 8 / 5.0; 1.0; 17; 6.1 | The setup of Mantine's official Vite template (`mantinedev/vite-template`, read 2026-10-28); MIT. A probe build (React, Mantine, TanStack Router plugin) passed and weighed about 118 KB gzip. |
| Routing | TanStack Router, file-based, auto code-splitting | 1.170 | Type-safe paths and search params (filters, tabs); loaders prefetch queries |
| Server state | TanStack Query | 5.105 | Caching, retries, optimistic updates; realtime events write into its cache |
| API client | `@hey-api/openapi-ts` → types, fetch SDK and TanStack Query options from Core's OpenAPI document | 0.99 | One generated source of truth; `openapi-typescript` does not support TypeScript 6 yet |
| Realtime | `@microsoft/signalr` | 10.0 | Matches Core's ASP.NET Core 10 SignalR |
| Forms and validation | Mantine form `schemaResolver` + zod | 9.7 / 4.6 | Built-in Standard Schema support, no extra resolver package |
| Console | `@tanstack/react-virtual` | 3.14 | Virtualised lines; a terminal emulator is not needed for server consoles |
| Icons, fonts | `@tabler/icons-react`; `@fontsource-variable/inter`, `@fontsource-variable/jetbrains-mono` | 3.50; 5.3 | Self-hosted |
| Dates | dayjs (Mantine dates) | 1.11 | Relative times ("5 min ago"), VM local time |
| Tests | Vitest (through Vite+) + Testing Library + MSW (mock API), with the template's `test-utils` render wrapper; Playwright for a few end-to-end smoke tests | 5.0 / 16.3 / 3.0; 1.64 | |
| Runtime | Node.js LTS + npm (the template uses Yarn 4; npm keeps one tool less, `overrides` replace Yarn's `resolutions`), dev machine and CI only | 24 / 11 | No Node on the VM ([ADR-0010](../decisions/0010-panel-react-typescript.md)) |

Not used: Redux / Zustand (TanStack Query, the router and Mantine hooks cover state), Tailwind or a second component kit, CSS-in-JS runtimes, ESLint and Prettier (replaced by Vite+'s oxlint and oxfmt, as in the template), Storybook (in the template; the review gates use the running panel and screenshots instead).

## 8. Project structure

```
panel/
  package.json  vite.config.mjs  tsconfig.json  postcss.config.cjs  .stylelintrc.json  openapi-ts.config.ts
                               (vite.config is .mjs as in the template, outside the type-checked sources)
  openapi/gamewarden-api.json  Core's OpenAPI document, written by Core's build
  public/                      favicon
  src/
    main.tsx                   global styles, i18n, the mock API in `--mode mock`, then <App />
    theme.ts                   Mantine theme (colours, fonts, component defaults, status colours)
    app/                       App.tsx (providers: Mantine, dates, Query, modals, notifications, Router),
                               router.ts (QueryClient, router with its context)
    api/
      generated/               @hey-api output (committed; CI fails if stale)
      client-config.ts         base URL and credentials (CSRF header in phase 2)
      queries.ts               query options shared by loaders and components
      problems.ts              ProblemDetails → a message to show
      live.ts                  polling intervals until SignalR (phase 3: realtime.ts)
    layout/                    AppShell: header, navbar, spotlight
    routes/                    file-based routes (TanStack Router)
    features/                  servers/ (wizard/)  console/  jobs/  system/ (later: activity/  setup/  auth/)
    components/                our building blocks: StatusBadge, PageHeader, ResourceMeter,
                               JobProgress, EmptyState, ProblemAlert, router links for Mantine
    i18n/                      en.ts (source), vi.ts (typed: exactly en's keys)
    mocks/                     MSW handlers and data for `npm run dev:mock` (dev only)
  test-utils/                  render() with MantineProvider and i18n (from the template)
  tests/e2e/                   Playwright smoke tests (phase 5)
```

## 9. Data flow

```
Core (ASP.NET Core 10)
  minimal APIs with typed results ──► OpenAPI document (build time) ──► panel/src/api/generated
  SignalR hub /hubs/events ─────────► realtime.ts ──► queryClient.setQueryData / invalidate
Panel
  route loader → queryClient.query({ ...options, staleTime: 'static' }) → component useQuery(options) → Mantine UI
                 (warms the cache once; `ensureQueryData` is deprecated in Query 5.105)
  mutation → generated SDK (cookie + X-GW-CSRF) → toast + cache update
```

## 10. Working rules

[`panel/CONTRIBUTING.md`](../panel/CONTRIBUTING.md) has the commands and the patterns that apply them; the repository [`CLAUDE.md`](../CLAUDE.md) points coding agents to both. Tools enforce what they can: `npm run typecheck`, `lint` (oxlint, stylelint), `format:check` and `test`, which CI runs.

1. **Mantine first.** Use a Mantine component, hook or pattern ([ui.mantine.dev](https://ui.mantine.dev)) before writing our own. Style with theme tokens, Mantine style props and CSS modules (`postcss-preset-mantine`); no inline colours or pixel values outside the theme, no second UI kit.
2. **Our building blocks are few and named.** Repeated UI becomes a component in `components/` (StatusBadge, PageHeader, …); one place decides how a state looks.
3. **All server access goes through the generated client and TanStack Query.** No `fetch` in components; query keys come from the generated options; realtime handlers only touch the cache.
4. **Types are strict.** `strict`, no `any`, no `@ts-ignore`; API types are generated, never hand-written; CI fails when the generated client is stale.
5. **Feature folders, small files.** A route file only composes; components stay under ~200 lines; tests sit next to the code.
6. **The UX rules of §6 are review criteria.** Pending state, result, reason for disabled, confirmation for destructive actions, phone width, light and dark.
7. **No secret leaves a form.** Secret inputs are write-only; nothing secret goes into the query cache, logs or local storage. Never render HTML from the server; the CSP forbids inline scripts.
8. **Accessible by default.** Every input has a label; status is never colour-only; keyboard reaches every action; focus stays visible.
9. **Budgets.** First load ≤ 350 KB JavaScript (gzip) on the dashboard route; route code is split automatically; CI reports the size.
10. **Pull requests with panel changes include screenshots** at 1280 px and 390 px, light and dark.

## 11. Testing and CI

| Layer | What | Where |
|---|---|---|
| Unit / component | Vitest + Testing Library with MSW mocking the API: StatusBadge mapping, wizard validation, console search, 412 → dialog | CI `panel` job (Ubuntu) |
| Contract | Generated client up to date with Core's OpenAPI document; typecheck; lint; build; bundle size | CI `panel` job |
| End-to-end | `panel/e2e/smoke.mjs`: the built panel on a Release Core with FakeGame — setup → create → start → console → stop → sign out | CI job `e2e` (Windows, no admin) |
| VM | Panel over Radmin from another PC and from a phone; checklist [10 §8](10-build-test-release.md#8-manual-tests-on-the-vm) | By hand |

## 12. Delivery into Core

`npm ci && npm run build` writes `panel/dist`; `build/package.ps1` copies it, without source maps, to `wwwroot` next to Core, which serves it and serves it with an SPA fallback (`/` and client routes → `index.html`, `/api` and `/hubs` excluded), long-lived caching for hashed assets, `no-cache` for `index.html`, and a Content-Security-Policy without inline scripts. `build/package.ps1` builds the panel before publishing Core; no dependency runs an install script (`panel/.npmrc`), because the release job signs after building. `npm run dev` proxies `/api` and `/hubs` to a development Core.

## 13. Core work for M2

| Item | Notes |
|---|---|
| Accounts and sessions | Owner account (Identity `PasswordHasher`), cookie authentication, Data Protection keys in `keys\` protected with DPAPI, CSRF header, login rate limiting ([06 §4](06-security-and-access.md#4-authentication)) |
| Setup | One-time setup token written by Core at first start; `/setup/*` loopback only; `gamewarden setup-url`, `gamewarden owner reset-password` |
| Network | Kestrel on `0.0.0.0:8765` with the local-address filter (OQ-28) and host filtering; firewall rule `GameWarden:panel:8765/tcp` for the VPN ranges |
| API shape | Typed results and DTO records on every endpoint so the OpenAPI document is complete; panel and CLI share it |
| Realtime | SignalR hub `/hubs/events` with the events of [05 §3](05-api.md#3-realtime-signalr) |
| Metrics | CPU and RAM per instance (game process tree) and for the VM, sampled every few seconds, with a short history for sparklines |
| Activity | `events` table: state changes, commands with the user, jobs, updates |
| System settings | `PUT /system/settings` for panel access networks and VPN ranges (written to `gamewarden.json`) |
| Static hosting | `wwwroot`, SPA fallback, cache headers, CSP |

Phase 2 (2026-10-28) delivered accounts and sessions, setup, the network filter and the panel firewall rule, the API shape and static hosting, with the panel's sign-in, setup and account menu. The setup page has the Owner step only; its prerequisites and panel-access steps come with the System work of phase 4. Realtime, metrics, activity and system settings follow in phases 3–4.

Phase 3, first part (2026-10-28): Core's event bus feeds the SignalR hub and the `events` table; the panel keeps its query cache current from the hub (state, jobs, console, activity), shows the link's state in the header with a red banner when it is lost, and polls only every 30 s as a safety net (2 s without the link, e.g. on the mock API). Metrics (CPU / RAM with sparklines) are the second part.

Phase 3, second part (2026-10-28): Core samples CPU and RAM of the VM and of each running game's process tree every 5 s (ten minutes kept) and pushes each sample over the hub. The dashboard has five overview cards (servers, CPU and memory with sparklines, disk, jobs), the server list rows show each running server's CPU and RAM, and the server overview has a *Resources* card. Dashboard first load: 315 KB gzip.

Phase 4 (2026-10-28): the setup page is the 3-step wizard of §4 (Owner account → prerequisites and DNS → panel access, the last two optional); System changes the panel access networks and VPN ranges (`PUT /system/settings`, applied at once, refusing a change that would lock the browser out), switches back to the previous version (optionally restoring the database) and points to adapter problems; the header shows an available update; Activity filters by type as well as by server. Creating servers and the Adapters page were wired in phase 1–2.

Phase 5 (2026-10-28): toasts for finished jobs, crashes and Core restarts or updates (from the realtime events, once per transition; bottom right, where they cover no controls); the favicon's colour and the `(N⚠)` title prefix of docs/07 §8; *GameWarden restarted after a failure* among the dashboard's attention items (a marker file a clean stop removes); start, stop and restart in the command palette; an accessibility pass with axe-core on every page in both schemes, now without violations (contrast tokens in `cssVariablesResolver`, named progress bars and close buttons, zoom allowed); the end-to-end smoke test in CI. `v0.2.0-rc.1` passed the Owner's VM test on 2026-10-28 (10 §8.2).

Owner review after M2 (2026-10-28): the new server wizard and the Settings tab use sections with a sticky footer (D10), and the tab title has no attention prefix (D11). Screenshots in `docs/panel/sections`.

## 14. Plan

1. **Shell and look** — panel project, theme, AppShell, navigation, dashboard and server detail with mock data (MSW). *Review gate: the Owner checks look and layout on desktop and phone (screenshots, or `npm run dev` without Core).*
2. **Core access** — accounts, setup, sessions, CSRF, network filter, panel firewall rule, static hosting, OpenAPI typed results.
3. **Live data** — generated client, SignalR, metrics, activity; dashboard, server detail, console, jobs, settings wired to Core.
4. **Create and maintain** — new server wizard, System (updates, prerequisites, network), adapters.
5. **Polish and ship** — palette, notifications, favicon and title, error and empty states, accessibility pass, tests, docs → release candidate → VM test over Radmin and from a phone.

## 15. Decisions (made 2026-10-28)

| # | Question | Decision |
|---|---|---|
| D1 | Interface language | English and Vietnamese from the start (i18next; follows the browser, switch in the account menu) |
| D2 | Primary colour | Violet (§5) |
| D3 | Router | TanStack Router instead of React Router (amends ADR-0010, [ADR-0015](../decisions/0015-panel-mantine-and-toolchain.md)) |
| D4 | Activity feed in M2 | Yes (`events` table) |
| D5 | Prerequisite installs from the panel | Links only in M2; installer jobs later (they run downloaded installers as SYSTEM) |
| D6 | Per-server CPU / RAM in M2 | Yes |
| D7 | Dashboard (Owner review of phase 1) | Monitoring only: overview numbers first, then summaries of servers, problems and activity that link to their screens; no server actions on it |
| D8 | Where *New server* lives (Owner review) | A button on the Servers screen opens the wizard in a wide drawer; no sidebar entry |
| D9 | Wizard steps (Owner review) | Vertical stepper beside the step in the drawer; a compact row on phones. Replaced by D10 |
| D10 | Wizard layout (Owner review after M2) | Sections of one scrolling form instead of D9's stepper, which took a column of the drawer: completed sections sum up in one line, Back and Next stay in a sticky footer, so many sections or a long one scroll naturally. The same `FormSections` serves the Settings tab (all open). Built on `Accordion` (WAI-ARIA accordion pattern): Mantine's `Stepper` renders the step content after the whole list of steps, never under each step. Pattern: Material's vertical stepper (content under each step, Continue inside), summaries of completed steps ([Baymard #648](https://baymard.com/guidelines/648-collapsing-checkout-accordion-steps)), Back disabled on the first step and Next becoming Create on the last ([Carbon create flows](https://carbondesignsystem.com/community/patterns/create-flows)), one page with sections instead of a paged wizard ([EC2 launch, 2022](https://aws.amazon.com/about-aws/whats-new/2022/04/amazon-ec2-launch-experience-console/)) |
| D11 | Tab title (Owner review after M2) | No `(N⚠)` title prefix: a problem that waits for someone stays on the Dashboard, since a tab cannot dismiss it; the favicon shows only what is happening now ([07 §8](07-ux-and-user-flows.md#8-is-it-running--visibility-without-a-tray-app)) |
