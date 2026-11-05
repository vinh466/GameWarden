# 21 · Operations Kernel

**Status: accepted 2026-11-05** — the next layer above [ADR-0030](../decisions/0030-operations-agent-in-core.md),
[ADR-0031](../decisions/0031-discord-gateway-bot.md) and
[ADR-0032](../decisions/0032-notifications-through-a-provider-neutral-outbox.md) and
[ADR-0033](../decisions/0033-operations-kernel.md). The current bot is the first surface;
this document defines the kernel it talks to, the contracts between the layers, and how to extend it without touching
the orchestrator.

The Owner's day-to-day interface stays Discord. The panel stays the place for configuration, incident history and
traces. The Agent API and MCP bridge stay scoped external surfaces. All of them use the same capabilities and policy;
none of them owns a private path to the Supervisor or the game.

## 1. Goals and non-goals

Goals:

- one **Operations Kernel** inside Core, independent of the chat surface;
- typed **capabilities** with risk, scope, timeout, verification and redaction;
- **policy as data** for who may do what, where and when;
- durable **approvals** that survive a Core restart and resume the conversation after execution;
- first-class **incidents** with a timeline, an owner and a closure;
- declarative **runbooks** for repeatable diagnosis and repair;
- end-to-end **traces** for a Discord message, model call, tool call, approval, job and notification;
- adding a game is an adapter (and optionally a provider) plus a runbook pack, not an Agent change;
- the same kernel can later be exposed through MCP or the panel without rewriting it.

Non-goals for v1:

- multiple Agent processes or multi-agent delegation;
- a general workflow engine outside Core;
- arbitrary shell, HTTP or PowerShell from the model;
- vector search or a separate knowledge service;
- cross-guild or public Discord bots;
- autonomous writes without human approval (the policy engine can add that later, deliberately).

## 2. Layers

Dependencies point down only. A lower layer never calls a surface.

```text
┌────────────────────────────── Surfaces ──────────────────────────────┐
│ Discord gateway · panel chat · CLI · Agent API · MCP bridge           │
│ IAgentSurface: send, update, ask approval, create thread, notify     │
└──────────────────────────────────┬────────────────────────────────────┘
                                   ▼
┌────────────────────── Conversation and incidents ────────────────────┐
│ session · conversation · incident · incident timeline · context pack  │
│ AgentContextBuilder: channel, instance state, jobs, approvals, time  │
└──────────────────────────────────┬────────────────────────────────────┘
                                   ▼
┌────────────────────────── Agent orchestrator ────────────────────────┐
│ turn state machine · model gateway · prompt catalog · memory         │
│ AgentRuntime: plan → tool → observe → answer; resume after approval  │
└──────────────────────────────────┬────────────────────────────────────┘
                                   ▼
┌──────────────────────── Policy and approvals ────────────────────────┐
│ risk · scope · rule · rate limit · approval · execution · verify     │
│ AgentPolicyService · AgentApprovalService · AgentApprovalCoordinator │
└──────────────────────────────────┬────────────────────────────────────┘
                                   ▼
┌──────────────────────── Capability registry ─────────────────────────┐
│ query/command · JSON schema · timeout · idempotency · verify         │
│ AgentTools: the one registry shared by Discord, panel and MCP        │
└──────────────────────────────────┬────────────────────────────────────┘
                                   ▼
┌────────────────────────── Domain services ───────────────────────────┐
│ Supervisor · Jobs · Mods · Backups · Updates · Diagnostics · Files   │
│ Notifications · Activity · System log · Metrics                      │
└──────────────────────────────────┬────────────────────────────────────┘
                                   ▼
┌──────────────────── Adapters and game providers ────────────────────┐
│ adapter.json · IGameIntegrationProvider · runbook packs              │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.1 Surface layer

A surface turns a transport event into an `AgentTurnRequest` and renders an `AgentReply`.

```csharp
public interface IAgentSurface
{
    string Id { get; }                    // discord, panel, cli, api
    Task<SurfaceRef> SendAsync(SurfaceContext context, AgentMessage message, CancellationToken ct);
    Task UpdateAsync(SurfaceRef message, AgentMessage message, CancellationToken ct);
    Task<ApprovalRef> AskApprovalAsync(SurfaceContext context, AgentApprovalPrompt prompt, CancellationToken ct);
    Task<SurfaceRef?> CreateThreadAsync(SurfaceContext context, string name, CancellationToken ct);
}
```

Rules:

- The surface owns wire details: Discord embeds, buttons, chunking, threads, rate limits, localization and the
  3-second interaction deadline.
- The kernel never sees a Discord id except as an opaque `SurfaceRef`.
- A surface may be unavailable; the incident and notification path must still work. The webhook channel remains the
  fallback when the bot is off.

### 2.2 Conversation and incident layer

- A **conversation** is one channel or thread. The id maps to the mapped instance.
- An **incident** is a first-class object. It can be opened by a notification event, a user report or a runbook.
- A **context pack** is built before every model call: channel binding, instance snapshot, open jobs, pending
  approvals, active incident, recent activity, local time and the user's permissions.
- The context pack is a bounded, redacted summary. It is not a dump of logs or the whole database.

### 2.3 Orchestrator

The orchestrator is a durable state machine, not an in-memory loop.

```text
Received
  → ContextBuilt
  → ModelRequested
  → ToolProposed
      → (query)  ToolExecuted → ModelRequested
      → (command) PolicyEvaluated
                    → deny          → Completed
                    → requireApproval → AwaitingApproval
                                          → approved → ToolExecuted → Verifying → ModelRequested
                                          → denied/expired → Completed
  → Completed | Failed | Cancelled
