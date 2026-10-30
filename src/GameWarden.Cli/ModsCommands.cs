using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameWarden.Cli;

/// <summary>
/// <c>gamewarden mods …</c> (docs/13 §4.11) over Core's API, with the conventions of the other commands: a problem is
/// printed as <c>&lt;status&gt; &lt;code&gt;: &lt;title&gt;</c> with exit code 1, a usage error is exit code 2, and a
/// command that starts a <c>mods.sync</c> job follows it with <c>--wait</c>. <c>add</c>, <c>remove</c> and
/// <c>update</c> send the active set's chosen mods with the change; Core adds the dependencies again.
/// </summary>
public sealed class ModsCommands(HttpClient http, TextWriter output, TextWriter error)
{
    public const string Usage = """
          mods <id>                                mod sets, Mods on/off, the last share code and newer versions
          mods import <id> <code> [--yes] [--wait] preview an r2modman profile code; --yes applies it
          mods add <id> <Namespace-Name>[@<version>] [--wait]
                                                   add a Thunderstore mod (the latest version by default)
          mods remove <id> <Namespace-Name> [--wait]
          mods update <id> [<Namespace-Name> | --all] [--wait]
                                                   check for newer versions; update one mod or all
          mods share <id>                          an r2modman code of the mods for friends (no config files)
          mods on <id> | mods off <id>             load the mods at the next start, or run vanilla
          mods rollback <id>                       back to the previous mod set
          mods configs <id> [<path>]               the active set's config files, or one file's text
          mods configs <id> <path> --history       the file's revisions, newest first
          mods configs <id> <path> --restore <revision>
                                                   write a revision back into the file (a new revision)
        """;

    private static readonly string[] Subcommands = ["import", "add", "remove", "update", "share", "on", "off", "rollback", "configs"];

    /// <summary>How often <c>--wait</c> asks for the job (tests make it shorter).</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <param name="arguments">The words after <c>mods</c>, global options already taken.</param>
    public async Task<int> RunAsync(List<string> arguments, bool wait)
    {
        var yes = arguments.Remove("--yes");
        var all = arguments.Remove("--all");
        var history = arguments.Remove("--history");
        string? restore = null;
        if (arguments.IndexOf("--restore") is var at and >= 0)
        {
            if (at + 1 >= arguments.Count)
            {
                return UsageError();
            }

            restore = arguments[at + 1];
            arguments.RemoveRange(at, 2);
        }

        if ((history || restore is not null) && arguments is not ["configs", _, _])
        {
            return UsageError();
        }

        return arguments switch
        {
            [var id] when !Subcommands.Contains(id) => await Show(id),
            ["import", var id, var code] => await Import(id, code, yes, wait),
            ["add", var id, var package] => await Add(id, package, wait),
            ["remove", var id, var package] => await Remove(id, package, wait),
            ["update", var id] => await Update(id, null, all, wait),
            ["update", var id, var package] when !all => await Update(id, package, all: false, wait),
            ["share", var id] => await Share(id),
            ["on" or "off", var id] => await SetEnabled(id, arguments[0] == "on"),
            ["rollback", var id] => await Rollback(id),
            ["configs", var id] => await Configs(id),
            ["configs", var id, var path] when history && restore is null => await ConfigHistory(id, path),
            ["configs", var id, var path] when restore is not null && !history => await ConfigRestore(id, path, restore),
            ["configs", var id, var path] when !history && restore is null => await ConfigContent(id, path),
            _ => UsageError(),
        };
    }

    /// <summary>Splits <c>Namespace-Name[@version]</c>; the version is null when none is named.</summary>
    public static (string Package, string? Version) ParsePackage(string text)
    {
        var at = text.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? (text, null) : (text[..at], text[(at + 1)..]);
    }

