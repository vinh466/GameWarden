namespace GameWarden.Setup;

/// <summary>The decisions of the Setup engine that do not touch the machine, so they can be unit-tested (ADR-0013).</summary>
internal static class SetupPlan
{
    /// <summary>The machine PATH with <paramref name="folder"/> present once, or absent; unchanged text when nothing changes.</summary>
    public static string WithPathEntry(string path, string folder, bool present)
    {
        static string Normal(string entry) => entry.Trim().TrimEnd('\\');

        var entries = path.Split(';');
        var found = entries.Any(entry => string.Equals(Normal(entry), Normal(folder), StringComparison.OrdinalIgnoreCase));
        if (found == present)
        {
            return path;
        }

        if (present)
        {
            return path.Length == 0 || path.EndsWith(';') ? path + folder : path + ";" + folder;
        }

        return string.Join(';', entries.Where(entry => !string.Equals(Normal(entry), Normal(folder), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary><c>gamewarden.cmd</c> next to <c>versions\</c>: runs the CLI of the current version and passes on its exit code.</summary>
    public static string ShimText(string version) =>
        $"@echo off\r\n\"%~dp0versions\\{version}\\gamewarden.exe\" %*\r\nexit /b %ERRORLEVEL%\r\n";

    /// <summary>
    /// Versions to keep: the current and the previous one (rollback, <c>version use</c>), plus any version a Runner still
    /// runs from (a game keeps its Runner until it restarts, 10 §6). <paramref name="inUse"/> is null when that is unknown.
    /// </summary>
    public static IReadOnlyList<string> VersionsToDelete(IEnumerable<string> installed, string current, string? previous, IReadOnlySet<string>? inUse)
    {
        if (inUse is null)
        {
            return [];
        }

        return [.. installed.Where(version =>
            !string.Equals(version, current, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(version, previous, StringComparison.OrdinalIgnoreCase)
            && !inUse.Contains(version))];
    }

    /// <summary>The version folder a process runs from, if it is under <paramref name="versionsPath"/>.</summary>
    public static string? VersionOf(string processPath, string versionsPath)
    {
        var root = Path.GetFullPath(versionsPath).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(processPath);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = full[root.Length..];
        var slash = rest.IndexOf('\\', StringComparison.Ordinal);
        return slash > 0 ? rest[..slash] : null;
    }
}
