using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// gamewarden: status and operations over Core's loopback API (docs/02-architecture.md §8).
// Global options: --url <http://127.0.0.1:8765> (or GAMEWARDEN_URL), --token-file <path> (or GAMEWARDEN_CLI_TOKEN).
const string Usage = """
    usage: gamewarden <command> [options]        (run from an Administrator terminal)

      status                                   list instances
      system                                   version, isolation, machine resources, prerequisites
      adapters                                 list adapters and adapter problems
      create <id> --adapter <id> [--name <display name>] [--set key=value]...
      settings <id>                            settings, required settings and which secrets are set
      set <id> key=value [key=value]...        change settings (key= removes one); applies on the next start
      secret <id> <name> [--value <v>]         set a secret such as the server password (prompted when no --value)
      install <id> [--force] [--wait]          install the game with SteamCMD
      update-game <id> [--wait]                update the game; a running server is stopped and started again
      jobs [<id>]                              recent jobs
      job <job-id> [--log] [--wait]            one job, or its log
      start <id> [--force] | stop <id> | restart <id> [--force]
      console <id> <command...>                send a console command to the game
      logs <id> [--tail <n>]                   recent console lines
      delete <id> --purge [--confirm <id>]     delete the instance with all its files, saves included

    options: --url <url>  --token-file <path>
    """;

var arguments = args.ToList();
var url = TakeOption(arguments, "--url") ?? Environment.GetEnvironmentVariable("GAMEWARDEN_URL") ?? "http://127.0.0.1:8765";
var tokenFile = TakeOption(arguments, "--token-file")
    ?? Environment.GetEnvironmentVariable("GAMEWARDEN_CLI_TOKEN")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameWarden", "cli.token");

if (arguments.Count == 0 || arguments[0] is "help" or "--help" or "-h")
{
    Console.WriteLine(Usage);
    return arguments.Count == 0 ? 2 : 0;
}

string token;
try
{
    token = (await File.ReadAllTextAsync(tokenFile)).Trim();
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Cannot read the CLI token at {tokenFile}: {ex.Message}");
    Console.Error.WriteLine("Run the terminal as Administrator on the GameWarden machine, or pass --token-file.");
    return 3;
}

using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(150) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

try
{
    var force = TakeFlag(arguments, "--force");
    var wait = TakeFlag(arguments, "--wait");
    return arguments[0] switch
    {
        "status" => await Status(),
        "system" => await SystemInfo(),
        "adapters" => await Adapters(),
        "create" when arguments.Count >= 2 => await Create(arguments[1], arguments.Skip(2).ToList()),
        "settings" when arguments.Count == 2 => await Settings(arguments[1]),
        "set" when arguments.Count >= 3 => await Set(arguments[1], arguments.Skip(2).ToList()),
        "secret" when arguments.Count >= 3 => await Secret(arguments[1], arguments[2], TakeOption(arguments, "--value")),
        "install" when arguments.Count == 2 => await StartJob(arguments[1], "install", force, wait),
        "update-game" when arguments.Count == 2 => await StartJob(arguments[1], "update", force: false, wait),
        "jobs" => await Jobs(arguments.Count >= 2 ? arguments[1] : null),
        "job" when arguments.Count >= 2 => await Job(arguments[1], TakeFlag(arguments, "--log"), wait),
        "start" or "restart" when arguments.Count == 2 => await Command(arguments[1], arguments[0] + (force ? "?force=true" : string.Empty)),
        "stop" when arguments.Count == 2 => await Command(arguments[1], "stop"),
        "console" when arguments.Count >= 3 => await SendConsole(arguments[1], string.Join(' ', arguments.Skip(2))),
        "logs" when arguments.Count >= 2 => await Logs(arguments[1], int.Parse(TakeOption(arguments, "--tail") ?? "100", CultureInfo.InvariantCulture)),
        "delete" when arguments.Count >= 2 => await Delete(arguments[1], TakeFlag(arguments, "--purge"), TakeOption(arguments, "--confirm")),
        _ => UsageError(),
    };
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Cannot reach GameWarden Core at {url}: {ex.Message}");
    Console.Error.WriteLine("Is the GameWardenCore service running? (services.msc, or: sc query GameWardenCore)");
    return 3;
}