    private async Task<int> Show(string id)
    {
        if (await Get(ModsPath(id)) is not { } mods)
        {
            return 1;
        }

        output.WriteLine($"{id}: Mods {(mods.GetProperty("enabled").GetBoolean() ? "on" : "off")} (loader {Text(mods, "loader")})");
        var newer = NewerVersions(mods);
        if (Set(mods, "active") is { } active)
        {
            output.WriteLine($"Active set {Text(active, "id")} ({Text(active, "origin")}, {Time(active, "createdAt")}):");
            PrintEntries(active, newer);
        }
        else
        {
            output.WriteLine($"No active mod set. Add mods: gamewarden mods import {id} <code>   or   gamewarden mods add {id} <Namespace-Name>");
        }

        if (Set(mods, "previous") is { } previous)
        {
            output.WriteLine($"Previous set {Text(previous, "id")} ({Text(previous, "origin")}, {Time(previous, "createdAt")}, {previous.GetProperty("entries").GetArrayLength()} mods): gamewarden mods rollback {id}");
        }

        if (Set(mods, "pending") is { } pending)
        {
            output.WriteLine($"Set {Text(pending, "id")} is {Text(pending, "state")} (job {Text(pending, "jobId")}): gamewarden job {Text(pending, "jobId")} --log");
        }

        if (mods.TryGetProperty("shareCode", out var code) && code.ValueKind == JsonValueKind.String)
        {
            output.WriteLine($"Share code for friends: {code.GetString()} (made {Time(mods, "shareCodeCreatedAt")})");
        }

        if (mods.TryGetProperty("newerVersions", out var check) && check.ValueKind == JsonValueKind.Object)
        {
            output.WriteLine(newer.Count == 0
                ? $"No newer versions (checked {Time(check, "checkedAt")})."
                : $"{newer.Count} newer version(s) (checked {Time(check, "checkedAt")}): gamewarden mods update {id} --all");
        }

        return 0;
    }

    private async Task<int> Import(string id, string code, bool yes, bool wait)
    {
        var response = await http.PostAsJsonAsync(ModsPath(id) + "/code-preview", new { code });
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entries = preview.GetProperty("entries");
        var configs = preview.GetProperty("configFiles").EnumerateArray().Select(file => file.GetString()!).ToList();
        output.WriteLine($"Code {Text(preview, "code")}: profile \"{Text(preview, "profileName")}\", {entries.GetArrayLength()} mod(s), {configs.Count} config file(s).");
        output.WriteLine($"  {"PACKAGE",-40} {"VERSION",-12} {"ENABLED",-8} THUNDERSTORE");
        foreach (var entry in entries.EnumerateArray())
        {
            output.WriteLine($"  {Text(entry, "package"),-40} {Text(entry, "version"),-12} {(entry.GetProperty("enabled").GetBoolean() ? "yes" : "no"),-8} {Text(entry, "state")}");
        }

        if (configs.Count > 0)
        {
            output.WriteLine($"Config files (they go over the mods' defaults and the current configs when the set is built): {string.Join(", ", configs.Take(10))}{(configs.Count > 10 ? $" and {configs.Count - 10} more" : string.Empty)}");
        }

        foreach (var warning in preview.GetProperty("warnings").EnumerateArray())
        {
            output.WriteLine($"Warning: {warning.GetString()}");
        }

        if (!yes)
        {
            output.WriteLine($"Nothing changed. Apply it with: gamewarden mods import {id} {Text(preview, "code")} --yes");
            return 0;
        }

        return await StartSync(id, new { previewId = Text(preview, "previewId") }, wait);
    }

    private async Task<int> Add(string id, string text, bool wait)
    {
        var (package, version) = ParsePackage(text);
        if (await ChosenAsync(id) is not { } chosen)
        {
            return 1;
        }

        var index = chosen.FindIndex(entry => entry.Package == package);
        if (index >= 0 && (version is null || version == chosen[index].Version))
        {
            output.WriteLine($"{package} {chosen[index].Version} is already in the set; nothing changed.");
            return 0;
        }

        var entry = new ModEntry(package, version, Enabled: true);
        if (index >= 0)
        {
            chosen[index] = entry;
        }
        else
        {
            chosen.Add(entry);
        }

        return await StartSync(id, new { entries = chosen }, wait);
    }

