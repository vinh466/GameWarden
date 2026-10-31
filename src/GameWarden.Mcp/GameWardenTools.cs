using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace GameWarden.Mcp;

/// <summary>
/// The curated Agent tool surface (docs/15). Reads first; the only state-changing tools are the explicitly named
/// control/install/console calls. The server-side scope and mode still decide whether each call is allowed.
/// </summary>
[McpServerToolType]
public static class GameWardenTools
{
    [McpServerTool, Description("Advanced: GET a path under /api/v1/agent. Prefer the named tools; use this for a newly added read endpoint until a dedicated tool exists. Scopes and modes are still enforced by Core.")]
    public static Task<string> gw_agent_get(
        GameWardenApiClient client,
        [Description("Path under /api/v1/agent, for example /status or /diagnostics/system")] string path,
        CancellationToken cancellationToken = default)
    {
        var normalized = path.StartsWith('/') ? path : "/" + path;
        if (normalized.StartsWith("/api/v1/agent", StringComparison.Ordinal))
        {
            // Already absolute.
        }
        else
        {
            normalized = "/api/v1/agent" + normalized;
        }

        return client.GetAsync(normalized, cancellationToken);
    }

    [McpServerTool, Description("Read the compact GameWarden health summary: Core, this Agent, instances and problems. Call this first for any diagnostic task.")]
    public static Task<string> gw_status(GameWardenApiClient client, CancellationToken cancellationToken) =>
        client.GetAsync("/api/v1/agent/status", cancellationToken);

    [McpServerTool, Description("Read this Agent's identity, permissions, token expiry and diagnostics/admin windows.")]
    public static Task<string> gw_me(GameWardenApiClient client, CancellationToken cancellationToken) =>
        client.GetAsync("/api/v1/agent/me", cancellationToken);

    [McpServerTool, Description("List game server instances with state, desired state, ports and last problem.")]
    public static Task<string> gw_list_instances(GameWardenApiClient client, CancellationToken cancellationToken) =>
        client.GetAsync("/api/v1/agent/instances", cancellationToken);

    [McpServerTool, Description("Read one game server instance.")]
    public static Task<string> gw_get_instance(
        GameWardenApiClient client,
        [Description("Instance id, for example valheim-main")] string instanceId,
        CancellationToken cancellationToken) =>
        client.GetAsync($"/api/v1/agent/instances/{Escape(instanceId)}", cancellationToken);

    [McpServerTool, Description("Read the tail of an instance console. Use small tails (50-200) and filter rather than reading everything.")]
    public static Task<string> gw_get_console(
        GameWardenApiClient client,
        [Description("Instance id")] string instanceId,
        [Description("Number of trailing lines, 1-1000; default 200")] int? tail = null,
        CancellationToken cancellationToken = default) =>
        client.GetAsync($"/api/v1/agent/instances/{Escape(instanceId)}/console?tail={Math.Clamp(tail ?? 200, 1, 1000)}", cancellationToken);

    [McpServerTool, Description("Read instance settings; secrets are returned only as { set: true }.")]
    public static Task<string> gw_get_settings(
        GameWardenApiClient client,
        [Description("Instance id")] string instanceId,
        CancellationToken cancellationToken) =>
        client.GetAsync($"/api/v1/agent/instances/{Escape(instanceId)}/settings", cancellationToken);

    [McpServerTool, Description("Tail GameWarden logs. source=core|job|instance|setup; core and setup need diagnostics mode. Prefer a small tail and a literal 'contains' filter.")]
    public static Task<string> gw_tail_logs(
        GameWardenApiClient client,
        [Description("core, job, instance or setup")] string source,
        [Description("Instance id for source=instance, job id for source=job; ignored otherwise")] string? id = null,
        [Description("Trailing lines, 1-1000; default 100")] int? tail = null,
        [Description("Optional level filter: trace, debug, info, warn, error, critical")] string? level = null,
        [Description("Optional literal text filter, case-insensitive")] string? contains = null,
        CancellationToken cancellationToken = default) =>
        client.GetAsync("/api/v1/agent/logs" + Query(
            ("source", source),
            ("id", id),
            ("tail", tail),
            ("level", level),
            ("contains", contains)), cancellationToken);

