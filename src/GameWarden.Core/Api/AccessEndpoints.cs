using System.Security.Claims;
using GameWarden.Core.Access;
using GameWarden.Core.Supervision;
using Microsoft.AspNetCore.Authentication;

namespace GameWarden.Core.Api;

/// <summary>Sign-in, session and first-run setup (docs/05-api.md §2 "Auth and session", "Setup").</summary>
public static class AccessEndpoints
{
    public static void MapAccess(RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginRequest request, HttpContext http, Accounts accounts, LoginThrottle throttle, TimeProvider time, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(request.UserName) || string.IsNullOrEmpty(request.Password))
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, "Enter a user name and a password.");
            }

            if (throttle.LockedFor(request.UserName) is { } wait)
            {
                return ApiProblems.Create(StatusCodes.Status429TooManyRequests, "rate_limited", $"Too many failed sign-ins. Try again in {Math.Ceiling(wait.TotalMinutes)} min.");
            }

            if (await accounts.VerifyAsync(request.UserName, request.Password, cancellationToken) is not { } user)
            {
                throttle.Failed(request.UserName);
                return ApiProblems.Create(StatusCodes.Status401Unauthorized, "invalid_credentials", "Wrong user name or password.");
            }

            throttle.Succeeded(request.UserName);
            return TypedResults.Ok(await AccessServices.SignInAsync(http, user, time));
        }).WithName("login").AllowAnonymous().RequireRateLimiting(AccessServices.LoginRateLimit)
            .Produces<SessionInfo>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AccessServices.PanelScheme);
            return TypedResults.NoContent();
        }).WithName("logout");

        auth.MapGet("/me", async (HttpContext http, Accounts accounts, CancellationToken cancellationToken) =>
            await CurrentUserAsync(http, accounts, cancellationToken) is { } user
                ? Results.Ok(AccessServices.Session(http, user))
                : ApiProblems.Create(StatusCodes.Status401Unauthorized, "unauthenticated", "Sign in first."))
            .WithName("getSession").Produces<SessionInfo>();

        auth.MapPost("/password", async (ChangePasswordRequest request, HttpContext http, Accounts accounts, TimeProvider time, CancellationToken cancellationToken) =>
        {
            if (await CurrentUserAsync(http, accounts, cancellationToken) is not { } user)
            {
                return ApiProblems.Create(StatusCodes.Status401Unauthorized, "unauthenticated", "Sign in first.");
            }

            if (await accounts.VerifyAsync(user.UserName, request.CurrentPassword ?? string.Empty, cancellationToken) is null)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "invalid_credentials", "The current password is wrong.");
            }

            if (Accounts.CheckPassword(request.NewPassword) is { } problem)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, problem);
            }

            // A new security stamp ends every other session (06 §3); this one continues with a fresh cookie.
            var changed = await accounts.SetPasswordAsync(user.Id, request.NewPassword, cancellationToken);
            return Results.Ok(await AccessServices.SignInAsync(http, changed!, time));
        }).WithName("changePassword").Produces<SessionInfo>().ProducesProblem(StatusCodes.Status400BadRequest);

        var setup = api.MapGroup("/setup").WithTags("Setup");

        setup.MapGet("/status", async (Accounts accounts, CancellationToken cancellationToken) =>
            TypedResults.Ok(new SetupStatus(await accounts.NeedsSetupAsync(cancellationToken))))
            .WithName("getSetupStatus").AllowAnonymous();

        // 06 §4, OQ-22: only while no account exists, only on the VM itself, only with the one-time token.
        setup.MapPost("/owner", async (CreateOwnerRequest request, HttpContext http, Accounts accounts, SetupToken token, TimeProvider time, CancellationToken cancellationToken) =>
        {
            if (!PanelNetwork.IsLoopback(http.Connection.RemoteIpAddress))
            {
                return ApiProblems.Create(StatusCodes.Status403Forbidden, "setup_loopback_only", "Create the Owner on the GameWarden machine itself, at http://localhost.");
            }

            if (!await accounts.NeedsSetupAsync(cancellationToken))
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "already_set_up", "The Owner exists already; sign in.");
            }

            if (!token.Matches(request.SetupToken))
            {
                return ApiProblems.Create(StatusCodes.Status403Forbidden, "invalid_setup_token", "The setup link is wrong or used. Run 'gamewarden setup-url' on the machine for a new one.");
            }

            if ((Accounts.CheckUserName(request.UserName) ?? Accounts.CheckPassword(request.Password)) is { } problem)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, problem);
            }

            if (await accounts.CreateOwnerAsync(request.UserName, request.Password, cancellationToken) is not { } owner)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "already_set_up", "The Owner exists already; sign in.");
            }

            token.Consume();
            return Results.Ok(await AccessServices.SignInAsync(http, owner, time));
        }).WithName("createOwner").AllowAnonymous().RequireRateLimiting(AccessServices.LoginRateLimit)
            .Produces<SessionInfo>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);

        // The CLI's side of setup and recovery (`gamewarden setup-url`, `gamewarden owner reset-password`); not for the panel.
        setup.MapGet("/url", async (Accounts accounts, SetupToken token, CancellationToken cancellationToken) =>
            await accounts.NeedsSetupAsync(cancellationToken)
                ? Results.Ok(new SetupUrl(token.Url()))
                : ApiProblems.Create(StatusCodes.Status409Conflict, "already_set_up", "The Owner exists already; sign in, or reset the password with 'gamewarden owner reset-password'."))
            .RequireAuthorization(AccessServices.CliPolicy).ExcludeFromDescription();

        api.MapPost("/owner/password", async (ResetPasswordRequest request, Accounts accounts, CancellationToken cancellationToken) =>
        {
            if (Accounts.CheckPassword(request.Password) is { } problem)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, problem);
            }

            if (await accounts.FindOwnerAsync(cancellationToken) is not { } owner)
            {
                return ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", "There is no Owner yet; run 'gamewarden setup-url'.");
            }

            await accounts.SetPasswordAsync(owner.Id, request.Password, cancellationToken);
            return Results.Ok(new OwnerPasswordReset(owner.UserName));
        }).RequireAuthorization(AccessServices.CliPolicy).ExcludeFromDescription();
    }

    private static async Task<Data.UserRecord?> CurrentUserAsync(HttpContext http, Accounts accounts, CancellationToken cancellationToken) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id && http.User.Identity?.AuthenticationType == AccessServices.PanelScheme
            ? await accounts.FindAsync(id, cancellationToken)
            : null;
}
