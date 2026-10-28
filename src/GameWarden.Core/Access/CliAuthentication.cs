using System.Security.Claims;
using System.Text.Encodings.Web;
using GameWarden.Core.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Access;

/// <summary>
/// The CLI's bearer token (05 §1): accepted only from loopback, so a stolen token is useless elsewhere. A bearer token
/// is not sent by browsers on their own, so CLI requests need no CSRF token.
/// </summary>
internal sealed class CliAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CliToken token) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!PanelNetwork.IsLoopback(Context.Connection.RemoteIpAddress))
        {
            return Task.FromResult(AuthenticateResult.Fail("The CLI API is available on loopback only."));
        }

        if (!token.Accepts(Request.Headers.Authorization))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing or wrong CLI token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "cli")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    // A CLI-only endpoint also carries the group's default policy, so the session scheme may have answered already.
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        await AccessServices.WriteProblemOnceAsync(Context, StatusCodes.Status401Unauthorized, "unauthenticated", result.Failure?.Message ?? "Missing or wrong CLI token.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        AccessServices.WriteProblemOnceAsync(Context, StatusCodes.Status403Forbidden, "forbidden", "Only the gamewarden command on this machine may do that.");
}
