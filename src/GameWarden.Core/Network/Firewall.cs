using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Network;

/// <summary>An inbound allow rule for one instance port (08 §7).</summary>
public sealed record FirewallRule(string InstanceId, int Port, string Protocol)
{
    public const string Prefix = "GameWarden:";

    public string Name => $"{Prefix}{InstanceId}:{Port.ToString(CultureInfo.InvariantCulture)}/{Protocol}";
}

/// <summary>Windows Firewall rules for game ports, limited to the VPN ranges (08 §7).</summary>
public interface IFirewall
{
    /// <summary>Core start-up: adds missing rules, updates their scope, removes orphans with the GameWarden: prefix.</summary>
    void Reconcile(IReadOnlyCollection<FirewallRule> wanted);

    /// <summary>Makes one instance's rules exactly <paramref name="rules"/> (empty removes them).</summary>
    void SetInstance(string instanceId, IReadOnlyCollection<FirewallRule> rules);

    /// <summary>Uninstall: removes every GameWarden rule.</summary>
    void RemoveAll();
}

/// <summary>Development and CI: no admin rights, no rules (02 §11).</summary>
public sealed class NoFirewall : IFirewall
{
    public void Reconcile(IReadOnlyCollection<FirewallRule> wanted)
    {
    }

    public void SetInstance(string instanceId, IReadOnlyCollection<FirewallRule> rules)
    {
    }

    public void RemoveAll()
    {
    }
}

/// <summary>Windows Firewall through its COM API (<c>HNetCfg.FwPolicy2</c>); needs admin rights, so tested on the VM.</summary>
public sealed class WindowsFirewall(IOptions<GameWardenOptions> options, ILogger<WindowsFirewall> logger) : IFirewall
{
    private const string PanelPrefix = FirewallRule.Prefix + "panel:";
    private const int ProtocolTcp = 6;
    private const int ProtocolUdp = 17;
    private const int DirectionIn = 1;
    private const int ActionAllow = 1;
    private const int AllProfiles = 0x7FFFFFFF;

    private readonly object _gate = new();

    public void Reconcile(IReadOnlyCollection<FirewallRule> wanted) =>
        Apply(name => !name.StartsWith(PanelPrefix, StringComparison.Ordinal), wanted);

    public void SetInstance(string instanceId, IReadOnlyCollection<FirewallRule> rules) =>
        Apply(name => name.StartsWith($"{FirewallRule.Prefix}{instanceId}:", StringComparison.Ordinal), rules);

    public void RemoveAll() => Apply(_ => true, []);

    /// <summary>Within the GameWarden rules that <paramref name="owned"/> selects, keeps <paramref name="wanted"/> and removes the rest.</summary>
    private void Apply(Func<string, bool> owned, IReadOnlyCollection<FirewallRule> wanted)
    {
        lock (_gate)
        {
            try
            {
                dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;
                dynamic rules = policy.Rules;
                var existing = new List<string>();
                foreach (dynamic rule in (IEnumerable)rules)
                {
                    var name = (string)rule.Name;
                    if (name.StartsWith(FirewallRule.Prefix, StringComparison.Ordinal) && owned(name))
                    {
                        existing.Add(name);
                    }
                }

                var remoteAddresses = string.Join(",", options.Value.ResolvedVpnRanges);
                var wantedNames = wanted.Select(rule => rule.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var name in existing.Where(name => !wantedNames.Contains(name)))
                {
                    rules.Remove(name);
                    logger.LogInformation("Removed firewall rule {Rule}.", name);
                }

                foreach (var rule in wanted)
                {
                    if (existing.Contains(rule.Name))
                    {
                        dynamic current = rules.Item(rule.Name);
                        current.RemoteAddresses = remoteAddresses; // VPN ranges may have changed
                        continue;
                    }

                    rules.Add(NewRule(rule, remoteAddresses));
                    logger.LogInformation("Added firewall rule {Rule} for {Remote}.", rule.Name, remoteAddresses);
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
            {
                // Friends cannot connect until this is fixed, but Core and the games keep working.
                logger.LogError(ex, "Updating Windows Firewall rules failed.");
            }
        }
    }

    private static object NewRule(FirewallRule rule, string remoteAddresses)
    {
        dynamic created = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
        created.Name = rule.Name;
        created.Description = $"GameWarden instance {rule.InstanceId}. Managed by GameWarden; changes are overwritten.";
        created.Grouping = "GameWarden";
        created.Protocol = rule.Protocol == "udp" ? ProtocolUdp : ProtocolTcp; // before LocalPorts
        created.LocalPorts = rule.Port.ToString(CultureInfo.InvariantCulture);
        created.RemoteAddresses = remoteAddresses;
        created.Direction = DirectionIn;
        created.Action = ActionAllow;
        created.Profiles = AllProfiles;
        created.Enabled = true;
        return created;
    }
}
