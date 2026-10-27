namespace GameWarden.Contracts;

/// <summary>
/// What a Runner starts and where it keeps its files. Core writes it as <c>launchSpec.json</c>
/// and passes the path with <c>--spec</c> (docs/02-architecture.md §4.2).
/// </summary>
public sealed record LaunchSpec
{
    public required string InstanceId { get; init; }

    /// <summary>Game server executable, absolute path.</summary>
    public required string Executable { get; init; }

    /// <summary>Arguments, passed one by one (no shell, no re-splitting).</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public required string WorkingDirectory { get; init; }

    /// <summary>Extra environment variables for the game process.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Console log folder, one file per day (<c>instances\&lt;id&gt;\logs</c>).</summary>
    public required string LogDirectory { get; init; }

    /// <summary>Runner state folder: diagnostics and the exit record.</summary>
    public required string StateDirectory { get; init; }

    /// <summary>Console lines kept in memory for <c>tail</c> after a reconnect.</summary>
    public int RingBufferLines { get; init; } = 1000;

    /// <summary>How long the Runner waits for Core to acknowledge the game's exit before it quits.</summary>
    public int LingerSeconds { get; init; } = 300;
}