    [McpServerTool, Description("List recent jobs, optionally for one instance and/or state (queued, running, succeeded, failed).")]
    public static Task<string> gw_get_jobs(
        GameWardenApiClient client,
        [Description("Optional instance id")] string? instanceId = null,
        [Description("Optional state")] string? state = null,
        CancellationToken cancellationToken = default) =>
        client.GetAsync("/api/v1/agent/jobs" + Query(("instanceId", instanceId), ("state", state)), cancellationToken);

    [McpServerTool, Description("Read the end of a job log.")]
    public static Task<string> gw_get_job_log(
        GameWardenApiClient client,
        string jobId,
        [Description("Trailing lines, 1-1000; default 200")] int? tail = null,
        CancellationToken cancellationToken = default) =>
        client.GetAsync($"/api/v1/agent/jobs/{Escape(jobId)}/log?tail={Math.Clamp(tail ?? 200, 1, 1000)}", cancellationToken);

    [McpServerTool, Description("Read the activity feed, newest first.")]
    public static Task<string> gw_get_activity(
        GameWardenApiClient client,
        [Description("Optional instance id")] string? instanceId = null,
        [Description("Maximum items, 1-200; default 50")] int? limit = null,
        CancellationToken cancellationToken = default) =>
        client.GetAsync("/api/v1/agent/activity" + Query(("instanceId", instanceId), ("limit", limit)), cancellationToken);

    [McpServerTool, Description("Read CPU and RAM samples for the VM and running servers.")]
    public static Task<string> gw_get_metrics(GameWardenApiClient client, CancellationToken cancellationToken) =>
        client.GetAsync("/api/v1/agent/metrics", cancellationToken);

    [McpServerTool, Description("Check GitHub Releases for a newer GameWarden. Read-only; requires agent.system.update but no admin window.")]
    public static Task<string> gw_check_update(
        GameWardenApiClient client,
        [Description("Include release candidates; default false")] bool? prerelease = null,
        CancellationToken cancellationToken = default) =>
        client.GetAsync("/api/v1/agent/system/update" + Query(("prerelease", prerelease)), cancellationToken);

    [McpServerTool, Description("Update GameWarden itself. Destructive: needs agent.system.update and a live admin window. Core restarts; poll gw_status afterwards and read the update result.")]
    public static Task<string> gw_update_gamewarden(
        GameWardenApiClient client,
        [Description("Include release candidates; default false")] bool? prerelease = null,
        [Description("Exact version to install, when known")] string? version = null,
        CancellationToken cancellationToken = default) =>
        client.PostAsync("/api/v1/agent/system/update", new { prerelease = prerelease == true, version }, cancellationToken);

    [McpServerTool, Description("Read deep host diagnostics. section=system|processes|storage|network|events. Diagnostics mode must be on.")]
    public static Task<string> gw_get_diagnostics(
        GameWardenApiClient client,
        [Description("system, processes, storage, network or events")] string section,
        [Description("For processes, 1-300; for events, 1-300; default 100")] int? limit = null,
        [Description("For events: Application or System; default Application")] string? log = null,
        [Description("For events: minimum level (critical, error, warning, information)")] string? level = null,
        [Description("For events: hours back, 1-168; default 24")] int? hours = null,
        CancellationToken cancellationToken = default) =>
        section switch
        {
            "system" => client.GetAsync("/api/v1/agent/diagnostics/system", cancellationToken),
            "processes" => client.GetAsync($"/api/v1/agent/diagnostics/processes?limit={Math.Clamp(limit ?? 100, 1, 300)}", cancellationToken),
            "storage" => client.GetAsync("/api/v1/agent/diagnostics/storage", cancellationToken),
            "network" => client.GetAsync("/api/v1/agent/diagnostics/network", cancellationToken),
            "events" => client.GetAsync("/api/v1/agent/diagnostics/events" + Query(
                ("log", log),
                ("level", level),
                ("hours", hours),
                ("limit", limit)), cancellationToken),
            _ => Task.FromResult(Error("section must be system, processes, storage, network or events.")),
        };

