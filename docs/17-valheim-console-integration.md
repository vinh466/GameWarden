# 17 · Valheim players from the console, admins from `adminlist.txt`

**Status: Accepted (2026-11-03).** The console integration is built; the VM check is the next step. Decision: [ADR-0026](../decisions/0026-valheim-console-events-and-adminlist.md). Generic framework: [16](16-game-integrations.md).

## 1. Why this shape

Valheim's vanilla dedicated server has no REST API and no RCON, and its headless process does **not** read standard input, so GameWarden cannot send admin commands to it. It does:

- print a line when a player connects (`Got connection SteamID …` or a PlayFab platform ID),
- print a line when the character enters the world (`Got character ZDOID from <name> : <zdo>`),
- print a line when a connection closes,
- read `adminlist.txt` from its `-savedir` folder at startup.

That is enough for a cheap, mod-free integration: **read player events from the console**, and **write the admin list file**. It is not enough for kick/ban actions or in-game FPS; those need a server mod or RCON (rejected for this version).

## 2. Transport and adapter block

```json
"integration": {
  "provider": "valheim-console",
  "transport": "console",
  "label": { "en": "Valheim players", "vi": "Người chơi Valheim" },
  "events": [
    { "event": "joining", "pattern": "Got connection SteamID (?<id>\\d+?)\\D*?$" },
    { "event": "joining", "pattern": "PlayFab socket with remote ID .*? received local Platform ID (?<id>\\w+?_\\d+?)$" },
    { "event": "online", "pattern": "Got character ZDOID from (?<name>.+?) : (?<zdo>[\\d-]+?)\\D*?:\\d+?\\D*?$" },
    { "event": "offline", "pattern": "Closing socket (?<key>\\d+?)\\D*?$" },
    { "event": "reset", "pattern": "Game server connected" }
  ]
}
```

`transport: "console"` has no `port`, auth or HTTP request. The provider reads the same console lines the Console tab shows and the Runner writes to `logs/<date>.log`; it never writes to the game's stdin.

## 3. The event map

| `event` | Required named groups | What it does |
|---|---|---|
| `joining` | `id` | The next character line belongs to this platform ID. Steam gives a bare SteamID64; crossplay gives `Steam_…`, `Xbox_…`, `PS_…` or `Switch_…`. |
| `online` | `name`, `zdo` | Pairs the character name and ZDO with the earliest waiting `joining` id and marks the player online. |
| `offline` | `key` | Matches the id, or the ZDO, and marks the player offline. Several patterns may produce `key`. |
| `reset` | – | Clears the roster; used at server start while replaying logs. |

The parser is state-free per line but the roster keeps the pending joins. Correlation is deliberate: the connection line and the character line are separate. When several players join at once, the earliest waiting id is paired first, matching the order in the log.

`IgnoreCase` is on; patterns are unanchored, so the Runner's `HH:mm:ss.fff ` prefix does not matter.

### Sample lines

These are the lines the mapping is tested with (`ConsolePlayerRegistryTests`):

```
15:01:02.123 Got connection SteamID 76561198000000001
15:01:02.456 Got character ZDOID from Vinh : -12345:1
15:10:00.000 Closing socket 76561198000000001

15:01:02.123 PlayFab socket with remote ID 123456 received local Platform ID Xbox_2535416401464117
15:01:02.456 Got character ZDOID from Crossy : -4242:1
15:10:00.000 Destroying abandoned non persistent zdo -4242:1
```

## 4. Rebuilding after a Core restart

`ConsolePlayerMonitor` starts with Core and subscribes to the console event stream. When Core starts while a server is already running, it reads the latest `logs/<date>.log` (up to 14 days, newest first), finds the last `reset` line and replays from there. That is why a player who was already online before Core restarted still appears. When an instance stops, crashes or is removed, its roster is cleared.

The roster is memory-only. Nothing is written to the database; a Core restart rebuilds it from the log.

## 5. Admin grant

The action is `admin` or `unadmin` with `userId`. The provider:

