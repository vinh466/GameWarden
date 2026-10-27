# ADR-0010: The panel is a React + TypeScript SPA built with Vite

- **Status:** Accepted (closes OQ-01)
- **Date:** 2026-10-27

## Context

- The panel is static files served by Core; it talks to the REST API and the SignalR hub ([02 §2](../docs/02-architecture.md#2-components)).
- Friends use it from phones over the VPN, so first-load size and behaviour when the phone sends the tab to the background matter ([07 §1](../docs/07-ux-and-user-flows.md#1-principles)).
- The main argument for Blazor was sharing DTOs with Core. ASP.NET Core generates an OpenAPI document (`Microsoft.AspNetCore.OpenApi`) from which TypeScript types can be generated.
- Nothing should need to be installed on the VM for the panel.

## Decision

- React + TypeScript, built with Vite. The build output is static files embedded in the Core publish.
- Libraries: React Router, TanStack Query (REST state; SignalR events invalidate queries), `@microsoft/signalr`, and one component library (Mantine or shadcn/ui, chosen at the start of M2).
- API types are generated from Core's OpenAPI document (e.g. `openapi-typescript`) during the build; CI fails when they are stale.
- No server-side rendering and no Node.js at runtime.

## Consequences

- Small bundles and a fast first load on phones; a large ecosystem for dashboards and forms.
- Two languages (C# and TypeScript). Node.js LTS is needed on the dev machine and in CI, not on the VM.
- The settings form renderer (`settings.schema.json` → form) is written in TypeScript.

## Alternatives considered

- **Blazor WebAssembly** — one language; the .NET runtime adds megabytes to the first load.
- **Blazor Server (interactive server rendering)** — needs a live circuit; phones drop the WebSocket in the background and show reconnect overlays.
- **Next.js** — server rendering needs Node.js on the VM; nothing here needs SSR.
