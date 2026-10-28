# ADR-0015: Panel built on Mantine 9, with TanStack Router and a generated API client

- **Status:** Accepted (the Owner approved it with [11-web-panel](../docs/11-web-panel.md) on 2026-10-28)
- **Date:** 2026-10-28

## Context

- [ADR-0010](0010-panel-react-typescript.md) chose React + TypeScript + Vite and left the component library (Mantine or shadcn/ui) to the start of M2. It also named React Router and `openapi-typescript`.
- The Owner prefers Mantine and its ecosystem, so that existing patterns are reused rather than rebuilt (2026-10-28).
- npm, checked 2026-10-28:
  - Mantine is at 9.7.4 for every `@mantine/*` package, needs React ≥ 19.2, and has a built-in Standard Schema `schemaResolver` (zod 4).
  - `mantine-datatable` 9.4.0 targets Mantine ≥ 9. `mantine-react-table` was last published in February 2025 (1.3.4, Mantine 6).
  - TypeScript 7.0.2 is released. `typescript-eslint` 8.72.1 accepts `typescript < 6.1.0`; `openapi-typescript` 7.13.0 declares `typescript ^5.x`; `@hey-api/openapi-ts` 0.99.0 accepts TypeScript 6 but fails on 7 (`ts.SyntaxKind` undefined, tried 2026-10-28).
  - Mantine's official Vite template (9.7.3, read 2026-10-28) uses Vite+ with oxlint and oxfmt (`oxc-config-mantine`), stylelint, Vitest 5, TypeScript 7 and Yarn 4.
- The panel is served over plain HTTP inside a VPN, possibly without internet on the VM ([ADR-0005](0005-http-only-over-vpn.md)).

## Decision

1. **Mantine 9** is the only UI kit. We use `core`, `hooks`, `form`, `notifications`, `modals`, `spotlight`, `dates`, `charts` and `nprogress`, plus `mantine-datatable` for tables and Tabler icons. Styling uses theme tokens, style props and CSS modules with `postcss-preset-mantine`; there is no Tailwind and no second kit.
2. **TanStack Router** (file-based, automatic code splitting) replaces React Router from ADR-0010, for type-safe paths and search params. **TanStack Query** holds server state, as ADR-0010 decided.
3. **`@hey-api/openapi-ts`** replaces `openapi-typescript`. It generates types, a fetch SDK and TanStack Query options from Core's OpenAPI document. Core's endpoints use typed results so the document is complete; the generated code is committed, and CI fails when it is stale.
4. **The toolchain of Mantine's official Vite template** (`mantinedev/vite-template`, read 2026-10-28): Vite+ (`vite-plus` 1.4, MIT) for dev, build, test, lint and format, with oxlint (type-aware) and oxfmt configured by `oxc-config-mantine`, and stylelint for CSS modules.
5. **TypeScript 7** (strict) for type checking, as in the template. The API generator calls the compiler API that TypeScript 7 no longer ships, so the name `typescript` points to the TypeScript team's compatibility package `@typescript/typescript6` and TypeScript 7 is installed as `@typescript/native` — the arrangement TanStack Router's examples use (tried 2026-10-28: type check, generation and build pass).
6. Fonts are self-hosted (`@fontsource-variable/inter`, `@fontsource-variable/jetbrains-mono`); the panel loads nothing from third-party hosts.
7. npm with a committed lockfile (the template uses Yarn 4); Node.js LTS on the dev machine and in CI only.

Details, versions, layout, style and working rules: [11-web-panel](../docs/11-web-panel.md).

## Consequences

- Dashboards, forms, modals, notifications, a command palette, charts and dates come from one design system; the panel code is mostly composition.
- Mantine's bundle is larger than a hand-picked set of primitives; route-level code splitting and a 350 KB (gzip) budget keep the first load in check.
- Pre-1.0 `@hey-api/openapi-ts` can change its output between versions; the exact version is pinned, and generated code is reviewed in pull requests.
- Two TypeScript packages sit side by side until the API generator supports TypeScript 7; then the alias goes.

## Alternatives considered

- **shadcn/ui + Tailwind** — copy-in components and full control, but every pattern is ours to maintain, and a second styling system sits next to C#. The Owner prefers Mantine.
- **`mantine-react-table`** — powerful, but not released for Mantine 9.
- **React Router 8** — fine as a router, but its search params are untyped and it needs more glue with TanStack Query.
- **`openapi-typescript` + `openapi-fetch`** — small and stable, but it does not support TypeScript 6 yet and needs hand-written query hooks.
- **TypeScript 6 only** — works, but leaves the template's version for no gain once the compatibility package covers the generator.
- **ESLint + typescript-eslint + Prettier** — the long-standing setup, but the template has moved to oxlint and oxfmt, which also lint with type information without typescript-eslint.
