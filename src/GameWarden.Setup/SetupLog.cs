using System.Globalization;

namespace GameWarden.Setup;

/// <summary>
/// Progress on the console and in <c>&lt;dataRoot&gt;\logs\setup\</c>. An update runs without a console (Core starts it),
/// so the file is the record; lines before the data root exists are kept and written once it does.
/// </summary>
internal sealed class SetupLog(string command) : IDisposable
{
    private readonly List<string> _pending = [];
    private StreamWriter? _file;

    public string? FilePath { get; private set; }

    public void AttachTo(string dataRoot)
    {
        if (_file is not null)
        {
            return;
        }

        try
        {
            var folder = Path.Combine(dataRoot, "logs", "setup");
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, $"{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{command}.log");
            _file = new StreamWriter(FilePath, append: true) { AutoFlush = true };
            foreach (var line in _pending)
            {
                _file.WriteLine(line);
            }

            _pending.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"(no log file: {ex.Message})");
        }
    }

    public void Info(string message) => Write(message, error: false);

    public void Error(string message) => Write("ERROR: " + message, error: true);

    public void Dispose()
    {
        _file?.Dispose();
        _file = null;
        FilePath = null;
    }

    private void Write(string message, bool error)
    {
        (error ? Console.Error : Console.Out).WriteLine(message);
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {message}";
        if (_file is null)
        {
            _pending.Add(line);
        }
        else
        {
            _file.WriteLine(line);
        }
    }
}
