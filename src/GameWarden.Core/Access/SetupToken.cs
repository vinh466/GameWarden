using GameWarden.Core.Hosting;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Access;

/// <summary>
/// The one-time token that lets the first visitor create the Owner (06 §4, OQ-22). Core writes it at start-up while no
/// account exists; `gamewarden setup-url` prints the URL that carries it; creating the Owner deletes it.
/// </summary>
public sealed class SetupToken(IOptions<GameWardenOptions> options, IsolationMode isolation, ILogger<SetupToken> logger)
{
    private readonly object _gate = new();
    private string? _value;

    private string Path => options.Value.ResolvedSetupTokenPath;

    public string Ensure()
    {
        lock (_gate)
        {
            return _value ??= TokenFile.ReadOrCreate(Path, isolation.Enabled, logger);
        }
    }

    public bool Matches(string? candidate)
    {
        lock (_gate)
        {
            return File.Exists(Path) && TokenFile.Matches(candidate, Ensure());
        }
    }

    public void Consume()
    {
        lock (_gate)
        {
            File.Delete(Path);
            _value = null;
        }
    }

    public string Url() => $"http://localhost:{options.Value.PanelPort}/setup?token={Uri.EscapeDataString(Ensure())}";
}
