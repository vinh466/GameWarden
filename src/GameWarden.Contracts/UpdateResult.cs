namespace GameWarden.Contracts;

/// <summary>
/// What the Setup engine did on its last install, update or switch; <c>&lt;dataRoot&gt;\updates\last-result.json</c>.
/// Core reads it on start and reports it (10 §6).
/// </summary>
/// <param name="Outcome"><c>succeeded</c>, <c>rolledBack</c> or <c>failed</c>.</param>
public sealed record UpdateResult(string? From, string To, string Outcome, string Message, DateTimeOffset At)
{
    public const string FileName = "last-result.json";
}
