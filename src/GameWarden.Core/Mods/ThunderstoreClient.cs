using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>
/// Thunderstore did not give what was asked; <see cref="NotFound"/> for a 404 (an unknown package, version or code),
/// <see cref="RateLimited"/> when it still answered 429 after the policy's tries.
/// </summary>
public sealed class ThunderstoreException(string message, bool notFound = false, Exception? inner = null, bool rateLimited = false) : Exception(message, inner)
{
    public bool NotFound { get; } = notFound;

    public bool RateLimited { get; } = rateLimited;
}

/// <summary>
/// Thunderstore's package API, downloads and r2modman's profile codes (docs/13 §3), the only way Core reaches Thunderstore: tests swap the named
/// <c>HttpClient</c>'s handler for recorded answers. Field names were checked against the live API on 2026-10-30; the
/// experimental endpoints are not documented as stable (ADR-0020). Timeouts, 429 (with <c>Retry-After</c>) and 5xx are
/// tried again per <see cref="ThunderstorePolicy"/>; a 404 is a clear "not found".
/// </summary>
public sealed class ThunderstoreClient(IHttpClientFactory http, ThunderstorePolicy policy, TimeProvider time, ILogger<ThunderstoreClient> logger)
{
    public const string HttpClientName = "thunderstore";

    public const string ProjectUrl = "https://github.com/vinh466/GameWarden";

    private static readonly Uri BaseUrl = new("https://thunderstore.io/");

    /// <summary>The longest profile text Core reads: the base64 of a code's largest zip, with room for its prefix and line breaks.</summary>
    private const long MaxProfileTextBytes = (R2ModmanCode.MaxZipBytes + 2) / 3 * 4 + 64 * 1024;

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
    /// <c>GET /api/experimental/legacyprofile/get/{key}/</c>: the text r2modman stored for a profile code (docs/13 §3),
    /// <c>#r2modman</c> and a newline, then the base64 of a zip. Thunderstore answers with a redirect to its CDN, which
    /// the client follows. A 404 is a code that is unknown or expired.
    /// </summary>
    /// <param name="code">A code as <see cref="R2ModmanCode.TryNormalizeCode"/> accepts it.</param>
    public async Task<string> GetProfileAsync(string code, CancellationToken cancellationToken)
    {
        if (!R2ModmanCode.TryNormalizeCode(code, out var key))
        {
            throw new ThunderstoreException($"'{code}' is not an r2modman profile code.");
        }

        try
        {
            return await SendAsync(
                new Uri(BaseUrl, $"api/experimental/legacyprofile/get/{key}/"),
                $"the profile code {key}",
                async (response, token) =>
                {
                    if (response.Content.Headers.ContentLength > MaxProfileTextBytes)
                    {
                        throw TooLarge(key);
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    using var memory = new MemoryStream();
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, token)) > 0)
                    {
                        if (memory.Length + read > MaxProfileTextBytes)
                        {
                            throw TooLarge(key);
                        }

                        memory.Write(buffer, 0, read);
                    }

                    return Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
                },
                cancellationToken);
        }
        catch (ThunderstoreException ex) when (ex.NotFound)
        {
            throw new ThunderstoreException($"The code {key} is unknown or expired; ask for a new code (r2modman: Export profile as code).", notFound: true, inner: ex);
        }

        static ThunderstoreException TooLarge(string key) =>
            new($"The profile of code {key} is larger than a code can be ({(R2ModmanCode.MaxZipBytes / 1_000_000).ToString(CultureInfo.InvariantCulture)} MB).");
    }

    /// <summary>
    /// <c>POST /api/experimental/legacyprofile/create/</c> with the profile text as <c>application/octet-stream</c>, as
    /// r2modman sends it; the answer's <c>key</c> is the code. Thunderstore keys a stored profile by the hash of its
    /// content, so a try again after a timeout gives the same code. It allows six new profiles a minute (429 beyond).
    /// </summary>
    public async Task<string> CreateProfileAsync(string profileText, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(profileText);
        return await SendAsync(
            new Uri(BaseUrl, "api/experimental/legacyprofile/create/"),
            "a new profile code",
            async (response, token) =>
            {
                using var json = await ReadJsonAsync(response, token);
                var root = json.RootElement;
                var key = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("key", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
                return R2ModmanCode.TryNormalizeCode(key, out var code)
                    ? code
                    : throw new ThunderstoreException($"Thunderstore answered the new profile with a key Core cannot read: {root.GetRawText()}");
            },
            cancellationToken,
            HttpMethod.Post,
            () =>
            {
                var content = new ByteArrayContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return content;
            });
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

    /// <param name="content">Makes the body of each try: a request's content is not sent twice.</param>
    private async Task<T> SendAsync<T>(
        Uri url,
        string what,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken,
        HttpMethod? method = null,
        Func<HttpContent>? content = null)
    {
        var client = http.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            string failure;
            RetryConditionHeaderValue? retryAfter = null;
            var rateLimited = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(policy.RequestTimeout);
                using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url) { Content = content?.Invoke() };
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
                rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests;
                retryAfter = rateLimited ? response.Headers.RetryAfter : null;
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
                var tries = attempt.ToString(CultureInfo.InvariantCulture);
                throw rateLimited
                    ? new ThunderstoreException($"{failure} ({tries} tries): Thunderstore limits how often it is asked; wait a minute and try again.", rateLimited: true)
                    : new ThunderstoreException($"{failure} ({tries} tries).");
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
