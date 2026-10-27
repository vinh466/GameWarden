# ADR-0006: Friends get accounts with scoped roles; Owner is separate from Admin

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

Friends should be able to operate servers (some as admins, some with limited rights). Some capabilities (adapters with hooks, system settings, prerequisite installs) amount to running code on the VM.

## Decision

- Permission-based authorization; roles are named permission sets: **Admin**, **Operator**, **Player**.
- Role bindings are scoped `global` or per instance.
- **Owner** is a separate flag with system, user and adapter management; Admins manage game servers only.
- All state-changing actions are audited.

## Consequences

- Friends can help at 1 a.m. without being able to break the VM.
- Admins can still run code inside the game process through mods; this is why games should run as a non-admin user (OQ-02).
- More UI work: role-aware screens, invites, bindings editor.

## Alternatives considered

- **Single shared admin account** — no accountability, all-or-nothing.
- **Fixed roles without scopes** — can't give a friend control of only their own server.
