# GameWarden MCP bridge

A local **stdio** MCP server for Agent access ([docs/15](../../docs/15-agent-access.md), [ADR-0023](../../decisions/0023-agent-clients.md)). It holds one GameWarden Agent token and calls the scoped `/api/v1/agent` surface over the VPN. It does not listen on a port.

## Configure

Create a file at `~/.config/gamewarden/agent.json` (mode 0600):

```json
{
  "url": "http://<vm-vpn-ip>:8765",
  "tokenFile": "/home/you/.config/gamewarden/agent.token"
}
```

Write the token from `gamewarden agents token <client-id>` to `agent.token` and run:

```bash
chmod 700 ~/.config/gamewarden
chmod 600 ~/.config/gamewarden/agent.json ~/.config/gamewarden/agent.token
```

Environment variables override the file: `GAMEWARDEN_URL`, `GAMEWARDEN_TOKEN_FILE`, `GAMEWARDEN_API_TOKEN`.

## OpenCode

```jsonc
{
  "mcp": {
    "gamewarden": {
      "type": "local",
      "command": ["dotnet", "/path/to/gamewarden-mcp.dll"],
      "enabled": true,
      "timeout": 120000
    }
  }
}
```

The token is never printed by the bridge; API errors are returned as readable JSON so the Agent can react (for example `diagnostics_mode_off`).

## Build

```bash
dotnet build src/GameWarden.Mcp/GameWarden.Mcp.csproj
```

The SDK supports protocol versions through 2025-11-25; a client that requests a newer version negotiates down during `initialize`.
