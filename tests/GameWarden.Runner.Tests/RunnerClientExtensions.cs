using GameWarden.Contracts.Runner;

namespace GameWarden.Runner.Tests;

internal static class RunnerClientExtensions
{
    /// <summary>Waits for a console line containing <paramref name="text"/>, looking at the tail first.</summary>
    public static async Task WaitForLineAsync(this RunnerClient client, string text, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RunnerHarness.Timeout);

        var tail = await client.GetTailAsync(1000, timeout.Token);
        if (tail.Any(line => line.Text.Contains(text, StringComparison.Ordinal)))
        {
            return;
        }

        await foreach (var message in client.Events.ReadAllAsync(timeout.Token))
        {
            if (message is OutputEvent output && output.Line.Text.Contains(text, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new IOException($"The Runner pipe closed before '{text}' appeared.");
    }

    public static async Task<ExitedEvent> WaitForExitAsync(this RunnerClient client, TimeSpan within, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(within);
        await foreach (var message in client.Events.ReadAllAsync(timeout.Token))
        {
            if (message is ExitedEvent exited)
            {
                return exited;
            }
        }

        throw new IOException("The Runner pipe closed before the game exited.");
    }
}
