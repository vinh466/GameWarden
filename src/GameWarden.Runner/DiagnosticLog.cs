namespace GameWarden.Runner;

/// <summary>The Runner's own diagnostics (not the game console): <c>&lt;state&gt;\runner.log</c>.</summary>
internal sealed class DiagnosticLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public DiagnosticLog(string path)
    {
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
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
