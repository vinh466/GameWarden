# 16 · Game integrations

**Status: Accepted (2026-11-03).** The generic provider path is built; Palworld is the first provider. ADR: [ADR-0024](../decisions/0024-game-apis-through-a-provider-registry.md). Palworld's server model is [14](14-palworld.md); the adapter format is [04](04-adapter-spec.md).

## 1. Scope

Some dedicated servers expose their own HTTP API next to the game port: players, live metrics, announcements, saves, kicks and bans. The panel should use that API when the game has one, without turning every game into its own tab and endpoint set.

An adapter may therefore declare an optional **integration**. Core has a **provider** for that API family; the panel shows one **Game API** tab for an instance whose adapter has an integration. Palworld 1.0's REST API is the first provider (`palworld-rest`).

An integration is read and action surface only. It does not replace install, start, stop, readiness, backups or console: those keep working through the adapter and the Runner.

## 2. The adapter block

```json
"integration": {
  "provider": "palworld-rest",
  "port": "rest",
  "auth": { "username": "admin", "password": "{secrets.AdminPassword}" },
  "timeoutSec": 10,
  "label": { "en": "Palworld API", "vi": "Palworld API" }
}
```

| Field | Required | Meaning |
|---|---|---|
| `provider` | ✓ | Id of a provider compiled into Core, `[a-z0-9-]+`. Unknown ids make the tab answer 409 `integration_provider_missing`. |
| `port` | ✓ | One of the adapter's ports. Core builds `http://127.0.0.1:<allocated port>/`; the port should be `internal`, so it gets no firewall rule (ADR-0022 decision 4). |
| `auth` | – | `{ "username", "password" }`, both templates like readiness and stop auth. The usual password is `{secrets.AdminPassword}`. Values are rendered per request and never sent to the panel. |
| `timeoutSec` | – | 1–60, default 10: one game API request's budget. |
| `label` | – | Text per language for the tab. The panel falls back to its own `Game API` text. |

The adapter's `integration` is validated at load: a known provider id shape, a declared port, a sane timeout and a complete basic-auth pair. The provider itself is resolved at request time, because providers are Core code and adapters are data.

## 3. The provider contract

```csharp
public interface IGameIntegrationProvider
{
    string Id { get; }
    IReadOnlyList<string> Capabilities { get; }
    Task<GameIntegrationData> ReadAsync(GameIntegrationContext context, CancellationToken cancellationToken);
    Task<GameIntegrationActionResult> ActAsync(
        GameIntegrationContext context,
        string action,
        GameIntegrationActionRequest request,
        CancellationToken cancellationToken);
}
```

`GameIntegrationContext` carries the instance id, the loopback base URI, the HTTP client, the rendered basic-auth values and the timeout. The provider is the only code that knows one game's paths, JSON field names and action bodies.

The read answer is the same shape for every game, with fields a provider may leave null:

- **Info**: version, server name, description, world id.
- **Metrics**: FPS, players (current and maximum), frame time, uptime, base camps, in-game days.
- **Players**: name, account name, player id, user id, IP, ping, position, level, building count.

**Capabilities** are stable lowercase tokens. The first set: `overview`, `players`, `announce`, `save`, `kick`, `ban`, `unban`. The panel renders a control only for a capability the provider declares; a new provider can offer a subset without a panel change. A new capability that needs new controls needs both a provider and a panel release.

Providers are registered in `GameIntegrationRegistry` by `Id`, so adding a game API is one class plus one registration — never a new endpoint or tab.

## 4. The request path

`GameIntegrationService` owns the generic path:

1. Find the instance in the Supervisor; 404 when it does not exist.
2. Find its adapter and its `integration`; 409 `integration_not_supported` without one.
3. Resolve the provider in the registry; 409 `integration_provider_missing` when Core does not have it.
4. Resolve the instance's allocated integration port, settings and secrets. Auth templates are rendered with the same values as launch/readiness/stop templates. A missing secret is 409 `integration_not_configured`, not a crash.
5. For a read, answer `online: false` with the provider's label and capabilities when the instance is not `Starting` or `Running`. The tab is calm while the server is stopped and can offer *Start*.
6. For a running instance, call the provider over `http://127.0.0.1:<port>/` and map its errors:
   - 502 `integration_auth_failed`: the game refused the configured credentials.
   - 502 `integration_unavailable`: connection refused, a non-success status, or a response this version cannot parse.
   - 504 `integration_timeout`: the game did not answer within `timeoutSec`.
   - 409 `invalid_state`: an action while the server is not running.

Actions are refused when the server is not `Running`. Core never proxies arbitrary URLs, paths or bodies from the panel to the game; only the provider's fixed operations exist.