1. validates the id (`[A-Za-z0-9_]{1,64}`; it is a platform ID, not a character name),
2. reads `data/adminlist.txt` as `gamewarden-run` (the adapter's `-savedir` is `{paths.data}`),
3. keeps comments and blank lines, adds or removes the exact id case-sensitively,
4. writes a temporary file in the same folder and replaces `adminlist.txt` atomically.

**Valheim reads the admin list at startup, so the change applies after a restart.** The API result and the panel toast say so. The provider never sends `admin` over stdin and never restarts the server itself.

`bannedlist.txt` and `permittedlist.txt` are deliberately out of scope in this version: adding a ban without a kick path is misleading, and the whitelist is a lockout hazard. Both can be added later with the same file helper and an explicit “next start” warning.

## 6. What the panel shows

The same Game API tab as Palworld, with the controls the provider's capabilities declare:

- **Summary:** world name, and the live player count. Valheim's own FPS/RAM are not available without a mod; GameWarden's CPU/RAM metrics stay on the server Overview.
- **Players:** online players from the console, with their platform ID. Level, ping and IP columns disappear when no provider supplies them.
- **Admin:** a shield button on each online or offline admin row. It writes `adminlist.txt`; the toast says to restart.
- **No kick/ban buttons** in this version, because vanilla has no stdin/RCON command path.

Admin-only entries are included even when offline, so an old admin can be removed without waiting for them to join. Their id is shown instead of a character name.

## 7. Remapping a changed log line

The mapping is data, not code. To adapt to a game update:

1. Find the new line in the server Console tab or in `instances/<id>/logs/<date>.log`.
2. Edit only that entry's `pattern` in `adapters/valheim/adapter.json`; keep the named groups the event needs.
3. Run the focused tests:
   ```bash
   dotnet test --filter "FullyQualifiedName~ConsolePlayerRegistryTests"
   dotnet test --filter "FullyQualifiedName~ValheimConsoleIntegrationTests"
   ```
4. If a line has no useful replacement, delete its entry. The rest of the tab keeps working; the missing event only means that player state is not updated.

Adapter validation catches an invalid regex, an unknown event name and a missing named group at load, with `integration.events[i]` in the message. The provider reads the map from the adapter, so an Owner adapter can override the built-in Valheim adapter and change the mapping without a Core release.

## 8. Limitations

- No kick, ban or unban; no in-game FPS; no A2S player names (Valheim often returns an empty name in `A2S_PLAYER`).
- Admin changes need a server restart; the file is read at startup.
- The roster depends on the console line formats. A changed format is a one-line mapping fix, but it is not detected automatically; the players table will simply be empty until the pattern is updated.
- Console lines and platform IDs are personal data; the API already requires the Owner, and the integration does not send them to Agent clients.
- The mapping assumes `-savedir {paths.data}`; a hand-edited launch that puts the world somewhere else needs the same path in the adapter.

## 9. Tests

- `ConsolePlayerRegistryTests` (Unit): Steam join/online/close, crossplay join/ZDO close, reset, multiple joiners, and adapter validation of a missing group.
- `ValheimConsoleIntegrationTests` (Integration): a portless console instance, live console lines through the monitor, a Core restart that reseeds the roster from the log, `admin`/`unadmin` writing `data/adminlist.txt`, the restart message, and invalid ids.
- `IntegrationTab.test.tsx` (panel): Valheim's world/player view, dynamic columns and the Make admin action.
- `serverTabs.test.ts` already covers the tab appearing only when the adapter has an integration.

## 10. Manual check on the VM

After updating a Valheim instance to adapter 0.5.0:

1. Open the server's **Valheim players** tab. The tab says the server is not running, with *Start*; start it.
2. Join with a friend. The player appears with the character name and platform ID; `instances\<id>\logs\<date>.log` has the `Got connection` and `Got character` lines.
3. Press the shield on that row. `instances\<id>\data\adminlist.txt` now contains the id on its own line; the toast says to restart. Restart the server, rejoin, press F5 and run `banned` (or `help`): an admin command now answers.
4. Press the shield again → the id leaves `adminlist.txt`; restart and confirm the player is no longer admin.
5. With the server running, restart the Core service. The tab still lists the player after reconnect: the monitor seeded the roster from the latest log.
6. Stop the server. The tab goes offline and the player list is empty. Start it again; a new join appears.
7. If a line is missing after a Valheim patch, follow §7: copy the new line, change the matching pattern, run the two focused test filters, and restart Core.