```

Every transition is persisted. A Core restart resumes `AwaitingApproval` and `ToolExecuted` from the database. A
command that was already sent but not recorded is marked `unknown`, never silently retried unless it is idempotent.

### 2.4 Model gateway

```csharp
public interface IModelGateway
{
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken ct);
}
```

`ModelRequest` carries a task (`chat`, `plan`, `summarize`, `classify`), messages, tool schemas, response schema,
limits and trace metadata. The gateway owns provider selection, fallback, token/cost accounting and prompt/version
metadata. The current OpenAI-compatible client is one provider.

Rules:

- The model never sees a secret, webhook URL, raw game password or unredacted file.
- Tool output is data. It is wrapped and labelled as untrusted; instructions inside logs are ignored.
- Tool arguments are validated against the capability schema before execution.
- The model cannot call an approval tool; only a human surface can approve.
- A repeated identical tool call is detected and stopped after a small limit.
- The model cannot change policy, settings, adapters or secrets.

### 2.5 Capability registry

`AgentTools` becomes the registry. A capability is a typed query or command:

```csharp
public sealed record AgentCapability(
    string Name,
    string Description,
    JsonElement Parameters,
    CapabilityKind Kind,          // Query | Command
    AgentToolRisk Risk,           // Read | Write
    IReadOnlyList<string> Scopes, // instance, system
    TimeSpan Timeout,
    bool Idempotent,
    Func<JsonElement, CancellationToken, Task<object>> Handler,
    Func<JsonElement, object, CancellationToken, Task<object?>>? Verify = null);
```

Rules:

- Names are stable and namespaced: `core.status`, `instance.control`, `instance.console.tail`, `logs.tail`,
  `mods.list`, `mods.log`, `files.list`, `files.read`, `jobs.list`, `jobs.wait`, `metrics.get`, `diagnostics.get`,
  `activity.list`, `notifications.deliveries`, `updates.check`, `incidents.list`, `incidents.get`.
- A command returns an operation handle (`jobId`, approval id) and a verification recipe, not only a success string.
- Queries return a bounded summary plus evidence; the model can ask for a narrower query.
- Every capability has a redaction policy and a maximum result size.
- `AgentTools` is also the future MCP surface: the same registry, with scopes and policy, can be exposed to external
  agents.

### 2.6 Policy and approvals

Policy is data, not an `if` in the runtime. The default is deny-writes.

```yaml
version: 1
default: deny-writes
rules:
  - id: restart-medium
    match: { capability: instance.control, action: restart }
    require: approval
    approvers: [role:ops]
    maxPerHour: 6
  - id: mods-update-high
    match: { capability: mods.sync }
    require: approval
    approvers: [owner]
    preconditions: [instance.stopped]
  - id: announce-low
    match: { capability: game.action, action: announce }
    require: none
    maxPerHour: 20