    private async Task<int> Remove(string id, string package, bool wait)
    {
        if (await Get(ModsPath(id)) is not { } mods)
        {
            return 1;
        }

        var entries = Set(mods, "active") is { } active ? active.GetProperty("entries").EnumerateArray().ToList() : [];
        var found = entries.FirstOrDefault(entry => Text(entry, "package") == package);
        if (found.ValueKind == JsonValueKind.Undefined)
        {
            error.WriteLine($"{package} is not in the active set of {id}.");
            return 1;
        }

        if (Text(found, "origin") != "chosen")
        {
            error.WriteLine($"{package} is a dependency of other mods in the set; remove those instead.");
            return 1;
        }

        return await StartSync(id, new { entries = Chosen(entries).Where(entry => entry.Package != package).ToList() }, wait);
    }

    private async Task<int> Update(string id, string? package, bool all, bool wait)
    {
        var response = await http.PostAsync(ModsPath(id) + "/check-updates", content: null);
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        var updates = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updates").EnumerateArray()
            .Select(update => (Package: Text(update, "package"), Current: Text(update, "current"), Latest: Text(update, "latest")))
            .ToList();
        if (await ChosenAsync(id) is not { } chosen)
        {
            return 1;
        }

        if (updates.Count == 0)
        {
            output.WriteLine("Every mod is at its latest version.");
            return 0;
        }

        output.WriteLine($"  {"PACKAGE",-40} {"CURRENT",-12} {"LATEST",-12} ORIGIN");
        foreach (var update in updates)
        {
            output.WriteLine($"  {update.Package,-40} {update.Current,-12} {update.Latest,-12} {(chosen.Any(entry => entry.Package == update.Package) ? "chosen" : "dependency")}");
        }

        if (package is null && !all)
        {
            output.WriteLine($"Nothing changed. Update them with: gamewarden mods update {id} --all   (or name one mod)");
            return 0;
        }

        var selected = updates.Where(update => (all || update.Package == package) && chosen.Any(entry => entry.Package == update.Package)).ToList();
        if (package is not null && selected.Count == 0)
        {
            if (updates.Any(update => update.Package == package))
            {
                error.WriteLine($"{package} is a dependency; pin a version with: gamewarden mods add {id} {package}@<version>");
            }
            else
            {
                error.WriteLine($"{package} has no newer version, or is not in the set.");
            }

            return 1;
        }

        if (selected.Count == 0)
        {
            output.WriteLine("Only dependencies have newer versions; the mods that need them name their versions. Nothing changed.");
            return 0;
        }

        var latest = selected.ToDictionary(update => update.Package, update => update.Latest, StringComparer.Ordinal);
        var entries = chosen.Select(entry => latest.TryGetValue(entry.Package, out var version) ? entry with { Version = version } : entry).ToList();
        return await StartSync(id, new { entries }, wait);
    }

    private async Task<int> Share(string id)
    {
        var response = await http.PostAsync(ModsPath(id) + "/share-code", content: null);
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        var share = await response.Content.ReadFromJsonAsync<JsonElement>();
        output.WriteLine($"Share code for friends: {Text(share, "code")}");
        output.WriteLine("In r2modman: Import profile, From code, then paste it. It carries the mods and their versions, no config files.");
        return 0;
    }

    private async Task<int> SetEnabled(string id, bool enabled)
    {
        var response = await http.PutAsJsonAsync(ModsPath(id) + "/enabled", new { enabled });
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        var mods = await response.Content.ReadFromJsonAsync<JsonElement>();
        output.WriteLine($"Mods are {(enabled ? "on" : "off")} for {id}; it applies at the next start: gamewarden restart {id}");
        if (enabled && Set(mods, "active") is null)
        {
            output.WriteLine($"There is no ready mod set yet, so a start is refused until one is: gamewarden mods import {id} <code>");
        }

        return 0;
    }

