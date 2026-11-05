using System.Text.Json;
using System.Text.Json.Serialization;
using GameWarden.Core.Activity;
using GameWarden.Core.Adapters;
using GameWarden.Core.AgentHost;
using GameWarden.Core.Agents;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using GameWarden.Core.Events;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Integrations;
using GameWarden.Core.Jobs;
using GameWarden.Core.Metrics;
using GameWarden.Core.Mods;
using GameWarden.Core.Observability;
using GameWarden.Core.Notifications;
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
        services.AddSingleton<IExternalScopeProvider>(_ => new LoggerExternalScopeProvider());
        services.AddSingleton<SystemLogWriter>();
        services.AddSingleton<SystemLogQuery>();
        services.AddSingleton<ILoggerProvider, SystemLogLoggerProvider>();

        // Notifications (docs/18): one outbox and one worker; Discord is a channel adapter.
        services.AddSingleton<NotificationSettings>();
        services.AddSingleton<INotificationChannel, DiscordWebhookChannel>();
        services.AddHttpClient(DiscordWebhookChannel.HttpClientName);
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AdapterCatalog>();
        services.AddPlatform(configuration); // isolation mode, secrets, processes, files, firewall, machine (ADR-0016)

        services.AddSingleton<SupervisorContext>();
        services.AddSingleton<Supervisor>();
        services.AddSingleton<CliToken>();

        // Agents (docs/15): clients, token credentials, the feature switches and the audit trail.
        services.AddSingleton<AgentSettingsService>();
        services.AddSingleton<AgentClientsService>();
        services.AddSingleton<AgentRedactor>();
        services.AddSingleton<AgentLogService>();
        services.AddSingleton<AgentDiagnosticsService>();
        services.AddSingleton<AgentFileService>();
        services.AddSingleton<AgentBundleService>();
        services.AddSingleton<AgentUsageTracker>();
        services.AddSingleton<AgentAuditLog>();

        // The Owner's Operations Agent (docs/20): settings, model, tools, approvals and the Discord bot.
        services.AddSingleton<AgentHostSettingsService>();
        services.AddHttpClient(AgentModelClient.HttpClientName);
        services.AddSingleton<AgentModelClient>();
        services.AddSingleton<AgentTrace>();
        services.AddSingleton<AgentContextBuilder>();
        services.AddSingleton<AgentPolicyService>();
        services.AddSingleton<AgentTools>();
        services.AddSingleton<AgentApprovalService>();
        services.AddSingleton<AgentRunbookService>();
        services.AddSingleton<AgentRuntime>();
        services.AddSingleton<AgentIncidentService>();
        services.AddSingleton<AgentApprovalCoordinator>();
        services.AddSingleton<DiscordClientProvider>();
        services.AddSingleton<DiscordMessenger>();
        services.AddSingleton<IAgentMessenger>(provider => provider.GetRequiredService<DiscordMessenger>());
        services.AddSingleton<INotificationChannel, DiscordBotNotificationChannel>();
        services.AddHostedService(provider => provider.GetRequiredService<AgentIncidentService>());
        services.AddSingleton<CoreRestartService>();

        services.AddHttpClient();
        services.AddSingleton(SteamCmdPolicy.Default);
        services.AddSingleton<SteamCmd>();
        services.AddSingleton<JobService>();

        // Mods (docs/13): the loaders Core has; an adapter naming another is invalid.
        services.AddSingleton<IModLoader, BepInExLoader>();
        services.AddSingleton<ModLaunch>();
        services.AddSingleton<ModJobGate>();
        services.AddSingleton<ModCodePreviews>();
        services.AddSingleton(ThunderstorePolicy.Default);
        services.AddHttpClient(ThunderstoreClient.HttpClientName);
        services.AddSingleton<ThunderstoreClient>();
        services.AddSingleton<ThunderstoreSearch>();
        services.AddSingleton<ModsService>();
        services.AddSingleton<ReleaseFeed>();
        services.AddSingleton<UpdateService>();

        // Game APIs (docs/16): a provider per family of APIs, one service for every instance and adapter. Valheim reads
        // players from the console stream and manages adminlist.txt, so it needs no HTTP client or port (docs/17).
        services.AddSingleton<IGameIntegrationProvider, PalworldIntegrationProvider>();
        services.AddSingleton<IGameIntegrationProvider, ValheimConsoleProvider>();
        services.AddSingleton<GameIntegrationRegistry>();
        services.AddSingleton<ConsolePlayerRegistry>();
        services.AddSingleton<ConsolePlayerMonitor>();
        services.AddHttpClient(GameIntegrationService.HttpClientName);
        services.AddSingleton<GameIntegrationService>();

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
        services.AddHostedService(provider => provider.GetRequiredService<SystemLogWriter>());
        services.AddHostedService(provider => provider.GetRequiredService<NotificationService>());
        services.AddHostedService(provider => provider.GetRequiredService<AgentUsageTracker>());
        services.AddHostedService(provider => provider.GetRequiredService<AgentAuditLog>());
        services.AddHostedService<RealtimeBridge>();
        services.AddHostedService(provider => provider.GetRequiredService<MetricsSampler>());
        services.AddHostedService(provider => provider.GetRequiredService<Supervisor>());
        // After the Supervisor: it seeds console players from the log of every instance already running (docs/17).
        services.AddHostedService(provider => provider.GetRequiredService<ConsolePlayerMonitor>());
        services.AddHostedService<UpdateCheckService>();
        services.AddSingleton<DiscordBotService>();
        services.AddHostedService(provider => provider.GetRequiredService<DiscordBotService>());
        return services;
    }
}
