# ADR-0033 · The Operations Kernel between surfaces and domain services

**Status: accepted 2026-11-05**

## Context

The first Operations Agent shipped as a Discord bot with a typed tool registry and durable approvals
([ADR-0030](0030-operations-agent-in-core.md), [ADR-0031](0031-discord-gateway-bot.md)). It works, but the bot is
also the place where conversation state, approval execution, incident context and tool errors meet. Notifications
are a separate webhook pipeline ([ADR-0032](0032-notifications-through-a-provider-neutral-outbox.md)). Runbooks and
incident history are not first-class, and a write is executed without a verification turn.

The next step is not another bot feature. It is a kernel that every surface can use: Discord today, the panel, the
CLI, the Agent API and MCP later.

## Decision

- Introduce an **Operations Kernel** inside Core, described in [docs/21](../docs/21-operations-kernel.md).
- The kernel owns five contracts:
  - **surfaces** turn a transport event into a turn and render a reply;
  - **capabilities** are typed queries/commands with risk, scope, timeout, redaction and verification;
  - **policy** is data and returns allow/deny/approval with a rule id and an impact summary;
  - **approvals** are durable proposals that execute once, verify and resume the conversation;
  - **incidents and runbooks** are first-class objects, not a chat convention.
- The existing `AgentTools` registry becomes the capability registry. The existing SQLite conversations, runs, tool
  calls and approvals stay; they gain trace, policy, execution and incident links.
- Notifications keep the outbox. `discord-webhook` stays; `discord-bot` is another channel of the same outbox,
  routed by instance to the mapped server channel. The bot is not a second notification pipeline.
- Every turn, tool call, approval, incident and notification carries a trace id. Events are stored in
  `agent_events`; prompt/response text is not logged by default.
- Runbooks are declarative YAML using capabilities. The first built-in one is read-only crash triage. Write steps and
  automatic resume are a later phase of the same contract, not a second engine.
- Adapters remain the source of truth for a game. The Agent derives capabilities from `adapter.json` and
  `IGameIntegrationProvider`; adding a game must not require an Agent code change.

## Consequences

- Core owns the Agent's availability and durability; a restart resumes or safely marks an in-flight approval unknown.
- A surface cannot bypass policy or approval; the kernel enforces both.
- A new game adds an adapter (and optionally a provider) plus runbook YAML, not a new branch in the runtime.
- Debugging starts from one trace and one incident timeline, not from a Discord channel and a set of log files.
- The first implementation of runbooks is intentionally smaller than the target: read-only steps and incident
  buttons for writes. The contract leaves room for checkpointed write steps.
- Adding a new model provider or a new surface is an adapter around the kernel, not a change to the domain services.

## Alternatives considered

- **Keep growing the Discord bot** — every surface would duplicate conversation, policy and approval logic; rejected.
- **Adopt a Python/JS agent framework inside Core** — a second runtime and deployment, and a poor fit for the
  existing .NET durability and tests; borrow the patterns instead.
- **A separate workflow service (Temporal et al.)** — too much for one VM and one Core; SQLite state machines are
  enough now and the contracts can move later.
- **An external MCP client as the only agent** — the bot must work when no external host is online; rejected.
