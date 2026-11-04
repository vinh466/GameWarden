# ADR-0030 · The Owner's Operations Agent runs inside Core

**Status: accepted 2026-11-05**

## Context

The Agent API and the MCP bridge ([15](../docs/15-agent-access.md)) are a scoped external surface: a client holds a
`gw_agent_` token, a permission set and time-boxed diagnostics/admin windows. The Owner now wants an Agent that runs
GameWarden itself: it reads the machine, diagnoses a server and can repair it after an approval, with Discord as the
only day-to-day interface.

That Agent cannot be just another MCP client: it must work when no external Agent host is online, it must share the
same services (Supervisor, jobs, system log, diagnostics, files) and it must create approvals and an audit trail of
its own.

## Decision

- The Operations Agent is a hosted service inside Core, not a separate process and not an external MCP client.
- Its tools are a **shared registry** of typed handlers with a JSON schema, a risk class (read/write) and permission
  metadata. The Discord runtime uses the registry directly; the MCP bridge can expose the same registry later.
- The model is an OpenAI-compatible chat-completions endpoint with tool calling. The endpoint, model and limits are
  settings; the API key is a secret (`agent-host/model-api-key`).
- The Agent never gets a shell or arbitrary HTTP. A tool call is validated against the registry; writes become
  approvals.
- Conversations, runs, tool calls and approvals are stored in SQLite; the system log and audit trail see every run.
- The Agent API (`/api/v1/agent`) stays the scoped external surface and is not weakened or reused for the Owner's bot.

## Consequences

- Core owns the Agent's availability, model cost and safety; a Core restart also restarts the bot.
- The registry is the one place to add a capability, test it and classify it.
- The model sees redacted tool output only; the secret store is never a tool.
- Write tools must be added with an approval path and a verification path, not only a success message.
- A future policy engine can allow L3 actions without changing the tool contracts.

## Alternatives considered

- **A separate `gamewarden-agent` process** like the MCP bridge: extra deployment, token management and a second
  failure domain; rejected because the Owner wants the Agent always available with Core.
- **An external MCP client controlled by a cloud model**: useful later, but it cannot hold the bot token or approve
  writes locally; the in-Core runtime is the safer default.
