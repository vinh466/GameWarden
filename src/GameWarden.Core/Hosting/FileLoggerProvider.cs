using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Hosting;

/// <summary>Core log: <c>logs\core\core-&lt;date&gt;.log</c>, kept 30 days (08 §5). Levels come from the <c>Logging</c> section.</summary>
public sealed class FileLoggerProvider(IOptions<GameWardenOptions> options, TimeProvider time) : ILoggerProvider
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateOnly _day;
    private bool _failed;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = time.GetUtcNow();
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Abbreviation(level))
            .Append(' ')
            .Append(category)
            .Append(": ")
            .Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        lock (_gate)
        {
            if (_failed)
            {
                return;
            }

            try
            {
                var day = DateOnly.FromDateTime(now.UtcDateTime);
                if (_writer is null || day != _day)
                {
                    _writer?.Dispose();
                    var folder = options.Value.CoreLogsPath;
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, $"core-{day:yyyy-MM-dd}.log");
                    var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
                    _day = day;
                    Prune(folder, now);
                }

                _writer.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Never take Core down for its log; the console and the Event Log still work.
                _failed = true;
            }
        }
    }

    private static void Prune(string folder, DateTimeOffset now)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "core-*.log"))
        {
            if (now - File.GetLastWriteTimeUtc(file) > Retention)
            {
                File.Delete(file);
            }
        }
    }

    private static string Abbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        _ => "CRIT",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
