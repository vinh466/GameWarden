namespace GameWarden.Core.Integrations;

/// <summary>The providers compiled into this Core, by the <c>integration.provider</c> id in an adapter.</summary>
public sealed class GameIntegrationRegistry(IEnumerable<IGameIntegrationProvider> providers)
{
    private readonly IReadOnlyDictionary<string, IGameIntegrationProvider> _providers =
        providers.ToDictionary(provider => provider.Id, StringComparer.Ordinal);

    public IGameIntegrationProvider? Find(string id) => _providers.GetValueOrDefault(id);

    public IReadOnlyList<string> Providers => [.. _providers.Keys.Order(StringComparer.Ordinal)];
}