    private async Task<int> Rollback(string id)
    {
        var response = await http.PostAsync(ModsPath(id) + "/rollback", content: null);
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        var mods = await response.Content.ReadFromJsonAsync<JsonElement>();
        var active = Set(mods, "active");
        output.WriteLine($"Back to set {(active is { } set ? Text(set, "id") : "-")}; it applies at the next start: gamewarden restart {id}");
        return 0;
    }

    private async Task<int> Configs(string id)
    {
        if (await Get(ModsPath(id) + "/configs") is not { } list)
        {
            return 1;
        }

        var files = list.GetProperty("files");
        if (files.GetArrayLength() == 0)
        {
            output.WriteLine(list.GetProperty("setId").ValueKind == JsonValueKind.Null ? $"{id} has no active mod set." : "The active set has no config files yet.");
            return 0;
        }

        output.WriteLine($"Config files of set {Text(list, "setId")}:");
        foreach (var file in files.EnumerateArray())
        {
            output.WriteLine($"  {Text(file, "size"),10}  {Time(file, "modified")}  {Text(file, "path")}");
        }

        return 0;
    }

    private async Task<int> ConfigContent(string id, string path)
    {
        if (await Get($"{ModsPath(id)}/configs/content?path={Uri.EscapeDataString(path)}") is not { } file)
        {
            return 1;
        }

        output.Write(Text(file, "text"));
        return 0;
    }

    private async Task<int> ConfigHistory(string id, string path)
    {
        if (await Get($"{ModsPath(id)}/configs/revisions?path={Uri.EscapeDataString(path)}") is not { } list)
        {
            return 1;
        }

        var revisions = list.GetProperty("revisions");
        if (revisions.GetArrayLength() == 0)
        {
            output.WriteLine($"{Text(list, "path")} has no revisions yet; the first save in the panel makes one.");
            return 0;
        }

        output.WriteLine($"Revisions of {Text(list, "path")}, newest first:");
        output.WriteLine($"  {"ID",8}  {"WHEN",-16}  {"KIND",-8}  {"SIZE",10}  SHA-256");
        foreach (var revision in revisions.EnumerateArray())
        {
            output.WriteLine($"  {Text(revision, "id"),8}  {Time(revision, "createdAt"),-16}  {Text(revision, "kind"),-8}  {Text(revision, "size"),10}  {Text(revision, "sha256")[..12]}");
        }

        output.WriteLine($"Restore one: gamewarden mods configs {id} {path} --restore <id>");
        return 0;
    }

    /// <summary>The restore names the file's current SHA-256, so a file the game wrote in between is not overwritten.</summary>
    private async Task<int> ConfigRestore(string id, string path, string revision)
    {
        if (!long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revisionId))
        {
            error.WriteLine($"'{revision}' is not a revision id; list them with: gamewarden mods configs {id} {path} --history");
            return 2;
        }

        if (await Get($"{ModsPath(id)}/configs/revisions/{revisionId.ToString(CultureInfo.InvariantCulture)}") is not { } found)
        {
            return 1;
        }

        if (Text(found, "path") != path.Replace('\\', '/'))
        {
            error.WriteLine($"Revision {revisionId.ToString(CultureInfo.InvariantCulture)} is of {Text(found, "path")}, not {path}.");
            return 1;
        }

        if (await Get($"{ModsPath(id)}/configs/content?path={Uri.EscapeDataString(path)}") is not { } file)
        {
            return 1;
        }

