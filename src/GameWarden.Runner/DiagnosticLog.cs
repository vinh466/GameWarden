namespace GameWarden.Runner;

/// <summary>The Runner's own diagnostics (not the game console): <c>&lt;state&gt;\runner.log</c>.</summary>
internal sealed class DiagnosticLog : IDisposable
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public DiagnosticLog(string path)
    {
        Rotate(path);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    }

    /// <summary>Keeps one previous file so the diagnostics bundle stays small (docs/08 §5).</summary>
    private static void Rotate(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < MaxBytes)
            {
                return;
            }

            var previous = path + ".1";
            File.Delete(previous);
            File.Move(path, previous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only or locked folder is reported by the first Write; starting the Runner matters more.
        }
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}
