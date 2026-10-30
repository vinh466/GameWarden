using GameWarden.Core.Access;
using GameWarden.Core.Activity;
using GameWarden.Core.Adapters;
using GameWarden.Core.Checks;
using GameWarden.Core.Hosting;
using GameWarden.Core.Isolation;
using GameWarden.Core.Jobs;
using GameWarden.Core.Metrics;
using GameWarden.Core.Mods;
using GameWarden.Core.Network;
using GameWarden.Core.Secrets;
using GameWarden.Core.Supervision;
using GameWarden.Core.Updates;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Api;

/// <summary>
/// The part of docs/05-api.md implemented so far. Every endpoint has an operation name and its response types, so the
/// OpenAPI document describes it fully; the panel's client is generated from that document (docs/11 §9).
/// </summary>
public static class ApiEndpoints
{
    public static void MapGameWardenApi(this WebApplication app)
    {
        app.MapGet("/healthz", () => TypedResults.Ok(new HealthStatus("ok")))
            .WithName("getHealth").WithTags("System");

        // Every endpoint needs the panel's session or the CLI's token unless it allows anonymous use (sign-in, setup).
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .AddEndpointFilter<CsrfFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        AccessEndpoints.MapAccess(api);
        MapSystem(api);
        MapAdapters(api);
        MapInstances(api);
        MapJobs(api);
    }

