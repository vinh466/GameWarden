using System.Globalization;
using System.Text;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Jobs;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Agents;

/// <summary>
/// Reads the log files Core already writes (docs/08 §5): core, job, instance console and setup. Every line passes
/// through <see cref="AgentRedactor"/>; paths are built from known roots, never from a caller-supplied path.
/// </summary>
public sealed class AgentLogService(
    IOptions<GameWardenOptions> options,
    AgentRedactor redactor,
    TimeProvider time)
{
    public const int MaxTail = 1000;
    private const int MaxBytes = 4 * 1024 * 1024;

    public AgentLogsView Tail(string? source, string? id, int? tail, string? level, string? contains)
    {
        var path = Resolve(source, id);
        var wanted = Math.Clamp(tail ?? 100, 1, MaxTail);
        var lines = ReadTail(path, wanted, level, contains, out var truncated);
        return new AgentLogsView(
            source!,
            Path.GetFileName(path),
            [.. lines],
            null,
            truncated,
            Redacted: true);
    }

    public string Resolve(string? source, string? id)
    {
        source = source?.Trim().ToLowerInvariant();
        return source switch
        {
            "core" => Latest(options.Value.CoreLogsPath, "core-*.log"),
            "job" => JobLog.PathFor(options.Value.JobLogsPath, CheckId(id, "job")),
            "instance" => Latest(InstancePaths.For(options.Value.DataRoot, CheckId(id, "instance")).Logs, "*.log"),
            "setup" => Latest(Path.Combine(options.Value.DataRoot, "logs", "setup"), "*.log"),
            _ => throw new AgentRequestException(
                StatusCodes.Status400BadRequest,
                ProblemCodes.ValidationFailed,
                "source must be core, job, instance or setup."),
        };
    }

    private IReadOnlyList<AgentLogLine> ReadTail(string path, int wanted, string? level, string? contains, out bool truncated)
    {
        truncated = false;
        if (!File.Exists(path))
        {
            return [];
        }

        var text = ReadCapped(path);
        var all = text.Split('\n');
        var parsed = new List<AgentLogLine>(all.Length);
        foreach (var raw in all)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var parsedLine = Parse(line);
            if (level is { Length: > 0 } && !string.Equals(parsedLine.Level, level.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (contains is { Length: > 0 } && !parsedLine.Text.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            parsed.Add(parsedLine);
        }

        if (parsed.Count > wanted)
        {
            truncated = true;
            parsed = parsed[^wanted..];
        }

        return parsed;
    }

    private string ReadCapped(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxBytes)
        {
            stream.Seek(-MaxBytes, SeekOrigin.End);
            // Drop the partial first line; the cut is only a safety limit, not a cursor.
            using var reader = new StreamReader(stream, Encoding.UTF8);
            _ = reader.ReadLine();
            return reader.ReadToEnd();
        }

        using var full = new StreamReader(stream, Encoding.UTF8);
        return full.ReadToEnd();
    }

    private AgentLogLine Parse(string line)
    {
        var text = redactor.Redact(line);
        var level = "info";
        DateTimeOffset? at = null;

        // Core: 2026-10-31T12:34:56.789Z INFO Category: message
        if (line.Length >= 24 && DateTimeOffset.TryParseExact(
                line[..24],
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var coreAt))
        {
            at = coreAt;
            var rest = line[24..].TrimStart();
            var token = rest.Split(' ', 2)[0];
            level = token.ToLowerInvariant() switch
            {
                "trce" => "trace",
                "dbug" => "debug",
                "info" => "info",
                "warn" => "warn",
                "fail" or "err" or "error" => "error",
                "crit" => "critical",
                _ => "info",
            };
        }
        // Job: 2026-10-31 12:34:56 message
        else if (line.Length >= 19 && DateTimeOffset.TryParseExact(
                     line[..19],
                     "yyyy-MM-dd HH:mm:ss",
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.AssumeLocal,
                     out var jobAt))
        {
            at = jobAt;
        }
        // Console: 12:34:56.789 [ERR ] message
        else if (line.Length >= 12 && TimeOnly.TryParseExact(
                     line[..12],
                     "HH:mm:ss.fff",
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.None,
                     out var localTime))
        {
            at = new DateTimeOffset(time.GetLocalNow().Date).Add(localTime.ToTimeSpan());
            if (line.Length >= 16 && line[12..16] is " ERR" or "ERR ")
            {
                level = "error";
            }
        }

        return new AgentLogLine(at, level, text);
    }

    private static string Latest(string folder, string pattern)
    {
        if (!Directory.Exists(folder))
        {
            throw new AgentRequestException(StatusCodes.Status404NotFound, "not_found", $"No logs in {Path.GetFileName(folder)} yet.");
        }

        var file = new DirectoryInfo(folder).EnumerateFiles(pattern)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
        if (file is null)
        {
            throw new AgentRequestException(StatusCodes.Status404NotFound, "not_found", $"No logs in {Path.GetFileName(folder)} yet.");
        }

        return file.FullName;
    }

    private static string CheckId(string? id, string what)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"{what} is required and must be a simple id.");
        }

        return id;
    }
}
