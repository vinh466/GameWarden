using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GameWarden.Core.Integrations;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PalworldIntegrationProviderTests
{
    [Fact]
    public async Task A_read_asks_info_metrics_and_players_and_parses_them()
    {
        var requests = new List<(string Path, string? Authorization)>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            var json = request.RequestUri.AbsolutePath switch
            {
                "/v1/api/info" => """{"version":"1.0.4","serverName":"Pals","description":"A world","worldGuid":"guid-1"}""",
                "/v1/api/metrics" => """{"serverFps":"60","currentPlayerNum":1,"serverFrameTime":16.6,"maxPlayerNum":32,"uptime":3600,"baseCampNum":2,"days":12}""",
                "/v1/api/players" => """{"players":[{"name":"Vinh","accountName":"vinh","playerId":"player-1","userId":"steam_1","ip":"127.0.0.1","ping":12.5,"location_x":1.5,"location_y":-2.5,"level":20,"building_count":3}]}""",
                _ => throw new InvalidOperationException(request.RequestUri.ToString()),
            };
            return Json(HttpStatusCode.OK, json);
        }));
        var context = new GameIntegrationContext(
            "pw-1",
            new Uri("http://127.0.0.1:8212/"),
            http,
            "admin",
            "secret",
            5);

        var data = await new PalworldIntegrationProvider().ReadAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["info", "metrics", "players"], requests.Select(request => request.Path.Split('/')[^1]));
        Assert.All(requests, request =>
            Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret")), request.Authorization));
        Assert.Equal(("1.0.4", "Pals", "A world", "guid-1"),
            (data.Info!.Version, data.Info.ServerName, data.Info.Description, data.Info.WorldGuid));
        Assert.Equal((60, 1, 16.6, 32, 3600L, 2, 12),
            (data.Metrics!.ServerFps, data.Metrics.CurrentPlayerNum, data.Metrics.ServerFrameTime,
                data.Metrics.MaxPlayerNum, data.Metrics.Uptime, data.Metrics.BaseCampNum, data.Metrics.Days));
        var player = Assert.Single(data.Players);
        Assert.Equal(("Vinh", "vinh", "player-1", "steam_1", 12.5, 20, 3),
            (player.Name, player.AccountName, player.PlayerId, player.UserId, player.Ping, player.Level, player.BuildingCount));
    }

    [Fact]
    public async Task Actions_send_the_path_and_body_the_palworld_api_expects()
    {
        var requests = new List<(string Method, string Path, string Body)>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body));
            return Json(HttpStatusCode.OK, "{}");
        }));
        var provider = new PalworldIntegrationProvider();
        var context = new GameIntegrationContext("pw-1", new Uri("http://127.0.0.1:8212/"), http, "admin", "secret", 5);

        Assert.True((await provider.ActAsync(context, "announce", new GameIntegrationActionRequest(Message: "hello"), TestContext.Current.CancellationToken)).Ok);
        Assert.True((await provider.ActAsync(context, "save", new GameIntegrationActionRequest(), TestContext.Current.CancellationToken)).Ok);
        Assert.True((await provider.ActAsync(context, "kick", new GameIntegrationActionRequest(Message: "bye", UserId: "steam_1"), TestContext.Current.CancellationToken)).Ok);
        Assert.True((await provider.ActAsync(context, "ban", new GameIntegrationActionRequest(Message: "bye", UserId: "steam_1"), TestContext.Current.CancellationToken)).Ok);
        Assert.True((await provider.ActAsync(context, "unban", new GameIntegrationActionRequest(UserId: "steam_1"), TestContext.Current.CancellationToken)).Ok);

        Assert.Equal(
            [
                ("POST", "/v1/api/announce", """{"message":"hello"}"""),
                ("POST", "/v1/api/save", ""),
                ("POST", "/v1/api/kick", """{"userid":"steam_1","message":"bye"}"""),
                ("POST", "/v1/api/ban", """{"userid":"steam_1","message":"bye"}"""),
                ("POST", "/v1/api/unban", """{"userid":"steam_1"}"""),
            ],
            requests);
    }

    [Fact]
    public async Task A_refused_password_is_reported_as_an_auth_failure()
    {
        using var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"unauthorized"}""")));
        var context = new GameIntegrationContext("pw-1", new Uri("http://127.0.0.1:8212/"), http, "admin", "wrong", 5);

        var error = await Assert.ThrowsAsync<GameIntegrationException>(() =>
            new PalworldIntegrationProvider().ReadAsync(context, TestContext.Current.CancellationToken));

        Assert.Equal(502, error.Status);
        Assert.Equal("integration_auth_failed", error.Code);
    }

    [Fact]
    public async Task An_unknown_action_and_a_missing_parameter_are_validation_errors()
    {
        using var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));
        var provider = new PalworldIntegrationProvider();
        var context = new GameIntegrationContext("pw-1", new Uri("http://127.0.0.1:8212/"), http, "admin", "secret", 5);

        var unknown = await Assert.ThrowsAsync<GameIntegrationException>(() =>
            provider.ActAsync(context, "shutdown", new GameIntegrationActionRequest(), TestContext.Current.CancellationToken));
        var missingMessage = await Assert.ThrowsAsync<GameIntegrationException>(() =>
            provider.ActAsync(context, "announce", new GameIntegrationActionRequest(), TestContext.Current.CancellationToken));
        var missingUser = await Assert.ThrowsAsync<GameIntegrationException>(() =>
            provider.ActAsync(context, "kick", new GameIntegrationActionRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(("unsupported_action", 400), (unknown.Code, unknown.Status));
        Assert.Equal(("validation_failed", 400), (missingMessage.Code, missingMessage.Status));
        Assert.Equal(("validation_failed", 400), (missingUser.Code, missingUser.Status));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
