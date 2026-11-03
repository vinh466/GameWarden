namespace GameWarden.Contracts;

/// <summary>
/// What the Setup engine did on its last install or update; <c>&lt;dataRoot&gt;\updates\last-result.json</c>.
/// Core reads it on start and reports it (10 §6).
/// </summary>
/// <param name="Outcome"><c>succeeded</c> or <c>failed</c>; <c>rolledBack</c> is only read from files written before ADR-0027.</param>
public sealed record UpdateResult(string? From, string To, string Outcome, string Message, DateTimeOffset At)
{
    public const string FileName = "last-result.json";
}