        var response = await http.PostAsJsonAsync(
            $"{ModsPath(id)}/configs/revisions/{revisionId.ToString(CultureInfo.InvariantCulture)}/restore",
            new { baseSha256 = Text(file, "sha256") });
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        output.WriteLine($"{Text(saved, "path")} restored to revision {revisionId.ToString(CultureInfo.InvariantCulture)} (new revision {Text(saved, "revisionId")}).");
        output.WriteLine(saved.GetProperty("running").GetBoolean()
            ? $"The server runs this set: mods that watch their file apply it now, the others at the next restart: gamewarden restart {id}"
            : "It applies when the server starts.");
        return 0;
    }

    /// <summary><c>PUT /mods</c> and, with <c>--wait</c>, the job until it ends.</summary>
    private async Task<int> StartSync(string id, object body, bool wait)
    {
        var response = await http.PutAsJsonAsync(ModsPath(id), body);
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
        output.WriteLine($"Job {jobId} started (mods.sync of {id}); the server keeps running.");
        if (!wait)
        {
            output.WriteLine($"Follow it with: gamewarden job {jobId} --wait");
            return 0;
        }

        string? shown = null;
        while (true)
        {
            if (await Get($"/api/v1/jobs/{Uri.EscapeDataString(jobId)}") is not { } job)
            {
                return 1;
            }

            var state = Text(job, "state");
            var line = $"{state} {Text(job, "stage")}";
            if (line != shown)
            {
                output.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
                shown = line;
            }

            if (state is "succeeded" or "failed")
            {
                output.WriteLine(Text(job, "message"));
                output.WriteLine(state == "succeeded" ? $"Restart to apply: gamewarden restart {id}" : $"Log: gamewarden job {jobId} --log");
                return state == "succeeded" ? 0 : 1;
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>The active set's chosen mods with their versions, or null when the mods cannot be read.</summary>
    private async Task<List<ModEntry>?> ChosenAsync(string id) =>
        await Get(ModsPath(id)) is { } mods
            ? Chosen(Set(mods, "active") is { } active ? active.GetProperty("entries").EnumerateArray() : []).ToList()
            : null;

    private static IEnumerable<ModEntry> Chosen(IEnumerable<JsonElement> entries) =>
        entries.Where(entry => Text(entry, "origin") == "chosen")
            .Select(entry => new ModEntry(Text(entry, "package"), Text(entry, "version"), entry.GetProperty("enabled").GetBoolean()));

    private void PrintEntries(JsonElement set, Dictionary<string, string> newer)
    {
        output.WriteLine($"  {"PACKAGE",-40} {"VERSION",-12} {"ENABLED",-8} {"ORIGIN",-11} NEWER");
        foreach (var entry in set.GetProperty("entries").EnumerateArray())
        {
            var package = Text(entry, "package");
            output.WriteLine(
                $"  {package,-40} {Text(entry, "version"),-12} {(entry.GetProperty("enabled").GetBoolean() ? "yes" : "no"),-8} {Text(entry, "origin"),-11} {newer.GetValueOrDefault(package, string.Empty)}");
        }
    }

    private static Dictionary<string, string> NewerVersions(JsonElement mods) =>
        mods.TryGetProperty("newerVersions", out var check) && check.ValueKind == JsonValueKind.Object
            ? check.GetProperty("updates").EnumerateArray().ToDictionary(update => Text(update, "package"), update => Text(update, "latest"), StringComparer.Ordinal)
            : [];

    private static JsonElement? Set(JsonElement mods, string name) =>
        mods.TryGetProperty(name, out var set) && set.ValueKind == JsonValueKind.Object ? set : null;

    private static string ModsPath(string id) => $"/api/v1/instances/{Uri.EscapeDataString(id)}/mods";

    private async Task<JsonElement?> Get(string path)
    {
        var response = await http.GetAsync(path);
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task PrintProblem(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            var problem = JsonDocument.Parse(body).RootElement;
            error.WriteLine($"{(int)response.StatusCode} {Text(problem, "code")}: {Text(problem, "title")}");
        }
        catch (JsonException)
        {
            error.WriteLine($"{(int)response.StatusCode}: {body}");
        }
    }

    private int UsageError()
    {
        error.WriteLine("usage: gamewarden mods …");
        error.WriteLine(Usage);
        return 2;
    }

    private static string Time(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var at)
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "-";

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
            : "-";

    /// <summary>An entry of <c>PUT /mods</c>; a null version is the latest.</summary>
    private sealed record ModEntry(string Package, string? Version, bool Enabled);
}
