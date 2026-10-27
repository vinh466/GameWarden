# ADR-0001: Build our own platform instead of adopting an existing manager

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

- Servers have been hosted by hand (Valheim, Terraria, Palworld). The goal is a repeatable, organized setup on a Windows VM.
- WindowsGSM was tried on 2026-10-27: servers did not start. The VM also lacked VC++ and DirectX runtimes, which may have been the cause **[Unverified]**.
- WindowsGSM maintenance is sporadic (last release v1.23.1; latest merged PRs 2025-12-26, previous commit 2024-11-18); plugins are community-maintained with varying quality.
- AMP (CubeCoders) is mature but closed source and paid per instance tier; deep per-game customization is limited to its own template system.
- Pterodactyl / Pelican require Linux + Docker.
- Scope is small: one VM, < 10 players per server, a handful of games, one Owner.

## Decision

Build GameWarden: a small Core + per-game adapter platform for Windows. Use existing tools as **references** (Pelican eggs for launch parameters and config formats, LinuxGSM/WindowsGSM for per-game quirks), not as the runtime.

## Consequences

- Full control of behavior, UX, and per-game support; support grows only for games actually played.
- We own maintenance: game updates that break an adapter are our job.
- Upfront effort before it is better than manual hosting (milestones M0–M2).

## Alternatives considered

- **WindowsGSM** — failed start on the VM, low maintenance, C# plugins of uneven quality.
- **AMP** — closed source, paid tiers, less freedom for custom adapters.
- **Pterodactyl / Pelican** — Linux only (a separate Linux plan exists in `gamehost-infra`).
- **Keep doing it by hand** — always possible as a fallback; no automation, no friend access.
