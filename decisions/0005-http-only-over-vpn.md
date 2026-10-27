# ADR-0005: Panel uses HTTP only, reachable only over VPN

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

Friends connect over Tailscale or Radmin VPN, both of which encrypt traffic (Radmin: **[Unverified]**). HTTPS on a VPN address needs either Tailscale-specific tooling (`tailscale serve`) or self-managed certificates, which would not work for Radmin users.

## Decision

Serve the panel over plain HTTP, bound only to loopback and VPN interface addresses by default. Rely on the VPN for transport encryption.

## Consequences

- Works the same for Tailscale and Radmin users; no certificate management.
- Cookies cannot be `Secure`; mitigated with `HttpOnly`, `SameSite=Strict`, CSRF tokens, VPN-only binding ([06 §3](../docs/06-security-and-access.md#3-http-only-decision-and-mitigations)).
- No PWA install, no web push; browsers show "Not secure".
- Binding to LAN/public addresses is possible only by explicit Owner choice, with a warning.

## Alternatives considered

- **HTTP default + optional HTTPS via `tailscale serve`** — more capability, more configuration paths; can be added later by superseding this ADR.
- **Tailscale-only + HTTPS** — drops Radmin users.
