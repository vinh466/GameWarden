using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using GameWarden.Core.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The Thunderstore client against recorded answers (docs/13 §4.12): fields, retries, 429 and 404.</summary>
[Trait("Category", "Unit")]
public sealed class ThunderstoreClientTests : IDisposable
{
    private static readonly ThunderstorePolicy NoDelays = new([TimeSpan.Zero, TimeSpan.Zero], TimeSpan.Zero, TimeSpan.FromSeconds(30));

    private readonly FakeThunderstore _thunderstore = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "thunderstore-" + Guid.NewGuid().ToString("N")[..10]);

    public ThunderstoreClientTests() => Directory.CreateDirectory(_folder);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ThunderstoreClient Client(ThunderstorePolicy? policy = null) =>
        new(new FakeHttpClientFactory(_thunderstore), policy ?? NoDelays, TimeProvider.System, NullLogger<ThunderstoreClient>.Instance);

    [Fact]
    public async Task A_package_gives_its_latest_version_flags_and_listings()
    {
        var package = await Client().GetPackageAsync(FakeThunderstore.Loader, Ct);

        Assert.Equal("5.4.2360", package.Latest.Version);
        Assert.False(package.IsDeprecated);
        Assert.Equal([new ThunderstoreListing("valheim", "unreviewed")], package.Listings);
        Assert.Equal(new Uri("https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2360/"), package.Latest.DownloadUrl);
        var (url, userAgent) = Assert.Single(_thunderstore.Requests);
        Assert.Equal("https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/", url);
        Assert.StartsWith("GameWarden/", userAgent, StringComparison.Ordinal);
        Assert.EndsWith("(+https://github.com/vinh466/GameWarden)", userAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_gives_its_exact_dependencies()
    {
        var version = await Client().GetVersionAsync("RandyKnapp-EpicLoot", "0.15.4", Ct);

        Assert.Equal(["ValheimModding-JsonDotNET-13.0.4", "ValheimModding-Jotunn-2.30.2", "denikson-BepInExPack_Valheim-5.4.2351"], version.Dependencies);
    }

    [Fact]
    public async Task An_unknown_version_is_a_clear_not_found()
    {
        var error = await Assert.ThrowsAsync<ThunderstoreException>(() => Client().GetVersionAsync("RandyKnapp-EpicLoot", "0.0.1", Ct));

        Assert.True(error.NotFound);
        Assert.Equal("RandyKnapp-EpicLoot 0.0.1 was not found on Thunderstore.", error.Message);
        Assert.Single(_thunderstore.Requests); // not tried again
    }

    [Fact]
    public async Task Server_errors_and_timeouts_are_tried_again()
    {
        var calls = 0;
        _thunderstore.Intercept = _ => ++calls switch
        {
            1 => throw new TaskCanceledException("timeout", new TimeoutException()),
            2 => new HttpResponseMessage(HttpStatusCode.BadGateway),
            _ => null,
        };

        var version = await Client().GetVersionAsync("ValheimModding-Jotunn", "2.30.2", Ct);

        Assert.Equal("2.30.2", version.Version);
        Assert.Equal(3, _thunderstore.Requests.Count);
    }

    [Fact]
    public async Task Too_many_requests_waits_and_tries_again_until_the_tries_run_out()
    {
        _thunderstore.Intercept = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        };

        var error = await Assert.ThrowsAsync<ThunderstoreException>(() => Client().GetPackageAsync("RandyKnapp-EpicLoot", Ct));

        Assert.Contains("Thunderstore answered 429 for RandyKnapp-EpicLoot (3 tries)", error.Message, StringComparison.Ordinal);
        Assert.False(error.NotFound);
        Assert.Equal(3, _thunderstore.Requests.Count);
    }

    [Fact]
    public async Task Other_client_errors_are_not_tried_again()
    {
        _thunderstore.Intercept = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var error = await Assert.ThrowsAsync<ThunderstoreException>(() => Client().GetPackageAsync("RandyKnapp-EpicLoot", Ct));

        Assert.Equal("Thunderstore answered 403 for RandyKnapp-EpicLoot.", error.Message);
        Assert.Single(_thunderstore.Requests);
    }

    [Fact]
    public void Retry_after_is_honoured_up_to_the_cap()
    {
        var policy = new ThunderstorePolicy([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)], TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5));
        var now = DateTimeOffset.Parse("2026-10-30T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(TimeSpan.FromSeconds(30), ThunderstoreClient.RetryDelay(policy, 1, new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)), now));
        Assert.Equal(TimeSpan.FromSeconds(60), ThunderstoreClient.RetryDelay(policy, 1, new RetryConditionHeaderValue(TimeSpan.FromHours(1)), now));
        Assert.Equal(TimeSpan.FromSeconds(10), ThunderstoreClient.RetryDelay(policy, 1, new RetryConditionHeaderValue(now.AddSeconds(10)), now));
        Assert.Equal(TimeSpan.Zero, ThunderstoreClient.RetryDelay(policy, 1, new RetryConditionHeaderValue(now.AddSeconds(-10)), now));
        Assert.Equal(TimeSpan.FromSeconds(5), ThunderstoreClient.RetryDelay(policy, 1, null, now));
        Assert.Equal(TimeSpan.FromSeconds(20), ThunderstoreClient.RetryDelay(policy, 2, null, now));
    }

    [Fact]
    public async Task A_download_is_hashed_and_measured_on_the_way()
    {
        var file = Path.Combine(_folder, "jotunn.zip");

        var download = await Client().DownloadAsync("ValheimModding-Jotunn", "2.30.2", file, Ct);

        var bytes = await File.ReadAllBytesAsync(file, Ct);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), download.Sha256);
        Assert.Equal(bytes.Length, download.Size);
        Assert.Equal("https://thunderstore.io/package/download/ValheimModding/Jotunn/2.30.2/", Assert.Single(_thunderstore.Requests).Url);
    }

    public void Dispose()
    {
        _thunderstore.Dispose();
        Directory.Delete(_folder, recursive: true);
    }
}
