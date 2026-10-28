using System.Reflection;
using System.Security.Principal;
using GameWarden.Core.Access;
using GameWarden.Core.Activity;
using GameWarden.Core.Data;
using GameWarden.Core.Isolation;
using GameWarden.Core.Jobs;
using GameWarden.Core.Network;
using GameWarden.Core.Updates;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Hosting;

/// <summary>Whether Core isolates games (ADR-0008/0009) and manages the firewall; see <see cref="GameWardenOptions.Isolation"/>.</summary>
public sealed record IsolationMode(bool Enabled)
{
    public static IsolationMode From(IConfiguration configuration) =>
        (configuration[$"{GameWardenOptions.Section}:{nameof(GameWardenOptions.Isolation)}"] ?? "auto") switch
        {
            "on" => new(true),
            "off" => new(false),
            _ => new(WindowsIdentity.GetCurrent().IsSystem),
        };
}

public static class CoreInfo
{
    /// <summary>Release version (e.g. <c>0.1.0-rc.1</c>), without the commit suffix.</summary>
    public static string Version { get; } =
        (typeof(CoreInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public static DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Runs before the supervisor (hosted services start in registration order): data root and its ACL, the run-as
/// user, then the database. Jobs left running by a previous Core and update results are handled by their services.
/// </summary>
internal sealed class CoreStartup(
    IServiceProvider services,
    IOptions<GameWardenOptions> options,
    IsolationMode isolation,
    IDataRootAcl dataRoot,
    ILogger<CoreStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "GameWarden Core {Version} starting as {User}; data root {Root}; isolation {Isolation}.",
            CoreInfo.Version,
            WindowsIdentity.GetCurrent().Name,
            options.Value.DataRoot,
            isolation.Enabled ? "on" : "off");
        if (isolation.Enabled)
        {
            // Secrets (the run-as password among them) go under the data root: protect it before writing any.
            WindowsDataRootAcl.Protect(options.Value.DataRoot, logger);
            services.GetRequiredService<RunAsAccount>().Ensure();
        }

        dataRoot.EnsureLayout();
        await services.MigrateDatabaseAsync(cancellationToken);
        if (isolation.Enabled)
        {
            services.GetRequiredService<IFirewall>().SetPanel(options.Value.PanelPort, options.Value.ResolvedPanelAccessNetworks);
        }

        if (await services.GetRequiredService<Accounts>().NeedsSetupAsync(cancellationToken))
        {
            // The token stays until the Owner exists, so the setup link keeps working across restarts (06 §4).
            services.GetRequiredService<SetupToken>().Ensure();
            logger.LogInformation("No Owner yet: open the setup link from 'gamewarden setup-url' on this machine.");
        }

        await services.GetRequiredService<JobService>().FailInterruptedAsync(cancellationToken);
        var activity = services.GetRequiredService<ActivityLog>();
        if (services.GetRequiredService<UpdateService>().LastResult() is { } update)
        {
            logger.LogInformation("Last install or update: {From} -> {To} {Outcome} at {At}: {Message}", update.From, update.To, update.Outcome, update.At, update.Message);
            if (DateTimeOffset.UtcNow - update.At < TimeSpan.FromMinutes(10))
            {
                // This start is the update's own: say how it went.
                activity.Add("update", update.Outcome switch { "succeeded" => "info", "rolledBack" => "warning", _ => "error" }, update.Message);
            }
        }

        activity.Add("core.started", "info", $"GameWarden {CoreInfo.Version} started.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
