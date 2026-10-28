using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace GameWarden.Core.Tests;

/// <summary>The realtime hub and the activity feed (docs/05-api.md §2–3) on a fresh Core.</summary>
[Trait("Category", "Api")]
[Collection(CoreApiFactory.Collection)]
public sealed class RealtimeApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hub_clients_hear_instance_changes_and_the_activity_feed_records_them()
    {
        using var factory = new CoreApiFactory();
        var token = factory.CliToken();
        var changed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = new List<JsonElement>();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/events"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
                options.Headers["Authorization"] = "Bearer " + token;
            })
            .Build();
        hub.On<JsonElement>("InstanceChanged", instance => changed.TrySetResult(instance));
        hub.On<JsonElement>("ActivityAdded", item =>
        {
            lock (activity)
            {
                activity.Add(item);
            }
        });
        await hub.StartAsync(Ct);

        var cli = factory.CreateClient();
        cli.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var created = await cli.PostAsJsonAsync("/api/v1/instances", new { id = "rt-fake", adapterId = "fakegame" }, Ct);
        var instance = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var started = await cli.PostAsync("/api/v1/instances/rt-fake/start", null, Ct); // fails (no executable) but is still asked for

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("rt-fake", instance.GetProperty("id").GetString());
        Assert.Equal("stopped", instance.GetProperty("state").GetString()); // enums as camelCase strings, as in the API
        var feed = await WaitForAsync(async () =>
        {
            var items = await cli.GetFromJsonAsync<JsonElement[]>("/api/v1/activity?instanceId=rt-fake", Ct);
            return items is { Length: > 0 } ? items : null;
        });
        Assert.Contains(feed, item => item.GetProperty("kind").GetString() == "instance.created");
        Assert.True(started.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Accepted or HttpStatusCode.Conflict);
        lock (activity)
        {
            Assert.Contains(activity, item => item.GetProperty("kind").GetString() == "instance.created");
        }
    }

    [Fact]
    public async Task Metrics_report_the_machine_with_its_history()
    {
        using var factory = new CoreApiFactory();
        var cli = factory.CreateClient();
        cli.DefaultRequestHeaders.Authorization = new("Bearer", factory.CliToken());

        var report = await WaitForAsync(async () =>
        {
            var metrics = await cli.GetFromJsonAsync<JsonElement>("/api/v1/metrics", Ct);
            return metrics.GetProperty("machine").GetProperty("latest").ValueKind == JsonValueKind.Object ? (JsonElement?)metrics : null;
        });

        var latest = report!.Value.GetProperty("machine").GetProperty("latest");
        Assert.True(latest.GetProperty("ramMb").GetInt64() > 0);
        Assert.InRange(latest.GetProperty("cpuPercent").GetDouble(), 0, 100);
        Assert.Equal(JsonValueKind.Object, report.Value.GetProperty("instances").ValueKind);
    }

    [Fact]
    public async Task The_hub_needs_a_session_and_a_page_of_this_panel()
    {
        using var factory = new CoreApiFactory();
        var client = factory.CreateClient();

        var anonymous = await client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", null, Ct);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/hubs/events/negotiate?negotiateVersion=1");
        foreign.Headers.Add("Origin", "http://evil.example");
        foreign.Headers.Authorization = new("Bearer", factory.CliToken());
        var crossSite = await client.SendAsync(foreign, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition never held.");
            }

            await Task.Delay(100, Ct);
        }
    }
}
