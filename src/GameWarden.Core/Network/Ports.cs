using System.Net.NetworkInformation;
using GameWarden.Core.Adapters;

namespace GameWarden.Core.Network;

/// <summary>Port choice for new instances and the port check before a start (08 §2, 05 §5 <c>port_conflict</c>).</summary>
public static class Ports
{
    /// <summary>A second instance of a game moves all its ports by this much (e.g. Valheim 2456/2457 → 2466/2467).</summary>
    public const int Step = 10;

    /// <summary>
    /// The adapter's default ports, moved by <see cref="Step"/> until none of them is used by another instance
    /// or by a listening process.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Allocate(
        AdapterDefinition adapter,
        IReadOnlyCollection<int> usedByInstances,
        Func<string, int, bool> isListening)
    {
        var defaults = LaunchSpecBuilder.DefaultPorts(adapter);
        if (defaults.Count == 0)
        {
            return defaults;
        }

        for (var shift = 0; shift < 100 * Step; shift += Step)
        {
            var candidate = defaults.ToDictionary(pair => pair.Key, pair => pair.Value + shift, StringComparer.Ordinal);
            var free = candidate.All(pair =>
                pair.Value <= ushort.MaxValue
                && !usedByInstances.Contains(pair.Value)
                && !isListening(Protocol(adapter, pair.Key), pair.Value));
            if (free)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free ports for {adapter.Id} near its defaults.");
    }

    /// <summary>Ports of this instance that another process listens on; checked before a start.</summary>
    public static IReadOnlyList<string> Conflicts(AdapterDefinition adapter, IReadOnlyDictionary<string, int> ports, Func<string, int, bool> isListening) =>
    [
        .. ports
            .Where(pair => isListening(Protocol(adapter, pair.Key), pair.Value))
            .Select(pair => $"{pair.Value}/{Protocol(adapter, pair.Key)} ({pair.Key})"),
    ];

    public static string Protocol(AdapterDefinition adapter, string portName) =>
        adapter.Ports.FirstOrDefault(port => port.Name == portName)?.Protocol ?? "tcp";

    /// <summary>An internal port gets no firewall rule: only Core uses it, on loopback (ADR-0022 decision 4).</summary>
    public static bool IsInternal(AdapterDefinition adapter, string portName) =>
        adapter.Ports.FirstOrDefault(port => port.Name == portName)?.Internal == true;

    public static bool IsListening(string protocol, int port)
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = protocol == "udp" ? properties.GetActiveUdpListeners() : properties.GetActiveTcpListeners();
            return listeners.Any(endpoint => endpoint.Port == port);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }
}
