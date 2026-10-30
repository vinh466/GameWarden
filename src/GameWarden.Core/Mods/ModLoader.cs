namespace GameWarden.Core.Mods;

/// <summary>
/// How a game loads mods (docs/13 §4.1): code in Core, named by an adapter's <c>mods.loader</c>. It decides where each
/// file of a package goes inside a set folder, which files go next to the game's exe, and the launch arguments for
/// <i>Mods on</i> and <i>off</i>. Production registers <see cref="BepInExLoader"/>; tests add a fake one.
/// </summary>
public interface IModLoader
{
    /// <summary>The id adapters use in <c>mods.loader</c>, e.g. <c>bepinex</c>.</summary>
    string Id { get; }

    /// <summary>
    /// Where a file of a package goes, relative to the set folder with <c>/</c> separators, or null to leave it out.
    /// A pure function: <paramref name="entry"/> is a normalized zip entry name (<see cref="ModLayout.Normalize"/>).
    /// </summary>
    /// <param name="package"><c>Namespace-Name</c>.</param>
    /// <param name="isLoaderPackage">The package is the adapter's <c>mods.loaderPackage</c> (the loader itself).</param>
    string? Place(string package, bool isLoaderPackage, string entry);

    /// <summary>
    /// The files copied from the active set into <c>server\</c> before a start with <i>Mods on</i> (docs/13 §4.3),
    /// e.g. Doorstop's <c>winhttp.dll</c>.
    /// </summary>
    IReadOnlyList<LoaderEntryFile> EntryFiles { get; }

    /// <summary>
    /// The folder of a set, relative with <c>/</c> separators, whose files carry over from the set that ran before at a
    /// set's first start (docs/13 §4.7).
    /// </summary>
    string ConfigFolder { get; }

    /// <summary>What lands in <c>server\</c> for <paramref name="file"/>, given its bytes in the set. A pure function.</summary>
    byte[] EntryFileContent(LoaderEntryFile file, byte[] content);

    /// <summary><i>Mods on</i>: the arguments that make the game load the set in <paramref name="setFolder"/>.</summary>
    IReadOnlyList<string> OnArguments(string setFolder);

    /// <summary><i>Mods off</i>: the arguments that keep the loader's entry files from loading anything.</summary>
    IReadOnlyList<string> OffArguments { get; }
}

/// <summary>A loader file that goes next to the game's exe.</summary>
/// <param name="SetPath">Where the build put it, relative to the set folder with <c>/</c> separators.</param>
/// <param name="ServerName">Its name in <c>server\</c>.</param>
public sealed record LoaderEntryFile(string SetPath, string ServerName);

/// <summary>
/// Zip entry names and target paths of a set build (docs/13 §4.5). Paths compare by ordinal, case-sensitively, as on
/// Linux. An entry that would leave its folder, an absolute path or one too long is refused: the build fails rather than
/// skipping a file the mod may need.
/// </summary>
public static class ModLayout
{
    /// <summary>
    /// Longest full path a build writes: Windows' <c>MAX_PATH</c> less the terminating null. Long paths need a registry
    /// switch and every program reading them to opt in; the game and its loader may not.
    /// </summary>
    public const int MaxPathLength = 259;

    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// A zip entry name with <c>/</c> separators and without empty or <c>.</c> segments; null for a folder entry.
    /// Throws <see cref="InvalidDataException"/> for an absolute path, a drive or stream (<c>:</c>), or a <c>..</c> segment.
    /// </summary>
    public static string? Normalize(string entryName)
    {
        if (entryName.EndsWith('/') || entryName.EndsWith('\\'))
        {
            return null;
        }

        return Join(entryName) ?? throw new InvalidDataException($"'{entryName}' is an empty path.");
    }

    /// <summary>
    /// The full path of <paramref name="relative"/> inside <paramref name="root"/>, or <see cref="InvalidDataException"/>
    /// when it would leave the folder, is absolute or is longer than <see cref="MaxPathLength"/>.
    /// </summary>
    public static string Resolve(string root, string relative)
    {
        var joined = Join(relative) ?? throw new InvalidDataException($"'{relative}' is an empty path.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, joined.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"'{relative}' leaves the set folder.");
        }

        if (full.Length > MaxPathLength)
        {
            throw new InvalidDataException($"'{relative}' would be {full.Length} characters long in the set folder; the limit is {MaxPathLength}.");
        }

        return full;
    }

    private static string? Join(string path)
    {
        if (path.Contains('\0', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"'{path}' names a drive or a stream.");
        }

        if (path.StartsWith('/') || path.StartsWith('\\') || Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"'{path}' is an absolute path.");
        }

        var segments = path.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != ".").ToList();
        if (segments.Contains(".."))
        {
            throw new InvalidDataException($"'{path}' leaves its folder.");
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
