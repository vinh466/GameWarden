using GameWarden.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A local stdio MCP server: OpenCode starts it on the dev box, and it calls GameWarden over the VPN with the Agent
// token from GAMEWARDEN_API_TOKEN or ~/.config/gamewarden/agent.token. Nothing listens on a port here.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Validate the URL and token before the MCP handshake, so a misconfiguration is one clear line on stderr.
GameWardenApiClient api;
try
{
    api = new GameWardenApiClient();
}
catch (McpToolException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

builder.Services.AddSingleton(api);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
