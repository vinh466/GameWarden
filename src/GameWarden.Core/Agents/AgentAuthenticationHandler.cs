using System.Security.Claims;
using System.Text.Encodings.Web;
using GameWarden.Core.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Agents;

/// <summary>
/// Authenticates <c>Authorization: Bearer gw_agent_...</c> (docs/15). Unlike the CLI token, an Agent token may be
/// used from another machine on the VPN; the client's allowed networks decide where.
/// </summary>
internal sealed class AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AgentClientsService clients) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var authorization = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.Ordinal)
            || !authorization[prefix.Length..].StartsWith(AgentClientsService.TokenPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        try
        {
            var principal = await clients.AuthenticateAsync(
                authorization[prefix.Length..].Trim(),
                Context.Connection.RemoteIpAddress,
                Request.Headers.UserAgent.ToString() is { Length: > 0 } version ? version : null,
                Context.RequestAborted);
            Context.Items[AgentPrincipal.ItemKey] = principal;

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, principal.ClientId),
                    new Claim(ClaimTypes.Name, "agent:" + principal.ClientName),
                    new Claim("gw:agent_client_id", principal.ClientId),
                    new Claim("gw:agent_token_id", principal.TokenId),
                    new Claim("gw:agent_trusted_full", principal.TrustedFull ? "true" : "false"),
                    new Claim("gw:agent_permissions", string.Join(',', principal.Permissions)),
                    new Claim("gw:agent_diagnostics_live", principal.DiagnosticsLive ? "true" : "false"),
                    new Claim("gw:agent_admin_live", principal.AdminLive ? "true" : "false"),
                    new Claim("gw:agent_require_admin", principal.RequireAdminMode ? "true" : "false"),
                ],
                Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (AgentRequestException ex)
        {
            return AuthenticateResult.Fail(ex);
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        var failure = result.Failure as AgentRequestException;
        await AccessServices.WriteProblemOnceAsync(
            Context,
            failure?.Status ?? StatusCodes.Status401Unauthorized,
            failure?.Code ?? "unauthenticated",
            failure?.Message ?? "An Agent token is required.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        AccessServices.WriteProblemOnceAsync(
            Context,
            StatusCodes.Status403Forbidden,
            "forbidden",
            "This Agent token may not do that.");
}
