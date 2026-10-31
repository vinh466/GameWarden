using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;

namespace GameWarden.Mcp;

/// <summary>
/// The local bridge's only credential holder: a GameWarden Agent token from the environment or a 0600 file. The
/// token is never printed and never placed in a URL; every call goes to the scoped <c>/api/v1/agent</c> surface.
/// </summary>
public sealed class GameWardenApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public GameWardenApiClient()
    {
        var (url, token) = Load();
        _baseUrl = url.TrimEnd('/');
        _http = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl + "/"),
            Timeout = TimeSpan.FromMinutes(2),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("gamewarden-mcp/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public string BaseUrl => _baseUrl;

    public async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path.TrimStart('/'), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public Task<string> PostAsync(string path, object? body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, body, cancellationToken);

    public Task<string> PutAsync(string path, object? body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, path, body, cancellationToken);

    /// <summary>Downloads an authenticated binary (a diagnostics bundle) to a local path.</summary>
    public async Task DownloadAsync(string path, string destination, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path.TrimStart('/'), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new McpToolException(await ReadAsync(response, cancellationToken));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destination);
        await source.CopyToAsync(file, cancellationToken);
    }

    public async Task<string> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return string.IsNullOrWhiteSpace(text) ? "{}" : text;
        }

        var fallback = $"GameWarden {(int)response.StatusCode} {response.StatusCode}: {Truncate(text, 800)}";
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var detail = root.TryGetProperty("detail", out var detailElement) ? detailElement.GetString() : null;
            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    status = (int)response.StatusCode,
                    code,
                    title,
                    detail,
                    message = string.Join(": ", new[] { code, title, detail }.Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } message ? message : fallback,
                },
            });
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { error = new { status = (int)response.StatusCode, message = fallback } });
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    private static (string Url, string Token) Load()
    {
        var configPath = Path.Combine(Home(), ".config", "gamewarden", "agent.json");
        string? url = Environment.GetEnvironmentVariable("GAMEWARDEN_URL");
        string? tokenFile = Environment.GetEnvironmentVariable("GAMEWARDEN_TOKEN_FILE");
        string? token = Environment.GetEnvironmentVariable("GAMEWARDEN_API_TOKEN");

        if (File.Exists(configPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(configPath));
                var root = document.RootElement;
                url ??= root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                tokenFile ??= root.TryGetProperty("tokenFile", out var fileElement) ? fileElement.GetString() : null;
            }
            catch (JsonException ex)
            {
                throw new McpToolException($"{configPath} is not valid JSON: {ex.Message}");
            }
        }

        tokenFile ??= Path.Combine(Home(), ".config", "gamewarden", "agent.token");
        tokenFile = ExpandHome(tokenFile);
        if (token is null && File.Exists(tokenFile))
        {
            WarnIfReadableByOthers(tokenFile);
            token = File.ReadAllText(tokenFile).Trim();
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new McpToolException("GAMEWARDEN_URL is not set. Put it in ~/.config/gamewarden/agent.json or the environment.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new McpToolException($"No Agent token. Set GAMEWARDEN_API_TOKEN or write a token to {tokenFile}.");
        }

        if (!token.StartsWith("gw_agent_", StringComparison.Ordinal))
        {
            throw new McpToolException("The configured credential is not a GameWarden Agent token (it must start with gw_agent_).");
        }

        return (url, token);
    }

    private static void WarnIfReadableByOthers(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                Console.Error.WriteLine($"warning: {path} is readable by other users; run: chmod 600 {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Home(), path[2..]) : path;

    public void Dispose() => _http.Dispose();
}

/// <summary>A readable, actionable tool error; the SDK returns this text to the Agent instead of a stack trace.</summary>
public sealed class McpToolException(string message) : McpException(message);
