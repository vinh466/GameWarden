# ADR-0026: Valheim players come from the console stream; admins come from `adminlist.txt`

- **Status:** Accepted (the Owner chose this shape on 2026-11-03). Built the same day.
- **Date:** 2026-11-03

## Context

- Valheim's vanilla dedicated server has no REST API, no RCON and no remote console. Its headless process does not read standard input, so GameWarden cannot send `kick`, `ban` or `unban` to it.
- It does print structured-enough player lines: `Got connection SteamID …`, `PlayFab socket … local Platform ID …`, `Got character ZDOID from <name> : <zdo>`, and connection-close lines. The Runner already captures every console line and writes a daily log.
- `adminlist.txt`, `bannedlist.txt` and `permittedlist.txt` are plain text in the `-savedir` folder. GameWarden already sets `-savedir {paths.data}`.
- A2S on game port + 1 gives server info and player counts, but Valheim's `A2S_PLAYER` commonly returns empty names, so it cannot drive per-player admin actions.
- Server-side mods exist with better management (`ConsoleStdinHeeler` adds stdin commands; `ValheimRcon` adds a full RCON with `addAdmin` and `serverStats`). The Owner wants the cheap, mod-free path first, with a mapping that is quick to repair or remove.
- The generic integration framework (ADR-0024) assumes HTTP. Valheim needs one more transport, but should not become a second endpoint/tab shape.

## Decision

1. **One new transport, `console`.** An adapter integration may set `"transport": "console"` instead of an HTTP port. The same Game API tab, provider registry, service, DTOs and action endpoint are reused.
2. **Player events are an adapter mapping.** `integration.events` is a list of `{ event, pattern }`:
   - `joining` captures `id` (SteamID64 or prefixed platform ID),
   - `online` captures `name` and `zdo`,
   - `offline` captures `key` (id or ZDO),
   - `reset` clears the roster.
   The map is compiled once per adapter and can be edited or removed without a Core release. Adapter validation checks the regex and required named groups.
3. **The console stream is the source.** A hosted `ConsolePlayerMonitor` subscribes to `CoreEvents.ConsoleLine`, correlates connection/character/close lines into a per-instance roster, clears it when the server stops, and seeds it from the latest log when Core starts while a server is already running. It never writes to the game's stdin.
4. **Admin is a file action.** `admin`/`unadmin` write `{paths.data}/adminlist.txt` as `gamewarden-run`, preserving comments and other ids, with an atomic replace. Valheim reads the file at startup, so the API and panel say **restart to apply**; Core does not restart the server automatically.
5. **No kick/ban in the mod-free version.** Vanilla has no command path; a ban file without a kick is misleading. `bannedlist.txt`/`permittedlist.txt` are deferred. A mod-based RCON provider can be added later as a separate provider.
6. **The panel stays provider-neutral.** Valheim uses the existing players table, hides columns no provider fills, and shows an admin shield only when the provider declares the `admin` capability. Admin-only entries are listed offline so they can be removed.
7. **Host metrics stay host metrics.** The integration shows the player count; CPU/RAM remain GameWarden's per-process metrics. In-game FPS is not available without a mod.

## Consequences

- A Valheim player list and admin toggle cost no mod, no extra port and no new endpoint. The data is one file write plus console parsing.
- The tab depends on Valheim's log wording. A game update is a mapping edit and a focused test, not a Core code change; if a line disappears, only that state update is lost.
- The roster is memory-only and rebuilt from logs after a Core restart. It may miss a player who joined while Core was not running if the log has already rotated past the reset marker (14-day seed window).
- Admin changes need a restart. That is Valheim's own behavior, and the UI states it rather than implying an immediate effect.
- No kick/ban means a banned player can stay connected until they reconnect or the server restarts; the Owner can still use the in-game F5 console as before.
- The console stream contains player names and platform IDs, so it is Owner-only data. No Agent permission is added in this version.
- `A2S` `players` remains dormant in the Valheim adapter; it is not used for names.

## Alternatives considered

- **A2S only** (no mods): player count yes, names commonly empty; cannot grant admin. Rejected as the primary path.
- **`ConsoleStdinHeeler` + stdin commands**: gives structured `players`, kick/ban and save without a port, but still needs a server mod and has no `addAdmin` command; admin still needs the file/restart. A good later addition if kick/ban is needed.
- **`ValheimRcon` + RCON**: gives `players`, `addAdmin`/`removeAdmin`, `ban`/`unban` and `serverStats` immediately, but needs a server mod, an internal RCON port, a password and a new RCON transport. Deferred until the cheap path proves insufficient.
- **Editing `adminlist.txt` by hand**: no panel, no player id discovery, and easy to get wrong; the console mapping makes the id visible and the action safe.
- **Watching only the console tail**: loses players who joined before the tail window. The log seed with a reset marker is better.
- **A Valheim-only endpoint and tab**: duplicates the generic path and makes the next game start from scratch. Rejected.
