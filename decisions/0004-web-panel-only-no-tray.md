# ADR-0004: Web panel only, no tray app

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

The Owner and friends need to operate servers from PCs and phones over the VPN. A tray app only helps someone logged in to the VM via RDP.

## Decision

The only UI is a web panel served by Core. "Is it running?" visibility comes from: favicon/title status, header heartbeat and connection-lost banner, Discord notifications with a live status message, the `gamewarden status` CLI, Windows Event Log, and `/healthz`.

## Consequences

- One UI to build and maintain; usable from any device on the VPN.
- No desktop notifications on the VM itself (Discord covers it).

## Alternatives considered

- **Tray app** — duplicated UI, only useful during RDP sessions.
- **Native desktop app (WPF/WinUI)** — usable only on the VM.
