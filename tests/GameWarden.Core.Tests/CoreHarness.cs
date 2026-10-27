using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Processes;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameWarden.Core.Tests;

/// <summary>A Core (supervisor, database, adapters) on a temp data root, without the web host. Can be "restarted".</summary>
internal sealed class CoreHarness : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private readonly RestartPolicy _policy;
    private ServiceProvider? _services;

    private CoreHarness(string dataRoot, RestartPolicy policy)
    {
        DataRoot = dataRoot;
        _policy = policy;
    }

    public string DataRoot { get; }

    public Supervisor Supervisor { get; private set; } = null!;

    public T Get<T>()
        where T : notnull => (_services ?? throw new InvalidOperationException("Core is stopped.")).GetRequiredService<T>();

    public static string FakeGameExe => Path.Combine(AppContext.BaseDirectory, "FakeGame.exe");

    public static async Task<CoreHarness> StartAsync(RestartPolicy? policy = null)
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "core-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(dataRoot);
        var harness = new CoreHarness(dataRoot, policy ?? RestartPolicy.Default with { Backoff = [TimeSpan.FromMilliseconds(200)] });
        await harness.StartCoreAsync();
        return harness;
    }

    public async Task StartCoreAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GameWarden:DataRoot"] = DataRoot,
            ["GameWarden:RunnerPath"] = Path.Combine(AppContext.BaseDirectory, "GameWarden.Runner.exe"),
            ["GameWarden:BuiltinAdaptersPath"] = Path.Combine(AppContext.BaseDirectory, "adapters-builtin"),
            ["GameWarden:CliTokenPath"] = Path.Combine(DataRoot, "cli.token"),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGameWardenCore(configuration);
        services.AddSingleton(_policy);
        _services = services.BuildServiceProvider();
        await _services.MigrateDatabaseAsync();
        Supervisor = _services.GetRequiredService<Supervisor>();
        await Supervisor.StartAsync(CancellationToken.None);
    }

    /// <summary>Like a service stop: Core goes away, Runners and games keep running.</summary>
    public async Task StopCoreAsync()
    {
        if (_services is not null)
        {
            await Supervisor.StopAsync(CancellationToken.None);
            await _services.DisposeAsync();
            _services = null;
        }
    }

    public async Task<InstanceSnapshot> CreateFakeGameAsync(string id = "fake-1")
    {
        var result = await Supervisor.CreateAsync(
            new CreateInstanceRequest(id, "fakegame", "Fake", new Dictionary<string, string> { ["executable"] = FakeGameExe }),
            CancellationToken.None);
        return result.Instance ?? throw new InvalidOperationException(result.Error);
    }

    public InstanceActor Instance(string id) => Supervisor.Find(id) ?? throw new InvalidOperationException($"No instance {id}.");

    public async Task<InstanceSnapshot> WaitForAsync(string id, Func<InstanceSnapshot, bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        InstanceSnapshot snapshot;
        do
        {
            snapshot = Instance(id).Snapshot;
            if (condition(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"{id} never became {what}; last: {snapshot}");
    }

    public string WorldFile(string id) => Path.Combine(InstancePaths.For(DataRoot, id).Data, "world.txt");

    public async ValueTask DisposeAsync()
    {
        var runnerPids = _services is null ? [] : Supervisor.List().Select(s => s.RunnerPid).OfType<int>().ToArray();
        await StopCoreAsync();
        var host = new CurrentUserProcessHost();
        foreach (var pid in runnerPids)
        {
            host.Kill(pid);
        }

        try
        {
            Directory.Delete(DataRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A Runner may still hold a log for a moment; the OS cleans temp later.
        }
    }
}
