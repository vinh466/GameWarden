# ADR-0024: Game APIs go through one provider registry, not one endpoint per game

- **Status:** Accepted (2026-11-03). Built the same day with Palworld as the first provider.
- **Date:** 2026-11-03

## Context

- Palworld 1.0 ships a REST API next to the game port: info, metrics, players, announce, save, kick, ban, unban, shutdown and stop. It is useful in the panel and safe to expose through Core, because the game API stays on loopback and the panel never sees `AdminPassword`.
- More games will expose APIs with different shapes. Adding a tab, endpoint, DTO set and service per game would multiply code and make the panel a pile of game-specific branches.
- The project already separates per-game knowledge (adapter data) from Core services (renderers, readiness, stop, mods). Game APIs should follow the same boundary.
- The game API is on the same machine, over plain HTTP with basic auth. It is not a trusted public API and must not be exposed directly.
- The Owner wants this as an add-on to the existing Palworld server, not a replacement for the adapter's install/start/stop path.

## Decision

1. **An adapter may declare an optional `integration`**: a provider id, one of its ports, basic-auth templates, a timeout and a per-language label. No integration means no tab.
2. **Core has a provider registry keyed by provider id.** A provider owns one game family's paths, JSON, action bodies and capability list. A new game API is a new provider class and one DI registration, not a new endpoint or database table.
3. **One generic service and one endpoint pair.** `GET /instances/{id}/integration` reads; `POST /instances/{id}/integration/actions/{action}` acts. The service resolves the instance, adapter, port, settings and secrets, renders auth with the existing template engine and calls the provider on `http://127.0.0.1:<port>/`.
4. **The panel is provider-neutral.** One Game API tab appears when the adapter summary has an integration. The provider's `label` names it; its `capabilities` control which standard controls render (`players`, `announce`, `save`, `kick`, `ban`, `unban`). A new capability needs a panel release; a new game with existing capabilities does not.
5. **Secrets stay server-side.** `AdminPassword` is read from `ISecretStore`, rendered into a request header in memory and never returned by the API or written to logs.
6. **The game API is only reachable through fixed provider operations.** Core does not proxy arbitrary paths, URLs or bodies. The integration port is `internal`: it gets no firewall rule and only Core uses it on loopback.
7. **Offline is not an error.** A stopped instance answers `online: false` with the provider's label and capabilities; a running instance that refuses or times out answers 502/504 with a stable code. Actions while stopped are 409 `invalid_state`.
8. **Shutdown stays with the adapter.** The panel gets announce/save/kick/ban/unban; stopping remains the adapter's controlled `stop` path so a tab button cannot race install/update/stop jobs.

## Consequences

- Palworld gets a useful API tab without changing its launch, readiness or stop model.
- The next game with a REST API needs an adapter block plus a provider; the panel, endpoints, DTOs and error codes are reused.
- The provider contract is intentionally small: info/metrics/players plus named actions. A game with a very different read shape must fit those DTOs or extend them in a later ADR.
- Capabilities are a UI contract. A provider that lies about one produces a broken control, so provider tests must check the list against the actions it implements.
- The API's actual paths and bodies drift with game updates. Defensive parsing and a provider unit test are required; a game update can still need a Core release.
- `timeoutSec` and concurrent reads bound the damage of a hung game. The integration never blocks the Supervisor or the instance lock.
- No Agent permission is added. The tab is an Owner/CLI feature in this version; if Agents need it later, it gets explicit scopes rather than reusing the Owner endpoint.

## Alternatives considered

- **A tab and endpoints per game.** Fast for Palworld, but every game duplicates the same read/action/error/secret path and the panel grows a branch per game.
- **A generic adapter-declared HTTP template** (paths, JSON pointers, bodies). No new C# per game, but it becomes a small programming language for JSON shapes, offers no typed capabilities, and makes validation and tests much harder.
- **Expose the game API directly to the panel.** The panel would handle `AdminPassword`, CORS, the internal port and the game's error shape; it also breaks the rule that only Core talks to game APIs. Rejected.
- **Use the game API for start/stop/readiness.** Readiness and stop already work through the adapter and are shared with games that have no API. Mixing them would make the stop path depend on a provider and duplicate existing tests.
- **A dynamic plugin or script provider.** Providers run in Core's process; loading Owner-supplied code there is the same trust problem as arbitrary hooks, for little gain. Providers stay compiled and released with GameWarden.
