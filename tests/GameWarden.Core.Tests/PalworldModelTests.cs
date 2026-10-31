using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using GameWarden.Core.Instances;
using GameWarden.Core.Secrets;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests;

/// <summary>
/// The Core model of ADR-0022 against FakeGame's HTTP mode: a rendered <c>ue-struct-ini</c> file, http readiness and
/// stop, an internal port with no firewall rule, and a generated secret (docs/14 §6 phase 1).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PalworldModelTests
{
    private const string Schema = """
        {
          "groups": [
            { "id": "server", "label": "Server" },
            { "id": "world", "label": "World" }
          ],
          "settings": [
            { "key": "executable", "type": "string", "group": "server", "required": true },
            { "key": "ServerName", "type": "string", "group": "server", "default": "Default Palworld Server", "target": { "kind": "file", "file": "settings", "field": "ServerName" } },
            { "key": "ExpRate", "type": "number", "group": "world", "default": 1, "min": 0.1, "max": 20, "step": 0.1, "target": { "kind": "file", "file": "settings" } },
            { "key": "ServerPlayerMaxNum", "type": "int", "group": "server", "default": 32, "target": { "kind": "file", "file": "settings" } },
            { "key": "bIsUseBackupSaveData", "type": "bool", "group": "world", "default": true, "target": { "kind": "file", "file": "settings" } },
            { "key": "Difficulty", "type": "enum", "group": "world", "default": "None", "options": [{ "value": "None" }, { "value": "Normal" }, { "value": "Hard" }], "target": { "kind": "file", "file": "settings" } },
            { "key": "CrossplayPlatforms", "type": "list", "group": "server", "default": ["Steam"], "options": [{ "value": "Steam" }, { "value": "Xbox" }], "target": { "kind": "file", "file": "settings" } },
            { "key": "AdminPassword", "type": "secret", "group": "server", "required": true, "generate": true },
            { "key": "ServerPassword", "type": "secret", "group": "server" }
          ]
        }
        """;

    private const string Adapter = """
        {
          "schemaVersion": 1,
          "id": "pwfake",
          "name": "Palworld (fake)",
          "version": "0.1.0",
          "level": "L2",
          "source": { "type": "manual" },
          "launch": {
            "exe": "{settings.executable}",
            "args": ["--world", "{paths.data}/world.txt", "--rest-port", "{ports.rest}", "--rest-password", "{secrets.AdminPassword}", "--game-port", "{ports.game}", "--utf16-first-line", "--ready-after-ms", "50"]
          },
          "ports": [
            { "name": "game", "protocol": "udp", "default": 18211 },
            { "name": "rest", "protocol": "tcp", "default": 18212, "internal": true }
          ],
          "files": [
            {
              "name": "settings",
              "path": "{paths.server}/PalWorldSettings.ini",
              "template": "{paths.server}/DefaultPalWorldSettings.ini",
              "format": "ue-struct-ini",
              "section": "/Script/Pal.PalGameWorldSettings",
              "field": "OptionSettings",
              "fixed": {
                "RESTAPIEnabled": "True",
                "RESTAPIPort": "{ports.rest}",
                "AdminPassword": "\"{secrets.AdminPassword}\"",
                "ServerPassword": "\"{secrets.ServerPassword}\""
              }
            }
          ],
          "readiness": {
            "type": "http",
            "url": "http://127.0.0.1:{ports.rest}/v1/api/info",
            "status": 200,
            "auth": { "username": "admin", "password": "{secrets.AdminPassword}" },
            "timeoutSec": 30,
            "intervalSec": 1
          },
          "stop": {
            "method": "http",
            "timeoutSec": 20,
            "requests": [
              { "method": "POST", "url": "http://127.0.0.1:{ports.rest}/v1/api/save" },
              { "method": "POST", "url": "http://127.0.0.1:{ports.rest}/v1/api/shutdown", "body": "{\"waittime\": 1}" }
            ],
            "auth": { "username": "admin", "password": "{secrets.AdminPassword}" }
          },
          "backup": { "paths": ["{paths.data}"], "mode": "stop" },
          "resources": { "min": { "cpuCores": 1, "ramMb": 64, "diskMb": 10 } }
        }
        """;

    [Fact]
    public async Task Start_renders_the_settings_file_waits_for_http_and_stop_saves()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("pwfake", Adapter, new Dictionary<string, string> { ["settings.schema.json"] = Schema });
        var ports = FreePorts();
        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "pw-1",
                "pwfake",
                "Palworld",
                new Dictionary<string, string>
                {
                    ["executable"] = CoreHarness.FakeGameExe,
                    ["ServerName"] = "GameWarden test",
                    ["ExpRate"] = "2",
                    ["CrossplayPlatforms"] = "Steam,Xbox",
                },
                ports),
            TestContext.Current.CancellationToken);
        Assert.NotNull(created.Instance);

        var paths = InstancePaths.For(core.DataRoot, "pw-1");
        Directory.CreateDirectory(paths.Server);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "TestData", "PalworldTemplate-1.0.5.ini"),
            Path.Combine(paths.Server, "DefaultPalWorldSettings.ini"));

        var started = await core.Instance("pw-1").StartAsync();
        var running = await core.WaitForAsync("pw-1", snapshot => snapshot.State == ActualState.Running, "Running");

        Assert.Equal(CommandOutcome.Accepted, started.Outcome);
        var rendered = await File.ReadAllTextAsync(Path.Combine(paths.Server, "PalWorldSettings.ini"), TestContext.Current.CancellationToken);
        Assert.Contains("ServerName=\"GameWarden test\"", rendered, StringComparison.Ordinal);
        Assert.Contains("ExpRate=2.000000", rendered, StringComparison.Ordinal);
        Assert.Contains("CrossplayPlatforms=(Steam,Xbox)", rendered, StringComparison.Ordinal);
        Assert.Contains("bIsUseBackupSaveData=True", rendered, StringComparison.Ordinal);
        Assert.Contains("RESTAPIEnabled=True", rendered, StringComparison.Ordinal);
        Assert.Contains($"RESTAPIPort={ports["rest"]}", rendered, StringComparison.Ordinal);
        Assert.Contains("ServerPassword=\"\"", rendered, StringComparison.Ordinal);
        Assert.Matches(new Regex("AdminPassword=\"[A-Za-z0-9]{8,}\""), rendered);
        Assert.True(File.Exists(Path.Combine(paths.Config, "PalWorldSettings.ini")), "the rendered copy is in {paths.config}");

        Assert.Contains("AdminPassword", core.Get<ISecretStore>().Names(SecretScopes.Instance("pw-1")));
        Assert.DoesNotContain(core.Supervisor.FirewallRules("pw-1", "pwfake", running.Ports), rule => rule.Port == ports["rest"]);

        // The first console line is UTF-16; the lines after it still arrive (spike S-P1).
        var tail = await core.Instance("pw-1").TailAsync(200);
        Assert.Contains(tail, line => line.Text.Contains("FakeGame starting", StringComparison.Ordinal));

        await core.Instance("pw-1").StopAsync();
        await core.WaitForAsync("pw-1", snapshot => snapshot.State == ActualState.Stopped, "Stopped");
        Assert.Contains("rest-shutdown", await File.ReadAllTextAsync(core.WorldFile("pw-1"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_admin_password_fails_the_start_with_the_reason()
    {
        var policy = RestartPolicy.Default with { Backoff = [TimeSpan.FromMilliseconds(100)], MaxRestarts = 0 };
        await using var core = await CoreHarness.StartAsync(policy);
        await core.AddOwnerAdapterAsync("pwbad", BadAdapter, new Dictionary<string, string> { ["settings.schema.json"] = BadSchema });
        var ports = FreePorts();
        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "pw-2",
                "pwbad",
                "Palworld",
                new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe, ["restPassword"] = "right" },
                ports),
            TestContext.Current.CancellationToken);
        Assert.NotNull(created.Instance);

        var paths = InstancePaths.For(core.DataRoot, "pw-2");
        Directory.CreateDirectory(paths.Server);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "TestData", "PalworldTemplate-1.0.5.ini"),
            Path.Combine(paths.Server, "DefaultPalWorldSettings.ini"));
        core.Get<ISecretStore>().Set(SecretScopes.Instance("pw-2"), "AdminPassword", "wrong");

        await core.Instance("pw-2").StartAsync();
        var faulted = await core.WaitForAsync("pw-2", snapshot => snapshot.State == ActualState.Faulted, "Faulted");

        Assert.Contains("refused the configured credentials (401)", faulted.LastProblem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_template_refuses_the_start_with_a_clear_message()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("pwfake", Adapter, new Dictionary<string, string> { ["settings.schema.json"] = Schema });
        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "pw-3",
                "pwfake",
                "Palworld",
                new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe },
                FreePorts()),
            TestContext.Current.CancellationToken);
        Assert.NotNull(created.Instance);

        var result = await core.Instance("pw-3").StartAsync();

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains("template file was not found", result.Message, StringComparison.Ordinal);
        Assert.Contains("Install the game first", result.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, int> FreePorts()
    {
        static int Free()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        return new Dictionary<string, int> { ["game"] = Free(), ["rest"] = Free() };
    }

    /// <summary>The same adapter with the REST password coming from a setting, so the fake server and the check disagree.</summary>
    private const string BadAdapter = """
        {
          "schemaVersion": 1,
          "id": "pwbad",
          "name": "Palworld (bad password)",
          "version": "0.1.0",
          "level": "L2",
          "source": { "type": "manual" },
          "launch": {
            "exe": "{settings.executable}",
            "args": ["--world", "{paths.data}/world.txt", "--rest-port", "{ports.rest}", "--rest-password", "{settings.restPassword}", "--ready-after-ms", "50"]
          },
          "ports": [
            { "name": "game", "protocol": "udp", "default": 18311 },
            { "name": "rest", "protocol": "tcp", "default": 18312, "internal": true }
          ],
          "files": [
            {
              "name": "settings",
              "path": "{paths.server}/PalWorldSettings.ini",
              "template": "{paths.server}/DefaultPalWorldSettings.ini",
              "format": "ue-struct-ini",
              "section": "/Script/Pal.PalGameWorldSettings",
              "field": "OptionSettings",
              "fixed": { "AdminPassword": "\"{secrets.AdminPassword}\"", "RESTAPIEnabled": "True", "RESTAPIPort": "{ports.rest}" }
            }
          ],
          "readiness": {
            "type": "http",
            "url": "http://127.0.0.1:{ports.rest}/v1/api/info",
            "status": 200,
            "auth": { "username": "admin", "password": "{secrets.AdminPassword}" },
            "timeoutSec": 30,
            "intervalSec": 1
          },
          "stop": { "method": "kill", "timeoutSec": 10 },
          "backup": { "paths": ["{paths.data}"], "mode": "stop" },
          "resources": { "min": { "cpuCores": 1, "ramMb": 64, "diskMb": 10 } }
        }
        """;

    private const string BadSchema = """
        {
          "groups": [{ "id": "server", "label": "Server" }],
          "settings": [
            { "key": "executable", "type": "string", "group": "server", "required": true },
            { "key": "restPassword", "type": "string", "group": "server", "default": "right" },
            { "key": "AdminPassword", "type": "secret", "group": "server", "required": true, "generate": true }
          ]
        }
        """;
}
