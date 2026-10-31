using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameWarden.Core.Tests;

/// <summary>The Agent feature end to end (docs/15): management, token auth, scope, modes, audit and the narrow surface.</summary>
[Trait("Category", "Api")]
[Collection(CoreApiFactory.Collection)]
public sealed class AgentApiTests
{
    private const string Status = "agent.status.read";
    private const string Instances = "agent.instances.read";
    private const string DiagnosticsSystem = "agent.diagnostics.system";
    private const string DiagnosticsStorage = "agent.diagnostics.storage";
    private const string DiagnosticsNetwork = "agent.diagnostics.network";
    private const string DiagnosticsEvents = "agent.diagnostics.events";
    private const string DiagnosticsBundles = "agent.diagnostics.bundles";
    private const string Logs = "agent.logs.read";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_token_is_limited_to_the_agent_surface_and_its_permissions()
    {
        using var factory = new CoreApiFactory();
        var (clientId, token) = await CreateAgentAsync(factory, [Status, Instances]);
        using var agent = AgentClient(factory, token);

        var me = await agent.GetAsync("/api/v1/agent/me", Ct);
        var status = await agent.GetAsync("/api/v1/agent/status", Ct);
        var instances = await agent.GetAsync("/api/v1/agent/instances", Ct);
        var panelApi = await agent.GetAsync("/api/v1/instances", Ct);
        var management = await agent.GetAsync("/api/v1/agents", Ct);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("test-agent", (await me.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("clientName").GetString());
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK, instances.StatusCode);
        await AssertProblemAsync(panelApi, HttpStatusCode.Forbidden, "forbidden");
        await AssertProblemAsync(management, HttpStatusCode.Forbidden, "forbidden");
        Assert.StartsWith("agt_", clientId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_owner_session_manages_agents_and_still_needs_csrf()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);
        var setupUrl = (await cli.GetFromJsonAsync<JsonElement>("/api/v1/setup/url", Ct)).GetProperty("url").GetString()!;
        var setupToken = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(setupUrl).Query)["token"].ToString();
        using var owner = factory.CreateClient();
        var created = await owner.PostAsJsonAsync(
            "/api/v1/setup/owner",
            new { setupToken, userName = "owner", password = "correct horse battery" },
            Ct);
        created.EnsureSuccessStatusCode();
        var csrf = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("csrfToken").GetString()!;