```

The policy service returns a `PolicyDecision`: `allow`, `deny` or `requireApproval`, with the rule id, required
approvers, impact summary and a time window. Every decision is stored.

An approval is a durable proposal:

- id, turn/run, capability, arguments, impact, risk, policy rule;
- scope: instance/channel/thread, allowed approvers, expiry;
- state: `pending → approved → running → succeeded | failed | unknown`;
- decided by, decided at, executed at, verification;
- message id and trace id.

After a successful execution the **conversation resumes**: the tool result is appended and the model is asked to
verify and report. Approval is not a fire-and-forget button.

### 2.7 Incident engine

An incident is opened by a trigger and lives until it is closed.

```text
open → acknowledged → diagnosing → mitigating → verifying → resolved → closed
                                    └→ false-alarm
```

Incident fields: instance, severity, state, title, summary, trigger, opened/acknowledged/resolved/closed timestamps,
timeline, linked runs, approvals, jobs, notifications and a postmortem.

Triggers:

- `instance.crashed`, `instance.faulted`, repeated `job.failed` → open or attach an incident;
- `instance.running` after a crash → recovery event, auto-verify, resolve if healthy;
- update/mods available → advisory card, not an incident;
- user report → open a user incident;
- runbook → attach to an existing incident or open one.

The incident card in Discord has buttons: **Diagnose**, **Restart**, **Silence**, **Close**. A thread is created for
long incidents so the main channel stays readable.

### 2.8 Runbook engine

A runbook is a versioned YAML file. It uses capabilities; it never contains shell or arbitrary HTTP.

```yaml
id: generic.crash-triage
version: 1
appliesTo: { trigger: incident, topics: [instance.crashed, instance.faulted] }
steps:
  - id: status
    use: core.status
  - id: instance
    use: instance.get
    args: { instanceId: "{{ incident.instanceId }}" }
  - id: logs
    use: logs.tail
    args: { source: instance, id: "{{ incident.instanceId }}", tail: 200, level: error }
  - id: summary
    use: model.summarize
    args:
      task: crash-triage
      evidence: ["{{ status.output }}", "{{ instance.output }}", "{{ logs.output }}"]
  - id: note
    use: incident.note
    args: { text: "{{ summary.output.summary }}" }
```

Rules:

- The loader validates every `use` against the registry and every input against the schema.
- `when` is a small safe predicate language, never C# `eval`.
- Outputs are JSON; templates reference `{{ steps.<id>.output }}`.
- Checkpoints after each step; Core restart resumes or marks the run failed.
- `model.summarize` is a capability: the model judges evidence, it does not execute actions.
- Write steps are proposed through the approval engine; a runbook can stop at `waitingApproval` and resume after the
  decision.
- A runbook can be dry-run with recorded evidence.

Built-in packs: `generic.crash-triage`, `generic.job-failed`, `valheim.mods-update`, `valheim.backup-restore`.

### 2.9 Observability

Use `ActivitySource("GameWarden.Agent")` and standard trace ids. A trace is:

```text
discord.interaction
  agent.turn
    context.build
    model.call
    capability.call
    policy.evaluate
    approval.wait
    approval.execute
    capability.verify
    incident.transition
    notification.publish
