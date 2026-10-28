using System.Net;

namespace GameWarden.Core.Access;

/// <summary>
/// Where the panel may be reached from (06 §2, OQ-28): the local address a connection arrived at must be loopback or
/// inside an access network, and the <c>Host</c> header must be an IP address, <c>localhost</c> or a configured name.
/// </summary>
public sealed class PanelNetwork
{
    private volatile IReadOnlyList<IPNetwork> _networks = [];
    private volatile HashSet<string> _hostNames = [];

    public PanelNetwork(IEnumerable<string> networks, IEnumerable<string> hostNames) => Update(networks, hostNames);

    /// <summary>Configured networks that are not CIDR ranges; ignored, and logged at start-up.</summary>
    public IReadOnlyList<string> Invalid { get; private set; } = [];

    /// <summary>The Owner changed the settings on the System page: new connections follow them at once.</summary>
    public void Update(IEnumerable<string> networks, IEnumerable<string> hostNames)
    {
        var parsed = new List<IPNetwork>();
        var invalid = new List<string>();
        foreach (var network in networks)
        {
            if (IPNetwork.TryParse(network, out var value))
            {
                parsed.Add(value);
            }
            else
            {
                invalid.Add(network);
            }
        }

        Invalid = invalid;
        _hostNames = new(hostNames, StringComparer.OrdinalIgnoreCase) { "localhost" };
        _networks = parsed;
    }

    public static PanelNetwork From(GameWardenOptions options) => new(options.ResolvedPanelAccessNetworks, options.PanelHostNames);

    /// <summary>Loopback always; otherwise an address inside an access network, i.e. a VPN adapter's own address.</summary>
    public bool AcceptsLocal(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        address = Normalize(address);
        return IPAddress.IsLoopback(address) || _networks.Any(network => network.Contains(address));
    }

    /// <summary>
    /// DNS rebinding needs a host name the attacker controls, so IP addresses are safe here (which addresses answer
    /// at all is <see cref="AcceptsLocal"/>'s decision); names must be <c>localhost</c> or configured.
    /// </summary>
    public bool AcceptsHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out _) || _hostNames.Contains(host);
    }

    /// <summary>Requests on a dual-stack socket carry IPv4 addresses as IPv6 (<c>::ffff:26.1.2.3</c>).</summary>
    public static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public static bool IsLoopback(IPAddress? address) => address is not null && IPAddress.IsLoopback(Normalize(address));
}
