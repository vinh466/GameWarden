# ADR-0031 · Discord bot over the Gateway, one channel per server

**Status: accepted 2026-11-05**

## Context

The Owner wants Discord to be the only daily interface. The existing Discord integration is a webhook channel
(`docs/18`): it can send notifications and edit a status message, but it cannot receive messages, answer a question,
run a diagnostic or ask for an approval.

The VM is behind a VPN and must not gain a public inbound endpoint.

## Decision

- Use a **Discord bot** with an outbound **Gateway** connection from Core (Discord.Net 3.20.2, `net10.0`).
  No public port, no inbound firewall rule; reconnect/resume is the library's job.
- Intents: `Guilds`, `GuildMessages`, `MessageContent` (the Owner enables Message Content Intent in the portal).
- **One channel per server**, plus a `system` channel. The channel id maps to an instance id in `agent-host.json`.
  The bot answers in the channel that asked, so the context is obvious.
- Slash commands for deterministic actions (`/status`, `/server`, `/logs`, `/diagnose`, `/ask`, `/approve`, `/deny`,
  `/stop`) and mention chat (`@GameWarden …`) for free-form questions.
- Writes are approved with **buttons** in the same channel. Only an allowed user or role may press them; the approval
  expires and is stored in SQLite.
- The bot token is a secret (`agent-host/discord-bot-token`); guild, channel and allowlist ids are settings.
- Webhook notifications stay as they are; the bot is the conversational surface, not a replacement for the outbox.

## Consequences

- The bot only works while Core runs; a Core restart reconnects it.
- Message Content is a privileged intent; the bot is private to one guild and the allowlist is checked before any
  action.
- Discord's 3-second interaction deadline is handled with a deferred response and follow-ups; long tool runs edit the
  deferred reply.
- No public endpoint, no slash-command HTTP receiver, no OAuth: the Gateway carries interactions and components.

## Alternatives considered

- **Webhook only** — cannot receive chat or approvals; rejected.
- **HTTP interactions endpoint** — needs a public URL/TLS or a tunnel through the VPN; rejected for the VM.
- **A separate bot process** — a second deployment and token store; rejected while Core is the single service.
