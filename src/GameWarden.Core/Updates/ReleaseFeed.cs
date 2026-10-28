using System.Net.Http.Headers;
using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Core.Hosting;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Updates;

public sealed record ReleaseInfo(SemanticVersion Version, string Tag, bool Prerelease, string Url, IReadOnlyDictionary<string, Uri> Assets);

/// <summary>
/// Releases on GitHub (10 §6). Unauthenticated REST API: 60 requests per hour per IP, far more than a daily check
/// needs. <c>releases/latest</c> never returns a pre-release, so release candidates come from the list.
/// </summary>
public sealed class ReleaseFeed(IHttpClientFactory http, IOptions<GameWardenOptions> options)
{
    /// <summary>The newest release, or the newest of releases and release candidates with <paramref name="prerelease"/>.</summary>
    public async Task<ReleaseInfo?> LatestAsync(bool prerelease, CancellationToken cancellationToken)
    {
        if (!prerelease)
        {
            using var latest = await GetAsync("releases/latest", cancellationToken);
            return latest is null ? null : Parse(latest.RootElement);
        }

        return (await ListAsync(cancellationToken)).OrderByDescending(release => release.Version).FirstOrDefault();
    }

    public async Task<ReleaseInfo?> FindAsync(string version, CancellationToken cancellationToken)
    {
        var wanted = SemanticVersion.Parse(version);
        return (await ListAsync(cancellationToken)).FirstOrDefault(release => release.Version == wanted);
    }

    private async Task<IReadOnlyList<ReleaseInfo>> ListAsync(CancellationToken cancellationToken)
    {
        using var list = await GetAsync("releases?per_page=30", cancellationToken);
        return list is null ? [] : [.. list.RootElement.EnumerateArray().Select(Parse).OfType<ReleaseInfo>()];
    }

    private async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var client = http.CreateClient("github");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/repos/{options.Value.UpdateRepository}/{path}"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GameWarden", CoreInfo.Version));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // no releases yet
        }

        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private static ReleaseInfo? Parse(JsonElement release)
    {
        var tag = release.GetProperty("tag_name").GetString();
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean() || !SemanticVersion.TryParse(tag, out var version))
        {
            return null;
        }

        var assets = release.GetProperty("assets").EnumerateArray().ToDictionary(
            asset => asset.GetProperty("name").GetString()!,
            asset => new Uri(asset.GetProperty("browser_download_url").GetString()!),
            StringComparer.Ordinal);
        return new ReleaseInfo(version, tag!, release.GetProperty("prerelease").GetBoolean(), release.GetProperty("html_url").GetString()!, assets);
    }
}
