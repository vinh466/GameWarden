using System.IO.Compression;
using System.Reflection;

namespace GameWarden.Setup;

/// <summary>The release zip: embedded in <c>GameWarden-Setup-&lt;v&gt;.exe</c>, or given with <c>--from</c> (10 §4).</summary>
internal sealed class Payload : IDisposable
{
    public const string VersionFile = "version.txt";

    private readonly Stream _stream;
    private readonly ZipArchive _zip;

    private Payload(Stream stream)
    {
        _stream = stream;
        _zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var versionEntry = _zip.GetEntry(VersionFile) ?? throw new InvalidDataException($"The package has no {VersionFile}.");
        using var reader = new StreamReader(versionEntry.Open());
        Version = reader.ReadToEnd().Trim();
        if (!GameWarden.Contracts.SemanticVersion.TryParse(Version, out _))
        {
            throw new InvalidDataException($"The package version '{Version}' is not valid.");
        }

        if (_zip.GetEntry("GameWarden.Core.exe") is null || _zip.GetEntry("GameWarden.Setup.exe") is null)
        {
            throw new InvalidDataException("The package is not a GameWarden release (GameWarden.Core.exe or GameWarden.Setup.exe missing).");
        }
    }

    public string Version { get; }

    public long UncompressedSize => _zip.Entries.Sum(entry => entry.Length);

    /// <summary>The zip from <paramref name="fromZip"/>, else the one embedded in this exe; null when there is none.</summary>
    public static Payload? Open(string? fromZip)
    {
        var stream = fromZip is not null
            ? File.OpenRead(fromZip)
            : Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        return stream is null ? null : new Payload(stream);
    }

    /// <summary>Extracts into a fresh <paramref name="folder"/>: first beside it, then renamed, so a half copy never looks installed.</summary>
    public void ExtractTo(string folder)
    {
        var partial = folder + ".partial";
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        var root = Path.GetFullPath(partial) + Path.DirectorySeparatorChar;
        foreach (var entry in _zip.Entries.Where(entry => entry.Name.Length > 0))
        {
            var target = Path.GetFullPath(Path.Combine(partial, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe path in the package: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.Move(partial, folder);
    }

    public void Dispose()
    {
        _zip.Dispose();
        _stream.Dispose();
    }
}
