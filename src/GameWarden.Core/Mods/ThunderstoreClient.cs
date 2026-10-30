using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using GameWarden.Core.Hosting;

namespace GameWarden.Core.Mods;

/// <summary>Retries and limits of Thunderstore requests (docs/13 §4.5), shaped like <see cref="Jobs.SteamCmdPolicy"/>: tries and delays.</summary>
/// <param name="MaxRetryAfter">Longest <c>Retry-After</c> honoured after a 429; a longer one is cut to it.</param>
/// <param name="RequestTimeout">One try, the answer's body included (a zip download); a try that takes longer is a timeout.</param>
public sealed record ThunderstorePolicy(IReadOnlyList<TimeSpan> RetryDelays, TimeSpan MaxRetryAfter, TimeSpan RequestTimeout)
{
    public static ThunderstorePolicy Default { get; } =
        new([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)], TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5));

    public int Attempts => RetryDelays.Count + 1;
}

/// <summary>A package's listing: its latest version, the deprecated flag and the review status in each community.</summary>
public sealed record ThunderstorePackage(string Package, bool IsDeprecated, ThunderstoreVersion Latest, IReadOnlyList<ThunderstoreListing> Listings);

public sealed record ThunderstoreListing(string Community, string? ReviewStatus);

/// <summary>One version: its exact dependencies (<c>Namespace-Name-x.y.z</c>) and its download address.</summary>
public sealed record ThunderstoreVersion(string Package, string Version, IReadOnlyList<string> Dependencies, Uri DownloadUrl);

/// <summary>A downloaded zip: Thunderstore publishes no hash, so this is what the download hashed to.</summary>
public sealed record ThunderstoreDownload(string Sha256, long Size);

/// <summary>Thunderstore did not give what was asked; <see cref="NotFound"/> for a 404 (an unknown package or version).</summary>
public sealed class ThunderstoreException(string message, bool notFound = false, Exception? inner = null) : Exception(message, inner)
{
    public bool NotFound { get; } = notFound;
}

/// <summary>
/// Thunderstore's package API and downloads (docs/13 §3), the only way Core reaches Thunderstore: tests swap the named
/// <c>HttpClient</c>'s handler for recorded answers. Field names were checked against the live API on 2026-10-30; the
/// experimental endpoints are not documented as stable (ADR-0020). Timeouts, 429 (with <c>Retry-After</c>) and 5xx are
/// tried again per <see cref="ThunderstorePolicy"/>; a 404 is a clear "not found".
/// </summary>
public sealed class ThunderstoreClient(IHttpClientFactory http, ThunderstorePolicy policy, TimeProvider time, ILogger<ThunderstoreClient> logger)
{
    public const string HttpClientName = "thunderstore";

    public const string ProjectUrl = "https://github.com/vinh466/GameWarden";

    private static readonly Uri BaseUrl = new("https://thunderstore.io/");

    /// <summary><c>GET /api/experimental/package/{ns}/{name}/</c>: latest version and flags.</summary>
    public async Task<ThunderstorePackage> GetPackageAsync(string package, CancellationToken cancellationToken)
    {
        var (ns, name) = Split(package);
        return await SendAsync(
            new Uri(BaseUrl, $"api/experimental/package/{ns}/{name}/"),
            package,
            async (response, token) =>
            {
                using var json = await ReadJsonAsync(response, token);
                var root = json.RootElement;
                var listings = root.TryGetProperty("community_listings", out var list) && list.ValueKind == JsonValueKind.Array
                    ? [.. list.EnumerateArray().Select(listing => new ThunderstoreListing(
                        listing.GetProperty("community").GetString() ?? string.Empty,
                        listing.TryGetProperty("review_status", out var status) ? status.GetString() : null))]
                    : (IReadOnlyList<ThunderstoreListing>)[];
                return new ThunderstorePackage(
                    package,
                    root.TryGetProperty("is_deprecated", out var deprecated) && deprecated.ValueKind == JsonValueKind.True,
                    ReadVersion(root.GetProperty("latest"), package),
                    listings);
            },
            cancellationToken);
    }

    /// <summary><c>GET /api/experimental/package/{ns}/{name}/{version}/</c>: dependencies and download address.</summary>
    public async Task<ThunderstoreVersion> GetVersionAsync(string package, string version, CancellationToken cancellationToken)
    {
        var (ns, name) = Split(package);
        return await SendAsync(
            new Uri(BaseUrl, $"api/experimental/package/{ns}/{name}/{version}/"),
            $"{package} {version}",
            async (response, token) =>
            {
                using var json = await ReadJsonAsync(response, token);
                return ReadVersion(json.RootElement, package);
            },
            cancellationToken);
    }

