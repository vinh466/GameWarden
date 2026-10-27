using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        base.Dispose(disposing);
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
}
