using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameWarden.Contracts;

namespace GameWarden.Setup;

/// <summary>Setup talks to Core like the CLI does: loopback plus the CLI token (02 §8).</summary>
internal static class CoreApi
{
    /// <summary>Waits until Core answers with <paramref name="expected"/> as its version; returns null, or the last problem seen.</summary>
    public static async Task<string?> WaitForVersionAsync(InstallLayout layout, string expected, TimeSpan timeout, SetupLog log)
    {
        var deadline = DateTime.UtcNow + timeout;
        var wanted = SemanticVersion.Parse(expected);
        string problem = "Core did not answer.";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var http = Client(layout, TimeSpan.FromSeconds(10));
                var system = await http.GetFromJsonAsync<JsonElement>("/api/v1/system");
                var version = system.GetProperty("version").GetString();
                if (SemanticVersion.TryParse(version, out var running) && running == wanted)
                {
                    log.Info($"Core {version} is up.");
                    return null;
                }

                problem = $"Core reports version {version}, expected {expected}.";
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or JsonException or KeyNotFoundException)
            {
                problem = ex.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return problem;
    }

    /// <summary>Before uninstalling: Core stops the games, removes its firewall rules and the run-as user (08 §4).</summary>
    public static async Task<string?> UninstallCleanupAsync(InstallLayout layout, SetupLog log)
    {
        try
        {
            using var http = Client(layout, TimeSpan.FromSeconds(180));
            var response = await http.PostAsync("/api/v1/system/uninstall-cleanup", content: null);
            var body = await response.Content.ReadAsStringAsync();
            log.Info($"Core clean-up: {(int)response.StatusCode} {body}");
            return response.IsSuccessStatusCode ? null : $"Core answered {(int)response.StatusCode}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return ex.Message;
        }
    }

    private static HttpClient Client(InstallLayout layout, TimeSpan timeout)
    {
        var token = File.ReadAllText(InstallLayout.TokenPath).Trim(); // Core writes it once it has started
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{layout.PanelPort()}"), Timeout = timeout };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