async Task<int> Status()
{
    if (await Get("/api/v1/instances") is not { } list)
    {
        return 1;
    }

    Console.WriteLine($"{"ID",-20} {"ADAPTER",-10} {"STATE",-12} {"DESIRED",-8} {"PORTS",-18} {"JOB",-22} PROBLEM");
    foreach (var instance in list.EnumerateArray())
    {
        var ports = instance.TryGetProperty("ports", out var p) && p.ValueKind == JsonValueKind.Object
            ? string.Join(",", p.EnumerateObject().Select(port => port.Value.GetRawText()))
            : "-";
        Console.WriteLine(
            $"{Text(instance, "id"),-20} {Text(instance, "adapterId"),-10} {Text(instance, "state"),-12} {Text(instance, "desiredState"),-8} {ports,-18} {Text(instance, "activeJobId"),-22} {Text(instance, "lastProblem")}");
    }

    return 0;
}

async Task<int> SystemInfo()
{
    if (await Get("/api/v1/system") is not { } system)
    {
        return 1;
    }

    var machine = system.GetProperty("machine");
    Console.WriteLine($"GameWarden Core {Text(system, "version")}, started {Text(system, "startedAt")}");
    Console.WriteLine($"Data root   {Text(system, "dataRoot")}");
    Console.WriteLine($"Isolation   {(system.GetProperty("isolation").GetBoolean() ? $"on (games run as {Text(system, "runAsUser")})" : "off (development)")}");
    Console.WriteLine($"VPN ranges  {string.Join(", ", system.GetProperty("vpnRanges").EnumerateArray().Select(r => r.GetString()))}");
    Console.WriteLine($"Machine     {Text(machine, "cpuCores")} CPU cores, {Text(machine, "ramFreeMb")} of {Text(machine, "ramTotalMb")} MB RAM free, {Text(machine, "diskFreeMb")} MB disk free");
    Console.WriteLine($"SteamCMD    {(system.GetProperty("steamCmdInstalled").GetBoolean() ? "installed" : "not yet (downloaded by the first install)")}");
    foreach (var prerequisite in system.GetProperty("prerequisites").EnumerateArray())
    {
        var installed = prerequisite.GetProperty("installed").GetBoolean();
        Console.WriteLine($"  {(installed ? "ok     " : "MISSING")} {Text(prerequisite, "id"),-15} {(installed ? string.Empty : Text(prerequisite, "hint"))}");
    }

    return 0;
}

async Task<int> Adapters()
{
    if (await Get("/api/v1/adapters") is not { } result)
    {
        return 1;
    }

    foreach (var adapter in result.GetProperty("adapters").EnumerateArray())
    {
        var origin = adapter.GetProperty("isBuiltIn").GetBoolean() ? "built-in" : "owner";
        Console.WriteLine($"{Text(adapter, "id"),-14} {Text(adapter, "level"),-3} {Text(adapter, "version"),-9} {origin,-9} {Text(adapter, "name")}");
    }

    foreach (var problem in result.GetProperty("problems").EnumerateArray())
    {
        Console.WriteLine($"INVALID {Text(problem, "folder")}: {string.Join("; ", problem.GetProperty("errors").EnumerateArray().Select(e => e.GetString()))}");
    }

    return 0;
}

async Task<int> Create(string id, List<string> options)
{
    var adapter = TakeOption(options, "--adapter");
    if (adapter is null)
    {
        return UsageError();
    }

    var name = TakeOption(options, "--name");
    var settings = new Dictionary<string, string>();
    while (TakeOption(options, "--set") is { } pair)
    {
        if (!TrySplit(pair, out var key, out var value))
        {
            return UsageError();
        }

        settings[key] = value;
    }

    var response = await http.PostAsJsonAsync("/api/v1/instances", new { id, adapterId = adapter, displayName = name, settings });
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    var instance = await response.Content.ReadFromJsonAsync<JsonElement>();
    PrintInstance(instance);
    if (Text(instance, "state") == "notInstalled")
    {
        Console.WriteLine($"Next: gamewarden settings {id}   then   gamewarden install {id} --wait");
    }

    return 0;
}