```

Every run, tool call, approval, incident and message stores the trace id. The panel has a trace view (or the API
does until the view ships). Prompt/response text is not logged by default; redacted text is available only in
diagnostics mode with a short retention.

### 2.10 Adapters and game providers

`adapter.json` remains the source of truth for a game. The Agent derives capabilities:

| Adapter field | Capability |
|---|---|
| `launch`, `stop`, `readiness` | `instance.control`, `instance.verify` |
| `console` | `instance.console.tail`, `instance.console.send` |
| `mods` | `mods.list`, `mods.log`, `mods.sync` |
| `backup` | `backup.create`, `backup.restore` |
| `integration` | `game.read`, `game.action` |
| (built in) | `core.*`, `logs.*`, `files.*`, `jobs.*`, `metrics.*`, `diagnostics.*`, `activity.*`, `notifications.*`, `updates.*`, `incidents.*` |

An optional block only opts in or overrides:

```json
"agent": {
  "capabilities": ["instance.control", "mods.sync"],
  "runbooks": ["valheim.crash-triage", "valheim.mods-update"],
  "riskOverrides": { "game.action.kick": "medium" }
}
```

Adding a game:

1. `adapter.json` with launch/stop/readiness/backup, settings schema, optional mods and integration.
2. `IGameIntegrationProvider` if the game has an API or console events.
3. A runbook pack for its known failures.
4. Adapter validation and a FakeGame scenario.
5. No change to `AgentRuntime`, `DiscordBotService` or `AgentTools`.

## 3. Data model

New tables; existing Agent tables get trace and state columns.

| Table | Key fields |
|---|---|
| `agent_conversations` | id, surface/channel, instance, created/updated (the session) |
| `agent_runs` | id, conversation, user message, state, model, prompt version, trace, tokens, error (the turn) |
| `agent_tool_calls` | existing + trace id, capability, policy decision, approval id, instance, error code |
| `agent_approvals` | existing + impact, policy rule, scope, execution state, executed/verified, verification |
| `agent_incidents` | id, instance, severity, state, title, summary, trigger, times, thread/message |
| `agent_incident_events` | id, incident, at, type, payload, trace |
| `agent_runbook_runs` | id, runbook id/version, incident, instance, state, current step, checkpoint, trace |
| `agent_policy_decisions` | id, turn, capability, rule, inputs hash, result, trace |
| `agent_summaries` | conversation/incident, range, text, tokens |
| `agent_events` | trace/span, type, payload, at |

Retention: traces and events 30 days, incidents and approvals 1 year (or until the Owner deletes them), conversation
messages follow the existing retention. Secrets and webhook URLs never enter these tables.

## 4. End-to-end flows

### 4.1 Chat query

```text
Discord message
  → surface → turn request
  → context pack
  → model chooses capabilities
  → query executes, result redacted and bounded
  → model answers
  → surface sends, trace recorded
```

### 4.2 Write with approval

```text
model proposes instance.control(restart)
  → policy: require approval (rule restart-medium)
  → approval row + Discord card
  → turn state AwaitingApproval
  → human clicks Approve
  → interaction deferred, approval claimed, command executed
  → verify: wait job + instance.health
  → model resumes with the result
  → final answer, approval closed, audit written
```

### 4.3 Crash incident

```text
instance.crashed activity
  → incident open/attach
  → incident card in the mapped channel + thread
  → triage runbook (read-only)
  → findings and proposal
  → [Restart] → approval
  → execute → verify → incident resolved
  → postmortem + status update
```

### 4.4 Notification routing

```text
CoreEvents/Activity
  → NotificationService outbox (existing)
  → Discord webhook channel (existing)
  → Discord bot channel (new): route by instance to the mapped channel/thread
  → IncidentEngine: open/attach incident for actionable topics
