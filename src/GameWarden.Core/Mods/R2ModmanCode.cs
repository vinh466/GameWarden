using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GameWarden.Core.Mods;

/// <summary>A mod of an r2modman profile: <c>Namespace-Name</c>, <c>x.y.z</c>, and whether the profile has it enabled.</summary>
public sealed record R2ModmanMod(string Package, string Version, bool Enabled);

/// <summary>A config file of a code: its path under <c>BepInEx/config</c> with <c>/</c> separators, and its bytes.</summary>
public sealed record CodeConfigFile(string Path, byte[] Content);

/// <summary>What a code holds: the profile's name, its mods in order, its config files, and what was left out.</summary>
public sealed record R2ModmanProfile(string ProfileName, IReadOnlyList<R2ModmanMod> Mods, IReadOnlyList<CodeConfigFile> ConfigFiles, IReadOnlyList<string> Warnings);

/// <summary>A code's text cannot be used: not r2modman's format, too large, or an entry that leaves its folder.</summary>
public sealed class R2ModmanCodeException(string message) : Exception(message);

/// <summary>
/// r2modman's profile codes (docs/13 §3), checked against r2modmanPlus <c>develop</c> on 2026-10-30:
/// <list type="bullet">
/// <item>The text is <c>#r2modman</c>, a newline and the base64 of a zip (<c>ProfileModList.exportModListAsCode</c>);
/// the reader drops the prefix and trims, as <c>ProfileImportExport.saveDownloadedProfile</c> does.</item>
/// <item>The zip holds <c>export.r2x</c>, the YAML that js-yaml writes for <c>ExportFormat</c>: <c>profileName</c> and
/// <c>mods[]</c> of <c>name</c>, <c>version { major, minor, patch }</c> and <c>enabled</c>
/// (<c>ProfileModList.createExport</c>); it is read as <c>ProfileUtils.parseYamlToExportFormat</c> does, a missing
/// <c>enabled</c> counting as enabled.</item>
/// <item>The profile's <c>BepInEx/config</c> is under <c>config/</c>; r2modman also adds other config-like files of the
/// profile (<c>.cfg</c>, <c>.txt</c>, <c>.json</c>, <c>.yml</c>, <c>.yaml</c>, <c>.ini</c>) by their path in it. Only
/// <c>config/</c> is taken (it goes over the set's configs when it is built, §4.7); the others are listed as left out, and so
/// are files with the extensions <c>ProfileUtils.extractConfigsToImportedProfile</c> blocks.</item>
/// <item>A code is the key Thunderstore gives the stored text: a UUID (<c>legacyprofile/get/&lt;uuid:key&gt;/</c> in
/// Thunderstore's <c>modpacks/api/experimental/urls.py</c>).</item>
/// </list>
/// Limits keep a code small: r2modman refuses to make a code of a zip over 20 MB (<c>MAX_EXPORT_AS_CODE_SIZE</c>).
/// </summary>
public static class R2ModmanCode
{
    public const string Prefix = "#r2modman";

    public const string ExportFile = "export.r2x";

    /// <summary>r2modman's <c>MAX_EXPORT_AS_CODE_SIZE</c>.</summary>
    public const long MaxZipBytes = 20_000_000;

    public const int MaxEntries = 5000;

    public const long MaxExportBytes = 1024 * 1024;

    public const long MaxConfigFileBytes = 4 * 1024 * 1024;

    public const long MaxConfigBytes = 64 * 1024 * 1024;

    public const int MaxMods = 2000;

    /// <summary>Deeper YAML than this is refused before it is loaded: <c>export.r2x</c> nests three levels.</summary>
    private const int MaxYamlDepth = 16;

    /// <summary><c>blockedExtensions</c> of <c>ProfileUtils.extractConfigsToImportedProfile</c>.</summary>
    private static readonly string[] BlockedExtensions =
    [
        ".dll", ".exe", ".scr", ".com", ".pif", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
        ".msi", ".msix", ".sys", ".drv", ".cpl", ".ocx", ".lnk", ".reg", ".inf",
    ];