async Task<int> Settings(string id)
{
    if (await Get($"/api/v1/instances/{Uri.EscapeDataString(id)}/settings") is not { } view)
    {
        return 1;
    }

    var settings = view.GetProperty("settings");
    var required = view.GetProperty("requiredSettings").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
    foreach (var name in required.Union(settings.EnumerateObject().Select(s => s.Name)).Order(StringComparer.Ordinal))
    {
        var value = settings.TryGetProperty(name, out var v) ? v.GetString() : null;
        Console.WriteLine($"  {name,-20} {value ?? "(not set)"}{(required.Contains(name) ? string.Empty : "   (not used by the adapter)")}");
    }

    foreach (var secret in view.GetProperty("secrets").EnumerateObject())
    {
        Console.WriteLine($"  {secret.Name,-20} {(secret.Value.GetProperty("set").GetBoolean() ? "(secret, set)" : $"(secret, NOT set: gamewarden secret {id} {secret.Name})")}");
    }

    if (view.GetProperty("restartRequired").GetBoolean())
    {
        Console.WriteLine("Changes apply on the next start: gamewarden restart " + id);
    }

    return 0;
}

async Task<int> Set(string id, List<string> pairs)
{
    var settings = new Dictionary<string, string?>();
    foreach (var pair in pairs)
    {
        if (!TrySplit(pair, out var key, out var value))
        {
            return UsageError();
        }

        settings[key] = value.Length == 0 ? null : value;
    }

    var response = await http.PutAsJsonAsync($"/api/v1/instances/{Uri.EscapeDataString(id)}/settings", new { settings });
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    return await Settings(id);
}

async Task<int> Secret(string id, string name, string? value)
{
    value ??= ReadSecret($"Value for secret '{name}' of {id}: ");
    if (string.IsNullOrEmpty(value))
    {
        Console.Error.WriteLine("Empty value; nothing changed.");
        return 2;
    }

    var response = await http.PutAsJsonAsync(
        $"/api/v1/instances/{Uri.EscapeDataString(id)}/settings",
        new { secrets = new Dictionary<string, string> { [name] = value } });
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    Console.WriteLine($"Secret '{name}' of {id} is set (stored encrypted; it is never shown again).");
    return 0;
}

async Task<int> StartJob(string id, string verb, bool force, bool wait)
{
    var response = await http.PostAsync($"/api/v1/instances/{Uri.EscapeDataString(id)}/{verb}{(force ? "?force=true" : string.Empty)}", content: null);
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
    Console.WriteLine($"Job {jobId} started ({verb} of {id}).");
    if (!wait)
    {
        Console.WriteLine($"Follow it with: gamewarden job {jobId} --wait");
        return 0;
    }

    return await Job(jobId, log: false, wait: true);
}

async Task<int> Jobs(string? instanceId)
{
    var query = instanceId is null ? string.Empty : $"?instanceId={Uri.EscapeDataString(instanceId)}";
    if (await Get("/api/v1/jobs" + query) is not { } jobs)
    {
        return 1;
    }

    Console.WriteLine($"{"JOB",-22} {"TYPE",-8} {"INSTANCE",-18} {"STATE",-10} {"STAGE",-20} MESSAGE");
    foreach (var job in jobs.EnumerateArray())
    {
        Console.WriteLine($"{Text(job, "id"),-22} {Text(job, "type"),-8} {Text(job, "instanceId"),-18} {Text(job, "state"),-10} {Text(job, "stage"),-20} {Text(job, "message")}");
    }

    return 0;
}

async Task<int> Job(string jobId, bool log, bool wait)
{
    var path = $"/api/v1/jobs/{Uri.EscapeDataString(jobId)}";
    if (log)
    {
        if (await Get(path + "/log?tail=1000") is not { } result)
        {
            return 1;
        }

        foreach (var line in result.GetProperty("lines").EnumerateArray())
        {
            Console.WriteLine(line.GetString());
        }

        return 0;
    }

    string? shown = null;
    while (true)
    {
        if (await Get(path) is not { } job)
        {
            return 1;
        }

        var state = Text(job, "state");
        var progress = job.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number ? $" {p.GetDouble():0.0}%" : string.Empty;
        var line = $"{state} {Text(job, "stage")}{progress}";
        if (line != shown)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
            shown = line;
        }

        if (state is "succeeded" or "failed")
        {
            Console.WriteLine(Text(job, "message"));
            if (state == "failed")
            {
                Console.WriteLine($"Log: gamewarden job {jobId} --log");
            }

            return state == "succeeded" ? 0 : 1;
        }

        if (!wait)
        {
            return 0;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }
}