    /// <summary>
    /// Downloads <c>/package/download/{ns}/{name}/{version}/</c> into <paramref name="targetFile"/> (a folder only Core
    /// writes), hashing it on the way. A retry starts the file again.
    /// </summary>
    public async Task<ThunderstoreDownload> DownloadAsync(string package, string version, string targetFile, CancellationToken cancellationToken)
    {
        var (ns, name) = Split(package);
        return await SendAsync(
            new Uri(BaseUrl, $"package/download/{ns}/{name}/{version}/"),
            $"{package} {version}",
            async (response, token) =>
            {
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var target = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long size = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    size += read;
                }

                return new ThunderstoreDownload(Convert.ToHexStringLower(hash.GetHashAndReset()), size);
            },
            cancellationToken);
    }

    /// <summary>
    /// How long to wait before try <paramref name="attempt"/> + 1: a 429's <c>Retry-After</c> up to the policy's cap,
    /// otherwise the policy's delay.
    /// </summary>
    public static TimeSpan RetryDelay(ThunderstorePolicy policy, int attempt, RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        var asked = retryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - now,
            _ => (TimeSpan?)null,
        };
        if (asked is { } wait)
        {
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait > policy.MaxRetryAfter ? policy.MaxRetryAfter : wait;
        }

        return policy.RetryDelays[Math.Min(attempt, policy.RetryDelays.Count) - 1];
    }

    private async Task<T> SendAsync<T>(Uri url, string what, Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            string failure;
            RetryConditionHeaderValue? retryAfter = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(policy.RequestTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GameWarden", CoreInfo.Version));
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue($"(+{ProjectUrl})"));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ThunderstoreException($"{what} was not found on Thunderstore.", notFound: true);
                }

                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    return await read(response, timeout.Token);
                }

                if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout) && status < 500)
                {
                    throw new ThunderstoreException($"Thunderstore answered {status.ToString(CultureInfo.InvariantCulture)} for {what}.");
                }

                failure = $"Thunderstore answered {status.ToString(CultureInfo.InvariantCulture)} for {what}";
                retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests ? response.Headers.RetryAfter : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                failure = ex is OperationCanceledException ? $"Thunderstore did not answer in time for {what}" : $"Thunderstore could not be reached for {what}: {ex.Message}";
                if (attempt >= policy.Attempts)
                {
                    throw new ThunderstoreException($"{failure} ({attempt.ToString(CultureInfo.InvariantCulture)} tries).", inner: ex);
                }
            }

            if (attempt >= policy.Attempts)
            {
                throw new ThunderstoreException($"{failure} ({attempt.ToString(CultureInfo.InvariantCulture)} tries).");
            }

            var delay = RetryDelay(policy, attempt, retryAfter, time.GetUtcNow());
            logger.LogWarning("{Failure}; trying again in {Delay}.", failure, delay);
            await Task.Delay(delay, time, cancellationToken);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new ThunderstoreException($"Thunderstore answered with JSON Core cannot read: {ex.Message}", inner: ex);
        }
    }

    private static ThunderstoreVersion ReadVersion(JsonElement version, string package)
    {
        try
        {
            var number = version.GetProperty("version_number").GetString();
            if (!Packages.IsVersion(number))
            {
                throw new ThunderstoreException($"Thunderstore gave {package} a version Core cannot read: '{number}'.");
            }

            return new ThunderstoreVersion(
                package,
                number!,
                [.. version.GetProperty("dependencies").EnumerateArray().Select(dependency => dependency.GetString() ?? string.Empty)],
                new Uri(version.GetProperty("download_url").GetString()!));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or UriFormatException or ArgumentNullException)
        {
            throw new ThunderstoreException($"Thunderstore's answer for {package} lacks a field Core needs: {ex.Message}", inner: ex);
        }
    }

    private static (string Namespace, string Name) Split(string package)
    {
        if (!Packages.IsPackage(package))
        {
            throw new ThunderstoreException($"'{package}' is not a Thunderstore package name (Namespace-Name).");
        }

        var dash = package.IndexOf('-', StringComparison.Ordinal);
        return (package[..dash], package[(dash + 1)..]);
    }
}
