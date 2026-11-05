using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameWarden.Cli;

/// <summary>
/// <c>gamewarden agent-host</c>: the Owner's Operations Agent settings, Discord ids and secrets (docs/20).
/// The bot token and model key are write-only; the API returns only "set".
/// </summary>
public sealed class AgentHostCommands(HttpClient http, TextWriter output, TextWriter error)
{
    public const string Usage = """
        agent-host                               show the Operations Agent status
        agent-host enable|disable                turn the Agent and its Discord bot on or off
        agent-host set [--guild <id>] [--channel <instance>=<id>]... [--model <id>]
                       [--base-url <url>] [--language vi|en] [--max-tools <n>]
                       [--timeout <seconds>] [--approval-minutes <n>]
                       [--allow-user <discord-id>]... [--allow-role <discord-id>]...
        agent-host secret <model-api-key|discord-bot-token> [--value <v>]
                                                set a write-only secret (prompted when no --value)
        """;

    public async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] == "status")
        {
            return await ShowAsync();
        }

        return args[0] switch
        {
            "enable" => await SetEnabledAsync(true),
            "disable" => await SetEnabledAsync(false),
            "set" => await SetAsync(args.Skip(1).ToList()),
            "secret" when args.Count >= 2 => await SecretAsync(args[1], TakeOption(args.ToList(), "--value")),
            _ => UsageError(),
        };
    }

    private async Task<int> ShowAsync()
    {
        var view = await GetAsync();
        if (view is null)
        {
            return 1;
        }

        var settings = view["settings"]!;
        var model = settings["model"]!;
        var discord = settings["discord"]!;
        output.WriteLine($"Enabled:          {Yes(view["enabled"])}");
        output.WriteLine($"Model:            {model["model"]} @ {model["baseUrl"]}");
        output.WriteLine($"Model ready:      {Yes(view["modelConfigured"])}");
        output.WriteLine($"Model key set:    {Yes(view["modelKeySet"])}");
        output.WriteLine($"Bot token set:    {Yes(view["botTokenSet"])}");
        output.WriteLine($"Bot connected:    {Yes(view["botConnected"])}");
        output.WriteLine($"Guild:            {discord["guildId"]}");
        output.WriteLine($"Language:         {discord["language"]}");
        output.WriteLine($"Require mention:  {Yes(discord["requireMention"])}");
        output.WriteLine("Channels:");
        foreach (var channel in discord["channels"]!.AsObject())
        {
            output.WriteLine($"  {channel.Key,-24} {channel.Value}");
        }

        if (view["lastError"]?.GetValue<string>() is { Length: > 0 } lastError)
        {
            output.WriteLine($"Last error:       {lastError}");
        }

        return 0;
    }

    private async Task<int> SetEnabledAsync(bool enabled)
    {
        var view = await GetAsync();
        if (view is null)
        {
            return 1;
        }

        var settings = view["settings"]!.DeepClone();
        settings["enabled"] = enabled;
        return await PutAsync(settings);
    }

    private async Task<int> SetAsync(List<string> args)
    {
        var view = await GetAsync();
        if (view is null)
        {
            return 1;
        }

        var settings = view["settings"]!.DeepClone();
        var model = settings["model"]!.AsObject();
        var discord = settings["discord"]!.AsObject();
        var autonomy = settings["autonomy"]!.AsObject();

        if (TakeOption(args, "--model") is { } modelId)
        {
            model["model"] = modelId;
        }

        if (TakeOption(args, "--base-url") is { } baseUrl)
        {
            model["baseUrl"] = baseUrl;
        }

        if (TakeOption(args, "--max-tools") is { } maxTools)
        {
            model["maxToolCalls"] = int.Parse(maxTools, CultureInfo.InvariantCulture);
        }

        if (TakeOption(args, "--timeout") is { } timeout)
        {
            model["timeoutSeconds"] = int.Parse(timeout, CultureInfo.InvariantCulture);
        }

        if (TakeOption(args, "--approval-minutes") is { } minutes)
        {
            autonomy["approvalTimeoutMinutes"] = int.Parse(minutes, CultureInfo.InvariantCulture);
        }

        if (TakeOption(args, "--guild") is { } guild)
        {
            discord["guildId"] = guild;
        }

        if (TakeOption(args, "--language") is { } language)
        {
            discord["language"] = language;
        }

        if (TakeOption(args, "--require-mention") is { } mention)
        {
            discord["requireMention"] = bool.Parse(mention);
        }

        var channels = discord["channels"]!.AsObject();
        while (TakeOption(args, "--channel") is { } pair)
        {
            if (!TrySplit(pair, out var key, out var value))
            {
                error.WriteLine("--channel needs <instance>=<discord-channel-id>.");
                return 2;
            }

            channels[key] = value;
        }

        var users = discord["allowedUserIds"]!.AsArray();
        while (TakeOption(args, "--allow-user") is { } user)
        {
            users.Add(user);
        }

        var roles = discord["allowedRoleIds"]!.AsArray();
        while (TakeOption(args, "--allow-role") is { } role)
        {
            roles.Add(role);
        }

        return await PutAsync(settings);
    }

    private async Task<int> SecretAsync(string name, string? value)
    {
        if (name is not ("model-api-key" or "discord-bot-token"))
        {
            error.WriteLine("secret name must be model-api-key or discord-bot-token.");
            return 2;
        }

        value ??= ReadSecret($"Value for {name}: ");
        if (string.IsNullOrWhiteSpace(value))
        {
            error.WriteLine("Empty value; nothing changed.");
            return 2;
        }

        var response = await http.PutAsJsonAsync(
            $"/api/v1/system/agent-host/secrets/{Uri.EscapeDataString(name)}",
            new { value });
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        output.WriteLine($"{name} is set (write-only; it is never shown again).");
        return 0;
    }

    private async Task<JsonNode?> GetAsync()
    {
        var response = await http.GetAsync("/api/v1/system/agent-host");
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JsonNode>();
    }

    private async Task<int> PutAsync(JsonNode settings)
    {
        var response = await http.PutAsJsonAsync("/api/v1/system/agent-host", settings);
        if (!response.IsSuccessStatusCode)
        {
            await PrintProblem(response);
            return 1;
        }

        output.WriteLine("Operations Agent settings saved.");
        return await ShowAsync();
    }

    private async Task PrintProblem(HttpResponseMessage response)
    {
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var title = problem.RootElement.TryGetProperty("title", out var value) ? value.GetString() : null;
            error.WriteLine(title ?? $"Core answered HTTP {(int)response.StatusCode}.");
        }
        catch (JsonException)
        {
            error.WriteLine($"Core answered HTTP {(int)response.StatusCode}.");
        }
    }

    private static string Yes(JsonNode? value) => value?.GetValue<bool>() == true ? "yes" : "no";

    private static bool TrySplit(string pair, out string key, out string value)
    {
        var index = pair.IndexOf('=');
        key = index > 0 ? pair[..index] : string.Empty;
        value = index > 0 ? pair[(index + 1)..] : string.Empty;
        return key.Length > 0 && value.Length > 0;
    }

    private static string? TakeOption(List<string> args, string name)
    {
        var index = args.IndexOf(name);
        if (index < 0 || index + 1 >= args.Count)
        {
            return null;
        }

        var value = args[index + 1];
        args.RemoveRange(index, 2);
        return value;
    }

    private static string ReadSecret(string prompt)
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

    private int UsageError()
    {
        error.WriteLine(Usage);
        return 2;
    }
}
