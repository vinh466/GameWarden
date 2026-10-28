using System.Text.Json;
using System.Text.Json.Serialization;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Jobs;
using GameWarden.Core.Network;
using GameWarden.Core.Processes;
using GameWarden.Core.Secrets;
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
        services.AddSingleton(RestartPolicy.Default);
        services.AddSingleton<ShutdownSignal>();
        services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        services.AddSingleton<AdapterCatalog>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();

        var isolation = IsolationMode.From(configuration);
        services.AddSingleton(isolation);
        if (isolation.Enabled)
        {
            services.AddSingleton<RunAsAccount>();
            services.AddSingleton<IProcessHost, RunAsUserProcessHost>();
            services.AddSingleton<IInstanceFiles, ImpersonatedInstanceFiles>();
            services.AddSingleton<IDataRootAcl, WindowsDataRootAcl>();
            services.AddSingleton<IFirewall, WindowsFirewall>();
        }
        else
        {
            services.AddSingleton<IProcessHost, CurrentUserProcessHost>();
            services.AddSingleton<IInstanceFiles, DirectInstanceFiles>();
            services.AddSingleton<IDataRootAcl, PlainDataRoot>();
            services.AddSingleton<IFirewall, NoFirewall>();
        }

        services.AddSingleton<SupervisorContext>();
        services.AddSingleton<Supervisor>();
        services.AddSingleton<CliToken>();
        services.AddHttpClient();
        services.AddSingleton(SteamCmdPolicy.Default);
        services.AddSingleton<SteamCmd>();
        services.AddSingleton<JobService>();
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
        return services;
    }

    /// <summary>
    /// What a running Core does on its own: data root, database, supervision, update checks. Hosted services start in
    /// this order and stop in reverse. Left out when the build only writes the OpenAPI document.
    /// </summary>
    public static IServiceCollection AddGameWardenHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<CoreStartup>();
        services.AddHostedService(provider => provider.GetRequiredService<Supervisor>());
        services.AddHostedService<UpdateCheckService>();
        return services;
    }
}