        var list = await owner.GetAsync("/api/v1/agents", Ct);
        var withoutCsrf = await owner.PostAsJsonAsync(
            "/api/v1/agents",
            new { name = "panel-agent", permissions = new[] { Status } },
            Ct);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agents")
        {
            Content = JsonContent.Create(new { name = "panel-agent", permissions = new[] { Status } }),
        };
        request.Headers.Add("X-GW-CSRF", csrf);
        var withCsrf = await owner.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        await AssertProblemAsync(withoutCsrf, HttpStatusCode.Forbidden, "csrf_failed");
        Assert.Equal(HttpStatusCode.Created, withCsrf.StatusCode);
    }

    [Fact]
    public async Task A_missing_permission_is_a_403_naming_the_permission()
    {
        using var factory = new CoreApiFactory();
        var (_, token) = await CreateAgentAsync(factory, [Status]);
        using var agent = AgentClient(factory, token);

        var response = await agent.GetAsync("/api/v1/agent/instances", Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", problem.GetProperty("code").GetString());
        Assert.Contains(Instances, problem.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_feature_flag_and_token_revocation_take_effect_at_once()
    {
        using var factory = new CoreApiFactory();
        var (clientId, token) = await CreateAgentAsync(factory, [Status]);
        using var agent = AgentClient(factory, token);
        var cli = Cli(factory);

        var before = await agent.GetAsync("/api/v1/agent/me", Ct);
        var disabled = await cli.PutAsJsonAsync("/api/v1/agents/settings", new { enabled = false }, Ct);
        var whileDisabled = await agent.GetAsync("/api/v1/agent/me", Ct);
        await cli.PutAsJsonAsync("/api/v1/agents/settings", new { enabled = true }, Ct);
        var tokens = await cli.GetFromJsonAsync<JsonElement>($"/api/v1/agents/{clientId}/tokens", Ct);
        var tokenId = tokens.EnumerateArray().Single().GetProperty("id").GetString()!;
        var revoked = await cli.DeleteAsync($"/api/v1/agents/{clientId}/tokens/{tokenId}", Ct);
        var afterRevoke = await agent.GetAsync("/api/v1/agent/me", Ct);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        await AssertProblemAsync(whileDisabled, HttpStatusCode.Forbidden, "agent_access_disabled");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        await AssertProblemAsync(afterRevoke, HttpStatusCode.Unauthorized, "token_revoked");
    }

    [Fact]
    public async Task An_allowed_network_narrows_where_the_token_works()
    {
        using var factory = new CoreApiFactory();
        var (_, token) = await CreateAgentAsync(factory, [Status], ["26.0.0.0/8"]);
        using var agent = AgentClient(factory, token);

        var fromLoopback = await agent.GetAsync("/api/v1/agent/me", Ct);
        using var fromVpn = new HttpRequestMessage(HttpMethod.Get, "/api/v1/agent/me");
        fromVpn.Headers.Add(CoreApiFactory.RemoteIpHeader, "26.12.34.56");
        var allowed = await agent.SendAsync(fromVpn, Ct);

        await AssertProblemAsync(fromLoopback, HttpStatusCode.Forbidden, "ip_not_allowed");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Diagnostics_mode_gates_core_logs_and_host_probes()
    {
        using var factory = new CoreApiFactory();
        var (_, token) = await CreateAgentAsync(
            factory,
            [Status, DiagnosticsSystem, DiagnosticsStorage, DiagnosticsNetwork, DiagnosticsEvents, DiagnosticsBundles, Logs]);
        using var agent = AgentClient(factory, token);
        var cli = Cli(factory);

        var systemOff = await agent.GetAsync("/api/v1/agent/diagnostics/system", Ct);
        var logsOff = await agent.GetAsync("/api/v1/agent/logs?source=core&tail=5", Ct);
        await cli.PutAsJsonAsync(
            "/api/v1/agents/settings",
            new { diagnostics = new { enabled = true, ttlMinutes = 30, reason = "agent api test" } },
            Ct);
        var systemOn = await agent.GetAsync("/api/v1/agent/diagnostics/system", Ct);
        var storage = await agent.GetAsync("/api/v1/agent/diagnostics/storage", Ct);
        var network = await agent.GetAsync("/api/v1/agent/diagnostics/network", Ct);
        var events = await agent.GetAsync("/api/v1/agent/diagnostics/events?log=Application&hours=1&limit=5", Ct);
        var logsOn = await agent.GetAsync("/api/v1/agent/logs?source=core&tail=5", Ct);
        var bundle = await agent.PostAsJsonAsync("/api/v1/agent/diagnostics/bundles", new { }, Ct);
        var bundleJson = await bundle.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var download = await agent.GetAsync($"/api/v1/agent/diagnostics/bundles/{bundleJson.GetProperty("id").GetString()}/download", Ct);
        await cli.PutAsJsonAsync(
            "/api/v1/agents/settings",
            new { diagnostics = new { enabled = false } },
            Ct);
        var systemAfter = await agent.GetAsync("/api/v1/agent/diagnostics/system", Ct);

        await AssertProblemAsync(systemOff, HttpStatusCode.Forbidden, "diagnostics_mode_off");
        await AssertProblemAsync(logsOff, HttpStatusCode.Forbidden, "diagnostics_mode_off");
        Assert.Equal(HttpStatusCode.OK, systemOn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, storage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, network.StatusCode);
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logsOn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.True((await download.Content.ReadAsByteArrayAsync(Ct)).Length > 100);
        await AssertProblemAsync(systemAfter, HttpStatusCode.Forbidden, "diagnostics_mode_off");
    }

    [Fact]
    public async Task A_self_update_needs_the_permission_and_a_live_admin_window()
    {
        using var factory = new CoreApiFactory();
        var (_, token) = await CreateAgentAsync(factory, ["agent.system.update"]);
        using var agent = AgentClient(factory, token);
        var cli = Cli(factory);

        var before = await agent.PostAsJsonAsync("/api/v1/agent/system/update", new { prerelease = true }, Ct);
        await cli.PutAsJsonAsync(
            "/api/v1/agents/settings",
            new { admin = new { enabled = true, ttlMinutes = 15, reason = "agent api test" } },
            Ct);
        var after = await agent.PostAsJsonAsync("/api/v1/agent/system/update", new { prerelease = true }, Ct);

        await AssertProblemAsync(before, HttpStatusCode.Forbidden, "admin_mode_off");
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
    }

    [Fact]
    public async Task Every_agent_request_reaches_the_client_audit()
    {
        using var factory = new CoreApiFactory();
        var (clientId, token) = await CreateAgentAsync(factory, [Status]);
        using var agent = AgentClient(factory, token);
        var cli = Cli(factory);

        await agent.GetAsync("/api/v1/agent/me", Ct);
        await agent.GetAsync("/api/v1/agent/instances", Ct);

        JsonElement rows = default;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            rows = await cli.GetFromJsonAsync<JsonElement>($"/api/v1/agents/{clientId}/audit?limit=20", Ct);
            if (rows.GetArrayLength() >= 2)
            {
                break;
            }

            await Task.Delay(100, Ct);
        }
        while (DateTime.UtcNow < deadline);

        Assert.True(rows.GetArrayLength() >= 2);
        Assert.Contains(rows.EnumerateArray(), row => row.GetProperty("action").GetString() == "getAgentMe");
        Assert.Contains(rows.EnumerateArray(), row => row.GetProperty("outcome").GetString() == "denied");
        Assert.All(rows.EnumerateArray(), row => Assert.Equal(clientId, row.GetProperty("clientId").GetString()));
    }

    private static HttpClient Cli(CoreApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CliToken());
        return client;
    }

    private static HttpClient AgentClient(CoreApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("gamewarden-test/1.0");
        return client;
    }

    private static async Task<(string ClientId, string Token)> CreateAgentAsync(
        CoreApiFactory factory,
        IReadOnlyList<string> permissions,
        IReadOnlyList<string>? allowedNetworks = null)
    {
        var cli = Cli(factory);
        (await cli.PutAsJsonAsync("/api/v1/agents/settings", new { enabled = true }, Ct)).EnsureSuccessStatusCode();
        var created = await cli.PostAsJsonAsync(
            "/api/v1/agents",
            new { name = "test-agent", permissions, allowedNetworks = allowedNetworks ?? [] },
            Ct);
        created.EnsureSuccessStatusCode();
        var clientId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        var tokenResponse = await cli.PostAsJsonAsync(
            $"/api/v1/agents/{clientId}/tokens",
            new { name = "test", expiresInDays = 7 },
            Ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
        return (clientId, token);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
