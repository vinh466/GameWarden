namespace GameWarden.Core.Mods;

/// <summary>A mod with its version fixed: from a code, or picked by the Owner (the latest when they named none).</summary>
public sealed record ModChoice(string Package, string Version, bool Enabled);

/// <param name="Origin"><see cref="ModResolver.Chosen"/> or <see cref="ModResolver.Dependency"/>.</param>
public sealed record ResolvedMod(string Package, string Version, bool Enabled, string Origin);

public sealed record ModResolution(IReadOnlyList<ResolvedMod> Mods, IReadOnlyList<string> Warnings);

/// <summary>The set's dependencies cannot be worked out: a cycle, a dependency Core cannot read, no settled answer.</summary>
public sealed class ModResolveException(string message) : Exception(message);

/// <summary>
/// Works out a set from the chosen mods (docs/13 §4.5). Pure apart from the two lookups it is given (Thunderstore in
/// the job, dictionaries in tests):
/// <list type="bullet">
/// <item>a chosen version never changes;</item>
/// <item>a missing dependency is added at the highest version any mod of the set names;</item>
/// <item>a present mod at a lower version than another names stays, with a warning (r2modman installs newer
/// dependencies too, so mods are taken to treat them as minimums, 13 §4.5 [Unverified]);</item>
/// <item>the loader's own package is a dependency like any other: added at the latest version when nothing names it;</item>
/// <item>only enabled mods pull in dependencies; a cycle is refused.</item>
/// </list>
/// </summary>
public static class ModResolver
{
    public const string Chosen = "chosen";

    public const string Dependency = "dependency";

    /// <summary>Adding a dependency can change which version of another is named highest; real sets settle in a few rounds.</summary>
    private const int MaxRounds = 50;

    /// <param name="dependencies">A version's dependencies as Thunderstore writes them (<c>Namespace-Name-x.y.z</c>).</param>
    /// <param name="latestVersion">A package's latest version; asked only for the loader package.</param>
    public static async Task<ModResolution> ResolveAsync(
        IReadOnlyList<ModChoice> chosen,
        string? loaderPackage,
        Func<PackageRef, Task<IReadOnlyList<string>>> dependencies,
        Func<string, Task<string>> latestVersion)
    {
        var fixedMods = new Dictionary<string, ModChoice>(StringComparer.Ordinal);
        foreach (var choice in chosen)
        {
            if (!fixedMods.TryAdd(choice.Package, choice))
            {
                throw new ModResolveException($"{choice.Package} is in the list twice.");
            }
        }

        var added = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, List<(string Version, string NamedBy)>> named = [];
        string? loaderLatest = null;
        for (var round = 1; ; round++)
        {
            if (round > MaxRounds)
            {
                throw new ModResolveException($"The dependencies did not settle after {MaxRounds} rounds.");
            }

            named = await NamedVersionsAsync(Members(fixedMods, added), dependencies);
            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (package, names) in named.Where(pair => !fixedMods.ContainsKey(pair.Key)))
            {
                next[package] = names.Select(name => name.Version).Aggregate((a, b) => Packages.CompareVersions(a, b) >= 0 ? a : b);
            }

            if (loaderPackage is not null && !fixedMods.ContainsKey(loaderPackage) && !next.ContainsKey(loaderPackage))
            {
                next[loaderPackage] = loaderLatest ??= await latestVersion(loaderPackage);
            }

            if (next.Count == added.Count && next.All(pair => added.TryGetValue(pair.Key, out var version) && version == pair.Value))
            {
                break;
            }

            added = next;
        }

        var members = Members(fixedMods, added);
        await RefuseCyclesAsync(members, dependencies);

        var warnings = new List<string>();
        foreach (var (package, names) in named.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var present = fixedMods.TryGetValue(package, out var choice) ? choice.Version : added[package];
            foreach (var (version, namedBy) in names.Where(name => Packages.CompareVersions(name.Version, present) > 0))
            {
                warnings.Add($"{package} {present} is older than the {version} that {namedBy} names.");
            }

            if (choice is { Enabled: false })
            {
                warnings.Add($"{package} is disabled, but {string.Join(", ", names.Select(name => name.NamedBy).Distinct())} needs it.");
            }
        }

        return new ModResolution(
            [
                .. chosen.Select(choice => new ResolvedMod(choice.Package, choice.Version, choice.Enabled, Chosen)),
                .. added.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => new ResolvedMod(pair.Key, pair.Value, true, Dependency)),
            ],
            warnings);
    }

    /// <summary>The enabled mods: chosen ones, then added dependencies.</summary>
    private static List<PackageRef> Members(Dictionary<string, ModChoice> fixedMods, Dictionary<string, string> added) =>
    [
        .. fixedMods.Values.Where(choice => choice.Enabled).Select(choice => new PackageRef(choice.Package, choice.Version)),
        .. added.Select(pair => new PackageRef(pair.Key, pair.Value)),
    ];

    /// <summary>Every version of a package that a member names, with who names it; a package naming itself is ignored.</summary>
    private static async Task<Dictionary<string, List<(string Version, string NamedBy)>>> NamedVersionsAsync(
        List<PackageRef> members,
        Func<PackageRef, Task<IReadOnlyList<string>>> dependencies)
    {
        var named = new Dictionary<string, List<(string Version, string NamedBy)>>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            foreach (var dependency in await ParsedAsync(member, dependencies))
            {
                if (dependency.Package == member.Package)
                {
                    continue;
                }

                if (!named.TryGetValue(dependency.Package, out var list))
                {
                    named[dependency.Package] = list = [];
                }

                list.Add((dependency.Version, member.ToString()));
            }
        }

        return named;
    }

    private static async Task RefuseCyclesAsync(List<PackageRef> members, Func<PackageRef, Task<IReadOnlyList<string>>> dependencies)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            edges[member.Package] = [.. (await ParsedAsync(member, dependencies)).Select(d => d.Package).Where(package => package != member.Package)];
        }

        // Depth-first with three colours: a grey node reached again closes a cycle.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();
        foreach (var start in edges.Keys.Order(StringComparer.Ordinal))
        {
            Visit(start);
        }

        void Visit(string package)
        {
            if (!edges.ContainsKey(package) || state.GetValueOrDefault(package) == 2)
            {
                return;
            }

            if (state.GetValueOrDefault(package) == 1)
            {
                var cycle = path[path.IndexOf(package)..].Append(package);
                throw new ModResolveException($"The dependencies form a cycle: {string.Join(" -> ", cycle)}.");
            }

            state[package] = 1;
            path.Add(package);
            foreach (var next in edges[package])
            {
                Visit(next);
            }

            path.RemoveAt(path.Count - 1);
            state[package] = 2;
        }
    }

    private static async Task<IReadOnlyList<PackageRef>> ParsedAsync(PackageRef member, Func<PackageRef, Task<IReadOnlyList<string>>> dependencies)
    {
        var parsed = new List<PackageRef>();
        foreach (var text in await dependencies(member))
        {
            parsed.Add(Packages.TryParseDependency(text, out var dependency)
                ? dependency
                : throw new ModResolveException($"{member} names a dependency Core cannot read: '{text}'."));
        }

        return parsed;
    }
}
