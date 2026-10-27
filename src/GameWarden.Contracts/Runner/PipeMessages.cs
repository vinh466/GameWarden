using System.Text.Json.Serialization;

namespace GameWarden.Contracts.Runner;

/// <summary>
/// One line of newline-delimited JSON on the Runner pipe. Core sends requests; the Runner answers each
/// with a reply (<see cref="PipeReply.ReplyTo"/> = request id) and pushes events in between.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(GetStatus), "getStatus")]
[JsonDerivedType(typeof(StatusReply), "status")]
[JsonDerivedType(typeof(WriteStdin), "stdin")]
[JsonDerivedType(typeof(SendCtrlC), "ctrlC")]
[JsonDerivedType(typeof(KillGame), "kill")]
[JsonDerivedType(typeof(GetTail), "getTail")]
[JsonDerivedType(typeof(TailReply), "tail")]
[JsonDerivedType(typeof(AckExit), "ackExit")]
[JsonDerivedType(typeof(Accepted), "accepted")]
[JsonDerivedType(typeof(Rejected), "rejected")]
[JsonDerivedType(typeof(OutputEvent), "output")]
[JsonDerivedType(typeof(ExitedEvent), "exited")]
public abstract record PipeMessage;

public abstract record PipeRequest : PipeMessage
{
    public int Id { get; init; }
}

public abstract record PipeReply : PipeMessage
{
    public int ReplyTo { get; init; }
}

/// <summary>First message from Core: the protocol versions it speaks (current and previous, OQ-26).</summary>
public sealed record Hello(IReadOnlyList<int> Protocols) : PipeRequest;

public sealed record Welcome(int Protocol, string RunnerVersion, string InstanceId, RunnerStatus Status) : PipeReply;

public sealed record GetStatus : PipeRequest;

public sealed record StatusReply(RunnerStatus Status) : PipeReply;

public sealed record WriteStdin(string Text) : PipeRequest;

public sealed record SendCtrlC : PipeRequest;

public sealed record KillGame : PipeRequest;

public sealed record GetTail(int Lines) : PipeRequest;

public sealed record TailReply(IReadOnlyList<OutputLine> Lines) : PipeReply;

/// <summary>Core has recorded the exit; the Runner may quit.</summary>
public sealed record AckExit : PipeRequest;

public sealed record Accepted : PipeReply;

public sealed record Rejected(string Reason) : PipeReply;

public sealed record OutputEvent(OutputLine Line) : PipeMessage;

public sealed record ExitedEvent(int ExitCode, DateTimeOffset At) : PipeMessage;

public enum OutputStream
{
    Stdout,
    Stderr,
    Runner,
}

public sealed record OutputLine(DateTimeOffset At, OutputStream Stream, string Text);

public sealed record RunnerStatus(
    int RunnerPid,
    int? GamePid,
    DateTimeOffset? GameStartedAt,
    bool GameRunning,
    int? ExitCode,
    DateTimeOffset? ExitedAt);
