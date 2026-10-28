using System.Diagnostics;
using GameWarden.Core.Metrics;

namespace GameWarden.Core.Tests.Unit;

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
    public async Task A_process_tree_includes_the_children_a_game_starts()
    {
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            IReadOnlyList<int> tree;
            do
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                tree = ProcessTree.WithDescendants(parent.Id);
            }
            while (tree.Count < 2 && DateTime.UtcNow < deadline);

            Assert.Equal(parent.Id, tree[0]);
            Assert.Contains(tree.Skip(1), pid => SafeName(pid) is "PING" or "conhost");
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
            return process.ProcessName.ToUpperInvariant() == "PING" ? "PING" : process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