    private static readonly YamlScalarNode NameKey = new("name");
    private static readonly YamlScalarNode VersionKey = new("version");
    private static readonly YamlScalarNode EnabledKey = new("enabled");
    private static readonly YamlScalarNode ProfileNameKey = new("profileName");
    private static readonly YamlScalarNode ModsKey = new("mods");

    /// <summary>A code as Thunderstore writes it, lowercase; spaces around it are dropped.</summary>
    public static bool TryNormalizeCode(string? text, [NotNullWhen(true)] out string? code)
    {
        code = null;
        var trimmed = text?.Trim();
        if (trimmed is not { Length: 36 } || !Guid.TryParseExact(trimmed, "D", out var key))
        {
            return false;
        }

        code = key.ToString("D");
        return true;
    }

    /// <summary>Reads a code's text.</summary>
    /// <param name="checkConfigPath">
    /// Throws <see cref="InvalidDataException"/> for a config path that cannot be placed (too long where it will go).
    /// </param>
    /// <exception cref="R2ModmanCodeException">The text is not a profile Core can use; the message says why.</exception>
    public static R2ModmanProfile Read(string text, Action<string>? checkConfigPath = null)
    {
        var body = text.TrimStart('﻿');
        if (!body.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new R2ModmanCodeException($"The code's profile does not start with '{Prefix}'; it is not an r2modman profile.");
        }

        byte[] zipBytes;
        try
        {
            zipBytes = Convert.FromBase64String(body[Prefix.Length..].Trim());
        }
        catch (FormatException)
        {
            throw new R2ModmanCodeException("The code's profile is not base64 after its first line.");
        }

        if (zipBytes.Length > MaxZipBytes)
        {
            throw new R2ModmanCodeException($"The code's zip is {zipBytes.Length.ToString(CultureInfo.InvariantCulture)} bytes; a code holds at most {MaxZipBytes.ToString(CultureInfo.InvariantCulture)}.");
        }

        try
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read);
            return ReadZip(zip, checkConfigPath);
        }
        catch (InvalidDataException ex)
        {
            throw new R2ModmanCodeException($"The code's profile is not a zip Core can read: {ex.Message}");
        }
    }

    /// <summary>
    /// The text of a code for <paramref name="mods"/>, as r2modman writes it: <c>#r2modman</c>, a newline and the base64
    /// of a zip that holds <c>export.r2x</c> only (a share code never carries configs, docs/13 §2 answer 6).
    /// </summary>
    public static string Write(string profileName, IEnumerable<R2ModmanMod> mods)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(ExportFile, CompressionLevel.Optimal).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(ExportYaml(profileName, mods));
        }

        return $"{Prefix}\n{Convert.ToBase64String(memory.GetBuffer(), 0, (int)memory.Length)}";
    }

    /// <summary>
    /// <c>export.r2x</c> in js-yaml's shape: two-space indents, list items indented under their key, the version as
    /// three numbers.
    /// </summary>
    public static string ExportYaml(string profileName, IEnumerable<R2ModmanMod> mods)
    {
        var export = new ExportFormat
        {
            ProfileName = profileName,
            Mods =
            [
                .. mods.Select(mod =>
                {
                    var parts = mod.Version.Split('.').Select(part => long.Parse(part, CultureInfo.InvariantCulture)).ToArray();
                    return new ExportMod { Name = mod.Package, Version = new ExportVersion { Major = parts[0], Minor = parts[1], Patch = parts[2] }, Enabled = mod.Enabled };
                }),
            ],
        };
        return new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithIndentedSequences()
            .WithNewLine("\n")
            .Build()
            .Serialize(export);
    }

    private static R2ModmanProfile ReadZip(ZipArchive zip, Action<string>? checkConfigPath)
    {
        if (zip.Entries.Count > MaxEntries)
        {
            throw new R2ModmanCodeException($"The code's zip has {zip.Entries.Count.ToString(CultureInfo.InvariantCulture)} entries; a code may have {MaxEntries.ToString(CultureInfo.InvariantCulture)}.");
        }

        string? exportYaml = null;
        var configs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        List<string> leftOut = [];
        List<string> blocked = [];
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            string? name;
            try
            {
                name = ModLayout.Normalize(entry.FullName);
            }
            catch (InvalidDataException ex)
            {
                throw new R2ModmanCodeException($"The code's zip has a refused entry '{entry.FullName}': {ex.Message}");
            }

            if (name is null)
            {
                continue;
            }

            if (name.Equals(ExportFile, StringComparison.OrdinalIgnoreCase))
            {
                exportYaml = Encoding.UTF8.GetString(ReadEntry(entry, MaxExportBytes, ref total, long.MaxValue)).TrimStart('﻿');
                continue;
            }

            if (name.Equals("mods.yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!name.StartsWith("config/", StringComparison.Ordinal))
            {
                leftOut.Add(name);
                continue;
            }

            var path = name["config/".Length..];
            if (BlockedExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                blocked.Add(name);
                continue;
            }

            try
            {
                checkConfigPath?.Invoke(path);
            }
            catch (InvalidDataException ex)
            {
                throw new R2ModmanCodeException($"The code's zip has a refused entry '{entry.FullName}': {ex.Message}");
            }

            configs[path] = ReadEntry(entry, MaxConfigFileBytes, ref total, MaxConfigBytes);
        }

        if (exportYaml is null)
        {
            throw new R2ModmanCodeException($"The code's zip has no {ExportFile}; it is not an r2modman profile.");
        }

        var (profileName, mods, warnings) = ReadExport(exportYaml);
        if (blocked.Count > 0)
        {
            warnings.Add($"Left out {blocked.Count.ToString(CultureInfo.InvariantCulture)} file(s) that r2modman does not import either (programs and scripts): {Sample(blocked)}.");
        }

        if (leftOut.Count > 0)
        {
            warnings.Add($"Left out {leftOut.Count.ToString(CultureInfo.InvariantCulture)} file(s) outside config/; only the profile's BepInEx config folder is used: {Sample(leftOut)}.");
        }

        return new R2ModmanProfile(
            profileName,
            mods,
            [.. configs.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new CodeConfigFile(pair.Key, pair.Value))],
            warnings);
    }

    /// <summary>An entry's bytes, counting what is actually read: a zip's headers can lie.</summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry, long maxFile, ref long total, long maxTotal)
    {
        if (entry.Length > maxFile)
        {
            throw TooLarge(entry.FullName, entry.Length, maxFile);
        }

        using var source = entry.Open();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > maxFile)
            {
                throw TooLarge(entry.FullName, memory.Length + read, maxFile);
            }

            total += read;
            if (total > maxTotal)
            {
                throw new R2ModmanCodeException($"The code's config files unpack to more than {maxTotal.ToString(CultureInfo.InvariantCulture)} bytes.");
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static R2ModmanCodeException TooLarge(string name, long bytes, long max) =>
        new($"The code's '{name}' is larger than {max.ToString(CultureInfo.InvariantCulture)} bytes (at least {bytes.ToString(CultureInfo.InvariantCulture)}).");

    /// <summary><c>ProfileUtils.parseYamlToExportFormat</c>: a missing <c>profileName</c> or <c>mods</c> list refuses the code; a bad mod is left out with a warning.</summary>
    private static (string ProfileName, List<R2ModmanMod> Mods, List<string> Warnings) ReadExport(string yaml)
    {
        YamlNode? root;
        try
        {
            CheckDepth(yaml);
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
        }
        catch (YamlException ex)
        {
            throw new R2ModmanCodeException($"The code's {ExportFile} is not YAML Core can read: {ex.Message}");
        }

        if (root is not YamlMappingNode profile)
        {
            throw new R2ModmanCodeException($"The code's {ExportFile} is empty or not a mapping.");
        }

        if (Scalar(profile, ProfileNameKey) is not { } profileName)
        {
            throw new R2ModmanCodeException($"The code's {ExportFile} has no profileName.");
        }

        if (!profile.Children.TryGetValue(ModsKey, out var modsNode) || modsNode is not YamlSequenceNode modList)
        {
            throw new R2ModmanCodeException($"The code's {ExportFile} has no list of mods.");
        }

        if (modList.Children.Count > MaxMods)
        {
            throw new R2ModmanCodeException($"The code lists {modList.Children.Count.ToString(CultureInfo.InvariantCulture)} mods; Core takes at most {MaxMods.ToString(CultureInfo.InvariantCulture)}.");
        }

        List<R2ModmanMod> mods = [];
        List<string> warnings = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (index, node) in modList.Children.Index())
        {
            var number = (index + 1).ToString(CultureInfo.InvariantCulture);
            if (node is not YamlMappingNode mod || Scalar(mod, NameKey) is not { } name)
            {
                warnings.Add($"Mod {number} of the code has no name; it is left out.");
                continue;
            }

            if (!Packages.IsPackage(name))
            {
                warnings.Add($"'{name}' is not a Thunderstore package (Namespace-Name); it is left out.");
                continue;
            }

            if (!mod.Children.TryGetValue(VersionKey, out var versionNode) || VersionText(versionNode) is not { } version)
            {
                warnings.Add($"{name} has no version Core can read (major, minor, patch); it is left out.");
                continue;
            }

            if (!seen.Add(name))
            {
                warnings.Add($"{name} is in the code twice; the first one is kept.");
                continue;
            }

            // r2modman: `mod.enabled === undefined || mod.enabled`.
            var enabled = Scalar(mod, EnabledKey) is not { } flag || !flag.Equals("false", StringComparison.OrdinalIgnoreCase);
            mods.Add(new R2ModmanMod(name, version, enabled));
        }

        return (profileName, mods, warnings);
    }

    private static string? VersionText(YamlNode node)
    {
        if (node is not YamlMappingNode version)
        {
            return null;
        }

        var parts = new[] { "major", "minor", "patch" }.Select(key => Scalar(version, new YamlScalarNode(key))).ToArray();
        if (parts.Any(part => part is null || !long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }

        var text = string.Join('.', parts.Select(part => long.Parse(part!, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));
        return Packages.IsVersion(text) ? text : null;
    }

    private static string? Scalar(YamlMappingNode mapping, YamlScalarNode key) =>
        mapping.Children.TryGetValue(key, out var value) && value is YamlScalarNode { Value: { } text } ? text : null;

    /// <summary>
    /// Walks the YAML's events (no recursion) and refuses deep nesting, which would otherwise exhaust the stack when the
    /// representation model is built.
    /// </summary>
    private static void CheckDepth(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        var depth = 0;
        while (parser.MoveNext())
        {
            switch (parser.Current)
            {
                case MappingStart or SequenceStart:
                    if (++depth > MaxYamlDepth)
                    {
                        throw new R2ModmanCodeException($"The code's {ExportFile} nests deeper than {MaxYamlDepth.ToString(CultureInfo.InvariantCulture)} levels.");
                    }

                    break;
                case MappingEnd or SequenceEnd:
                    depth--;
                    break;
            }
        }
    }

    private static string Sample(List<string> names) =>
        string.Join(", ", names.Take(5)) + (names.Count > 5 ? $" and {(names.Count - 5).ToString(CultureInfo.InvariantCulture)} more" : string.Empty);

    // ExportFormat, ExportMod and VersionNumber of r2modman, in the order js-yaml writes their fields.
    private sealed class ExportFormat
    {
        public required string ProfileName { get; init; }

        public required List<ExportMod> Mods { get; init; }
    }

    private sealed class ExportMod
    {
        public required string Name { get; init; }

        public required ExportVersion Version { get; init; }

        public bool Enabled { get; init; }
    }

    private sealed class ExportVersion
    {
        public long Major { get; init; }

        public long Minor { get; init; }

        public long Patch { get; init; }
    }
}
