using GameWarden.Core.Adapters;
using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Supervision;

/// <summary>Services shared by every instance actor.</summary>
public sealed class SupervisorContext(
    IOptions<GameWardenOptions> options,
    AdapterCatalog adapters,
    IProcessHost processHost,
    IDbContextFactory<GameWardenDb> database,
    TimeProvider time,
    RestartPolicy restartPolicy,
    ILoggerFactory loggerFactory)
{
    public GameWardenOptions Options => options.Value;

    public AdapterCatalog Adapters => adapters;

    public IProcessHost ProcessHost => processHost;

    public IDbContextFactory<GameWardenDb> Database => database;

    public TimeProvider Time => time;

    public RestartPolicy RestartPolicy => restartPolicy;

    public ILoggerFactory LoggerFactory => loggerFactory;

    /// <summary>How long Core waits for a new Runner's pipe.</summary>
    public TimeSpan RunnerConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
