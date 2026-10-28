using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using GameWarden.Core.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Access;

/// <summary>
/// Who may use the API (docs/05-api.md §1, docs/06-security-and-access.md §2–4), from ASP.NET Core's own parts: a
/// session cookie for the panel, the CLI's bearer token on loopback, Antiforgery for the CSRF header, Data Protection
/// keys under the data root encrypted with DPAPI, the rate limiter for sign-ins.
/// </summary>
public static class AccessServices
{
    public const string PanelScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string CliScheme = "Cli";
    public const string CliPolicy = "Cli";
    public const string LoginRateLimit = "login";
    public const string CsrfHeader = "X-GW-CSRF";

    /// <summary>Sliding session; at most 30 days after sign-in (06 §3).</summary>
    public static readonly TimeSpan SessionIdle = TimeSpan.FromDays(7);

    public static readonly TimeSpan SessionMax = TimeSpan.FromDays(30);

    private const string ForwardScheme = "GameWarden";

    /// <param name="keysPath">Data Protection keys: <see cref="GameWardenOptions.KeysPath"/> for a running Core.</param>
    public static IServiceCollection AddGameWardenAccess(this IServiceCollection services, string keysPath)
    {
        services.AddSingleton<Accounts>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<SetupToken>();
        services.AddSingleton(provider => PanelNetwork.From(provider.GetRequiredService<IOptions<GameWardenOptions>>().Value));

        // Sessions and CSRF tokens survive a Core restart; the keys are readable only by the account Core runs as.
        services.AddDataProtection()
            .SetApplicationName("GameWarden")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .ProtectKeysWithDpapi();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = ForwardScheme;
                options.DefaultSignInScheme = PanelScheme;
                options.DefaultSignOutScheme = PanelScheme;
            })
            .AddPolicyScheme(ForwardScheme, null, options =>
                options.ForwardDefaultSelector = context => context.Request.Headers.Authorization.Count > 0 ? CliScheme : PanelScheme)
            .AddCookie(PanelScheme, options =>
            {
                options.Cookie.Name = "gw.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTP over the VPN (ADR-0005)
                options.ExpireTimeSpan = SessionIdle;
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    // An API answers 401 / 403; the panel shows its own sign-in page.
                    OnRedirectToLogin = context =>
                        WriteProblemOnceAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "unauthenticated", "Sign in first."),
                    OnRedirectToAccessDenied = context =>
                        WriteProblemOnceAsync(context.HttpContext, StatusCodes.Status403Forbidden, "forbidden", "Not allowed."),
                    OnValidatePrincipal = ValidateSessionAsync,
                };
            })
            .AddScheme<AuthenticationSchemeOptions, CliAuthenticationHandler>(CliScheme, null);

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(ForwardScheme).RequireAuthenticatedUser().Build())
            // Combined with the group's default policy the schemes add up, so require the CLI identity itself.
            .AddPolicy(CliPolicy, policy => policy
                .AddAuthenticationSchemes(CliScheme)
                .RequireAssertion(context => context.User.Identities.Any(identity => identity is { IsAuthenticated: true, AuthenticationType: CliScheme })));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = "gw.csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
            options.OnRejected = (context, _) => new ValueTask(
                ApiProblems.Create(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many attempts from this address. Wait a minute.")
                    .ExecuteAsync(context.HttpContext));
        });

        return services;
    }

    /// <summary>First in the pipeline: refuses unknown host names and adds the security headers to every response.</summary>
    public static WebApplication UsePanelGuards(this WebApplication app)
    {
        var network = app.Services.GetRequiredService<PanelNetwork>();
        foreach (var invalid in network.Invalid)
        {
            app.Logger.LogWarning("Ignoring panel access network {Network}: not a CIDR range like 26.0.0.0/8.", invalid);
        }

        app.Use(async (context, next) =>
        {
            if (!network.AcceptsHost(context.Request.Host.Host))
            {
                // DNS rebinding (OQ-22): a page on another name must not reach the API through this browser.
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Unknown host name.");
                return;
            }

            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            // Scripts only from the panel's own files; Mantine sets inline styles, so style-src allows them.
            headers.ContentSecurityPolicy =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
                "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
            }

            await next();
        });
        return app;
    }

    /// <summary>After the static panel files, which need no session: sessions, permissions, sign-in rate limits.</summary>
    public static WebApplication UseGameWardenAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        return app;
    }

    /// <summary>
    /// Kestrel listens on every address and drops connections that arrived at an address outside the access networks
    /// (06 §2, OQ-28): binding to the VPN address itself would fail whenever the VPN starts after the service.
    /// </summary>
    public static void ListenForPanel(this KestrelServerOptions kestrel, int port)
    {
        var network = kestrel.ApplicationServices.GetRequiredService<PanelNetwork>();
        var logger = kestrel.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccessServices));
        kestrel.ListenAnyIP(port, listen => listen.Use(next => async connection =>
        {
            var local = (connection.LocalEndPoint as IPEndPoint)?.Address;
            if (network.AcceptsLocal(local))
            {
                await next(connection);
                return;
            }

            // Returning without calling next closes the connection.
            logger.LogDebug("Refused a panel connection to {Local} from {Remote}: not an access network.", local, connection.RemoteEndPoint);
        }));
    }

    /// <summary>
    /// Signs <paramref name="user"/> in and returns the session with a CSRF token bound to them; the panel sends that
    /// token in <see cref="CsrfHeader"/> on every change.
    /// </summary>
    public static async Task<SessionInfo> SignInAsync(HttpContext http, Data.UserRecord user, TimeProvider time)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(AccessClaims.Stamp, user.SecurityStamp),
                new Claim(AccessClaims.SignedInAt, time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ],
            PanelScheme);
        var principal = new ClaimsPrincipal(identity);
        await http.SignInAsync(PanelScheme, principal, new AuthenticationProperties { IsPersistent = true });
        http.User = principal; // Antiforgery binds the token to the current user
        return Session(http, user);
    }

    /// <summary>A 401 / 403 problem from whichever authentication scheme answers first; the others leave it.</summary>
    internal static Task WriteProblemOnceAsync(HttpContext http, int status, string code, string title) =>
        http.Response.HasStarted ? Task.CompletedTask : ApiProblems.Create(status, code, title).ExecuteAsync(http);

    public static SessionInfo Session(HttpContext http, Data.UserRecord user) =>
        new(user.UserName, user.IsOwner, http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http).RequestToken!);

    /// <summary>Ends sessions whose password changed since (sign-out everywhere) or that are older than 30 days.</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal?.FindFirstValue(AccessClaims.Stamp);
        var signedInAt = long.TryParse(principal?.FindFirstValue(AccessClaims.SignedInAt), CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.MinValue;
        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var user = id is null ? null : await services.GetRequiredService<Accounts>().FindAsync(id, context.HttpContext.RequestAborted);
        if (user is null || user.SecurityStamp != stamp || now - signedInAt > SessionMax)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(PanelScheme);
        }
    }
}

public static class AccessClaims
{
    public const string Stamp = "gw:stamp";
    public const string SignedInAt = "gw:signed_in_at";
}
