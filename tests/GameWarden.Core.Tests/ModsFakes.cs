using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using GameWarden.Core.Mods;
using GameWarden.Core.Processes;

namespace GameWarden.Core.Tests;

/// <summary>
/// The loader of the tests (docs/13 §4.12): the loader package's files go into <c>loader/</c>, every other package's
/// into <c>plugins/&lt;Namespace-Name&gt;/</c>; Thunderstore's own files at a zip's root are left out. It has no entry
/// files; its arguments are <c>--mods &lt;set&gt;</c> and <c>--mods-off</c>.
/// </summary>
internal sealed class FakeModLoader : IModLoader
{
    public const string LoaderId = "fake";

    private static readonly string[] PackageFiles = ["manifest.json", "icon.png", "README.md", "CHANGELOG.md"];

    public string Id => LoaderId;

    public IReadOnlyList<LoaderEntryFile> EntryFiles => [];

    public string ConfigFolder => "config";

    public IReadOnlyList<string> OffArguments => ["--mods-off"];

    public IReadOnlyList<string> OnArguments(string setFolder) => ["--mods", setFolder];

    public byte[] EntryFileContent(LoaderEntryFile file, byte[] content) => content;

    public string? Place(string package, bool isLoaderPackage, string entry) =>
        PackageFiles.Contains(entry, StringComparer.Ordinal) ? null
        : isLoaderPackage ? $"loader/{entry}"
        : $"plugins/{package}/{entry}";
}

/// <summary>
/// Starts Runners as the current user and keeps the arguments of each launch spec, which the Runner deletes once it
/// has read it.
/// </summary>
internal sealed class SpecRecordingProcessHost(IProcessHost inner) : IProcessHost
{
    private readonly ConcurrentDictionary<string, string[]> _arguments = new(StringComparer.Ordinal);

    /// <summary>The arguments of the last launch spec written to <paramref name="launchSpecPath"/>.</summary>
    public string[] Arguments(string launchSpecPath) => _arguments[launchSpecPath];

    public RunnerProcess StartRunner(string runnerPath, string launchSpecPath, string workingDirectory)
    {
        using (var spec = JsonDocument.Parse(File.ReadAllText(launchSpecPath)))
        {
            _arguments[launchSpecPath] = [.. spec.RootElement.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!)];
        }

        return inner.StartRunner(runnerPath, launchSpecPath, workingDirectory);
    }

    public bool IsAlive(int pid, DateTimeOffset? startedAt) => inner.IsAlive(pid, startedAt);

    public void Kill(int pid) => inner.Kill(pid);
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// Thunderstore with recorded answers (docs/13 §4.12). The JSON has the shape the live API gave on 2026-10-30 for
/// <c>denikson-BepInExPack_Valheim</c>, <c>RandyKnapp-EpicLoot</c> 0.15.4, <c>ValheimModding-Jotunn</c> 2.30.2 and
/// <c>ValheimModding-JsonDotNET</c> 13.0.4, with their real dependencies; the zips are small stand-ins. Downloads are
/// answered directly rather than through Thunderstore's redirect to its CDN.
/// </summary>
internal sealed class FakeThunderstore : HttpMessageHandler
{
    public const string Loader = "denikson-BepInExPack_Valheim";

    private readonly ConcurrentDictionary<string, SortedDictionary<Version, FakePackageVersion>> _packages = new(StringComparer.Ordinal);

    /// <summary>The start of the pack's <c>doorstop_config.ini</c> (5.4.2360), with its CRLF line ends.</summary>
    public const string DoorstopConfig = "# General options for Unity Doorstop\r\n[General]\r\n\r\n# Enable Doorstop?\r\nenabled = true\r\n\r\n"
        + "target_assembly=BepInEx\\core\\BepInEx.Preloader.dll\r\n\r\n[UnityMono]\r\ndebug_enabled = false\r\n";

    public FakeThunderstore()
    {
        // Laid out like the real pack: its files under BepInExPack_Valheim/, Thunderstore's at the root.
        foreach (var version in new[] { "5.4.2333", "5.4.2351", "5.4.2360" })
        {
            Add(
                Loader,
                version,
                [],
                ("README.md", "pack"),
                ("BepInExPack_Valheim/winhttp.dll", "doorstop " + version),
                ("BepInExPack_Valheim/doorstop_config.ini", DoorstopConfig),
                ("BepInExPack_Valheim/start_server_bepinex.sh", "#!/bin/sh"),
                ("BepInExPack_Valheim/BepInEx/core/BepInEx.Preloader.dll", "preloader " + version),
                ("BepInExPack_Valheim/BepInEx/config/BepInEx.cfg", "[Logging]"));
        }

        Add("ValheimModding-JsonDotNET", "13.0.4", [], ("BepInEx/plugins/Newtonsoft.Json.dll", "json"));
        Add("ValheimModding-Jotunn", "2.30.2", [$"{Loader}-5.4.2333"], ("plugins/Jotunn.dll", "jotunn"));
        Add(
            "RandyKnapp-EpicLoot",
            "0.15.4",
            ["ValheimModding-JsonDotNET-13.0.4", "ValheimModding-Jotunn-2.30.2", $"{Loader}-5.4.2351"],
            ("EpicLoot.dll", "epic loot"),
            ("config/randyknapp.mods.epicloot.cfg", "[General]"));
    }

