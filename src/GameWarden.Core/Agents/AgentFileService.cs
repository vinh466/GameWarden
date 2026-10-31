using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Agents;

/// <summary>
/// Read-only file access under one instance root (docs/15): the path is relative, canonical and checked against
/// reparse points; text files only, one megabyte at most, and every byte passes the redactor.
/// </summary>
public sealed class AgentFileService(
    Supervisor supervisor,
    IOptions<GameWardenOptions> options,
    AgentRedactor redactor)
{
    private const long MaxReadBytes = 1024 * 1024;
    private const int MaxEntries = 500;

    public AgentFilesView List(string instanceId, string? path, int? depth)
    {
        var root = Root(instanceId);
        var relative = Normalize(path);
        var full = Resolve(root, relative);
        if (!Directory.Exists(full) && !File.Exists(full))
        {
            throw new AgentRequestException(StatusCodes.Status404NotFound, "not_found", $"No '{relative}' in {instanceId}.");
        }

        var entries = new List<AgentFileEntry>();
        var levels = Math.Clamp(depth ?? 2, 1, 4);
        if (File.Exists(full))
        {
            entries.Add(Entry(root, full));
        }
        else
        {
            Walk(root, full, levels, entries);
        }

        return new AgentFilesView(instanceId, relative, entries, entries.Count >= MaxEntries);
    }

    public AgentFileContent Read(string instanceId, string? path)
    {
        var root = Root(instanceId);
        var relative = Normalize(path);
        if (relative == ".")
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, "validation_failed", "path must name a file.");
        }

        var full = Resolve(root, relative);
        if (!File.Exists(full))
        {
            throw new AgentRequestException(StatusCodes.Status404NotFound, "not_found", $"No '{relative}' in {instanceId}.");
        }

        if (new FileInfo(full).Length > MaxReadBytes)
        {
            throw new AgentRequestException(StatusCodes.Status409Conflict, "invalid_state", $"{relative} is larger than 1 MB.");
        }

        if (!IsText(full))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, "validation_failed", "Only text files can be read.");
        }

        var bytes = File.ReadAllBytes(full);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8192)).Contains((byte)0))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, "validation_failed", $"{relative} looks binary.");
        }

        var info = new FileInfo(full);
        return new AgentFileContent(instanceId, relative, info.Length, new DateTimeOffset(info.LastWriteTimeUtc), redactor.Redact(System.Text.Encoding.UTF8.GetString(bytes)));
    }

    private string Root(string instanceId)
    {
        if (supervisor.Find(instanceId) is null)
        {
            throw new AgentRequestException(StatusCodes.Status404NotFound, "not_found", $"No instance '{instanceId}'.");
        }

        return InstancePaths.For(options.Value.DataRoot, instanceId).Root;
    }

    private static string Normalize(string? path)
    {
        var value = (path ?? ".").Replace('\\', '/').Trim();
        while (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (value.Length == 0)
        {
            value = ".";
        }

        if (Path.IsPathRooted(value) || value.Split('/').Any(part => part is ".." or ""))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, "validation_failed", "path must stay inside the instance folder.");
        }

        return value;
    }

    private static string Resolve(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(rootFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, "validation_failed", "path must stay inside the instance folder.");
        }

        if (HasReparsePoint(rootFull, full))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, "validation_failed", "path crosses a link or reparse point.");
        }

        return full;
    }

    private static bool HasReparsePoint(string root, string full)
    {
        var relative = Path.GetRelativePath(root, full);
        if (relative == ".")
        {
            return false;
        }

        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void Walk(string root, string directory, int depth, List<AgentFileEntry> entries)
    {
        if (entries.Count >= MaxEntries)
        {
            return;
        }

        foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Count >= MaxEntries)
            {
                return;
            }

            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            entries.Add(Entry(root, item.FullName));
            if (depth > 1 && item is DirectoryInfo)
            {
                Walk(root, item.FullName, depth - 1, entries);
            }
        }
    }

    private static AgentFileEntry Entry(string root, string full)
    {
        var info = new FileInfo(full);
        var directory = Directory.Exists(full);
        return new AgentFileEntry(
            Path.GetRelativePath(root, full).Replace('\\', '/'),
            directory ? 0 : info.Length,
            new DateTimeOffset(directory ? Directory.GetLastWriteTimeUtc(full) : info.LastWriteTimeUtc),
            directory);
    }

    private static bool IsText(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".cfg" or ".ini" or ".json" or ".yml" or ".yaml" or ".txt" or ".xml" or ".properties" or ".log" or ".md" or ".csv";
}
