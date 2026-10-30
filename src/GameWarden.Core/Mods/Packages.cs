using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GameWarden.Core.Mods;

/// <summary>
/// Thunderstore package names and versions (docs/13 §3): a package is <c>Namespace-Name</c>, both parts letters, digits
/// and <c>_</c>; a version is <c>x.y.z</c>; a dependency is <c>Namespace-Name-x.y.z</c>. The strict form also keeps
/// names safe as folder and file names (<c>plugins\&lt;Namespace-Name&gt;\</c>, <c>cache\&lt;Namespace-Name-version&gt;.zip</c>).
/// </summary>
public static partial class Packages
{
    public static bool IsPackage(string? package) => package is not null && PackagePattern().IsMatch(package);

    public static bool IsVersion(string? version) => version is not null && VersionPattern().IsMatch(version) && Version.TryParse(version, out _);

    /// <summary>Compares two <c>x.y.z</c> versions by number (<c>1.10.0</c> is newer than <c>1.9.0</c>).</summary>
    public static int CompareVersions(string left, string right) => Version.Parse(left).CompareTo(Version.Parse(right));

    /// <summary>Splits <c>Namespace-Name-x.y.z</c>.</summary>
    public static bool TryParseDependency(string? text, [NotNullWhen(true)] out PackageRef? dependency)
    {
        dependency = null;
        var match = text is null ? Match.Empty : DependencyPattern().Match(text);
        if (!match.Success || !IsVersion(match.Groups["version"].Value))
        {
            return false;
        }

        dependency = new PackageRef(match.Groups["package"].Value, match.Groups["version"].Value);
        return true;
    }

    /// <summary>The zip's name in <c>cache\</c>.</summary>
    public static string CacheFileName(string package, string version) => $"{package}-{version}.zip";

    [GeneratedRegex("^[A-Za-z0-9_]+-[A-Za-z0-9_]+$")]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"^\d{1,9}\.\d{1,9}\.\d{1,9}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^(?<package>[A-Za-z0-9_]+-[A-Za-z0-9_]+)-(?<version>\d{1,9}\.\d{1,9}\.\d{1,9})$")]
    private static partial Regex DependencyPattern();
}

/// <summary>One version of a package.</summary>
public sealed record PackageRef(string Package, string Version)
{
    public override string ToString() => $"{Package}-{Version}";
}
