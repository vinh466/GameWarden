using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// gamewarden: status and emergency operations over Core's loopback API (docs/02-architecture.md §8).
// Global options: --url <http://127.0.0.1:8765> (or GAMEWARDEN_URL), --token-file <path> (or GAMEWARDEN_CLI_TOKEN).
const string Usage = """
    usage: gamewarden <command> [options]

      status                                   list instances
      adapters                                 list adapters and adapter problems
      create <id> --adapter <id> [--name <display name>] [--set key=value]...
      start <id> | stop <id> | restart <id>
      console <id> <command...>                send a console command to the game
      logs <id> [--tail <n>]                   recent console lines

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
    Console.Error.WriteLine("Run as Administrator on the GameWarden machine, or pass --token-file.");
    return 3;
}

using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

try
{
    return arguments[0] switch
    {
        "status" => await Status(),
        "adapters" => await Adapters(),
        "create" when arguments.Count >= 2 => await Create(arguments[1], arguments.Skip(2).ToList()),
        "start" or "stop" or "restart" when arguments.Count == 2 => await Command(arguments[1], arguments[0]),
        "console" when arguments.Count >= 3 => await SendConsole(arguments[1], string.Join(' ', arguments.Skip(2))),
        "logs" when arguments.Count >= 2 => await Logs(arguments[1], int.Parse(TakeOption(arguments, "--tail") ?? "100", CultureInfo.InvariantCulture)),
        _ => UsageError(),
    };
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Cannot reach GameWarden Core at {url}: {ex.Message}");
    return 3;
}

async Task<int> Status()
{
    var instances = await Get("/api/v1/instances");
    if (instances is not { } list)
    {
        return 1;
    }

    Console.WriteLine($"{"ID",-20} {"ADAPTER",-12} {"STATE",-12} {"DESIRED",-8} {"RUNNER",-7} PROBLEM");
    foreach (var instance in list.EnumerateArray())
    {
        Console.WriteLine(
            $"{Text(instance, "id"),-20} {Text(instance, "adapterId"),-12} {Text(instance, "state"),-12} {Text(instance, "desiredState"),-8} {Text(instance, "runnerPid"),-7} {Text(instance, "lastProblem")}");
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
        var separator = pair.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return UsageError();
        }

        settings[pair[..separator]] = pair[(separator + 1)..];
    }

    var response = await http.PostAsJsonAsync("/api/v1/instances", new { id, adapterId = adapter, displayName = name, settings });
    return await Report(response);
}

async Task<int> Command(string id, string verb) =>
    await Report(await http.PostAsync($"/api/v1/instances/{Uri.EscapeDataString(id)}/{verb}", content: null));

async Task<int> SendConsole(string id, string command) =>
    await Report(await http.PostAsJsonAsync($"/api/v1/instances/{Uri.EscapeDataString(id)}/console", new { command }));

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

async Task<int> Report(HttpResponseMessage response)
{
    if (!response.IsSuccessStatusCode)
    {
        await PrintProblem(response);
        return 1;
    }

    var instance = await response.Content.ReadFromJsonAsync<JsonElement>();
    Console.WriteLine($"{Text(instance, "id")}: {Text(instance, "state")} (desired {Text(instance, "desiredState")})");
    return 0;
}

static async Task PrintProblem(HttpResponseMessage response)
{
    var body = await response.Content.ReadAsStringAsync();
    try
    {
        var problem = JsonDocument.Parse(body).RootElement;
        Console.Error.WriteLine($"{(int)response.StatusCode} {Text(problem, "code")}: {Text(problem, "title")}");
    }
    catch (JsonException)
    {
        Console.Error.WriteLine($"{(int)response.StatusCode}: {body}");
    }
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

int UsageError()
{
    Console.Error.WriteLine(Usage);
    return 2;
}
