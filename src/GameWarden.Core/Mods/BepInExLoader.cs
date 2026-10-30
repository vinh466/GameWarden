using System.Text;
using System.Text.RegularExpressions;

namespace GameWarden.Core.Mods;

/// <summary>
/// BepInEx through Unity Doorstop (docs/13 §4.3, spike S-M1), placing packages the way r2modman does (spike S-M2,
/// r2modmanPlus <c>develop</c> at <c>b09ec5a</c>, 2026-10-28):
/// <list type="bullet">
/// <item>The loader package (<c>src/installers/BepInExInstaller.ts</c>, <c>install</c>): r2modman copies the content of
/// its root folder (<c>modloaderPackages[].rootFolder</c> in <c>src/assets/data/ecosystem.json</c>, the package's name,
/// e.g. <c>BepInExPack_Valheim</c>) into the profile. Here its <c>BepInEx/**</c> goes into the set's <c>BepInEx/</c>
/// and its two Doorstop files into <c>loader/</c>, the source for <c>server\</c>; the rest (Linux and macOS launch
/// scripts, <c>doorstop_libs/</c>, changelogs) is left out.</item>
/// <item>Every other package (<c>src/installers/InstallRulePluginInstaller.ts</c>, <c>buildInstallForRuleSubtype</c>,
/// with Valheim's <c>installRules</c> in <c>ecosystem.json</c>): a folder whose name is a rule's last segment
/// (<c>plugins</c>, <c>core</c>, <c>patchers</c>, <c>monomod</c>, <c>config</c>, <c>SlimVML</c>, compared without
/// case, the outermost first) takes what is under it into that rule's folder, keeping subfolders; any other file goes
/// by its extension (<c>.mm.dll</c> to <c>monomod</c>, anything else, <c>.dll</c> or not, to the default
/// <c>plugins</c>) under its own name, flattened. Folder rules put files in <c>&lt;route&gt;/&lt;Namespace-Name&gt;/</c>
/// (<c>installSubDir</c>); <c>config</c> is untracked and puts them in <c>BepInEx/config/</c> directly
/// (<c>installUntracked</c>). Thunderstore's own files at the zip root (<c>manifest.json</c>, <c>icon.png</c>,
/// <c>README.md</c>, <c>CHANGELOG.md</c>) land in the plugin folder too, as in r2modman.</item>
/// </list>
/// </summary>
public sealed partial class BepInExLoader : IModLoader
{
    public const string LoaderId = "bepinex";

    public const string WinHttp = "winhttp.dll";

    public const string DoorstopConfig = "doorstop_config.ini";

    /// <summary>Valheim's install rules in r2modman's <c>ecosystem.json</c> (same for the game and the dedicated server).</summary>
    private static readonly InstallRule[] Rules =
    [
        new("BepInEx/plugins", PerPackage: true, [".dll"]),
        new("BepInEx/core", PerPackage: true, []),
        new("BepInEx/patchers", PerPackage: true, []),
        new("BepInEx/monomod", PerPackage: true, [".mm.dll"]),
        new("BepInEx/config", PerPackage: false, []),
        new("BepInEx/SlimVML", PerPackage: true, []),
    ];

    /// <summary><c>isDefaultLocation</c>: where a file no extension rule claims goes.</summary>
    private static InstallRule DefaultRule => Rules[0];

    private static readonly LoaderEntryFile[] Entry =
    [
        new($"loader/{WinHttp}", WinHttp),
        new($"loader/{DoorstopConfig}", DoorstopConfig),
    ];

    public string Id => LoaderId;

    public IReadOnlyList<LoaderEntryFile> EntryFiles => Entry;

    public string ConfigFolder => "BepInEx/config";

    /// <summary>BepInEx's disk log, written into the set it runs from (S-M1, docs/13 §4.3).</summary>
    public string LogFile => "BepInEx/LogOutput.log";

    public IReadOnlyList<string> OffArguments { get; } = ["--doorstop-enabled", "false"];

    /// <summary>Doorstop 4's arguments, as r2modman passes them (<c>BepInExGameInstructions.ts</c>) and S-M1 checked.</summary>
    public IReadOnlyList<string> OnArguments(string setFolder) =>
        ["--doorstop-enabled", "true", "--doorstop-target-assembly", Path.Combine(setFolder, "BepInEx", "core", "BepInEx.Preloader.dll")];

    public string? Place(string package, bool isLoaderPackage, string entry)
    {
        var segments = entry.Split('/');
        return isLoaderPackage ? PlaceLoader(package, segments) : PlacePlugin(package, segments);
    }

    /// <summary>
    /// <c>server\doorstop_config.ini</c> says <c>enabled = false</c>, so a start without the arguments stays vanilla
    /// (docs/13 §4.3); the line is rewritten as <c>spikes/s-m1-doorstop/run.ps1</c> does. Other files go as they are.
    /// </summary>
    public byte[] EntryFileContent(LoaderEntryFile file, byte[] content)
    {
        if (file.ServerName != DoorstopConfig)
        {
            return content;
        }

        var text = Encoding.UTF8.GetString(content);
        if (!EnabledLine().IsMatch(text))
        {
            throw new InvalidDataException($"{DoorstopConfig} of the mod set has no 'enabled' line to turn off.");
        }

        return Encoding.UTF8.GetBytes(EnabledLine().Replace(text, "${indent}enabled = false"));
    }

    private static string? PlaceLoader(string package, string[] segments)
    {
        // r2modman's rootFolder is the package's name for both Valheim packs in ecosystem.json.
        var root = package[(package.IndexOf('-', StringComparison.Ordinal) + 1)..];
        var inRoot = segments.Length > 1 && segments[0].Equals(root, StringComparison.OrdinalIgnoreCase) ? segments[1..] : segments;
        if (inRoot.Length > 1 && inRoot[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
        {
            return "BepInEx/" + string.Join('/', inRoot[1..]);
        }

        if (inRoot.Length == 1 && Entry.FirstOrDefault(file => file.ServerName.Equals(inRoot[0], StringComparison.OrdinalIgnoreCase)) is { } entryFile)
        {
            return entryFile.SetPath;
        }

        return null;
    }

    private static string PlacePlugin(string package, string[] segments)
    {
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (Rules.FirstOrDefault(rule => rule.Name.Equals(segments[index], StringComparison.OrdinalIgnoreCase)) is { } folderRule)
            {
                return folderRule.Target(package, segments[(index + 1)..]);
            }
        }

        var file = segments[^1];
        // The longest matching extension wins (.mm.dll over .dll); no match goes to the default location.
        var byExtension = Rules
            .SelectMany(rule => rule.Extensions.Where(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).Select(extension => (Rule: rule, extension.Length)))
            .OrderByDescending(match => match.Length)
            .Select(match => match.Rule)
            .FirstOrDefault();
        return (byExtension ?? DefaultRule).Target(package, [file]);
    }

    [GeneratedRegex(@"^(?<indent>[ \t]*)enabled[ \t]*=[^\r\n]*", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex EnabledLine();

    /// <param name="PerPackage">r2modman's <c>subdir</c> tracking: files go in a folder named after the package.</param>
    private sealed record InstallRule(string Route, bool PerPackage, string[] Extensions)
    {
        public string Name => Route[(Route.LastIndexOf('/') + 1)..];

        public string Target(string package, string[] rest) =>
            PerPackage ? $"{Route}/{package}/{string.Join('/', rest)}" : $"{Route}/{string.Join('/', rest)}";
    }
}
