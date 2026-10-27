using System.Net;
using GameWarden.Core;
using GameWarden.Core.Api;
using GameWarden.Core.Hosting;

// GameWarden.Core [--service]
// Without --service: console mode for development (02 §11). With it: the Windows service GameWardenCore (ADR-0014).
var asService = args.Contains("--service");
args = [.. args.Where(arg => arg != "--service")];

using var singleCore = SingleCore.TryAcquire();
if (singleCore is null)
{
    Console.Error.WriteLine("Another GameWarden Core is already running on this machine.");
    return 3;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory, // a service starts in System32
});

// Owner overrides in the data root (08 §6); environment variables and the command line still win.
builder.Configuration.AddJsonFile(Path.Combine(GameWardenOptions.ReadDataRoot(builder.Configuration), "gamewarden.json"), optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

builder.Services.AddGameWardenCore(builder.Configuration);
// Longer than the supervisor's 110 s graceful stop when Windows shuts down (ADR-0014).
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(150));
if (asService)
{
    builder.Services.AddSingleton<IHostLifetime, WindowsServiceLifetime>();
}

var port = builder.Configuration.GetValue<int?>($"{GameWardenOptions.Section}:{nameof(GameWardenOptions.PanelPort)}") ?? 8765;
// Loopback only until the panel arrives with the local-address filter (OQ-28, M2).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));

var app = builder.Build();
app.Lifetime.ApplicationStarted.Register(() => _ = app.Services.GetRequiredService<CliToken>()); // the CLI needs the token file as soon as Core is up
app.MapGameWardenApi();
try
{
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "GameWarden Core stopped because of an error.");
    return 1;
}
