using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameWarden.Core.Tests;

/// <summary>System settings and installed versions (docs/11 §2 "System", §13) on a fresh Core.</summary>
[Trait("Category", "Api")]
[Collection(CoreApiFactory.Collection)]
public sealed class SystemApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Network_settings_are_saved_to_gamewarden_json_and_apply_at_once()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);

        var before = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system/settings", Ct);
        var saved = await cli.PutAsJsonAsync("/api/v1/system/settings", new { panelAccessNetworks = new[] { "192.168.1.0/24", " " }, vpnRanges = new[] { "26.0.0.0/8" } }, Ct);
        var system = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct);
        var file = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(factory.DataRoot, "gamewarden.json"), Ct)).RootElement.GetProperty("GameWarden");
        var back = await cli.PutAsJsonAsync("/api/v1/system/settings", new { panelAccessNetworks = Array.Empty<string>(), vpnRanges = (string[]?)null }, Ct);

        Assert.True(before.GetProperty("panelAccessNetworksDefault").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var view = await saved.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(["192.168.1.0/24"], view.GetProperty("panelAccessNetworks").EnumerateArray().Select(e => e.GetString()));
        Assert.False(view.GetProperty("vpnRangesDefault").GetBoolean());
        Assert.Equal(["26.0.0.0/8"], system.GetProperty("vpnRanges").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("192.168.1.0/24", file.GetProperty("PanelAccessNetworks")[0].GetString());
        var defaults = await back.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(defaults.GetProperty("panelAccessNetworksDefault").GetBoolean());
        Assert.True(defaults.GetProperty("vpnRangesDefault").GetBoolean());
    }

    [Fact]
    public async Task Settings_refuse_bad_ranges_and_a_change_that_locks_the_browser_out()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);

        var bad = await cli.PutAsJsonAsync("/api/v1/system/settings", new { panelAccessNetworks = new[] { "not-a-range" } }, Ct);
        using var overVpn = new HttpRequestMessage(HttpMethod.Put, "/api/v1/system/settings")
        {
            Content = JsonContent.Create(new { panelAccessNetworks = new[] { "192.168.1.0/24" } }),
        };
        overVpn.Headers.Add(CoreApiFactory.LocalIpHeader, "26.12.34.56"); // the panel was opened at the VM's Radmin address
        var lockedOut = await cli.SendAsync(overVpn, Ct);

        await AssertProblemAsync(bad, HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(lockedOut, HttpStatusCode.Conflict, "would_lock_out");
        Assert.False(File.Exists(Path.Combine(factory.DataRoot, "gamewarden.json")));
    }

    [Fact]
    public async Task Switching_versions_needs_an_installed_gamewarden()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);

        var versions = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system/versions", Ct);
        var switched = await cli.PostAsJsonAsync("/api/v1/system/versions/switch", new { version = "0.1.0-rc.2" }, Ct);

        Assert.Empty(versions.GetProperty("installed").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, versions.GetProperty("previous").ValueKind);
        await AssertProblemAsync(switched, HttpStatusCode.Conflict, "invalid_state");
    }

    private static HttpClient Cli(CoreApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CliToken());
        return client;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
