# 20 · Operations Agent

**Status: accepted 2026-11-05** — [ADR-0030](../decisions/0030-operations-agent-in-core.md),
[ADR-0031](../decisions/0031-discord-gateway-bot.md). The Agent API and MCP bridge ([15](15-agent-access.md)) stay the
scoped external surface; this document describes the Owner's own Agent inside Core.

## 1. What it is

An Agent that runs inside Core, talks to the Owner in Discord, reads the whole machine and can repair a server when
the Owner approves. It is not a second control plane: every read and write goes through the same services, permissions,
redaction and audit as the panel and the Agent API.

```
Discord (one channel per server) ──► Discord bot (Gateway, outbound)
                                           │
                                    Agent runtime (plan → tools → verify)
                                           │
                                    Tool registry (typed, risk-classified)
                                           │
             Supervisor · Jobs · System log · Metrics · Diagnostics · Files · Update
```

## 2. Decisions

- **One bot, one channel per server.** A channel maps to an instance (`valheim-main`, `palworld`, …) or to `system`.
  The answer, incident notes and approvals stay in that server's channel.
- **Mention and slash commands.** `@GameWarden …` for natural chat, `/ask`, `/status`, `/server`, `/logs`,
  `/diagnose`, `/approve`, `/deny`, `/stop`. Message Content Intent is enabled.
- **Reads are free; writes need approval.** Every write tool creates an approval row and posts Approve/Deny buttons in
  the channel. Only an allowed Discord user or role may decide. The approval expires.
- **Vietnamese by default**, English when the setting says so.
- **No shell, no arbitrary HTTP.** The model chooses from curated tools with JSON schemas. Tool output is data, never
  instructions.
- **Secrets never leave Core.** The model gets redacted text; the bot token and model key live in `ISecretStore`.

## 3. Tool tiers

| Tier | Tools |
|---|---|
| Observe | status, instances, instance, console, settings, jobs, job log, tail logs, query system log, activity, metrics, diagnostics (system/processes/storage/network/events), read file |
| Operate (approval) | start, stop, restart, send console, install, update |
| Verify | wait job, health check, error-free window (next) |
| Diagnose (next) | incident snapshot, doctor, runbooks, firewall rules, ports, last exit, restart history |
| Repair (next) | settings/mods/config writes, restore config revision, restore backup, firewall/ACL repair |

## 4. Autonomy

The default is `approve-writes`:

1. Read tools run immediately.
2. A write tool creates an approval; the channel gets the tool, its arguments and the reason.
3. The Owner presses **Approve** or **Deny**. The approval expires after the configured window.
4. An approved tool runs once; the result is posted in the channel and written to the audit trail.

A later policy layer may allow L3 actions (restart a crashed server, retry a job, clear temp) per instance, per action,
per time window. Until then, no write is automatic.

## 5. Model

OpenAI-compatible chat completions with tool calling:

- `baseUrl`, `model`, `maxTokens`, `temperature`, `maxToolCalls`, `timeoutSeconds`.
- API key in the secret store under `agent-host/model-api-key`.
- `maxToolCalls` bounds one turn; a run stops with a clear message when it is reached.
- Prompt: product rules, current channel/instance, current time, tool list; no secrets.
- The model's answer is capped and chunked for Discord (2,000 characters per message).

## 6. Panel

System → **Operations** is the Owner's setup screen: status badges, enable switch, guild and one **text channel** per server,
allowed users/roles, language, bot token and model key (write-only), model limits and approval limits. **Test model** makes one small completion with the saved key; **Test Discord** sends one real message through the
saved token and the `system` channel, using REST so it works before the Gateway is enabled. Both use the saved
configuration (press Save first) and show a checklist with the result and latency. Model requests can carry custom
headers; `%apiKey%` in a value is replaced with the model key. The CLI `gamewarden agent-host` does the same for a
headless VM.

## 7. Data

`agent_conversations`, `agent_messages`, `agent_runs`, `agent_tool_calls`, `agent_approvals`:

- conversations are keyed by Discord channel (and later thread);
- messages keep the chat history (redacted) for context;
- runs and tool calls are the audit of what the model did;
- approvals are the durable record of who approved what, when.

## 8. Security

- Bot token and model key in `agent-host` secrets; never in JSON, logs or prompts.
- Discord allowlist: guild, channel mapping, user IDs or role IDs; empty allowlist falls back to the guild owner.
- Tool output is redacted and treated as untrusted data.
- Writes are one-shot approvals; no re-use of an approval id.
- Kill switch: `/stop` or the `Enabled=false` setting stops the bot and the runtime.
- The bot connects outbound over the Gateway: no public port, no inbound firewall rule.
- Every model turn and tool call is logged with a request id; secrets are redacted before storage.

## 9. Discord setup (Owner)

1. Create the application and bot in the Discord Developer Portal; enable **Message Content Intent**.
2. Invite it to the server with `bot` and `applications.commands` scopes.
3. In each server channel, copy the channel id (Developer Mode → Copy Channel ID).
4. Give GameWarden the bot token and ids:
   `gamewarden agent-host secret discord-bot-token`
   `gamewarden agent-host set --guild <id> --channel valheim-main=<id> --channel system=<id>`
   `gamewarden agent-host secret model-api-key`
   `gamewarden agent-host set --model <id> --base-url <url> --enabled true`
5. `gamewarden agent-host status` shows whether the bot, model and channels are ready.

## 10. Phases

- **Phase 1 (this change):** settings, model client, read tools, write tools with approvals, Discord bot, chat history,
  audit.
- **Phase 2:** incident snapshot, doctor, runbooks, firewall/ports, last exit/restart history, log query parity,
  MCP tools for the same registry.
- **Phase 3:** repair tools (settings/mods/config, firewall/ACL), policy engine, L3 actions, verification and rollback.
- **Phase 4:** incident memory/FTS, postmortems, budgets, evaluation suite, optional local model fallback.
