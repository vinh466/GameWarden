using System.Globalization;
using System.Text;

namespace GameWarden.Core.Jobs;

/// <summary>One log file per job, <c>logs\jobs\&lt;jobId&gt;.log</c>, kept 90 days (08 §5). Written by Core into a folder only SYSTEM can write.</summary>
public sealed class JobLog : IDisposable
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public JobLog(string folder, string jobId)
    {
        Directory.CreateDirectory(folder);
        var stream = new FileStream(PathFor(folder, jobId), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    public static string PathFor(string folder, string jobId) => Path.Combine(folder, jobId + ".log");

    public static IReadOnlyList<string> Tail(string folder, string jobId, int lines)
    {
        var path = PathFor(folder, jobId);
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var all = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            all.Add(line);
        }

        return all.Count <= lines ? all : all[^lines..];
    }

    public static void Prune(string folder, DateTimeOffset now)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.log"))
        {
            if (now - File.GetLastWriteTimeUtc(file) > Retention)
            {
                File.Delete(file);
            }
        }
    }

    public void Write(string line)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {line}");
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
