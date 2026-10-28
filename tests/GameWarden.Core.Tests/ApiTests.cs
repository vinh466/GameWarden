using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameWarden.Core.Processes;
using GameWarden.Core.Supervision;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GameWarden.Core.Tests;

public sealed class CoreApiFactory : WebApplicationFactory<Program>
{
    public string DataRoot { get; } = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "api-" + Guid.NewGuid().ToString("N")[..10]);

    public string TokenPath => Path.Combine(DataRoot, "cli.token");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("GameWarden:DataRoot", DataRoot);
        builder.UseSetting("GameWarden:CliTokenPath", TokenPath);
        builder.UseSetting("GameWarden:RunnerPath", Path.Combine(AppContext.BaseDirectory, "GameWarden.Runner.exe"));
        builder.UseSetting("GameWarden:BuiltinAdaptersPath", Path.Combine(AppContext.BaseDirectory, "adapters-builtin"));
        // TestServer leaves RemoteIpAddress empty; requests here come from "loopback" like the CLI's.
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LoopbackStartupFilter>());
    }

    protected override void Dispose(bool disposing)
    {
        // Stopping Core leaves games running by design; tests must not leave any behind.
        int[] runners = [];
        try
        {
            runners = [.. Services.GetRequiredService<Supervisor>().List().Select(instance => instance.RunnerPid).OfType<int>()];
        }
        catch (InvalidOperationException)
        {
        }

        base.Dispose(disposing);
        foreach (var pid in runners)
        {
            new CurrentUserProcessHost().Kill(pid);
        }

        try
        {
            Directory.Delete(DataRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class LoopbackStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

[Trait("Category", "Api")]
public sealed class ApiTests(CoreApiFactory factory) : IClassFixture<CoreApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(factory.TokenPath).Trim());
        return client;
    }

    [Fact]
    public async Task Healthz_needs_no_token_and_says_nothing_else()
    {
        var response = await factory.CreateClient().GetAsync("/healthz", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Api_without_the_cli_token_is_refused_with_a_stable_code()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/instances", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("unauthenticated", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Creating_instances_validates_ids_and_rejects_duplicates()
    {
        using var client = AuthorizedClient();

        var created = await client.PostAsJsonAsync("/api/v1/instances", new { id = "api-fake", adapterId = "fakegame" }, Ct);
        var duplicate = await client.PostAsJsonAsync("/api/v1/instances", new { id = "api-fake", adapterId = "fakegame" }, Ct);
        var badId = await client.PostAsJsonAsync("/api/v1/instances", new { id = "Bad Id", adapterId = "fakegame" }, Ct);
        var badAdapter = await client.PostAsJsonAsync("/api/v1/instances", new { id = "api-x", adapterId = "nope" }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var instance = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("stopped", instance.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badAdapter.StatusCode);
    }

    [Fact]
    public async Task Unknown_instances_are_404_with_not_found()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsync("/api/v1/instances/missing/start", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task System_reports_version_isolation_and_machine()
    {
        using var client = AuthorizedClient();

        var system = await client.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct);

        Assert.False(system.GetProperty("isolation").GetBoolean()); // tests run as a normal user (02 §11)
        Assert.True(system.GetProperty("machine").GetProperty("cpuCores").GetInt32() > 0);
        var vcredist = Assert.Single(system.GetProperty("prerequisites").EnumerateArray(), p => p.GetProperty("id").GetString() == "vcredist-x64");
        Assert.Contains(vcredist.GetProperty("neededBy").EnumerateArray(), adapter => adapter.GetString() == "valheim");
        Assert.Equal(3, system.GetProperty("network").GetArrayLength()); // statuses depend on the runner's network
    }

    [Fact]
    public async Task Self_update_is_refused_outside_an_installed_version_folder()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/system/update", new { prerelease = true }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("installed GameWarden only", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_checks_settings_secrets_and_resources_then_purge_deletes_everything()
    {
        using var client = AuthorizedClient();
        await AddNeedyAdapterAsync(client);
        var created = await client.PostAsJsonAsync("/api/v1/instances", new { id = "needy-1", adapterId = "needy" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // 1. Settings and secrets the launch section needs.
        var missing = await client.PostAsync("/api/v1/instances/needy-1/start", null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingTitle = (await missing.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString();
        Assert.Contains("gamewarden set needy-1 executable=", missingTitle, StringComparison.Ordinal);
        Assert.Contains("gamewarden secret needy-1 readyLine", missingTitle, StringComparison.Ordinal);

        var put = await client.PutAsJsonAsync(
            "/api/v1/instances/needy-1/settings",
            new { settings = new { executable = CoreHarness.FakeGameExe }, secrets = new { readyLine = "needy secret ready" } },
            Ct);
        var settingsBody = await put.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.DoesNotContain("needy secret ready", settingsBody, StringComparison.Ordinal); // secrets are never returned
        var settings = JsonDocument.Parse(settingsBody).RootElement;
        Assert.True(settings.GetProperty("secrets").GetProperty("readyLine").GetProperty("set").GetBoolean());

        // 2. Minimum resources (the adapter asks for 4096 cores), unless forced.
        var tooSmall = await client.PostAsync("/api/v1/instances/needy-1/start", null, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, tooSmall.StatusCode);
        Assert.Equal("resources_insufficient", (await tooSmall.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());

        var forced = await client.PostAsync("/api/v1/instances/needy-1/start?force=true", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, forced.StatusCode);
        Assert.Contains("forced below the minimum", (await forced.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("message").GetString(), StringComparison.Ordinal);
        await WaitForStateAsync(client, "needy-1", "running"); // readiness matched the line passed in as a secret

        // 3. Purge needs a stopped instance and an explicit confirmation.
        var whileRunning = await client.DeleteAsync("/api/v1/instances/needy-1?mode=purge&confirm=needy-1", Ct);
        Assert.Equal(HttpStatusCode.Conflict, whileRunning.StatusCode);
        await client.PostAsync("/api/v1/instances/needy-1/stop", null, Ct);
        await WaitForStateAsync(client, "needy-1", "stopped");

        var unconfirmed = await client.DeleteAsync("/api/v1/instances/needy-1?mode=purge", Ct);
        var purged = await client.DeleteAsync("/api/v1/instances/needy-1?mode=purge&confirm=needy-1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, purged.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/instances/needy-1", Ct)).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(factory.DataRoot, "instances", "needy-1")));
        Assert.False(Directory.Exists(Path.Combine(factory.DataRoot, "secrets", "instances", "needy-1")));
    }

    /// <summary>An Owner adapter whose launch needs a setting and a secret, and far more CPU than any machine has.</summary>
    private async Task AddNeedyAdapterAsync(HttpClient client)
    {
        var folder = Path.Combine(factory.DataRoot, "adapters", "needy");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "adapter.json"), """
            {
              "schemaVersion": 1, "id": "needy", "name": "Needy", "version": "1.0.0", "level": "L1",
              "source": { "type": "manual" },
              "launch": { "exe": "{settings.executable}", "args": ["--world", "{paths.data}\\world.txt", "--ready-line", "{secrets.readyLine}"] },
              "ports": [],
              "readiness": { "type": "logRegex", "source": "stdout", "pattern": "needy secret ready", "timeoutSec": 60 },
              "stop": { "method": "ctrl-c", "timeoutSec": 20 },
              "backup": { "paths": ["{paths.data}"], "mode": "stop" },
              "resources": { "min": { "cpuCores": 4096, "ramMb": 1, "diskMb": 1 } }
            }
            """, Ct);
        var reload = await client.PostAsync("/api/v1/adapters/reload", null, Ct);
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
    }

    private static async Task WaitForStateAsync(HttpClient client, string id, string state)
    {
        var deadline = DateTime.UtcNow + CoreHarness.Timeout;
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/instances/{id}", Ct)).GetProperty("state").GetString();
            if (last == state)
            {
                return;
            }

            await Task.Delay(200, Ct);
        }

        Assert.Fail($"{id} never became {state}; last {last}.");
    }
}