async Task<int> Command(string id, string verb)
{
    var response = await http.PostAsync($"/api/v1/instances/{Uri.EscapeDataString(id)}/{verb}", content: null);
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    PrintInstance(body.GetProperty("instance"));
    Console.WriteLine(Text(body, "message"));
    return 0;
}

async Task<int> SendConsole(string id, string command)
{
    var response = await http.PostAsJsonAsync($"/api/v1/instances/{Uri.EscapeDataString(id)}/console", new { command });
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    Console.WriteLine("Sent. See the output with: gamewarden logs " + id);
    return 0;
}

async Task<int> Logs(string id, int tail)
{
    if (await Get($"/api/v1/instances/{Uri.EscapeDataString(id)}/console?tail={tail}") is not { } result)
    {
        return 1;
    }

    foreach (var line in result.GetProperty("lines").EnumerateArray())
    {
        var at = line.GetProperty("at").GetDateTimeOffset().ToLocalTime();
        Console.WriteLine($"{at:HH:mm:ss} {line.GetProperty("text").GetString()}");
    }

    return 0;
}

async Task<int> Delete(string id, bool purge, string? confirm)
{
    if (!purge)
    {
        Console.Error.WriteLine("Only --purge is available: it deletes the instance with all its files, saves included.");
        return 2;
    }

    if (confirm is null)
    {
        Console.Write($"This deletes {id} and all its files, saves included. Type the instance id to confirm: ");
        confirm = Console.ReadLine()?.Trim();
    }

    var response = await http.DeleteAsync($"/api/v1/instances/{Uri.EscapeDataString(id)}?mode=purge&confirm={Uri.EscapeDataString(confirm ?? string.Empty)}");
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Console.WriteLine($"Deleted {id}.");
    if (Text(body, "leftovers") is { } leftovers and not "-")
    {
        Console.WriteLine(leftovers);
    }

    return 0;
}

async Task<JsonElement?> Get(string path)
{
    var response = await http.GetAsync(path);
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return null;
    }

    return await response.Content.ReadFromJsonAsync<JsonElement>();
}

static void PrintInstance(JsonElement instance) =>
    Console.WriteLine($"{Text(instance, "id")}: {Text(instance, "state")} (desired {Text(instance, "desiredState")})");

static async Task PrintProblem(HttpResponseMessage response)
{
    var body = await response.Content.ReadAsStringAsync();
    try
    {
        var problem = JsonDocument.Parse(body).RootElement;
        Console.Error.WriteLine($"{(int)response.StatusCode} {Text(problem, "code")}: {Text(problem, "title")}");
        if (response.StatusCode == HttpStatusCode.PreconditionFailed && Text(problem, "code") == "resources_insufficient")
        {
            Console.Error.WriteLine("Add --force to go ahead anyway.");
        }
    }
    catch (JsonException)
    {
        Console.Error.WriteLine($"{(int)response.StatusCode}: {body}");
    }
}

static string ReadSecret(string prompt)
{
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine()?.TrimEnd('\r', '\n') ?? string.Empty;
    }

    Console.Write(prompt);
    var value = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return value.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (value.Length > 0)
            {
                value.Length--;
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            value.Append(key.KeyChar);
        }
    }
}

static bool TrySplit(string pair, out string key, out string value)
{
    var separator = pair.IndexOf('=', StringComparison.Ordinal);
    key = separator > 0 ? pair[..separator] : string.Empty;
    value = separator > 0 ? pair[(separator + 1)..] : string.Empty;
    return separator > 0;
}

static string Text(JsonElement element, string property) =>
    element.TryGetProperty(property, out var value) && value.ValueKind is not JsonValueKind.Null
        ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
        : "-";

static string? TakeOption(List<string> list, string name)
{
    var index = list.IndexOf(name);
    if (index < 0 || index + 1 >= list.Count)
    {
        return null;
    }

    var value = list[index + 1];
    list.RemoveRange(index, 2);
    return value;
}

static bool TakeFlag(List<string> list, string name) => list.Remove(name);

int UsageError()
{
    Console.Error.WriteLine(Usage);
    return 2;
}
