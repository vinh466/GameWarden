using System.Net.Http.Headers;
using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Core.Hosting;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Updates;

/// <summary>A release and the notes shown before an update. For the development line every release is a pre-release.</summary>
public sealed record ReleaseInfo(
    SemanticVersion Version,
    string Tag,
    bool Prerelease,
    string Url,
    string? Notes,
    DateTimeOffset? PublishedAt,
    IReadOnlyDictionary<string, Uri> Assets);

/// <summary>
/// Releases on GitHub (10 §6). Unauthenticated REST API: 60 requests per hour per IP, far more than a daily check
/// needs. The development line publishes every build as a pre-release, so the list is the source of truth.
/// </summary>
public sealed class ReleaseFeed(IHttpClientFactory http, IOptions<GameWardenOptions> options)
{
    /// <summary>The newest release of any kind: during development every release is a pre-release.</summary>
    public async Task<ReleaseInfo?> LatestAsync(CancellationToken cancellationToken)
    {
        return (await ListAsync(cancellationToken)).OrderByDescending(release => release.Version).FirstOrDefault();
    }

    /// <summary>One release by version; uses the tag endpoint so old releases do not fall off a list page.</summary>
    public async Task<ReleaseInfo?> FindAsync(string version, CancellationToken cancellationToken)
    {
        if (!SemanticVersion.TryParse(version, out var wanted))
        {
            return null;
        }

        foreach (var tag in new[] { $"v{wanted}", wanted.ToString() })
        {
            using var release = await GetAsync($"releases/tags/{Uri.EscapeDataString(tag)}", cancellationToken);
            if (release is not null)
            {
                return Parse(release.RootElement);
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<ReleaseInfo>> ListAsync(CancellationToken cancellationToken)
    {
        using var list = await GetAsync("releases?per_page=100", cancellationToken);
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
            return null; // no releases yet, or a version that was removed
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
        var notes = release.TryGetProperty("body", out var body) ? body.GetString() : null;
        var published = release.TryGetProperty("published_at", out var publishedAt)
            && publishedAt.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(publishedAt.GetString(), out var at)
                ? at
                : (DateTimeOffset?)null;
        return new ReleaseInfo(version, tag!, release.GetProperty("prerelease").GetBoolean(), release.GetProperty("html_url").GetString()!, notes, published, assets);
    }
}
