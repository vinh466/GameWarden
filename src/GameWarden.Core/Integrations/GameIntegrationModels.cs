using System.Text.Json;

namespace GameWarden.Core.Integrations;

/// <summary>
/// A game API integration (docs/16): a provider id Core knows, a port of the adapter and optional basic-auth
/// templates. The provider is the only code that knows a game's REST shape.
/// </summary>
public interface IGameIntegrationProvider
{
    string Id { get; }

    IReadOnlyList<string> Capabilities { get; }

    Task<GameIntegrationData> ReadAsync(GameIntegrationContext context, CancellationToken cancellationToken);

    Task<GameIntegrationActionResult> ActAsync(
        GameIntegrationContext context,
        string action,
        GameIntegrationActionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Everything a provider needs for one request; the auth values are already rendered, never logged.</summary>
public sealed record GameIntegrationContext(
    string InstanceId,
    Uri BaseUri,
    HttpClient Http,
    string? Username,
    string? Password,
    int TimeoutSec);

public sealed record GameIntegrationData(
    GameIntegrationInfo? Info,
    GameIntegrationMetrics? Metrics,
    IReadOnlyList<GameIntegrationPlayer> Players);

public sealed record GameIntegrationView(
    string Provider,
    IReadOnlyDictionary<string, string>? Label,
    IReadOnlyList<string> Capabilities,
    bool Online,
    GameIntegrationInfo? Info,
    GameIntegrationMetrics? Metrics,
    IReadOnlyList<GameIntegrationPlayer> Players);

public sealed record GameIntegrationInfo(
    string? Version,
    string? ServerName,
    string? Description,
    string? WorldGuid);

public sealed record GameIntegrationMetrics(
    int? ServerFps,
    int? CurrentPlayerNum,
    double? ServerFrameTime,
    int? MaxPlayerNum,
    long? Uptime,
    int? BaseCampNum,
    int? Days);

public sealed record GameIntegrationPlayer(
    string? Name,
    string? AccountName,
    string? PlayerId,
    string? UserId,
    string? Ip,
    double? Ping,
    double? X,
    double? Y,
    int? Level,
    int? BuildingCount);

/// <summary>The body of an integration action. Providers ignore fields they do not use.</summary>
public sealed record GameIntegrationActionRequest(
    string? Message = null,
    string? UserId = null,
    int? WaitTime = null);

public sealed record GameIntegrationActionResult(string Action, bool Ok, string? Message);

public sealed class GameIntegrationException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;
}
