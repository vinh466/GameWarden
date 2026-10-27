using GameWarden.Contracts.Runner;

namespace GameWarden.Runner;

/// <summary>
/// Every console line goes to the daily log file, the in-memory ring buffer (for <c>tail</c> after Core
/// reconnects) and the connected Core session, if any.
/// </summary>
internal sealed class OutputHub(string logDirectory, int capacity) : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<OutputLine> _ring = new(capacity);
    private StreamWriter? _log;
    private DateOnly _logDate;

    public event Action<OutputLine>? LineAdded;

    public void Add(OutputStream stream, string text)
    {
        var line = new OutputLine(DateTimeOffset.UtcNow, stream, text);
        lock (_gate)
        {
            if (_ring.Count == capacity)
            {
                _ring.Dequeue();
            }

            _ring.Enqueue(line);
            WriteToLog(line);
        }

        LineAdded?.Invoke(line);
    }

    public IReadOnlyList<OutputLine> Tail(int lines)
    {
        lock (_gate)
        {
            return _ring.Skip(Math.Max(0, _ring.Count - lines)).ToArray();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _log?.Dispose();
        }
    }

    private void WriteToLog(OutputLine line)
    {
        var local = line.At.ToLocalTime();
        var date = DateOnly.FromDateTime(local.DateTime);
        if (_log is null || date != _logDate)
        {
            _log?.Dispose();
            var path = Path.Combine(logDirectory, $"{date:yyyy-MM-dd}.log");
            _log = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            _logDate = date;
        }

        var prefix = line.Stream switch
        {
            OutputStream.Stderr => "ERR ",
            OutputStream.Runner => "GW  ",
            _ => string.Empty,
        };
        _log.WriteLine($"{local:HH:mm:ss.fff} {prefix}{line.Text}");
    }
}
