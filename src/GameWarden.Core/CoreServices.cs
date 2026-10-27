using System.Text.Json;
using System.Text.Json.Serialization;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Processes;
using GameWarden.Core.Supervision;
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
            });

        services.AddDbContextFactory<GameWardenDb>((provider, builder) =>
            builder.UseSqlite($"Data Source={provider.GetRequiredService<IOptions<GameWardenOptions>>().Value.DatabasePath}"));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(RestartPolicy.Default);
        services.AddSingleton<AdapterCatalog>();
        services.AddSingleton<IProcessHost, CurrentUserProcessHost>();
        services.AddSingleton<SupervisorContext>();
        services.AddSingleton<Supervisor>();
        services.AddHostedService(provider => provider.GetRequiredService<Supervisor>());
        services.AddSingleton<CliToken>();

        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        return services;
    }
}
