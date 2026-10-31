using System.Security.Claims;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Access;
using GameWarden.Core.Activity;
using GameWarden.Core.Api;
using GameWarden.Core.Checks;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Jobs;
using GameWarden.Core.Metrics;
using GameWarden.Core.Supervision;
using GameWarden.Core.Updates;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Agents;

/// <summary>
/// Two surfaces (docs/15): <c>/api/v1/agents</c> is the Owner's management API; <c>/api/v1/agent</c> is the
/// scoped runtime API that an Agent token may call. The two never share a policy.
/// </summary>
public static class AgentEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var agents = api.MapGroup("/agents")
            .RequireAuthorization(AccessServices.OwnerPolicy)
            .WithTags("Agents");

        agents.MapGet("", async (AgentClientsService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.ListAsync(cancellationToken)))
            .WithName("listAgentClients")
            .Produces<IReadOnlyList<AgentClientView>>();

        agents.MapGet("/permissions", () =>
            Results.Ok(new AgentPermissionCatalog(AgentPermissions.All, AgentPermissions.Presets)))
            .WithName("listAgentPermissions")
            .Produces<AgentPermissionCatalog>();

        agents.MapGet("/settings", async (AgentSettingsService settings, CancellationToken cancellationToken) =>
            Results.Ok(await settings.ViewAsync(cancellationToken)))
            .WithName("getAgentSettings")
            .Produces<AgentSettingsView>();

        agents.MapPut("/settings", async (UpdateAgentSettingsRequest body, AgentSettingsService settings, CancellationToken cancellationToken) =>
            await Guard(async () => Results.Ok(await settings.UpdateAsync(body, cancellationToken))))
            .WithName("updateAgentSettings")
            .Produces<AgentSettingsView>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        agents.MapGet("/audit", async (string? clientId, long? before, int? limit, AgentAuditLog audit, CancellationToken cancellationToken) =>
            Results.Ok(await audit.ListAsync(clientId, before, limit ?? 50, cancellationToken)))
            .WithName("listAgentAudit")
            .Produces<IReadOnlyList<AgentAuditView>>();

        agents.MapPost("", async (CreateAgentClientRequest body, AgentClientsService clients, CancellationToken cancellationToken) =>
            await Guard(async () =>
            {
                var client = await clients.CreateAsync(body, cancellationToken);
                return Results.Created($"/api/v1/agents/{client.Id}", client);
            }))
            .WithName("createAgentClient")
            .Produces<AgentClientView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        agents.MapGet("/{id}", async (string id, AgentClientsService clients, CancellationToken cancellationToken) =>
            await clients.FindAsync(id, cancellationToken) is { } client
                ? Results.Ok(client)
                : NotFound(id))
            .WithName("getAgentClient")
            .Produces<AgentClientView>();

        agents.MapPatch("/{id}", async (string id, UpdateAgentClientRequest body, AgentClientsService clients, CancellationToken cancellationToken) =>
            await Guard(async () =>
                await clients.UpdateAsync(id, body, cancellationToken) is { } client
                    ? Results.Ok(client)
                    : NotFound(id)))
            .WithName("updateAgentClient")
            .Produces<AgentClientView>();

        agents.MapDelete("/{id}", async (string id, AgentClientsService clients, CancellationToken cancellationToken) =>
            await clients.DeleteAsync(id, cancellationToken) ? Results.NoContent() : NotFound(id))
            .WithName("deleteAgentClient");

        agents.MapGet("/{id}/tokens", async (string id, AgentClientsService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.ListTokensAsync(id, cancellationToken)))
            .WithName("listAgentTokens")
            .Produces<IReadOnlyList<AgentTokenView>>();

        agents.MapPost("/{id}/tokens", async (string id, CreateAgentTokenRequest? body, AgentClientsService clients, CancellationToken cancellationToken) =>
            await Guard(async () =>
            {
                var created = await clients.CreateTokenAsync(id, body ?? new CreateAgentTokenRequest(null, null), cancellationToken);
                return Results.Created($"/api/v1/agents/{id}/tokens/{created.TokenInfo.Id}", created);
            }))
            .WithName("createAgentToken")
            .Produces<AgentTokenCreated>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        agents.MapPost("/{id}/tokens/{tokenId}/rotate", async (string id, string tokenId, AgentClientsService clients, CancellationToken cancellationToken) =>
            await Guard(async () =>
                await clients.RotateAsync(id, tokenId, cancellationToken) is { } created
                    ? Results.Ok(created)
                    : NotFound(tokenId)))
            .WithName("rotateAgentToken");

        agents.MapDelete("/{id}/tokens/{tokenId}", async (string id, string tokenId, AgentClientsService clients, CancellationToken cancellationToken) =>
            await clients.RevokeTokenAsync(id, tokenId, cancellationToken) ? Results.NoContent() : NotFound(tokenId))
            .WithName("revokeAgentToken");

        agents.MapGet("/{id}/audit", async (string id, long? before, int? limit, AgentAuditLog audit, CancellationToken cancellationToken) =>
            Results.Ok(await audit.ListAsync(id, before, limit ?? 50, cancellationToken)))
            .WithName("listAgentClientAudit");

        var agent = api.MapGroup("/agent")
            .RequireAuthorization(AccessServices.AgentPolicy)
            .WithTags("Agent");

        agent.MapGet("/me", (HttpContext http, TimeProvider time) =>
            Principal(http) is { } principal ? Results.Ok(Me(principal, time.GetUtcNow())) : Unauthenticated())
            .WithName("getAgentMe");

        agent.MapGet("/status", async (
                HttpContext http,
                Supervisor supervisor,
                JobService jobs,
                IOptions<GameWardenOptions> options,
                IMachine machine,
                TimeProvider time,
                CancellationToken cancellationToken) =>
            {
                if (Principal(http) is not { } principal)
                {
                    return Unauthenticated();
                }

                var snapshots = supervisor.List();
                var instances = snapshots.Select(AgentInstanceView.From).ToList();
                var now = time.GetUtcNow();
                var problems = new List<AgentProblemView>();
                foreach (var snapshot in snapshots)
                {
                    if (snapshot.State == ActualState.Faulted)
                    {
                        problems.Add(new AgentProblemView("instance.faulted", snapshot.LastProblem ?? "Faulted; it needs a human.", snapshot.Id, null));
                    }
                    else if (snapshot.DesiredState == DesiredState.Running
                             && snapshot.State is not (ActualState.Running or ActualState.Starting))
                    {
                        problems.Add(new AgentProblemView("instance.not_running", $"Asked to run but is {snapshot.State}.", snapshot.Id, null));
                    }
                    else if (snapshot.LastProblem is { Length: > 0 } lastProblem)
                    {
                        problems.Add(new AgentProblemView("instance.problem", lastProblem, snapshot.Id, null));
                    }
                }

                var failed = (await jobs.ListAsync(null, cancellationToken))
                    .Where(job => job.State == JobState.Failed && job.FinishedAt >= now.AddHours(-24))
                    .Take(20)
                    .Select(job => new AgentProblemView("job.failed", job.Message ?? "Job failed.", job.InstanceId, job.FinishedAt));
                problems.AddRange(failed);

                var machineSnapshot = machine.Snapshot(options.Value.DataRoot);
                if (machineSnapshot.DiskFreeMb < 5000)
                {
                    problems.Add(new AgentProblemView("disk.low", $"Only {machineSnapshot.DiskFreeMb} MB free on the data root volume.", null, now));
                }

                return Results.Ok(new AgentStatusView(
                    new AgentCoreView(CoreInfo.Version, (long)(now - CoreInfo.StartedAt).TotalSeconds, problems.Count == 0 ? "ok" : "degraded"),
                    Me(principal, now),
                    new AgentMachineView(machineSnapshot.CpuCores, machineSnapshot.RamTotalMb, machineSnapshot.RamFreeMb, machineSnapshot.DiskFreeMb),
                    instances,
                    problems));
            })
            .WithName("getAgentStatus")
            .RequireAgentPermission(AgentPermissions.StatusRead);

        agent.MapGet("/instances", (Supervisor supervisor) =>
            Results.Ok(supervisor.List().Select(AgentInstanceView.From).ToList()))
            .WithName("listAgentInstances")
            .RequireAgentPermission(AgentPermissions.InstancesRead);

        agent.MapGet("/instances/{id}", (string id, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor ? Results.Ok(AgentInstanceView.From(actor.Snapshot)) : NotFound(id))
            .WithName("getAgentInstance")
            .RequireAgentPermission(AgentPermissions.InstancesRead);

        agent.MapGet("/instances/{id}/console", async (string id, int? tail, Supervisor supervisor, AgentRedactor redactor) =>
        {
            if (supervisor.Find(id) is not { } actor)
            {
                return NotFound(id);
            }

            var lines = await actor.TailAsync(Math.Clamp(tail ?? 200, 1, 1000));
            return Results.Ok(lines.Select(line => new AgentLogLine(
                line.At,
                line.Stream == OutputStream.Stderr ? "error" : "info",
                redactor.Redact(line.Text))).ToList());
        })
            .WithName("getAgentConsole")
            .RequireAgentPermission(AgentPermissions.InstancesRead);

        agent.MapGet("/instances/{id}/settings", async (string id, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor ? Results.Ok(await actor.GetSettingsAsync()) : NotFound(id))
            .WithName("getAgentInstanceSettings")
            .RequireAgentPermission(AgentPermissions.SettingsRead);

        agent.MapPost("/instances/{id}/start", (
                string id,
                bool? force,
                Supervisor supervisor,
                ActivityLog activity,
                HttpContext http) =>
            Command(id, supervisor, actor => actor.StartAsync(force == true), activity, http, "Start requested."))
            .WithName("startAgentInstance")
            .RequireAgentPermission(AgentPermissions.InstancesControl);

        agent.MapPost("/instances/{id}/stop", (
                string id,
                Supervisor supervisor,
                ActivityLog activity,
                HttpContext http) =>
            Command(id, supervisor, actor => actor.StopAsync(), activity, http, "Stop requested."))
            .WithName("stopAgentInstance")
            .RequireAgentPermission(AgentPermissions.InstancesControl);

        agent.MapPost("/instances/{id}/restart", (
                string id,
                bool? force,
                Supervisor supervisor,
                ActivityLog activity,
                HttpContext http) =>
            Command(id, supervisor, actor => actor.RestartAsync(force == true), activity, http, "Restart requested."))
            .WithName("restartAgentInstance")
            .RequireAgentPermission(AgentPermissions.InstancesControl);

        agent.MapPost("/instances/{id}/console", (
                string id,
                ConsoleCommand body,
                Supervisor supervisor,
                ActivityLog activity,
                HttpContext http) =>
            Command(id, supervisor, actor => actor.SendConsoleAsync(body.Command), activity, http, "Console command sent."))
            .WithName("sendAgentConsole")
            .RequireAgentPermission(AgentPermissions.ConsoleWrite);

        agent.MapGet("/logs", (
                string? source,
                string? id,
                int? tail,
                string? level,
                string? contains,
                HttpContext http,
                AgentLogService logs) =>
            {
                if (Principal(http) is not { } principal)
                {
                    return Unauthenticated();
                }

                var normalizedSource = source?.Trim().ToLowerInvariant();
                if (normalizedSource is "core" or "setup" && !principal.Allows(AgentPermissions.DiagnosticsSystem))
                {
                    return ApiProblems.Create(
                        StatusCodes.Status403Forbidden,
                        "forbidden",
                        $"Core logs need the permission '{AgentPermissions.DiagnosticsSystem}'.");
                }

                if (normalizedSource is "core" or "setup" && !principal.DiagnosticsLive)
                {
                    return ApiProblems.Create(
                        StatusCodes.Status403Forbidden,
                        "diagnostics_mode_off",
                        "Core logs are behind diagnostics mode. Ask the Owner to enable it in Agents → Settings.");
                }

                return Results.Ok(logs.Tail(source, id, tail, level, contains));
            })
            .WithName("tailAgentLogs")
            .RequireAgentPermission(AgentPermissions.LogsRead);

        agent.MapGet("/jobs", async (string? instanceId, string? state, JobService jobs, CancellationToken cancellationToken) =>
        {
            var rows = await jobs.ListAsync(instanceId, cancellationToken);
            var filtered = state is { Length: > 0 }
                ? rows.Where(row => string.Equals(row.State.ToString(), state, StringComparison.OrdinalIgnoreCase))
                : rows;
            return Results.Ok(filtered.Select(JobInfo.From).ToList());
        })
            .WithName("listAgentJobs")
            .RequireAgentPermission(AgentPermissions.JobsRead);

        agent.MapGet("/jobs/{jobId}", async (string jobId, JobService jobs, CancellationToken cancellationToken) =>
            await jobs.FindAsync(jobId, cancellationToken) is { } job ? Results.Ok(JobInfo.From(job)) : NotFound(jobId))
            .WithName("getAgentJob")
            .RequireAgentPermission(AgentPermissions.JobsRead);

        agent.MapGet("/jobs/{jobId}/log", async (string jobId, int? tail, JobService jobs, AgentLogService logs, CancellationToken cancellationToken) =>
            await jobs.FindAsync(jobId, cancellationToken) is null
                ? NotFound(jobId)
                : Results.Ok(logs.Tail("job", jobId, tail, null, null)))
            .WithName("getAgentJobLog")
            .RequireAgentPermission(AgentPermissions.JobsRead);

        agent.MapGet("/activity", async (string? instanceId, long? before, int? limit, ActivityLog activity, CancellationToken cancellationToken) =>
            Results.Ok((await activity.ListAsync(instanceId, before, limit ?? 50, cancellationToken)).Select(ActivityItem.From).ToList()))
            .WithName("listAgentActivity")
            .RequireAgentPermission(AgentPermissions.ActivityRead);

        agent.MapGet("/metrics", (MetricsSampler metrics) => Results.Ok(metrics.Report()))
            .WithName("getAgentMetrics")
            .RequireAgentPermission(AgentPermissions.MetricsRead);

        agent.MapGet("/system/update", async (bool? prerelease, UpdateService updates, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await updates.CheckAsync(prerelease == true, cancellationToken));
            }
            catch (HttpRequestException ex)
            {
                return ApiProblems.Create(StatusCodes.Status502BadGateway, "upstream_unavailable", $"GitHub did not answer: {ex.Message}");
            }
        })
            .WithName("checkAgentUpdate")
            .RequireAgentPermission(AgentPermissions.SystemUpdate)
            .Produces<UpdateCheck>()
            .ProducesProblem(StatusCodes.Status502BadGateway);

        agent.MapPost("/system/update", async (UpdateRequest body, UpdateService updates, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Accepted(value: await updates.StartAsync(body, cancellationToken));
            }
            catch (UpdateRefusedException ex)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, ex.Code, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return ApiProblems.Create(StatusCodes.Status502BadGateway, "upstream_unavailable", $"Download failed: {ex.Message}");
            }
        })
            .WithName("startAgentUpdate")
            .RequireAgentPermission(AgentPermissions.SystemUpdate)
            .RequireAgentMode(AgentMode.Admin)
            .Produces<UpdateStarted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        agent.MapGet("/diagnostics/system", (AgentDiagnosticsService diagnostics) => Results.Ok(diagnostics.SystemInfo()))
            .WithName("getAgentSystemDiagnostics")
            .RequireAgentPermission(AgentPermissions.DiagnosticsSystem)
            .RequireAgentMode(AgentMode.Diagnostics);

        agent.MapGet("/diagnostics/processes", (int? limit, AgentDiagnosticsService diagnostics) =>
            Results.Ok(diagnostics.Processes(limit)))
            .WithName("getAgentProcesses")
            .RequireAgentPermission(AgentPermissions.DiagnosticsProcesses)
            .RequireAgentMode(AgentMode.Diagnostics);

        agent.MapGet("/diagnostics/storage", (AgentDiagnosticsService diagnostics) => Results.Ok(diagnostics.Storage()))
            .WithName("getAgentStorage")
            .RequireAgentPermission(AgentPermissions.DiagnosticsStorage)
            .RequireAgentMode(AgentMode.Diagnostics);

        agent.MapGet("/diagnostics/network", (AgentDiagnosticsService diagnostics) => Results.Ok(diagnostics.Network()))
            .WithName("getAgentNetwork")
            .RequireAgentPermission(AgentPermissions.DiagnosticsNetwork)
            .RequireAgentMode(AgentMode.Diagnostics);

        agent.MapGet("/diagnostics/events", (string? log, string? level, int? hours, int? limit, AgentDiagnosticsService diagnostics) =>
            Results.Ok(diagnostics.Events(log, level, hours, limit)))
            .WithName("getAgentEvents")
            .RequireAgentPermission(AgentPermissions.DiagnosticsEvents)
            .RequireAgentMode(AgentMode.Diagnostics);

        agent.MapGet("/diagnostics/files", async (string? instanceId, string? path, int? depth, AgentFileService files) =>
            await Guard(() => Task.FromResult<IResult>(
                instanceId is { Length: > 0 }
                    ? Results.Ok(files.List(instanceId, path, depth))
                    : ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, "instanceId is required."))))
            .WithName("listAgentFiles")
            .RequireAgentPermission(AgentPermissions.DiagnosticsFiles)
            .RequireAgentMode(AgentMode.Diagnostics)
            .Produces<AgentFilesView>();

        agent.MapGet("/diagnostics/files/content", async (string? instanceId, string? path, AgentFileService files) =>
            await Guard(() => Task.FromResult<IResult>(
                instanceId is { Length: > 0 }
                    ? Results.Ok(files.Read(instanceId, path))
                    : ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, "instanceId is required."))))
            .WithName("readAgentFile")
            .RequireAgentPermission(AgentPermissions.DiagnosticsFiles)
            .RequireAgentMode(AgentMode.Diagnostics)
            .Produces<AgentFileContent>();

        agent.MapPost("/diagnostics/bundles", async (
                CreateAgentBundleRequest? body,
                HttpContext http,
                AgentBundleService bundles,
                CancellationToken cancellationToken) =>
            Principal(http) is { } principal
                ? Results.Ok(await bundles.CreateAsync(body ?? new CreateAgentBundleRequest(null, null), principal, cancellationToken))
                : Unauthenticated())
            .WithName("createAgentBundle")
            .RequireAgentPermission(AgentPermissions.DiagnosticsBundles)
            .RequireAgentMode(AgentMode.Diagnostics)
            .Produces<AgentBundleView>();

        agent.MapGet("/diagnostics/bundles/{id}", (string id, AgentBundleService bundles) =>
            bundles.Get(id) is { } bundle ? Results.Ok(bundle) : NotFound(id))
            .WithName("getAgentBundle")
            .RequireAgentPermission(AgentPermissions.DiagnosticsBundles)
            .RequireAgentMode(AgentMode.Diagnostics)
            .Produces<AgentBundleView>();

        agent.MapGet("/diagnostics/bundles/{id}/download", (string id, AgentBundleService bundles) =>
        {
            if (bundles.Get(id) is null || bundles.PathOf(id) is not { } path)
            {
                return NotFound(id);
            }

            return Results.File(path, "application/zip", $"gamewarden-diagnostics-{id}.zip");
        })
            .WithName("downloadAgentBundle")
            .RequireAgentPermission(AgentPermissions.DiagnosticsBundles)
            .RequireAgentMode(AgentMode.Diagnostics);
    }

    private static AgentPrincipal? Principal(HttpContext http) =>
        http.Items[AgentPrincipal.ItemKey] as AgentPrincipal;

    private static IResult Unauthenticated() =>
        ApiProblems.Create(StatusCodes.Status401Unauthorized, "unauthenticated", "An Agent token is required.");

    private static IResult NotFound(string id) =>
        ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", $"No '{id}'.");

    private static AgentMeView Me(AgentPrincipal principal, DateTimeOffset now) => new(
        principal.ClientId,
        principal.ClientName,
        principal.TrustedFull,
        [.. principal.Permissions.Order(StringComparer.Ordinal)],
        principal.TokenId,
        principal.TokenExpiresAt,
        new AgentModeWindow(principal.DiagnosticsLive, principal.DiagnosticsExpiresAt, null),
        new AgentModeWindow(principal.AdminLive, principal.AdminExpiresAt, null),
        "1",
        now);

    private static async Task<IResult> Command(
        string id,
        Supervisor supervisor,
        Func<InstanceActor, Task<CommandResult>> command,
        ActivityLog activity,
        HttpContext http,
        string message)
    {
        if (supervisor.Find(id) is not { } actor)
        {
            return NotFound(id);
        }

        var result = await command(actor);
        if (result.Outcome == CommandOutcome.Rejected)
        {
            return ApiProblems.Create(StatusCodes.Status409Conflict, result.Code ?? ProblemCodes.InvalidState, result.Message);
        }

        activity.Add("command", "info", message, id, user: http.User.Identity?.Name);
        var response = new AgentCommandResponse(result.Message, AgentInstanceView.From(result.Instance));
        return result.Outcome == CommandOutcome.Accepted
            ? Results.Accepted($"/api/v1/agent/instances/{id}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AgentRequestException ex)
        {
            return ApiProblems.Create(ex.Status, ex.Code, ex.Message);
        }
    }
}
