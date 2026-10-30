using System.Text.Json;
using System.Text.Json.Serialization;
using GameWarden.Core.Activity;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using GameWarden.Core.Events;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Jobs;
using GameWarden.Core.Metrics;
using GameWarden.Core.Mods;
using GameWarden.Core.Platform;
using GameWarden.Core.Realtime;
using GameWarden.Core.Supervision;
using GameWarden.Core.Updates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameWarden.Core;

public static class CoreServices
{
    public static IServiceCollection AddGameWardenCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GameWardenOptions>()
            .Bind(configuration.GetSection(GameWardenOptions.Section))
            .PostConfigure(options =>
            {
                options.DataRoot = Environment.ExpandEnvironmentVariables(options.DataRoot);
                options.CliTokenPath = options.CliTokenPath is null ? null : Environment.ExpandEnvironmentVariables(options.CliTokenPath);
                options.SetupTokenPath = options.SetupTokenPath is null ? null : Environment.ExpandEnvironmentVariables(options.SetupTokenPath);
            });

        services.AddDbContextFactory<GameWardenDb>((provider, builder) =>
            builder.UseSqlite($"Data Source={provider.GetRequiredService<IOptions<GameWardenOptions>>().Value.DatabasePath}"));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CoreEvents>();
        services.AddSingleton<ActivityLog>();
        services.AddSingleton<MetricsSampler>();
        services.AddSingleton(RestartPolicy.Default);
        services.AddSingleton<ShutdownSignal>();
        services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        services.AddSingleton<AdapterCatalog>();
        services.AddPlatform(configuration); // isolation mode, secrets, processes, files, firewall, machine (ADR-0016)

        services.AddSingleton<SupervisorContext>();
        services.AddSingleton<Supervisor>();
        services.AddSingleton<CliToken>();
        services.AddHttpClient();
        services.AddSingleton(SteamCmdPolicy.Default);
        services.AddSingleton<SteamCmd>();
        services.AddSingleton<JobService>();

        // Mods (docs/13): no loader is registered until BepInEx (phase 1b); an adapter naming one is invalid until then.
        services.AddSingleton<ModJobGate>();
        services.AddSingleton(ThunderstorePolicy.Default);
        services.AddHttpClient(ThunderstoreClient.HttpClientName);
        services.AddSingleton<ThunderstoreClient>();
        services.AddSingleton<ModsService>();
        services.AddSingleton<ReleaseFeed>();
        services.AddSingleton<UpdateService>();

        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            // Numbers are numbers: the web default also accepts "123", which would type every number as number | string
            // in the OpenAPI document and the panel's generated client.
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        // The realtime hub speaks the same JSON as the API: camelCase, enums as camelCase strings (docs/05 §3).
        services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.PayloadSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        return services;
    }

    /// <summary>
    /// What a running Core does on its own: data root, database, supervision, update checks. Hosted services start in
    /// this order and stop in reverse. Left out when the build only writes the OpenAPI document.
    /// </summary>
    public static IServiceCollection AddGameWardenHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<CoreStartup>();
        // All are constructed before the first starts, so these listen from Core's first event on; they start after
        // CoreStartup has migrated the database.
        services.AddHostedService(provider => provider.GetRequiredService<ActivityLog>());
        services.AddHostedService<RealtimeBridge>();
        services.AddHostedService(provider => provider.GetRequiredService<MetricsSampler>());
        services.AddHostedService(provider => provider.GetRequiredService<Supervisor>());
        services.AddHostedService<UpdateCheckService>();
        return services;
    }
}