## 5. Palworld (`palworld-rest`)

Palworld 1.0 documents its REST API with HTTP basic auth: user `admin`, password `AdminPassword`. Core already puts that password in `PalWorldSettings.ini`, so the adapter reuses the same secret.

| Read | Palworld path | Used for |
|---|---|---|
| Info | `GET /v1/api/info` | Game version, server name, description, world GUID |
| Metrics | `GET /v1/api/metrics` | FPS, player count, frame time, uptime, base camps, days |
| Players | `GET /v1/api/players` | Player list, ping, level, building count |

| Action | Palworld path | Body |
|---|---|---|
| `announce` | `POST /v1/api/announce` | `{ "message": "..." }`, at most 512 characters |
| `save` | `POST /v1/api/save` | – |
| `kick` | `POST /v1/api/kick` | `{ "userid": "...", "message": "..." }` |
| `ban` | `POST /v1/api/ban` | `{ "userid": "...", "message": "..." }` |
| `unban` | `POST /v1/api/unban` | `{ "userid": "..." }` |

The provider parses casing variants and string numbers defensively, because the API's examples and community clients differ in casing and number types. `shutdown` and `stop` stay out of the panel: they are the adapter's stop path, not a button that races it.

## 6. API

| Method | Path | Notes |
|---|---|---|
| GET | `/instances/{id}/integration` | The provider's label and capabilities, `online`, and `info`, `metrics`, `players`; offline answers `online: false` with empty data. 404 `not_found`, 409 `integration_not_supported` / `integration_provider_missing` / `integration_not_configured`, 502/504 from the game. |
| POST | `/instances/{id}/integration/actions/{action}` | `{ message?, userId?, waitTime? }` → `{ action, ok, message }`. The action must be one the provider supports (400 `unsupported_action`) and the server must be running (409 `invalid_state`). |

Both are Owner/CLI API paths; no Agent scope is added in this version. The panel's generated client comes from the OpenAPI document as usual.

## 7. Panel

The server detail shows a tab only when its adapter summary has an `integration`. Its label is the adapter's localized label, else *Game API*. The page is provider-neutral:

- **Summary**: server name, version, world id, description; live metrics when present.
- **Actions**: announce, save and unban only when the capability is declared. A save is one click; announce and unban validate empty input.
- **Players**: a table when `players` is declared, with kick and ban only when those capabilities are declared. Kick and ban ask for a reason, then send the player's `userId`. A player without a user id cannot be acted on, and the button says why.

The query follows the server state with `live.fast`; actions invalidate it so a kick or save is visible without a reload. When offline, the page says so and offers *Start* instead of pretending to have data.

## 8. Adding another game API

1. Check that the game's API is HTTP and can be reached on loopback; keep its port `internal`.
2. Add an `integration` block to its adapter with a provider id, port and auth templates. Add the secret to the schema if needed.
3. Write `IGameIntegrationProvider`: fixed reads, fixed actions, defensive parsing, a capability list, and `GameIntegrationException` for expected failures.
4. Register it in `CoreServices`; add its id to any validation list if it becomes a shared provider.
5. Add provider unit tests with a stub handler and one integration test against FakeGame (or the real server on the VM).
6. Reuse the existing tab. Add a panel control only for a genuinely new capability.

No new endpoint, database table or job type is needed. The integration is stateless and reads current game state on demand.

## 9. Security and limits

- The game API is reached only from Core on `127.0.0.1`; the panel never receives the game's admin password or port.
- Auth templates are rendered in memory per request. Secrets stay in `ISecretStore` and are not logged or put in API responses.
- Only the provider's fixed operations are exposed. There is no generic URL/body proxy to the game.
- `timeoutSec` bounds one request. Read requests run info, metrics and players concurrently; a slow or dead game cannot hold a panel request indefinitely.
- A provider is trusted Core code compiled and released with GameWarden, not an Owner-supplied script. This keeps the same trust boundary as mod loaders and other Core services.
- The adapter's `internal` port must still be blocked at the machine firewall by the platform rule; `internal` means "no inbound rule", not a listener ACL.

## 10. Tests

- `PalworldIntegrationProviderTests` (Unit): parsing of info/metrics/players, defensive casing and string numbers, basic auth, every action's path and body, auth/validation errors.
- `PalworldIntegrationTests` (Integration): an Owner adapter with a `palworld-rest` integration, FakeGame's REST mode, a generated/configured secret, the read path, a wrong secret, every action, offline behaviour and an adapter without an integration.
- `IntegrationTab.test.tsx` (panel): summary, metrics, players, announce/save/unban and the kick/ban dialog against the mock API.
- `serverTabs.test.ts`: the tab appears only with an integration and `tabOfPath` knows it.