    [McpServerTool, Description("Create a redacted diagnostics bundle and download it to the agent host. Use this when live endpoints are not enough; diagnostics mode must be on. Returns a local path for unzip/rg/jq.")]
    public static async Task<string> gw_collect_bundle(
        GameWardenApiClient client,
        [Description("Optional instance ids; empty means every instance")] string[]? instanceIds = null,
        [Description("How many hours of logs/events, 1-168; default 24")] int? sinceHours = null,
        CancellationToken cancellationToken = default)
    {
        var created = await client.PostAsync(
            "/api/v1/agent/diagnostics/bundles",
            new { instanceIds, sinceHours },
            cancellationToken);
        using var document = JsonDocument.Parse(created);
        if (document.RootElement.TryGetProperty("error", out _))
        {
            return created;
        }

        var id = document.RootElement.GetProperty("id").GetString()!;
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "gamewarden",
            "diagnostics");
        var path = Path.Combine(folder, id + ".zip");
        await client.DownloadAsync($"/api/v1/agent/diagnostics/bundles/{Uri.EscapeDataString(id)}/download", path, cancellationToken);
        var info = new FileInfo(path);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));
        return JsonSerializer.Serialize(new
        {
            bundleId = id,
            path,
            sizeBytes = info.Length,
            sha256 = hash,
            expiresAt = document.RootElement.TryGetProperty("expiresAt", out var expires) ? expires.GetString() : null,
        });
    }

    [McpServerTool, Description("Start, stop or restart a game server. State-changing: verify with gw_get_instance afterwards.")]
    public static Task<string> gw_control_instance(
        GameWardenApiClient client,
        [Description("Instance id")] string instanceId,
        [Description("start, stop or restart")] string action,
        [Description("Skip the resource check (Owner-only force); default false")] bool? force = null,
        CancellationToken cancellationToken = default)
    {
        if (action is not ("start" or "stop" or "restart"))
        {
            return Task.FromResult(Error("action must be start, stop or restart."));
        }

        return client.PostAsync(
            $"/api/v1/agent/instances/{Escape(instanceId)}/{action}" + (force == true ? "?force=true" : string.Empty),
            null,
            cancellationToken);
    }

    [McpServerTool, Description("Install or update a game server with SteamCMD. Returns a job id; use gw_get_job_log / gw_wait_job.")]
    public static Task<string> gw_install_instance(
        GameWardenApiClient client,
        [Description("Instance id")] string instanceId,
        [Description("install or update")] string action,
        CancellationToken cancellationToken)
    {
        if (action is not ("install" or "update"))
        {
            return Task.FromResult(Error("action must be install or update."));
        }

        return client.PostAsync($"/api/v1/agent/instances/{Escape(instanceId)}/{action}", null, cancellationToken);
    }

    [McpServerTool, Description("Send an allow-listed console command to a game server. State-changing.")]
    public static Task<string> gw_send_console(
        GameWardenApiClient client,
        [Description("Instance id")] string instanceId,
        [Description("Command text; Core enforces the adapter's allow-list")] string command,
        CancellationToken cancellationToken) =>
        client.PostAsync($"/api/v1/agent/instances/{Escape(instanceId)}/console", new { command }, cancellationToken);

    [McpServerTool, Description("Poll a job until it finishes or the timeout passes. Read-only.")]
    public static async Task<string> gw_wait_job(
        GameWardenApiClient client,
        string jobId,
        [Description("Timeout in seconds, 1-120; default 60")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = Math.Clamp(timeoutSeconds ?? 60, 1, 120);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeout);
        string last = "{}";
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await client.GetAsync($"/api/v1/agent/jobs/{Escape(jobId)}", cancellationToken);
            using var document = JsonDocument.Parse(last);
            var state = document.RootElement.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
            if (state is not ("queued" or "running"))
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return $"{{\"timedOut\":true,\"job\":{last}}}";
    }

    private static string Query(params (string Key, object? Value)[] values)
    {
        var parts = values
            .Where(pair => pair.Value is not null && pair.Value.ToString() is { Length: > 0 })
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!.ToString()!)}")
            .ToArray();
        return parts.Length == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { error = new { message } });
}
