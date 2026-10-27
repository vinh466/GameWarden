using System.Net;
using GameWarden.Core;
using GameWarden.Core.Api;
using GameWarden.Core.Data;

// Console mode for development (02 §11). The Windows service host with pre-shutdown (ADR-0014) comes next.
using var singleCore = SingleCore.TryAcquire();
if (singleCore is null)
{
    Console.Error.WriteLine("Another GameWarden Core is already running on this machine.");
    return 3;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.AddGameWardenCore(builder.Configuration);

var port = builder.Configuration.GetValue<int?>($"{GameWardenOptions.Section}:{nameof(GameWardenOptions.PanelPort)}") ?? 8765;
// Loopback only until the panel arrives with the local-address filter (OQ-28, M2).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));

var app = builder.Build();
await app.Services.MigrateDatabaseAsync();
_ = app.Services.GetRequiredService<CliToken>(); // the CLI needs the token file as soon as Core is up
app.MapGameWardenApi();
await app.RunAsync();
return 0;