```

Webhook and bot are both channels of the same outbox. The bot adds buttons, threads and follow-up; the webhook
remains the fallback when the bot is off.

## 5. Extension and maintenance

### 5.1 Add a capability

1. Add a typed handler in the domain service, returning a summary + operation handle.
2. Register it in `AgentTools` with kind, risk, scopes, timeout and verify.
3. Add a policy default and a contract test.
4. Add it to the prompt routing table.
5. No surface change.

### 5.2 Add a runbook

1. Write YAML under `runbooks/` (built-in) or the data root (Owner).
2. Validate with `gamewarden agent runbook validate <file>`.
3. Dry-run against a recorded incident.
4. Add a golden test.
5. No code change.

### 5.3 Add a game

See §2.10. The adapter validation fails if a declared capability has no backing field or provider.

### 5.4 Add a surface

Implement `IAgentSurface`, register it, map the transport event to `AgentTurnRequest`. The kernel and policy do not
change. The surface must support approvals or declare that it cannot approve (read-only surfaces).

### 5.5 Add a model provider

Implement `IModelGateway`, map the provider's tool-calling and usage fields, add a contract test with a recorded
response. The runtime does not change.

### 5.6 Schema and versioning

- `adapter.schemaVersion` stays the adapter contract.
- `agent-policy.json` and runbooks carry `version`.
- Prompt templates carry `id` and `version`; runs record them.
- DB changes use EF Core migrations; no destructive migration without a backup note.
- Capability names are stable; renaming needs an alias and a deprecation window.

### 5.7 Testing and evaluation

- capability contract tests: schema, risk, timeout, redaction, error mapping;
- adapter tests: every adapter validates and every declared capability resolves;
- runbook tests: reference validation, dry-run, golden scenario;
- deterministic scenario tests with `ScriptedModel` and `FakeGame`;
- live model evals nightly/manual with a rubric and recorded traces;
- chaos: kill Core during approval, job and notification; assert recovery;
- security: prompt injection, secret in user text, cross-channel approval, unauthorized role, rate limit;
- performance: context size, token budget, concurrent turns.

### 5.8 Operations

- `agent_events` and `agent_policy_decisions` are the first place to look when the Agent behaves unexpectedly.
- The panel shows incident timeline, approval history and trace detail.
- A model error, tool error, policy deny or notification failure is an alertable event.
- The kill switch is per instance and global; a paused agent keeps reading but refuses writes.

## 6. Security

- No shell, no arbitrary HTTP, no arbitrary file path; only capabilities.
- Capability scopes + policy + approval are enforced in the kernel, not in the prompt.
- Discord roles are identity input; the policy maps them to scopes. A role never bypasses policy.
- Approval is one-shot, expiring and channel-scoped.
- Secrets and webhook URLs are redacted before the model, logs and trace storage.
- User messages are redacted before storage and model input.
- Game APIs go through `IGameIntegrationProvider` with fixed operations; no SSRF surface.
- Adapter hooks are not agent capabilities.
- Prompt injection in logs/player names is treated as data; no tool can be invoked from a log line.

## 7. Delivery plan

| Phase | Scope | Exit criteria |
|---|---|---|
| A | trace ids, context pack, prompt v2, approval continuation, command UX | current bot is reliable and debuggable; approvals verify |
| B | incident engine, incident cards/threads, triage runbook, bot notification channel | crash → diagnosis → approval → verify → close on the VM |
| C | policy engine, runbook engine, panel incident/trace views, recovery of `running` approvals | a new scenario is YAML, a new policy is data |
| D | adapter capability manifest, game packs, model router, eval harness, MCP surface | a new game ships without an Agent code change |

This document describes the target. Each phase ships behind its own release notes; a phase is not merged until its
exit criteria pass on the VM.

## 8. Implementation status (2026-11-05, 0.3.0-dev.22)

Built in this slice:

- trace ids and `agent_events`; run, tool, approval, incident and policy rows carry the trace;
- context pack (`channel → instance`, state, open jobs, pending approvals, open incidents, recent activity, local
  time);
- policy service with safe default rules and an optional `agent-policy.json`;
- durable approval fields (`impact`, `policyRule`, `source`, `executionState`, `executedAt`, `verifiedAt`);
- approval continuation: a successful write resumes the conversation with the result and a read-only verification
  turn; a Core restart marks a running approval `unknown`;
- incident tables, incident cards, threads, buttons (diagnose, restart, silence), recovery and job-success resolution;
- built-in read-only `generic.crash-triage` runbook, loaded from `runbooks/`;
- `discord-bot` notification channel routed by instance, with webhook fallback;
- `/pending`, `/help`, `/agent-off`, `/agent-on`, optional `/ask instance`, consistent `/server instance`;
- panel Incidents card with timeline and diagnose action; owner APIs for incidents, approvals and traces.

Next slices (do not change the contracts above):

- runbook write/approval steps and automatic resume after a decision;
- policy editor and policy decision view in the panel;
- trace explorer (the API exists; the tree view is next);
- adapter `agent` capability manifest and per-game runbook packs;
- model router, deterministic eval harness and live model evals;
- localization of notification titles and approval cards using the destination language.
