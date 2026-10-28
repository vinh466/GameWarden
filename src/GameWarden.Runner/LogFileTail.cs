using System.Text;

namespace GameWarden.Runner;

/// <summary>Follows a log file the game writes and hands over each complete line (<see cref="GameWarden.Contracts.LaunchSpec.ConsoleLogFile"/>).</summary>
internal sealed class LogFileTail(string path, Action<string> onLine)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static void DeleteOld(string path, DiagnosticLog diagnostics)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Write($"Could not delete the old console log {path}: {ex.Message}");
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        long offset = 0;
        var pending = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[64 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(path))
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length < offset)
                    {
                        offset = 0; // the game started the file again
                        pending.Clear();
                    }

                    stream.Seek(offset, SeekOrigin.Begin);
                    int read;
                    while ((read = await stream.ReadAsync(bytes, cancellationToken)) > 0)
                    {
                        offset += read;
                        var count = decoder.GetChars(bytes, 0, read, chars, 0);
                        Split(pending, chars.AsSpan(0, count));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked or being replaced; try again on the next poll.
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (pending.Length > 0)
        {
            onLine(pending.ToString());
        }
    }

    private void Split(StringBuilder pending, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c == '\n')
            {
                var length = pending.Length > 0 && pending[^1] == '\r' ? pending.Length - 1 : pending.Length;
                onLine(pending.ToString(0, length));
                pending.Clear();
            }
            else
            {
                pending.Append(c);
            }
        }
    }
}
