# Working on the panel

The web panel (M2) is a React SPA built with Mantine. Its design — functions, layout, style, stack — is [docs/11-web-panel.md](../docs/11-web-panel.md); its **working rules** are [§10](../docs/11-web-panel.md#10-working-rules) there and are the review criteria for every pull request. This page has the commands and the patterns that apply those rules.

## Setup

Node.js 24 and npm 11. The .NET SDK ([global.json](../global.json)) is needed only to regenerate the API document.

```powershell
cd panel
npm ci
npm run dev:mock      # http://localhost:5173 with the mock API (MSW); no Core needed
npm run dev           # proxies /api, /healthz and /hubs to a development Core (GAMEWARDEN_URL, default http://127.0.0.1:8765)
```

## Signing in

- **Mock API:** signed in as `owner` by default. To see the sign-in or setup page, set `localStorage['gamewarden.mock.auth']` to `signed-out` or `setup` and reload; the mock password is `correct horse battery`.
- **Development Core:** start it (README, *Development*), get the setup link with the CLI (`dotnet run --project src/GameWarden.Cli -- setup-url`, with `GAMEWARDEN_CLI_TOKEN` set as in the README), and open it with port 5173 instead of 8765, so it goes through Vite.

## Commands

| Command | What |
|---|---|
| `npm run typecheck` | `tsc` (TypeScript 7), strict |
| `npm run lint` | oxlint (type-aware, `oxc-config-mantine`) and stylelint for CSS modules |
| `npm run format` / `format:check` | oxfmt |
| `npm test` | Vitest + Testing Library (jsdom) |
| `npm run build` | type check, then the production build into `dist/` |
| `npm run gen:api` | regenerate `src/api/generated` from `openapi/gamewarden-api.json` |

CI runs all of them, and fails when the generated client or the OpenAPI document is stale.

## When Core's API changes

1. Build Core (`dotnet build GameWarden.slnx`; `dotnet` must be on `PATH`). The build writes `panel/openapi/gamewarden-api.json`.
2. `npm run gen:api`.
3. Commit the document and `src/api/generated` with the change. Never edit generated files.
4. If the panel uses the new endpoint, add a handler in `src/mocks/handlers.ts` so `dev:mock` keeps working.

## Patterns

We follow the published pattern of each library and change it only with a reason written in docs/11. Sources: Mantine's [vite-template](https://github.com/mantinedev/vite-template), TanStack Router's [React Query file-based example](https://tanstack.com/router/latest/docs/framework/react/examples/basic-react-query-file-based), the TanStack [custom link guide](https://tanstack.com/router/latest/docs/framework/react/guide/custom-link).

| Need | Do | Not |
|---|---|---|
| A page | A file in `src/routes/` that composes; parts live in `src/features/<area>/` | Logic or long JSX in the route file |
| Data for a page | Options from `src/api/queries.ts`; the loader warms the cache with `queryClient.query({ ...queries.x(), staleTime: 'static' })`, the component reads it with `useQuery(queries.x())` | `fetch`; `ensureQueryData` (deprecated) |
| A server command | `useServerCommands()` (notifications, the 412 "start anyway" dialog, cache refresh) | A new mutation per button |
| An error to show | `toProblem(error, fallback)` → notification or `<ProblemAlert>` | Showing raw error objects |
| A link | `AnchorLink`, `ButtonLink`, `ActionIconLink`, `NavLinkLink` from `@/components/links` (typed paths and params) | `component={Link}` on Mantine components |
| A form | `useForm` with `schemaResolver(zodSchema, { sync: true })`; secrets in `PasswordInput`, never pre-filled | Hand-written validation; secrets in the query cache or local storage |
| How a state looks | `StatusBadge` / `stateLook` and `statusColors` in the theme | Colours chosen per screen |
| Text | `t('area.key')`: add the key to `src/i18n/en.ts`, then `vi.ts` (TypeScript and `i18n.test.ts` check both have the same keys and placeholders) | Literal strings in JSX |
| Styles | Mantine props and theme tokens; a `*.module.css` next to the component for the rest | Inline colours, pixel values outside the theme, a second UI kit |
| Live values | `refetchInterval` from `src/api/live.ts` until SignalR (M2 phase 3) | Timers in components |
| A test | `*.test.ts(x)` next to the code, `render` from `@test-utils` | A separate test folder |

## Pull requests

- Screenshots at 1280 px and 390 px, light and dark (rule 10); `npm run dev:mock` gives the data.
- Check the [interaction rules](../docs/11-web-panel.md#6-interaction-rules): pending state, result, reason for a disabled action, confirmation for destructive ones.
- Keep components under ~200 lines (rule 5); split into `features/` when a file grows.
