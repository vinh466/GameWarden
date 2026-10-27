namespace GameWarden.Contracts.Runner;

public static class RunnerPipe
{
    /// <summary>Protocol version of this build. Core also accepts <see cref="PreviousProtocolVersion"/> (OQ-26).</summary>
    public const int ProtocolVersion = 1;

    public const int PreviousProtocolVersion = 1;

    public static string NameFor(string instanceId) => $"gamewarden.runner.{instanceId}";
}
