using GameWarden.Core.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;

namespace GameWarden.Core.Access;

/// <summary>
/// 05 §1: a change made with the panel's session cookie must carry the CSRF token in <c>X-GW-CSRF</c> (ASP.NET Core
/// Antiforgery, header mode). Bearer-token (CLI) requests and anonymous endpoints use no ambient credential and skip it.
/// </summary>
internal sealed class CsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var method = http.Request.Method;
        var changes = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
        var withSession = http.User.Identity is { IsAuthenticated: true, AuthenticationType: AccessServices.PanelScheme };
        var anonymous = http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        if (changes && withSession && !anonymous && !await antiforgery.IsRequestValidAsync(http))
        {
            return ApiProblems.Create(StatusCodes.Status403Forbidden, "csrf_failed", "The request lacks the panel's CSRF token; reload the page.");
        }

        return await next(context);
    }
}
