using System.Net.Http.Headers;
using System.Text;

namespace GameWarden.Core.Supervision;

public enum HttpProbeState
{
    Ready,
    Waiting,
    Problem,
}

/// <summary>A readiness answer: ready, still starting, or a problem that will not fix itself.</summary>
public sealed record HttpProbeResult(HttpProbeState State, string? Problem = null);

/// <summary>
/// The <c>http</c> readiness check (ADR-0022 decision 3): a GET with optional basic auth, ready on the expected
/// status. A connection error or a 5xx is transient while the game starts; a 401, 403 or other 4xx is final.
/// </summary>
public static class HttpProbe
{
    public static async Task<HttpProbeResult> CheckAsync(
        HttpClient client,
        string url,
        (string Username, string Password)? credentials,
        int status,
        CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (credentials is { } value)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{value.Username}:{value.Password}")));
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var code = (int)response.StatusCode;
            if (code == status)
            {
                return new HttpProbeResult(HttpProbeState.Ready);
            }

            return code switch
            {
                401 or 403 => new HttpProbeResult(HttpProbeState.Problem, $"the REST API refused the configured credentials ({code}); check the admin password"),
                >= 400 and < 500 => new HttpProbeResult(HttpProbeState.Problem, $"the readiness URL answered {code} {response.ReasonPhrase}"),
                _ => new HttpProbeResult(HttpProbeState.Waiting),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UriFormatException)
        {
            return new HttpProbeResult(HttpProbeState.Waiting);
        }
    }
}