    private static void MapSystem(RouteGroupBuilder api)
    {
        api.MapGet("/system", async (
            IOptions<GameWardenOptions> options,
            IsolationMode isolation,
            AdapterCatalog catalog,
            UpdateService updates,
            IMachine machine,
            IPrerequisiteChecker prerequisites,
            CancellationToken cancellationToken) =>
        {
            var o = options.Value;
            var network = await NetworkCheck.RunAsync(cancellationToken);
            return TypedResults.Ok(new SystemInfo(
                CoreInfo.Version,
                CoreInfo.StartedAt,
                UpdateService.InstallRoot,
                updates.LastResult(),
                updates.LastCheck,
                o.DataRoot,
                isolation.Enabled,
                isolation.Enabled ? o.RunAsUserName : null,
                o.ResolvedVpnRanges,
                machine.Snapshot(o.DataRoot),
                File.Exists(o.ResolvedSteamCmdPath),
                network,
                [.. Prerequisites.Known.Select(prerequisites.Check).Select(status => new PrerequisiteInfo(
                    status.Id,
                    status.Installed,
                    status.Hint,
                    [.. catalog.Adapters.Where(adapter => adapter.Prerequisites.Contains(status.Id)).Select(adapter => adapter.Id)]))],
                catalog.Problems.Count));
        }).WithName("getSystem").WithTags("System");

        api.MapGet("/system/update", async (bool? prerelease, UpdateService updates, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await updates.CheckAsync(prerelease == true, cancellationToken));
            }
            catch (HttpRequestException ex)
            {
                return ApiProblems.Create(StatusCodes.Status502BadGateway, "upstream_unavailable", $"GitHub did not answer: {ex.Message}");
            }
        }).WithName("checkForUpdate").WithTags("System").Produces<UpdateCheck>().ProducesProblem(StatusCodes.Status502BadGateway);

        // 10 §6: Core prepares and verifies the package, then the new version's Setup takes over and restarts the service.
        api.MapPost("/system/update", async (UpdateRequest request, UpdateService updates) =>
        {
            try
            {
                return Results.Accepted(value: await updates.StartAsync(request, CancellationToken.None));
            }
            catch (UpdateRefusedException ex)
            {
                return ApiProblems.Create(StatusFor(ex.Code), ex.Code, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return ApiProblems.Create(StatusCodes.Status502BadGateway, "upstream_unavailable", $"Download failed: {ex.Message}");
            }
        }).WithName("startUpdate").WithTags("System")
            .Produces<UpdateStarted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .RecordsActivity("Update requested.");

        api.MapGet("/system/versions", (UpdateService updates) => TypedResults.Ok(updates.Installed()))
            .WithName("listVersions").WithTags("System");

        // `gamewarden version use` for the panel (docs/11 §2 "Switch back to previous version").
        api.MapPost("/system/versions/switch", (SwitchVersionRequest request, UpdateService updates) =>
        {
            try
            {
                return Results.Accepted(value: updates.SwitchTo(request.Version, request.RestoreDatabase));
            }
            catch (UpdateRefusedException ex)
            {
                return ApiProblems.Create(StatusFor(ex.Code), ex.Code, ex.Message);
            }
        }).WithName("switchVersion").WithTags("System")
            .Produces<UpdateStarted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RecordsActivity("Switch to another version requested.");

        api.MapGet("/system/settings", (SystemSettings settings) => TypedResults.Ok(settings.Get()))
            .WithName("getSystemSettings").WithTags("System");

        api.MapPut("/system/settings", (SystemSettingsUpdate update, SystemSettings settings, HttpContext http) =>
        {
            try
            {
                return Results.Ok(settings.Update(update, http.Connection.LocalIpAddress));
            }
            catch (SystemSettingsRefusedException ex)
            {
                return ApiProblems.Create(ex.Status, ex.Code, ex.Message);
            }
        }).WithName("updateSystemSettings").WithTags("System")
            .Produces<SystemSettingsView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RecordsActivity("Network settings changed.");

        api.MapGet("/metrics", (MetricsSampler metrics) => TypedResults.Ok(metrics.Report()))
            .WithName("getMetrics").WithTags("System");

        // Setup calls this before removing the service (08 §3): games stop gracefully, rules and the run-as user go.
        // Only the uninstaller does that, with the CLI token on the machine itself; the panel cannot.
        api.MapPost("/system/uninstall-cleanup",async (Supervisor supervisor, IFirewall firewall, IsolationMode isolation, IServiceProvider services, ILogger<Supervisor> logger) =>
        {
            var stopped = await supervisor.StopAllAsync(Supervisor.ShutdownStopBudget);
            firewall.RemoveAll();
            string? accountProblem = null;
            if (isolation.Enabled)
            {
                try
                {
                    services.GetRequiredService<RunAsAccount>().Remove();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    logger.LogError(ex, "Removing the run-as user failed.");
                    accountProblem = ex.Message;
                }
            }

            return TypedResults.Ok(new UninstallCleanupResult(stopped, FirewallRulesRemoved: true, accountProblem));
        }).WithName("uninstallCleanup").WithTags("System").RequireAuthorization(AccessServices.CliPolicy).ExcludeFromDescription();
    }

    private static void MapAdapters(RouteGroupBuilder api)
    {
        api.MapGet("/adapters", (AdapterCatalog catalog) =>
            TypedResults.Ok(new AdapterList([.. catalog.Adapters.Select(AdapterSummary.From)], catalog.Problems)))
            .WithName("listAdapters").WithTags("Adapters");

        // The adapter's help.json (04 §5.3), fetched when the panel opens a help drawer, so GET /adapters stays light.
        api.MapGet("/adapters/{id}/help", (string id, AdapterCatalog catalog) =>
            catalog.Find(id) is { } adapter
                ? Results.Ok(adapter.Help ?? AdapterHelp.Empty)
                : ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", $"No adapter '{id}'."))
            .WithName("getAdapterHelp").WithTags("Adapters")
            .Produces<AdapterHelp>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/adapters/reload", (AdapterCatalog catalog) =>
        {
            catalog.Reload();
            return TypedResults.Ok(new AdapterReloadResult(catalog.Adapters.Count, catalog.Problems));
        }).WithName("reloadAdapters").WithTags("Adapters");
    }

    private static void MapInstances(RouteGroupBuilder api)
    {
        var instances = api.MapGroup("/instances").WithTags("Instances");

        instances.MapGet("", (Supervisor supervisor) => TypedResults.Ok(supervisor.List())).WithName("listInstances");

        instances.MapGet("/{id}", (string id, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor ? Results.Ok(actor.Snapshot) : NotFound(id))
            .WithName("getInstance").Produces<InstanceSnapshot>().ProducesProblem(StatusCodes.Status404NotFound);

        instances.MapPost("", async (CreateInstanceRequest request, Supervisor supervisor, CancellationToken cancellationToken) =>
        {
            var result = await supervisor.CreateAsync(request, cancellationToken);
            return result.Instance is { } instance
                ? Results.Created($"/api/v1/instances/{instance.Id}", instance)
                : ApiProblems.Create(result.StatusCode, result.Code ?? ProblemCodes.ValidationFailed, result.Error!);
        }).WithName("createInstance")
            .Produces<InstanceSnapshot>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        instances.MapDelete("/{id}", async (string id, string? mode, string? confirm, Supervisor supervisor, CancellationToken cancellationToken) =>
        {
            if (mode != "purge")
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, "Only mode=purge is available in this version (archive comes with backups).");
            }

            if (confirm != id)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"mode=purge deletes every file of the instance, saves included; confirm with confirm={id}.");
            }

            var result = await supervisor.PurgeAsync(id, cancellationToken);
            return result.Error is null
                ? Results.Ok(new InstanceDeleted(id, result.Leftovers))
                : result.StatusCode == StatusCodes.Status404NotFound ? NotFound(id) : ApiProblems.Create(result.StatusCode, result.Code!, result.Error);
        }).WithName("deleteInstance")
            .Produces<InstanceDeleted>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        instances.MapPost("/{id}/start", (string id, bool? force, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.StartAsync(force == true))).WithName("startInstance").WithCommandResponses()
            .RecordsActivity("Start requested.");
        instances.MapPost("/{id}/stop", (string id, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.StopAsync())).WithName("stopInstance").WithCommandResponses()
            .RecordsActivity("Stop requested.");
        instances.MapPost("/{id}/restart", (string id, bool? force, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.RestartAsync(force == true))).WithName("restartInstance").WithCommandResponses()
            .RecordsActivity("Restart requested.");
        // The command text stays out of the feed: it may carry a password (e.g. an admin login).
        instances.MapPost("/{id}/console", (string id, ConsoleCommand body, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.SendConsoleAsync(body.Command))).WithName("sendConsoleCommand").WithCommandResponses()
            .RecordsActivity("Console command sent.");

        instances.MapGet("/{id}/console", async (string id, int? tail, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor
                ? Results.Ok(new ConsoleTail(await actor.TailAsync(Math.Clamp(tail ?? 200, 1, 1000))))
                : NotFound(id))
            .WithName("getConsoleTail").Produces<ConsoleTail>().ProducesProblem(StatusCodes.Status404NotFound);

        instances.MapPost("/{id}/install", (string id, bool? force, JobService jobs, CancellationToken cancellationToken) =>
            StartJob(jobs.StartInstallAsync(id, update: false, force == true, cancellationToken))).WithName("installInstance").WithJobResponses()
            .RecordsActivity("Install started.");
        instances.MapPost("/{id}/update", (string id, JobService jobs, CancellationToken cancellationToken) =>
            StartJob(jobs.StartInstallAsync(id, update: true, force: false, cancellationToken))).WithName("updateInstanceGame").WithJobResponses()
            .RecordsActivity("Game update started.");

        instances.MapGet("/{id}/settings", async (string id, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor ? Results.Ok(await actor.GetSettingsAsync()) : NotFound(id))
            .WithName("getInstanceSettings").Produces<SettingsView>().ProducesProblem(StatusCodes.Status404NotFound);

        instances.MapPut("/{id}/settings", async (string id, SettingsUpdate body, Supervisor supervisor, ISecretStore secrets, AdapterCatalog adapters) =>
        {
            if (supervisor.Find(id) is not { } actor)
            {
                return NotFound(id);
            }

            var settings = body.Settings ?? new Dictionary<string, string?>();
            var secretChanges = body.Secrets ?? new Dictionary<string, string?>();
            var schema = adapters.Find(actor.Snapshot.AdapterId)?.Schema;
            if ((SettingsValidation.Problem(settings)
                ?? SettingsSchemaValidation.ValueProblem(schema, settings)
                ?? SettingsSchemaValidation.ValueProblem(schema, secretChanges, secrets: true)) is { } problem)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, problem);
            }

            if (secretChanges.Keys.FirstOrDefault(name => !SecretScopes.IsValidName(name)) is { } badName)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"Secret name '{badName}' must be 1-64 letters, digits, '.', '_' or '-'.");
            }

            foreach (var (name, value) in secretChanges)
            {
                if (string.IsNullOrEmpty(value))
                {
                    secrets.Delete(SecretScopes.Instance(id), name);
                }
                else
                {
                    secrets.Set(SecretScopes.Instance(id), name, value);
                }
            }

            return Results.Ok(settings.Count > 0 ? await actor.UpdateSettingsAsync(settings) : await actor.GetSettingsAsync());
        }).WithName("updateInstanceSettings")
            .Produces<SettingsView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RecordsActivity("Settings changed.");

        // Mods (docs/13 §4.11): a change builds a new set in a mods.sync job, which leaves the server alone; the set
        // takes effect at the next start. 409 for an adapter without mods, mods_busy while a mods job runs.
        instances.MapGet("/{id}/mods", async (string id, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.GetAsync(id, cancellationToken)))
            .WithName("getInstanceMods")
            .Produces<ModsView>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        instances.MapPut("/{id}/mods", (string id, ModsUpdate body, ModsService mods, CancellationToken cancellationToken) =>
            StartJob(mods.StartSyncAsync(id, body.Entries, body.PreviewId, cancellationToken))).WithName("updateInstanceMods").WithJobResponses()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RecordsActivity("Mods change started.");

        // r2modman codes (13 §4.4, §4.6): a preview reads a code and changes nothing; PUT /mods with its previewId applies it.
        instances.MapPost("/{id}/mods/code-preview", async (string id, ModCodePreviewRequest body, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.PreviewCodeAsync(id, body.Code, cancellationToken)))
            .WithName("previewInstanceModsCode")
            .Produces<ModCodePreview>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        instances.MapPost("/{id}/mods/share-code", async (string id, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.ShareCodeAsync(id, cancellationToken)))
            .WithName("createInstanceModsShareCode")
            .Produces<ModShareCode>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .RecordsActivity("Mods share code made.");

        instances.MapPost("/{id}/mods/check-updates", async (string id, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.CheckUpdatesAsync(id, cancellationToken)))
            .WithName("checkInstanceModsUpdates")
            .Produces<ModUpdateCheck>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        instances.MapGet("/{id}/mods/configs", async (string id, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.ConfigsAsync(id, cancellationToken)))
            .WithName("listInstanceModsConfigs")
            .Produces<ModConfigList>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        instances.MapGet("/{id}/mods/configs/content", async (string id, string? path, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.ConfigContentAsync(id, path, cancellationToken)))
            .WithName("getInstanceModsConfigContent")
            .Produces<ModConfigContent>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        instances.MapPut("/{id}/mods/enabled", async (string id, ModsEnabledUpdate body, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.SetEnabledAsync(id, body.Enabled, cancellationToken)))
            .WithName("setInstanceModsEnabled")
            .Produces<ModsView>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RecordsActivity("Mods on or off changed.");

        instances.MapPost("/{id}/mods/rollback", async (string id, ModsService mods, CancellationToken cancellationToken) =>
            ModsAnswer(await mods.RollbackAsync(id, cancellationToken)))
            .WithName("rollbackInstanceMods")
            .Produces<ModsView>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RecordsActivity("Mods set back to the previous set.");
    }

    private static IResult ModsAnswer(ModsResult result) =>
        result.Mods is { } mods ? Results.Ok(mods) : ApiProblems.Create(result.StatusCode, result.Code ?? ProblemCodes.InvalidState, result.Error!);

    private static IResult ModsAnswer<T>(ModsResult<T> result)
        where T : class =>
        result.Value is { } value ? Results.Ok(value) : ApiProblems.Create(result.StatusCode, result.Code ?? ProblemCodes.InvalidState, result.Error!);

    private static void MapJobs(RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs").WithTags("Jobs");

        jobs.MapGet("", async (string? instanceId, JobService service, CancellationToken cancellationToken) =>
            TypedResults.Ok((await service.ListAsync(instanceId, cancellationToken)).Select(JobInfo.From).ToList()))
            .WithName("listJobs");

        jobs.MapGet("/{jobId}", async (string jobId, JobService service, CancellationToken cancellationToken) =>
            await service.FindAsync(jobId, cancellationToken) is { } job
                ? Results.Ok(JobInfo.From(job))
                : JobNotFound(jobId))
            .WithName("getJob").Produces<JobInfo>().ProducesProblem(StatusCodes.Status404NotFound);

        jobs.MapGet("/{jobId}/log", async (string jobId, int? tail, JobService service, IOptions<GameWardenOptions> options, CancellationToken cancellationToken) =>
            await service.FindAsync(jobId, cancellationToken) is null
                ? JobNotFound(jobId)
                : Results.Ok(new JobLogTail(JobLog.Tail(options.Value.JobLogsPath, jobId, Math.Clamp(tail ?? 200, 1, 5000)))))
            .WithName("getJobLog").Produces<JobLogTail>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/activity", async (string? instanceId, long? before, int? limit, ActivityLog activity, CancellationToken cancellationToken) =>
            TypedResults.Ok((await activity.ListAsync(instanceId, before, limit ?? 50, cancellationToken)).Select(ActivityItem.From).ToList()))
            .WithName("listActivity").WithTags("Activity");
    }

    /// <summary>A successful command becomes an activity line with the user who sent it (docs/11 §13).</summary>
    private static RouteHandlerBuilder RecordsActivity(this RouteHandlerBuilder builder, string message) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: < 400 })
            {
                var http = context.HttpContext;
                http.RequestServices.GetRequiredService<ActivityLog>()
                    .Add("command", "info", message, http.GetRouteValue("id") as string, user: http.User.Identity?.Name);
            }

            return result;
        });

    private static async Task<IResult> Command(Supervisor supervisor, string id, Func<InstanceActor, Task<CommandResult>> command)
    {
        if (supervisor.Find(id) is not { } actor)
        {
            return NotFound(id);
        }

        var result = await command(actor);
        return result.Outcome switch
        {
            CommandOutcome.Accepted => Results.Accepted($"/api/v1/instances/{id}", new InstanceCommandResponse(result.Message, result.Instance)),
            CommandOutcome.NothingToDo => Results.Ok(new InstanceCommandResponse(result.Message, result.Instance)),
            _ => ApiProblems.Create(StatusFor(result.Code), result.Code ?? ProblemCodes.InvalidState, result.Message),
        };
    }

    /// <summary>Long operations answer 202 with the job and its Location (05 §1).</summary>
    private static async Task<IResult> StartJob(Task<JobStartResult> starting)
    {
        var result = await starting;
        return result.Job is { } job
            ? Results.Accepted($"/api/v1/jobs/{job.Id}", new JobStarted(job.Id, JobInfo.From(job)))
            : ApiProblems.Create(result.StatusCode, result.Code ?? ProblemCodes.InvalidState, result.Error!);
    }

    private static RouteHandlerBuilder WithCommandResponses(this RouteHandlerBuilder builder) => builder
        .Produces<InstanceCommandResponse>()
        .Produces<InstanceCommandResponse>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status412PreconditionFailed);

    private static RouteHandlerBuilder WithJobResponses(this RouteHandlerBuilder builder) => builder
        .Produces<JobStarted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status412PreconditionFailed);

    private static int StatusFor(string? code) => code switch
    {
        ProblemCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        ProblemCodes.PrerequisiteMissing or ProblemCodes.ResourcesInsufficient => StatusCodes.Status412PreconditionFailed,
        _ => StatusCodes.Status409Conflict,
    };

    private static IResult NotFound(string id) => ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", $"No instance '{id}'.");

    private static IResult JobNotFound(string jobId) => ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", $"No job '{jobId}'.");
}

public sealed record ConsoleCommand(string Command);

/// <summary>Body of <c>PUT /instances/{id}/settings</c>: a null value removes a setting; an empty or null secret removes it.</summary>
public sealed record SettingsUpdate(IReadOnlyDictionary<string, string?>? Settings, IReadOnlyDictionary<string, string?>? Secrets);

internal static class ApiProblems
{
    /// <summary>RFC 7807 ProblemDetails with the stable <c>code</c> of docs/05-api.md §5.</summary>
    public static IResult Create(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
