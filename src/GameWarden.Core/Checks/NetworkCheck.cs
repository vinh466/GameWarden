using System.Net;
using System.Net.Sockets;

namespace GameWarden.Core.Checks;

/// <param name="Status"><c>ok</c>, <c>blocked</c> (DNS answers with a loopback or null address) or <c>unresolved</c>.</param>
public sealed record HostCheck(string Host, string Purpose, string Status, string? Detail);

/// <summary>
/// Whether the hosts GameWarden downloads from resolve to real addresses (08 §2). Some ISPs' DNS answer blocked names
/// with 127.0.0.1 (Steam's akamaihd host in Vietnam, 2026-10-28), which shows up later as "connection refused".
/// </summary>
public static class NetworkCheck
{
    public static readonly IReadOnlyList<(string Host, string Purpose)> Hosts =
    [
        ("media.steampowered.com", "SteamCMD download"),
        ("steamcdn-a.akamaihd.net", "SteamCMD download and updates"),
        ("api.github.com", "GameWarden updates"),
    ];

    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(3);

    public static Task<HostCheck[]> RunAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(Hosts.Select(host => CheckAsync(host.Host, host.Purpose, cancellationToken)));

    public static string Classify(IReadOnlyCollection<IPAddress> addresses) =>
        addresses.Count == 0 ? "unresolved"
        : addresses.All(address => IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) ? "blocked"
        : "ok";

    private static async Task<HostCheck> CheckAsync(string host, string purpose, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LookupTimeout);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            var status = Classify(addresses);
            return new HostCheck(host, purpose, status, status == "blocked"
                ? $"DNS answers {string.Join(", ", addresses.Select(a => a.ToString()))}: the network's DNS blocks it. Set the VM's DNS to a public resolver such as 1.1.1.1 (08 §2)."
                : null);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return new HostCheck(host, purpose, "unresolved", ex is OperationCanceledException ? "DNS did not answer within 3 s." : ex.Message);
        }
    }
}
