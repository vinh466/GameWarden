using System.Net;
using System.Reflection;
using GameWarden.Core;
using GameWarden.Core.Access;
using GameWarden.Core.Api;
using GameWarden.Core.Hosting;

// GameWarden.Core [--service]
// Without --service: console mode for development (02 §11). With it: the Windows service GameWardenCore (ADR-0014).
var asService = args.Contains("--service");
args = [.. args.Where(arg => arg != "--service")];

// The build writes the OpenAPI document by starting this program with a mock server (Microsoft.Extensions.ApiDescription.Server).
// That run needs the endpoints only: it must not compete with a running Core, nor create a data root, a database or a
// token on the build machine (the guard ASP.NET Core's docs recommend for build-time document generation).
var buildingDocument = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
using var singleCore = buildingDocument ? null : SingleCore.TryAcquire();
if (singleCore is null && !buildingDocument)
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
builder.Services.AddGameWardenAccess(buildingDocument
    ? Path.Combine(Path.GetTempPath(), "gamewarden-openapi-keys")
    : Path.Combine(GameWardenOptions.ReadDataRoot(builder.Configuration), "keys"));
if (!buildingDocument)
{
    builder.Services.AddGameWardenHostedServices();
}

builder.Services.AddOpenApi(options => options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1);
// Longer than the supervisor's 110 s graceful stop when Windows shuts down (ADR-0014).
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(150));
if (asService)
{
    builder.Services.AddSingleton<IHostLifetime, WindowsServiceLifetime>();
}

var port = builder.Configuration.GetValue<int?>($"{GameWardenOptions.Section}:{nameof(GameWardenOptions.PanelPort)}") ?? 8765;
// The service answers on every address and keeps connections to loopback and the access networks (06 §2, OQ-28).
// Development and CI (isolation off) stay on loopback, like the firewall rules they do not create (02 §11).
var listenForPanel = IsolationMode.From(builder.Configuration).Enabled;
builder.WebHost.ConfigureKestrel(kestrel =>
{
    if (listenForPanel)
    {
        kestrel.ListenForPanel(port);
    }
    else
    {
        kestrel.Listen(IPAddress.Loopback, port);
    }
});

var app = builder.Build();
if (!buildingDocument)
{
    app.Lifetime.ApplicationStarted.Register(() => _ = app.Services.GetRequiredService<CliToken>()); // the CLI needs the token file as soon as Core is up
}

app.UsePanelGuards();
app.UsePanelFiles();
app.UseGameWardenAuthentication();
app.MapGameWardenApi();
app.MapPanelFallback();
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
