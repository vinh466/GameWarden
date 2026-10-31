using System.Diagnostics;
using System.Security.Claims;
using GameWarden.Core.Access;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using Microsoft.AspNetCore.Routing;

namespace GameWarden.Core.Agents;

public enum AgentMode
{
    Diagnostics,
    Admin,
}

/// <summary>Declares the permission an Agent endpoint needs; the management API and the panel never carry it.</summary>
public sealed record AgentPermissionRequirement(string Permission);

/// <summary>Declares that an Agent endpoint also needs a live diagnostics or admin window.</summary>
public sealed record AgentModeRequirement(AgentMode Mode);

public static class AgentEndpointExtensions
{
    public static RouteHandlerBuilder RequireAgentPermission(this RouteHandlerBuilder builder, string permission) =>
        builder.WithMetadata(new AgentPermissionRequirement(permission));

    public static RouteHandlerBuilder RequireAgentMode(this RouteHandlerBuilder builder, AgentMode mode) =>
        builder.WithMetadata(new AgentModeRequirement(mode));
}

/// <summary>
/// Checks the permission and the time-boxed mode before the endpoint runs. It sits inside the audit middleware,
/// so a denial is recorded like any other Agent request.
/// </summary>
public sealed class AgentPermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // An Agent credential is deliberately narrow: it never reaches the panel, CLI or management API, even when
        // that endpoint would otherwise accept any authenticated caller.
        if (context.User.Identity is { IsAuthenticated: true, AuthenticationType: AccessServices.AgentScheme }
            && !context.Request.Path.StartsWithSegments("/api/v1/agent", StringComparison.OrdinalIgnoreCase))
        {
            await AccessServices.WriteProblemOnceAsync(
                context,
                StatusCodes.Status403Forbidden,
                "forbidden",
                "An Agent token may only call /api/v1/agent.");
            return;
        }

        var endpoint = context.GetEndpoint();
        var required = endpoint?.Metadata.GetMetadata<AgentPermissionRequirement>();
        var mode = endpoint?.Metadata.GetMetadata<AgentModeRequirement>();
        if (required is null && mode is null)
        {
            await next(context);
            return;
        }

        var principal = context.Items[AgentPrincipal.ItemKey] as AgentPrincipal;
        if (principal is null)
        {
            // The Agent policy gives the 401/403; nothing to add here.
            await next(context);
            return;
        }

        if (required is not null && !principal.Allows(required.Permission))
        {
            await AccessServices.WriteProblemOnceAsync(
                context,
                StatusCodes.Status403Forbidden,
                "forbidden",
                $"This Agent token lacks the permission '{required.Permission}'.");
            return;
        }

        if (mode is { Mode: AgentMode.Diagnostics } && !principal.DiagnosticsLive)
        {
            await AccessServices.WriteProblemOnceAsync(
                context,
                StatusCodes.Status403Forbidden,
                "diagnostics_mode_off",
                "Diagnostics mode is off. Ask the Owner to enable it in Agents → Settings.");
            return;
        }

        if (mode is { Mode: AgentMode.Admin } && principal.RequireAdminMode && !principal.AdminLive)
        {
            await AccessServices.WriteProblemOnceAsync(
                context,
                StatusCodes.Status403Forbidden,
                "admin_mode_off",
                "Admin mode is off. Ask the Owner to enable it in Agents → Settings.");
            return;
        }

        await next(context);
    }
}

/// <summary>
/// One audit row per <c>/api/v1/agent</c> request, including reads, denials, failures and the 401/429 answers
/// produced later in the pipeline. The authoritative audit is server-side; the MCP bridge's log is not.
/// </summary>
public sealed class AgentAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AgentAuditLog audit, AgentRedactor redactor, TimeProvider time)
    {
        var agentCredential = context.User.Identity?.AuthenticationType == AccessServices.AgentScheme
            || context.Request.Headers.Authorization.ToString().StartsWith("Bearer " + AgentClientsService.TokenPrefix, StringComparison.Ordinal);
        if (!IsAgentPath(context.Request.Path) && !agentCredential)
        {
            await next(context);
            return;
        }

        var requestId = context.TraceIdentifier;
        context.Response.Headers["X-GW-Request-Id"] = requestId;
        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var principal = context.Items[AgentPrincipal.ItemKey] as AgentPrincipal;
            var status = failure is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
            var endpoint = context.GetEndpoint();
            var action = endpoint?.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName
                ?? endpoint?.DisplayName
                ?? $"{context.Request.Method} {context.Request.Path}";
            var target = context.Request.RouteValues.TryGetValue("id", out var instanceId) ? instanceId?.ToString()
                : context.Request.RouteValues.TryGetValue("jobId", out var jobId) ? jobId?.ToString()
                : null;
            var outcome = status switch
            {
                < 400 => "ok",
                401 or 403 or 429 => "denied",
                _ => "failed",
            };

            audit.Record(new AgentAuditRecord
            {
                At = time.GetUtcNow(),
                ClientId = principal?.ClientId ?? context.User.FindFirst("gw:agent_client_id")?.Value,
                ClientName = principal?.ClientName,
                TokenId = principal?.TokenId ?? context.User.FindFirst("gw:agent_token_id")?.Value,
                Action = action.Length <= 64 ? action : action[..64],
                Target = target,
                Outcome = outcome,
                StatusCode = status,
                SourceIp = context.Connection.RemoteIpAddress?.ToString(),
                RequestId = requestId,
                DurationMs = (long)elapsed.TotalMilliseconds,
                Details = AgentAuditLog.Details(new
                {
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value,
                    Query = redactor.RedactLine(context.Request.QueryString.Value is { Length: > 512 } query ? query[..512] : context.Request.QueryString.Value),
                    UserAgent = redactor.RedactLine(context.Request.Headers.UserAgent.ToString() is { Length: > 128 } agent ? agent[..128] : context.Request.Headers.UserAgent.ToString()),
                }),
            });
        }
    }

    private static bool IsAgentPath(PathString path) =>
        path.StartsWithSegments("/api/v1/agent", StringComparison.OrdinalIgnoreCase);
}
