namespace GameWarden.Contracts.Runner;

/// <summary>
/// Written by the Runner to <c>&lt;state&gt;\exit.json</c> when the game exits, so Core learns the exit code
/// even if it was down at the time (and the Runner has already quit).
/// </summary>
public sealed record RunnerExitRecord(int ExitCode, DateTimeOffset ExitedAt, int? GamePid)
{
    public const string FileName = "exit.json";
}