    /// <summary>The community a package is listed in, by <c>Namespace-Name</c>; <c>valheim</c> when not set.</summary>
    public ConcurrentDictionary<string, string> Communities { get; } = new(StringComparer.Ordinal);

    /// <summary>Every request, in order, with its <c>User-Agent</c>.</summary>
    public ConcurrentQueue<(string Url, string UserAgent)> Requests { get; } = new();

    /// <summary>Answers a request before the recorded answers when it returns a response.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>While set, downloads wait for it: keeps a job running.</summary>
    public TaskCompletionSource? HoldDownloads { get; set; }

    public int Count(string urlPart) => Requests.Count(request => request.Url.Contains(urlPart, StringComparison.Ordinal));

    public void Add(string package, string version, string[] dependencies, params (string Name, string Content)[] files) =>
        _packages.GetOrAdd(package, _ => [])[Version.Parse(version)] = new FakePackageVersion(version, dependencies, Zip([("manifest.json", "{}"), .. files]));

    /// <summary>Thunderstore now serves other bytes for a version, as a replaced or tampered file would be.</summary>
    public void ReplaceZip(string package, string version, byte[] zip)
    {
        var versions = _packages[package];
        versions[Version.Parse(version)] = versions[Version.Parse(version)] with { Zip = zip };
    }

    public static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return memory.ToArray();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        Requests.Enqueue((url.ToString(), request.Headers.UserAgent.ToString()));
        if (Intercept?.Invoke(request) is { } intercepted)
        {
            return intercepted;
        }

        Assert.Equal("thunderstore.io", url.Host);
        var parts = url.AbsolutePath.Trim('/').Split('/');
        switch (parts)
        {
            case ["api", "experimental", "package", var ns, var name] when Find($"{ns}-{name}") is { } versions:
                return Json(PackageJson(ns, name, versions.Values.Last(), Communities.GetValueOrDefault($"{ns}-{name}", "valheim")));
            case ["api", "experimental", "package", var ns, var name, var version] when Find($"{ns}-{name}", version) is { } found:
                return Json(VersionJson(ns, name, found));
            case ["package", "download", var ns, var name, var version] when Find($"{ns}-{name}", version) is { } found:
                if (HoldDownloads is { } hold)
                {
                    await hold.Task.WaitAsync(cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(found.Zip) };
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"detail":"Not found."}""", Encoding.UTF8, "application/json") };
        }
    }

    private SortedDictionary<Version, FakePackageVersion>? Find(string package) => _packages.GetValueOrDefault(package);

    private FakePackageVersion? Find(string package, string version) =>
        Version.TryParse(version, out var parsed) && Find(package)?.GetValueOrDefault(parsed) is { } found ? found : null;

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static Dictionary<string, object?> PackageJson(string ns, string name, FakePackageVersion latest, string community) => new()
    {
        ["namespace"] = ns,
        ["name"] = name,
        ["full_name"] = $"{ns}-{name}",
        ["owner"] = ns,
        ["package_url"] = $"https://thunderstore.io/package/{ns}/{name}/",
        ["date_created"] = "2021-02-14T18:07:34.498403Z",
        ["date_updated"] = "2026-10-28T15:59:56.112484Z",
        ["rating_score"] = -1,
        ["is_pinned"] = ns == "denikson",
        ["is_deprecated"] = false,
        ["total_downloads"] = -1,
        ["latest"] = VersionJson(ns, name, latest),
        ["community_listings"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["has_nsfw_content"] = false,
                ["categories"] = new[] { "Ashlands Update" },
                ["community"] = community,
                ["review_status"] = "unreviewed",
            },
        },
    };

    private static Dictionary<string, object?> VersionJson(string ns, string name, FakePackageVersion version) => new()
    {
        ["namespace"] = ns,
        ["name"] = name,
        ["version_number"] = version.Version,
        ["full_name"] = $"{ns}-{name}-{version.Version}",
        ["description"] = $"{name} for the tests.",
        ["icon"] = $"https://ccdn.thunderstore.io/live/repository/icons/{ns}-{name}-{version.Version}.png",
        ["dependencies"] = version.Dependencies,
        ["download_url"] = $"https://thunderstore.io/package/download/{ns}/{name}/{version.Version}/",
        ["downloads"] = 16952,
        ["date_created"] = "2026-10-28T05:27:27.743012Z",
        ["website_url"] = "https://github.com/vinh466/GameWarden",
        ["is_active"] = true,
    };

    private sealed record FakePackageVersion(string Version, string[] Dependencies, byte[] Zip);
}
