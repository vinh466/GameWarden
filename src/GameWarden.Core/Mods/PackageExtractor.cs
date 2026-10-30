using System.Globalization;
using System.IO.Compression;

namespace GameWarden.Core.Mods;

/// <summary>An entry of a package: its name, the size its header gives, and its content.</summary>
public sealed record PackageEntry(string FullName, long Length, Func<Stream> Open);

/// <summary>
/// Unpacks one package into a set folder (docs/13 §4.5), where the loader places each entry. Synchronous: it runs as
/// gamewarden-run inside <see cref="Isolation.IInstanceFiles.Run{T}"/>. An entry that would leave the set folder, an
/// absolute path or one too long fails the build, and so does a package that unpacks to more than the limit: the
/// headers' sizes are checked first, then the bytes actually written, since a zip's headers can lie.
/// </summary>
public static class PackageExtractor
{
    private const int BufferSize = 81920;

    /// <inheritdoc cref="Extract(IReadOnlyCollection{PackageEntry}, string, string, Func{string, string?}, long)"/>
    public static int Extract(ZipArchive zip, string package, string setFolder, Func<string, string?> place, long maxBytes) =>
        Extract([.. zip.Entries.Select(entry => new PackageEntry(entry.FullName, entry.Length, entry.Open))], package, setFolder, place, maxBytes);

    /// <returns>The number of files written.</returns>
    /// <exception cref="ModSyncException">An entry is refused or the package is too large; the message names the package.</exception>
    public static int Extract(IReadOnlyCollection<PackageEntry> entries, string package, string setFolder, Func<string, string?> place, long maxBytes)
    {
        var declared = entries.Sum(entry => entry.Length);
        if (declared > maxBytes)
        {
            throw TooLarge(package, declared, maxBytes);
        }

        var buffer = new byte[BufferSize];
        long written = 0;
        var files = 0;
        foreach (var entry in entries)
        {
            string path;
            try
            {
                if (ModLayout.Normalize(entry.FullName) is not { } name || place(name) is not { } relative)
                {
                    continue;
                }

                path = ModLayout.Resolve(setFolder, relative);
            }
            catch (InvalidDataException ex)
            {
                throw new ModSyncException($"{package}: refused the zip entry '{entry.FullName}': {ex.Message}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > maxBytes)
                {
                    throw TooLarge(package, written, maxBytes);
                }

                target.Write(buffer, 0, read);
            }

            files++;
        }

        return files;
    }

    private static ModSyncException TooLarge(string package, long bytes, long maxBytes) =>
        new($"{package}: unpacks to more than {maxBytes.ToString(CultureInfo.InvariantCulture)} bytes "
            + $"(at least {bytes.ToString(CultureInfo.InvariantCulture)}); the limit is GameWarden:ModPackageMaxBytes.");
}
