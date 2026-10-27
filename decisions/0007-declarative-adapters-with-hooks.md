# ADR-0007: Declarative adapters with optional PowerShell hooks and support levels

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

Every game differs in install source, launch arguments, config format, stop method and mod system. Support should grow game by game, and a game should be usable before it is fully automated.

## Decision

- An adapter is a folder: `adapter.json` (+ `settings.schema.json`, `guide.md`, `hooks/*.ps1`).
- Core implements generic mechanics; adapters declare game specifics. Hooks (Windows PowerShell 5.1) cover what JSON cannot express.
- Support levels **L0** (guide) → **L1** (install/run/stop/backup) → **L2** (settings form) → **L3** (mods), validated at load.
- Settings schema drives the panel form and config file rendering.

## Consequences

- Adding a game is usually JSON only; no Core change, no UI change.
- Hooks are code: only the Owner may add or change adapters.
- The adapter format becomes a contract; it is versioned with `schemaVersion`.

## Alternatives considered

- **C# plugins compiled into Core (WindowsGSM style)** — every game needs C# and a rebuild; plugin bugs can crash Core.
- **Scripts only** — no generated UI, no validation, every game reinvents the same steps.
