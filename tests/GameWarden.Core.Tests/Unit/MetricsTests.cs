using System.Diagnostics;
using GameWarden.Core.Checks;
using GameWarden.Core.Metrics;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class MetricsTests
{
    [Theory]
    [InlineData(5000, 5000, 1, 100)] // one core busy the whole time on a one-core machine
    [InlineData(5000, 5000, 4, 25)] // the same on four cores is a quarter of the machine
    [InlineData(500, 5000, 4, 2.5)]
    [InlineData(0, 5000, 4, 0)]
    [InlineData(100, 0, 4, 0)] // no time passed
    public void Cpu_is_a_share_of_the_whole_machine(int usedMs, int elapsedMs, int cores, double percent) =>
        Assert.Equal(percent, MetricsSampler.CpuPercent(TimeSpan.FromMilliseconds(usedMs), TimeSpan.FromMilliseconds(elapsedMs), cores));

    [Fact]
    public void A_process_tree_follows_parents_down_and_ignores_the_rest()
    {
        (int, int)[] processes = [(1, 0), (10, 1), (11, 10), (12, 11), (13, 10), (20, 1), (21, 20), (30, 30)];

        Assert.Equal([10, 11, 13, 12], ProcessTree.WithDescendants(10, processes));
        Assert.Equal([30], ProcessTree.WithDescendants(30, processes)); // its own parent: not a loop
        Assert.Equal([99], ProcessTree.WithDescendants(99, processes)); // already gone
    }

    [Fact]
    public void Meminfo_gives_total_and_available_ram_in_mb()
    {
        const string meminfo = """
            MemTotal:       32768000 kB
            MemFree:         1024000 kB
            MemAvailable:   16384000 kB
            Buffers:          102400 kB
            """;

        Assert.Equal((32000L, 16000L), ProcFs.ParseMemInfo(meminfo));
        Assert.Null(ProcFs.ParseMemInfo("MemTotal: 1024 kB\n")); // a kernel without MemAvailable
        Assert.Null(ProcFs.ParseMemInfo(null));
    }

    [Fact]
    public void Stat_gives_idle_and_total_cpu_ticks_of_the_whole_machine()
    {
        const string stat = """
            cpu  100 20 30 400 50 6 7 8 90 10
            cpu0 50 10 15 200 25 3 3 4 45 5
            intr 12345
            """;

        // idle + iowait; user .. steal (guest and guest_nice are already in user and nice)
        Assert.Equal((450L, 621L), ProcFs.ParseCpuTimes(stat));
        Assert.Equal((400L, 550L), ProcFs.ParseCpuTimes("cpu  100 20 30 400\n"));
        Assert.Null(ProcFs.ParseCpuTimes("cpu0 1 2 3 4\n"));
        Assert.Null(ProcFs.ParseCpuTimes(null));
    }

    [Theory]
    [InlineData("4242 (FakeGame) S 4200 4242 4200 0 -1 4194560", 4200)]
    [InlineData("4242 (Valheim (server) 2) R 17 4242 17 0 -1", 17)] // the command name may hold spaces and ')'
    [InlineData("1 (systemd) S 0 1 1 0 -1", 0)]
    [InlineData("4242 (truncated", null)]
    [InlineData("", null)]
    public void A_process_stat_line_gives_the_parent_pid(string stat, int? parent) =>
        Assert.Equal(parent, ProcFs.ParseParentPid(stat));

    [LinuxFact]
    public void The_linux_probe_reads_the_machine()
    {
        var snapshot = new LinuxMachine().Snapshot(Path.GetTempPath());

        Assert.True(snapshot.RamTotalMb > 0 && snapshot.RamFreeMb > 0 && snapshot.RamFreeMb <= snapshot.RamTotalMb);
        Assert.True(snapshot.DiskFreeMb > 0);
        Assert.True(new LinuxMachine().CpuTimes() is { Total: > 0 } times && times.Idle <= times.Total);
    }

    [LinuxFact]
    public Task The_linux_probe_finds_the_children_a_game_starts() =>
        AssertTreeHasChildAsync(new LinuxMachine(), new ProcessStartInfo("/bin/sh", ["-c", "sleep 5 & sleep 5"]), "sleep");

    [WindowsFact]
    public Task The_windows_probe_finds_the_children_a_game_starts() =>
        AssertTreeHasChildAsync(new WindowsMachine(), new ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 >nul"), "PING", "conhost");

    private static async Task AssertTreeHasChildAsync(IMachine machine, ProcessStartInfo start, params string[] childNames)
    {
        start.CreateNoWindow = true;
        start.UseShellExecute = false;
        using var parent = Process.Start(start)!;
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            IReadOnlyList<int> tree;
            do
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                tree = machine.WithDescendants(parent.Id);
            }
            while (tree.Count < 2 && DateTime.UtcNow < deadline);

            Assert.Equal(parent.Id, tree[0]);
            Assert.Contains(tree.Skip(1), pid => SafeName(pid) is { } name && childNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            parent.Kill(entireProcessTree: true);
        }
    }

    private static string? SafeName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
