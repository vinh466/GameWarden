# ADR-0015: Panel built on Mantine 9, with TanStack Router and a generated API client

- **Status:** Accepted (the Owner approved it with [11-web-panel](../docs/11-web-panel.md) on 2026-10-28)
- **Date:** 2026-10-28

## Context

- [ADR-0010](0010-panel-react-typescript.md) chose React + TypeScript + Vite and left the component library (Mantine or shadcn/ui) to the start of M2. It also named React Router and `openapi-typescript`.
- The Owner prefers Mantine and its ecosystem, so that existing patterns are reused rather than rebuilt (2026-10-28).
- npm, checked 2026-10-28:
  - Mantine is at 9.7.4 for every `@mantine/*` package, needs React ≥ 19.2, and has a built-in Standard Schema `schemaResolver` (zod 4).
  - `mantine-datatable` 9.4.0 targets Mantine ≥ 9. `mantine-react-table` was last published in February 2025 (1.3.4, Mantine 6).
  - TypeScript 7.0.2 is released, but `typescript-eslint` 8.72.1 accepts `typescript < 6.1.0`, and `openapi-typescript` 7.13.0 declares `typescript ^5.x`. `@hey-api/openapi-ts` 0.99.0 accepts TypeScript 6.
- The panel is served over plain HTTP inside a VPN, possibly without internet on the VM ([ADR-0005](0005-http-only-over-vpn.md)).

## Decision

1. **Mantine 9** is the only UI kit. We use `core`, `hooks`, `form`, `notifications`, `modals`, `spotlight`, `dates`, `charts` and `nprogress`, plus `mantine-datatable` for tables and Tabler icons. Styling uses theme tokens, style props and CSS modules with `postcss-preset-mantine`; there is no Tailwind and no second kit.
2. **TanStack Router** (file-based, automatic code splitting) replaces React Router from ADR-0010, for type-safe paths and search params. **TanStack Query** holds server state, as ADR-0010 decided.
3. **`@hey-api/openapi-ts`** replaces `openapi-typescript`. It generates types, a fetch SDK and TanStack Query options from Core's OpenAPI document. Core's endpoints use typed results so the document is complete; the generated code is committed, and CI fails when it is stale.
4. **TypeScript 6.0** (strict) until `typescript-eslint` supports 7.
5. Fonts are self-hosted (`@fontsource-variable/inter`, `@fontsource-variable/jetbrains-mono`); the panel loads nothing from third-party hosts.
6. npm with a committed lockfile; Node.js LTS on the dev machine and in CI only.

Details, versions, layout, style and working rules: [11-web-panel](../docs/11-web-panel.md).

## Consequences

- Dashboards, forms, modals, notifications, a command palette, charts and dates come from one design system; the panel code is mostly composition.
- Mantine's bundle is larger than a hand-picked set of primitives; route-level code splitting and a 350 KB (gzip) budget keep the first load in check.
- Pre-1.0 `@hey-api/openapi-ts` can change its output between versions; the exact version is pinned, and generated code is reviewed in pull requests.
- The panel stays on TypeScript 6 for a while after TypeScript 7.

## Alternatives considered

- **shadcn/ui + Tailwind** — copy-in components and full control, but every pattern is ours to maintain, and a second styling system sits next to C#. The Owner prefers Mantine.
- **`mantine-react-table`** — powerful, but not released for Mantine 9.
- **React Router 8** — fine as a router, but its search params are untyped and it needs more glue with TanStack Query.
- **`openapi-typescript` + `openapi-fetch`** — small and stable, but it does not support TypeScript 6 yet and needs hand-written query hooks.
- **TypeScript 7** — faster compiler, but lint tooling does not support it yet.
