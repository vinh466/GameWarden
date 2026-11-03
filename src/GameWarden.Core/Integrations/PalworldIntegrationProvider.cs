using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GameWarden.Core.Integrations;

/// <summary>
/// The Palworld 1.0 REST API (docs/16): info, metrics and players for the tab; announce, save, kick, ban and unban
/// for the actions. The admin password comes from the instance secret and never reaches the panel.
/// </summary>
public sealed class PalworldIntegrationProvider : IGameIntegrationProvider
{
    public const string ProviderId = "palworld-rest";

    public string Id => ProviderId;

    public IReadOnlyList<string> Capabilities =>
    [
        "overview", "players", "announce", "save", "kick", "ban", "unban",
    ];

    public async Task<GameIntegrationData> ReadAsync(GameIntegrationContext context, CancellationToken cancellationToken)
    {
        var infoTask = GetAsync(context, "info", cancellationToken);
        var metricsTask = GetAsync(context, "metrics", cancellationToken);
        var playersTask = GetAsync(context, "players", cancellationToken);
        await Task.WhenAll(infoTask, metricsTask, playersTask);

        using var info = JsonDocument.Parse(infoTask.Result);
        using var metrics = JsonDocument.Parse(metricsTask.Result);
        using var players = JsonDocument.Parse(playersTask.Result);
        return new GameIntegrationData(ParseInfo(info.RootElement), ParseMetrics(metrics.RootElement), ParsePlayers(players.RootElement));
    }

    public async Task<GameIntegrationActionResult> ActAsync(
        GameIntegrationContext context,
        string action,
        GameIntegrationActionRequest request,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "announce":
                RequireMessage(request.Message);
                await PostAsync(context, "announce", new { message = request.Message }, cancellationToken);
                return new GameIntegrationActionResult(action, true, "Announcement sent.");
            case "save":
                await PostAsync(context, "save", null, cancellationToken);
                return new GameIntegrationActionResult(action, true, "World saved.");
            case "kick" or "ban":
                RequireUserId(request.UserId);
                await PostAsync(context, action, new { userid = request.UserId, message = request.Message }, cancellationToken);
                return new GameIntegrationActionResult(action, true, action == "kick" ? "Player kicked." : "Player banned.");
            case "unban":
                RequireUserId(request.UserId);
                await PostAsync(context, "unban", new { userid = request.UserId }, cancellationToken);
                return new GameIntegrationActionResult(action, true, "Player unbanned.");
            default:
                throw new GameIntegrationException(
                    StatusCodes.Status400BadRequest,
                    "unsupported_action",
                    $"The Palworld integration does not support '{action}'.");
        }
    }

    private static void RequireMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 512)
        {
            throw new GameIntegrationException(StatusCodes.Status400BadRequest, "validation_failed", "message is required and may be at most 512 characters.");
        }
    }

    private static void RequireUserId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
        {
            throw new GameIntegrationException(StatusCodes.Status400BadRequest, "validation_failed", "userId is required.");
        }
    }

    private static async Task<string> GetAsync(GameIntegrationContext context, string path, CancellationToken cancellationToken) =>
        await SendAsync(context, HttpMethod.Get, path, null, cancellationToken);

    private static async Task<string> PostAsync(GameIntegrationContext context, string path, object? body, CancellationToken cancellationToken) =>
        await SendAsync(context, HttpMethod.Post, path, body, cancellationToken);

    private static async Task<string> SendAsync(
        GameIntegrationContext context,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(context.TimeoutSec));
        using var request = new HttpRequestMessage(method, new Uri(context.BaseUri, "v1/api/" + path));
        if (context.Username is not null && context.Password is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{context.Username}:{context.Password}")));
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await context.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return text;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new GameIntegrationException(
                    StatusCodes.Status502BadGateway,
                    "integration_auth_failed",
                    "The game API refused the configured admin password.");
            }

            throw new GameIntegrationException(
                StatusCodes.Status502BadGateway,
                "integration_unavailable",
                $"The game API answered {(int)response.StatusCode}{(string.IsNullOrWhiteSpace(text) ? "." : $": {text[..Math.Min(text.Length, 200)]}")}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GameIntegrationException(StatusCodes.Status504GatewayTimeout, "integration_timeout", "The game API did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new GameIntegrationException(StatusCodes.Status502BadGateway, "integration_unavailable", $"The game API is not reachable: {ex.Message}");
        }
    }

    private static GameIntegrationInfo ParseInfo(JsonElement root) => new(
        Text(root, "version"),
        Text(root, "servername", "serverName"),
        Text(root, "description"),
        Text(root, "worldguid", "worldGuid"));

    private static GameIntegrationMetrics ParseMetrics(JsonElement root) => new(
        Int(root, "serverfps", "serverFps"),
        Int(root, "currentplayernum", "currentPlayerNum"),
        Double(root, "serverframetime", "serverFrameTime"),
        Int(root, "maxplayernum", "maxPlayerNum"),
        Long(root, "uptime"),
        Int(root, "basecampnum", "baseCampNum"),
        Int(root, "days"));

    private static IReadOnlyList<GameIntegrationPlayer> ParsePlayers(JsonElement root)
    {
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("players", out var players)
                ? players
                : default;
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. array.EnumerateArray().Select(player => new GameIntegrationPlayer(
                Text(player, "name"),
                Text(player, "accountName", "accountname"),
                Text(player, "playerId", "playerid"),
                Text(player, "userId", "userid"),
                Text(player, "ip"),
                Double(player, "ping"),
                Double(player, "location_x", "locationX"),
                Double(player, "location_y", "locationY"),
                Int(player, "level"),
                Int(player, "building_count", "buildingCount"))),
        ];
    }

    private static string? Text(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? Int(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                {
                    return number;
                }
            }
        }

        return null;
    }

    private static long? Long(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
                {
                    return number;
                }
            }
        }

        return null;
    }

    private static double? Double(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String
                    && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                {
                    return number;
                }
            }
        }

        return null;
    }
}
