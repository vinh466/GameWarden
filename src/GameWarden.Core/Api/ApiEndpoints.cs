using GameWarden.Core.Adapters;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Api;

/// <summary>The part of docs/05-api.md implemented so far.</summary>
public static class ApiEndpoints
{
    public static void MapGameWardenApi(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

        var api = app.MapGroup("/api/v1").AddEndpointFilter<CliTokenFilter>();

        api.MapGet("/adapters", (AdapterCatalog catalog) => Results.Ok(new
        {
            adapters = catalog.Adapters.Select(adapter => new { adapter.Id, adapter.Name, adapter.Version, adapter.Level, adapter.IsBuiltIn }),
            problems = catalog.Problems,
        }));

        api.MapGet("/instances", (Supervisor supervisor) => Results.Ok(supervisor.List()));

        api.MapGet("/instances/{id}", (string id, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor ? Results.Ok(actor.Snapshot) : NotFound(id));

        api.MapPost("/instances", async (CreateInstanceRequest request, Supervisor supervisor, CancellationToken cancellationToken) =>
        {
            var result = await supervisor.CreateAsync(request, cancellationToken);
            return result.Instance is { } instance
                ? Results.Created($"/api/v1/instances/{instance.Id}", instance)
                : ApiProblems.Create(result.StatusCode, result.StatusCode == StatusCodes.Status409Conflict ? "conflict" : "validation_failed", result.Error!);
        });

        api.MapPost("/instances/{id}/start", (string id, Supervisor supervisor) => Command(supervisor, id, actor => actor.StartAsync()));
        api.MapPost("/instances/{id}/stop", (string id, Supervisor supervisor) => Command(supervisor, id, actor => actor.StopAsync()));
        api.MapPost("/instances/{id}/restart", (string id, Supervisor supervisor) => Command(supervisor, id, actor => actor.RestartAsync()));
        api.MapPost("/instances/{id}/console", (string id, ConsoleCommand body, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.SendConsoleAsync(body.Command)));

        api.MapGet("/instances/{id}/console", async (string id, int? tail, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor
                ? Results.Ok(new { lines = await actor.TailAsync(Math.Clamp(tail ?? 200, 1, 1000)) })
                : NotFound(id));
    }

    private static async Task<IResult> Command(Supervisor supervisor, string id, Func<InstanceActor, Task<CommandResult>> command)
    {
        if (supervisor.Find(id) is not { } actor)
        {
            return NotFound(id);
        }

        var result = await command(actor);
        return result.Outcome switch
        {
            CommandOutcome.Accepted => Results.Accepted($"/api/v1/instances/{id}", result.Instance),
            CommandOutcome.NothingToDo => Results.Ok(result.Instance),
            _ => ApiProblems.Create(StatusCodes.Status409Conflict, "invalid_state", result.Message),
        };
    }

    private static IResult NotFound(string id) => ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", $"No instance '{id}'.");
}

public sealed record ConsoleCommand(string Command);

internal static class ApiProblems
{
    /// <summary>RFC 7807 ProblemDetails with the stable <c>code</c> of docs/05-api.md §5.</summary>
    public static IResult Create(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
