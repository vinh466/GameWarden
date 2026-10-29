using GameWarden.Core.Adapters;
using GameWarden.Core.Checks;
using GameWarden.Core.Data;
using GameWarden.Core.Events;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Network;
using GameWarden.Core.Processes;
using GameWarden.Core.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Supervision;

/// <summary>Services shared by every instance actor.</summary>
public sealed class SupervisorContext(
    IOptions<GameWardenOptions> options,
    AdapterCatalog adapters,
    IProcessHost processHost,
    IInstanceFiles files,
    IDataRootAcl dataRoot,
    IFirewall firewall,
    ISecretStore secrets,
    IMachine machine,
    IPrerequisiteChecker prerequisites,
    IDbContextFactory<GameWardenDb> database,
    TimeProvider time,
    RestartPolicy restartPolicy,
    CoreEvents events,
    ILoggerFactory loggerFactory)
{
    public CoreEvents Events => events;

    public GameWardenOptions Options => options.Value;

    public AdapterCatalog Adapters => adapters;

    public IProcessHost ProcessHost => processHost;

    /// <summary>Access to files gamewarden-run can write (ADR-0009 §3).</summary>
    public IInstanceFiles Files => files;

    public IDataRootAcl DataRoot => dataRoot;

    public IFirewall Firewall => firewall;

    public ISecretStore Secrets => secrets;

    /// <summary>RAM, disk and CPU for the resource checks before a start or an install (OQ-07).</summary>
    public IMachine Machine => machine;

    public IPrerequisiteChecker Prerequisites => prerequisites;

    public IDbContextFactory<GameWardenDb> Database => database;

    public TimeProvider Time => time;

    public RestartPolicy RestartPolicy => restartPolicy;

    public ILoggerFactory LoggerFactory => loggerFactory;

    /// <summary>How long Core waits for a new Runner's pipe.</summary>
    public TimeSpan RunnerConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Port probe; replaceable in tests.</summary>
    public Func<string, int, bool> IsPortListening { get; init; } = Ports.IsListening;
}
