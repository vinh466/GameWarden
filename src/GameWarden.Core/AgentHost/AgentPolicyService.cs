using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.AgentHost;

public sealed record AgentPolicyDecision(
    bool Allowed,
    bool RequiresApproval,
    string Rule,
    string Reason,
    string? Impact = null,
    int? MaxPerHour = null);

public sealed class AgentPolicyDocument
{
    public int Version { get; set; } = 1;

    /// <summary><c>allow</c> or <c>deny-writes</c>; the safe default is deny-writes.</summary>
    public string Default { get; set; } = "deny-writes";

    public List<AgentPolicyRule> Rules { get; set; } = [];
}

public sealed class AgentPolicyRule
{
    public string Id { get; set; } = string.Empty;

    public AgentPolicyMatch Match { get; set; } = new();

    /// <summary><c>allow</c>, <c>approval</c> or <c>deny</c>.</summary>
    public string Require { get; set; } = "approval";

    public int? MaxPerHour { get; set; }

    public string? Impact { get; set; }
}

public sealed class AgentPolicyMatch
{
    public string? Capability { get; set; }

    public string? Action { get; set; }

    public string? Instance { get; set; }
}

/// <summary>
/// Policy as data (docs/21 §2.6). The default is deny-writes; a write becomes a durable approval unless a rule
/// explicitly allows it. The decision is stored so the panel can answer "why was this allowed?".
/// </summary>
public sealed class AgentPolicyService(
    IDbContextFactory<GameWardenDb> db,
    IOptions<GameWardenOptions> options,
    AgentHostSettingsService settings,
    TimeProvider time,
    ILogger<AgentPolicyService> logger)
{
    public const string FileName = "agent-policy.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<AgentPolicyDecision> EvaluateAsync(
        AgentTool tool,
        JsonElement args,
        string? instanceId,
        string? turnId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var action = Text(args, "action");
        var decision = tool.Risk == AgentToolRisk.Read
            ? new AgentPolicyDecision(true, false, "read", "Read capabilities need no approval.")
            : Decide(tool, action, instanceId);

        if (tool.Risk == AgentToolRisk.Write && decision.Allowed && decision.RequiresApproval && decision.MaxPerHour is { } max)
        {
            var executed = await ExecutedLastHourAsync(instanceId, cancellationToken);
            if (executed >= max)
            {
                decision = new AgentPolicyDecision(
                    false,
                    false,
                    decision.Rule,
                    $"The policy allows {max} write(s) per hour for this instance; {executed} already ran.",
                    decision.Impact,
                    max);
            }
        }

        if (tool.Risk == AgentToolRisk.Write)
        {
            try
            {
                await using var context = await db.CreateDbContextAsync(cancellationToken);
                context.AgentPolicyDecisions.Add(new AgentPolicyDecisionRecord
                {
                    At = time.GetUtcNow().UtcDateTime,
                    TraceId = traceId,
                    TurnId = turnId,
                    Capability = tool.Name,
                    Action = action,
                    InstanceId = instanceId,
                    Rule = decision.Rule,
                    Decision = decision.Allowed ? (decision.RequiresApproval ? "approval" : "allow") : "deny",
                    Reason = decision.Reason,
                    InputHash = Hash(args.GetRawText()),
                });
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not store the policy decision for {Tool}.", tool.Name);
            }
        }

        return decision;
    }

    private AgentPolicyDecision Decide(AgentTool tool, string? action, string? instanceId)
    {
        var document = Read();
        foreach (var rule in document.Rules)
        {
            if (rule.Match is null || !Matches(rule.Match, tool.Name, action, instanceId))
            {
                continue;
            }

            var require = rule.Require.Trim().ToLowerInvariant();
            var impact = rule.Impact ?? Impact(tool.Name, action, instanceId);
            return require switch
            {
                "allow" => new AgentPolicyDecision(true, false, rule.Id, "Allowed by policy.", impact, rule.MaxPerHour),
                "deny" => new AgentPolicyDecision(false, false, rule.Id, "Denied by policy.", impact, rule.MaxPerHour),
                _ => new AgentPolicyDecision(true, true, rule.Id, "Policy requires approval.", impact, rule.MaxPerHour),
            };
        }

        if (document.Default.Equals("allow", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentPolicyDecision(true, false, "default-allow", "Allowed by the default policy.", Impact(tool.Name, action, instanceId));
        }

        return new AgentPolicyDecision(false, false, "default-deny", "Denied: no policy rule matches this write.");
    }

    private AgentPolicyDocument Read()
    {
        var path = Path.Combine(options.Value.DataRoot, FileName);
        if (!File.Exists(path))
        {
            return Defaults(settings.Get().Autonomy.MaxWritesPerHour);
        }

        try
        {
            var document = JsonSerializer.Deserialize<AgentPolicyDocument>(File.ReadAllText(path), Json)
                ?? Defaults(settings.Get().Autonomy.MaxWritesPerHour);
            document.Rules ??= [];
            if (string.IsNullOrWhiteSpace(document.Default))
            {
                document.Default = "deny-writes";
            }

            return document;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not read {File}: {Reason}; using the default policy.", FileName, ex.Message);
            return Defaults(settings.Get().Autonomy.MaxWritesPerHour);
        }
    }

    private static AgentPolicyDocument Defaults(int maxWrites) => new()
    {
        Default = "deny-writes",
        Rules =
        [
            new() { Id = "instance-restart", Match = new() { Capability = "control_instance", Action = "restart" }, Require = "approval", MaxPerHour = maxWrites },
            new() { Id = "instance-stop", Match = new() { Capability = "control_instance", Action = "stop" }, Require = "approval", MaxPerHour = Math.Max(1, maxWrites / 2) },
            new() { Id = "instance-start", Match = new() { Capability = "control_instance", Action = "start" }, Require = "approval", MaxPerHour = maxWrites },
            new() { Id = "console-send", Match = new() { Capability = "send_console" }, Require = "approval", MaxPerHour = Math.Max(1, maxWrites * 2) },
            new() { Id = "install-update", Match = new() { Capability = "install_instance" }, Require = "approval", MaxPerHour = Math.Max(1, maxWrites / 3) },
        ],
    };

    private static bool Matches(AgentPolicyMatch match, string capability, string? action, string? instanceId) =>
        (match.Capability is null || match.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase))
        && (match.Action is null || string.Equals(match.Action, action, StringComparison.OrdinalIgnoreCase))
        && (match.Instance is null || match.Instance == "*" || string.Equals(match.Instance, instanceId, StringComparison.OrdinalIgnoreCase));

    private async Task<int> ExecutedLastHourAsync(string? instanceId, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().UtcDateTime.AddHours(-1);
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentApprovals.CountAsync(
            row => row.DecidedAt >= since
                && (instanceId == null || row.InstanceId == instanceId)
                && (row.ExecutionState == "running" || row.ExecutionState == "succeeded"),
            cancellationToken);
    }

    private static string? Text(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Impact(string capability, string? action, string? instanceId)
    {
        var server = instanceId ?? "the server";
        return capability switch
        {
            "control_instance" when action == "restart" => $"Restart {server}: players disconnect, the world saves before the stop.",
            "control_instance" when action == "stop" => $"Stop {server}: players disconnect.",
            "control_instance" => $"Start {server}.",
            "send_console" => $"Send one allow-listed console command to {server}.",
            "install_instance" => $"Install or update the game files of {server}; this can take a long time.",
            _ => $"Run {capability} on {server}.",
        };
    }
}
