using GameWarden.Core.Adapters;
using GameWarden.Core.Checks;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Network;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ProductionPartsTests
{
    private static AdapterDefinition Valheimish => new()
    {
        SchemaVersion = 1,
        Id = "demo",
        Name = "Demo",
        Version = "1.0.0",
        Level = "L1",
        Source = new AdapterSource("steamcmd", 1),
        Launch = new AdapterLaunch(
            "{paths.server}\\demo.exe",
            ["-name", "{settings.serverName}", "-password", "{secrets.password}", "-port", "{ports.game}"],
            Env: new Dictionary<string, string> { ["WORLD"] = "{settings.world}" },
            ConsoleLogFile: "{paths.logs}\\console.log"),
        Ports = [new AdapterPort("game", "udp", 2456), new AdapterPort("query", "udp", Offset: new PortOffset("game", 1))],
        Readiness = new AdapterReadiness("logRegex", "stdout", "ready"),
        Stop = new AdapterStop("ctrl-c"),
        Backup = new AdapterBackup(["{paths.data}"], "stop"),
        Resources = new AdapterResources(new ResourceAmounts(2, 2048, 2000), new ResourceAmounts(4, 4096, 4000)),
        Folder = @"C:\adapters\demo",
    };

    [Theory]
    [InlineData(new[] { "plain", "two words" }, "plain \"two words\"")]
    [InlineData(new[] { @"C:\Program Files\GameWarden\" }, "\"C:\\Program Files\\GameWarden\\\\\"")]
    [InlineData(new[] { "say \"hi\"" }, "\"say \\\"hi\\\"\"")]
    [InlineData(new[] { @"a\\b", "" }, @"a\\b """"")]
    public void Command_lines_quote_like_the_C_runtime_expects(string[] arguments, string expected)
    {
        Assert.Equal(expected, CommandLine.Build(arguments));
    }

    [Fact]
    public void A_second_instance_moves_all_ports_by_ten()
    {
        var ports = Ports.Allocate(Valheimish, usedByInstances: [2456, 2457], isListening: (_, _) => false);

        Assert.Equal(2466, ports["game"]);
        Assert.Equal(2467, ports["query"]);
    }

    [Fact]
    public void Ports_used_by_other_programs_are_skipped_too()
    {
        var ports = Ports.Allocate(Valheimish, usedByInstances: [], isListening: (protocol, port) => protocol == "udp" && port == 2457);

        Assert.Equal(2466, ports["game"]);
    }

    [Fact]
    public void Resource_shortfalls_name_what_is_missing()
    {
        var small = new MachineSnapshot(CpuCores: 1, RamTotalMb: 4096, RamFreeMb: 1024, DiskFreeMb: 2100);

        var start = MachineResources.Shortfalls(Valheimish.Resources!.Min, small, forInstall: false);
        var install = MachineResources.Shortfalls(Valheimish.Resources!.Min, small, forInstall: true);

        Assert.Equal(["2 CPU cores (have 1)", "2048 MB free RAM (have 1024 MB)"], start);
        Assert.Equal(["free disk 2400 MB (have 2100 MB)"], install); // 2000 MB + 20 %
        Assert.Empty(MachineResources.Shortfalls(Valheimish.Resources!.Min, small with { CpuCores = 8, RamFreeMb = 8192 }, forInstall: false));
    }

    [Fact]
    public void Required_values_come_from_the_launch_templates()
    {
        var required = LaunchSpecBuilder.Required(Valheimish);

        Assert.Equal(["serverName", "world"], required.Settings);
        Assert.Equal(["password"], required.Secrets);
    }

    [Fact]
    public void Missing_values_are_explained_with_the_command_that_fixes_them()
    {
        var required = LaunchSpecBuilder.Required(Valheimish);

        var message = required.DescribeMissing("vh", new Dictionary<string, string> { ["serverName"] = "Vikings" }, []);

        Assert.Equal("Missing settings: world (gamewarden set vh world=...). Missing secret: password (gamewarden secret vh password).", message);
        Assert.Null(required.DescribeMissing("vh", new Dictionary<string, string> { ["serverName"] = "a", ["world"] = "b" }, ["password"]));
    }

    [Fact]
    public void Secrets_and_the_console_log_file_are_rendered_into_the_launch_spec()
    {
        var paths = InstancePaths.For(@"C:\GameWarden", "vh");

        var spec = LaunchSpecBuilder.Build(
            Valheimish,
            "vh",
            "Vikings",
            paths,
            new Dictionary<string, int> { ["game"] = 2456, ["query"] = 2457 },
            new Dictionary<string, string> { ["serverName"] = "Vikings", ["world"] = "Midgard" },
            new Dictionary<string, string> { ["password"] = "s3cret" });

        Assert.Equal(["-name", "Vikings", "-password", "s3cret", "-port", "2456"], spec.Arguments);
        Assert.Equal(@"C:\GameWarden\instances\vh\logs\console.log", spec.ConsoleLogFile);
        Assert.Equal("Midgard", spec.Environment["WORLD"]);
    }

    [Fact]
    public void A_host_that_dns_answers_with_loopback_is_reported_as_blocked()
    {
        Assert.Equal("blocked", NetworkCheck.Classify([System.Net.IPAddress.Loopback]));
        Assert.Equal("blocked", NetworkCheck.Classify([System.Net.IPAddress.Any]));
        Assert.Equal("ok", NetworkCheck.Classify([System.Net.IPAddress.Parse("23.40.63.163")]));
        Assert.Equal("unresolved", NetworkCheck.Classify([]));
    }

    [Fact]
    public void A_configured_steamcmd_url_goes_first_and_the_defaults_stay_as_fallbacks()
    {
        var options = new GameWardenOptions { SteamCmdDownloadUrl = "https://mirror.example/steamcmd.zip" };

        Assert.Equal(
            ["https://mirror.example/steamcmd.zip", .. GameWardenOptions.DefaultSteamCmdDownloadUrls],
            options.ResolvedSteamCmdDownloadUrls);
        Assert.StartsWith("https://media.steampowered.com/", new GameWardenOptions().ResolvedSteamCmdDownloadUrls[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_prerequisites_make_an_adapter_invalid()
    {
        var errors = AdapterCatalog.Validate(Valheimish with { Prerequisites = ["vcredist-x64", "dotnet-48"] });

        Assert.Single(errors);
        Assert.Contains("dotnet-48", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preshutdown_stops_the_host_and_marks_a_system_shutdown()
    {
        var lifetime = new FakeLifetime();
        var signal = new ShutdownSignal();
        using var service = new WindowsServiceLifetime(lifetime, signal, NullLogger<WindowsServiceLifetime>.Instance);

        Assert.Equal(0u, service.OnControl(WindowsServiceLifetime.ControlPreshutdown));

        await lifetime.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(signal.SystemShuttingDown);
    }

    [Fact]
    public async Task Service_stop_stops_the_host_but_leaves_the_games_running()
    {
        var lifetime = new FakeLifetime();
        var signal = new ShutdownSignal();
        using var service = new WindowsServiceLifetime(lifetime, signal, NullLogger<WindowsServiceLifetime>.Instance);

        Assert.Equal(0u, service.OnControl(WindowsServiceLifetime.ControlStop));
        Assert.Equal(120u, service.OnControl(0x80)); // custom codes are not implemented

        await lifetime.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(signal.SystemShuttingDown);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public TaskCompletionSource StopRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopRequested.TrySetResult();
    }
}
