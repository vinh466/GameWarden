using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace GameWarden.Core.Tests;

/// <summary>Panel access end to end (docs/05-api.md §1–2, docs/06-security-and-access.md §2–4); each test gets a fresh Core.</summary>
[Trait("Category", "Api")]
[Collection(CoreApiFactory.Collection)]
public sealed class AccessApiTests
{
    private const string Password = "correct horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_run_setup_creates_the_owner_once_with_the_token_from_the_cli()
    {
        using var factory = new CoreApiFactory();
        var anonymous = factory.CreateClient();
        Assert.True((await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/setup/status", Ct)).GetProperty("needsSetup").GetBoolean());
        var token = await SetupTokenAsync(factory);

        var wrongToken = await anonymous.PostAsJsonAsync("/api/v1/setup/owner", Owner("not-the-token"), Ct);
        using var fromVpn = new HttpRequestMessage(HttpMethod.Post, "/api/v1/setup/owner") { Content = JsonContent.Create(Owner(token)) };
        fromVpn.Headers.Add(CoreApiFactory.RemoteIpHeader, "26.12.34.56");
        var remote = await anonymous.SendAsync(fromVpn, Ct);
        var created = await anonymous.PostAsJsonAsync("/api/v1/setup/owner", Owner(token), Ct);
        var again = await factory.CreateClient().PostAsJsonAsync("/api/v1/setup/owner", Owner(token), Ct);

        await AssertProblemAsync(wrongToken, HttpStatusCode.Forbidden, "invalid_setup_token");
        await AssertProblemAsync(remote, HttpStatusCode.Forbidden, "setup_loopback_only");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var session = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("owner", session.GetProperty("userName").GetString());
        Assert.True(session.GetProperty("isOwner").GetBoolean());
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "already_set_up");
        Assert.False((await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/setup/status", Ct)).GetProperty("needsSetup").GetBoolean());
        await AssertProblemAsync(await Cli(factory).GetAsync("/api/v1/setup/url", Ct), HttpStatusCode.Conflict, "already_set_up");
    }

    [Fact]
    public async Task The_session_cookie_opens_the_api_and_changes_need_the_csrf_header()
    {
        using var factory = new CoreApiFactory();
        var (owner, csrf) = await CreateOwnerAsync(factory);

        var read = await owner.GetAsync("/api/v1/instances", Ct);
        var withoutToken = await owner.PostAsync("/api/v1/adapters/reload", null, Ct);
        var withToken = await owner.SendAsync(WithCsrf(HttpMethod.Post, "/api/v1/adapters/reload", csrf), Ct);
        var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Ct);
        var cliOnly = await owner.GetAsync("/api/v1/setup/url", Ct);
        var logout = await owner.SendAsync(WithCsrf(HttpMethod.Post, "/api/v1/auth/logout", csrf), Ct);
        var afterLogout = await owner.GetAsync("/api/v1/instances", Ct);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await AssertProblemAsync(withoutToken, HttpStatusCode.Forbidden, "csrf_failed");
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Equal("owner", me.GetProperty("userName").GetString());
        Assert.False(string.IsNullOrEmpty(me.GetProperty("csrfToken").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, cliOnly.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await AssertProblemAsync(afterLogout, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Sign_in_checks_the_password_and_locks_the_name_after_five_failures()
    {
        using var factory = new CoreApiFactory();
        await CreateOwnerAsync(factory);
        var browser = factory.CreateClient();

        var right = await browser.PostAsJsonAsync("/api/v1/auth/login", new { userName = "OWNER", password = Password }, Ct);
        for (var i = 0; i < 5; i++)
        {
            await AssertProblemAsync(
                await browser.PostAsJsonAsync("/api/v1/auth/login", new { userName = "owner", password = "wrong password" }, Ct),
                HttpStatusCode.Unauthorized,
                "invalid_credentials");
        }

        var locked = await browser.PostAsJsonAsync("/api/v1/auth/login", new { userName = "owner", password = Password }, Ct);

        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        await AssertProblemAsync(locked, HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task Changing_the_password_signs_out_every_other_session()
    {
        using var factory = new CoreApiFactory();
        var (owner, csrf) = await CreateOwnerAsync(factory);
        var otherBrowser = factory.CreateClient();
        (await otherBrowser.PostAsJsonAsync("/api/v1/auth/login", new { userName = "owner", password = Password }, Ct)).EnsureSuccessStatusCode();

        var wrongCurrent = await owner.SendAsync(WithCsrf(HttpMethod.Post, "/api/v1/auth/password", csrf, new { currentPassword = "nope nope nope", newPassword = "another long password" }), Ct);
        var tooShort = await owner.SendAsync(WithCsrf(HttpMethod.Post, "/api/v1/auth/password", csrf, new { currentPassword = Password, newPassword = "short" }), Ct);
        var changed = await owner.SendAsync(WithCsrf(HttpMethod.Post, "/api/v1/auth/password", csrf, new { currentPassword = Password, newPassword = "another long password" }), Ct);

        await AssertProblemAsync(wrongCurrent, HttpStatusCode.BadRequest, "invalid_credentials");
        await AssertProblemAsync(tooShort, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/instances", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync("/api/v1/instances", Ct)).StatusCode);
    }

    [Fact]
    public async Task Resetting_the_owner_password_from_the_cli_ends_sessions()
    {
        using var factory = new CoreApiFactory();
        var (owner, _) = await CreateOwnerAsync(factory);

        var reset = await Cli(factory).PostAsJsonAsync("/api/v1/owner/password", new { password = "a brand new password" }, Ct);
        var oldSession = await owner.GetAsync("/api/v1/instances", Ct);
        var newLogin = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { userName = "owner", password = "a brand new password" }, Ct);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Unknown_host_names_and_remote_cli_tokens_are_refused()
    {
        using var factory = new CoreApiFactory();
        var rebinding = factory.CreateClient();
        rebinding.DefaultRequestHeaders.Host = "evil.example";
        var remoteCli = Cli(factory);
        remoteCli.DefaultRequestHeaders.Add(CoreApiFactory.RemoteIpHeader, "26.12.34.56");

        var hostResponse = await rebinding.GetAsync("/healthz", Ct);
        var cliResponse = await remoteCli.GetAsync("/api/v1/instances", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, hostResponse.StatusCode);
        await AssertProblemAsync(cliResponse, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    private static object Owner(string token) => new { setupToken = token, userName = "owner", password = Password };

    private static HttpClient Cli(CoreApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(factory.TokenPath).Trim());
        return client;
    }

    private static async Task<string> SetupTokenAsync(CoreApiFactory factory)
    {
        var url = (await Cli(factory).GetFromJsonAsync<JsonElement>("/api/v1/setup/url", Ct)).GetProperty("url").GetString()!;
        Assert.StartsWith("http://localhost:", url, StringComparison.Ordinal);
        return QueryHelpers.ParseQuery(new Uri(url).Query)["token"].ToString();
    }

    /// <summary>The Owner's browser: a client that keeps the session cookie, and the CSRF token it was given.</summary>
    private static async Task<(HttpClient Browser, string Csrf)> CreateOwnerAsync(CoreApiFactory factory)
    {
        var token = await SetupTokenAsync(factory);
        var browser = factory.CreateClient();
        var response = await browser.PostAsJsonAsync("/api/v1/setup/owner", Owner(token), Ct);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (browser, session.GetProperty("csrfToken").GetString()!);
    }

    private static HttpRequestMessage WithCsrf(HttpMethod method, string path, string csrf, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-GW-CSRF", csrf);
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
