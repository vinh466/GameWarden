using GameWarden.Core.Access;
using GameWarden.Core.Hosting;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Api;

/// <summary>
/// The CLI's bearer token (docs/02-architecture.md §8): created on first start, readable only by
/// Administrators, SYSTEM and the account Core runs as; accepted only on loopback.
/// </summary>
public sealed class CliToken(IOptions<GameWardenOptions> options, IsolationMode isolation, ILogger<CliToken> logger)
{
    public string Value { get; } = TokenFile.ReadOrCreate(options.Value.ResolvedCliTokenPath, isolation, logger);

    public bool Accepts(string? authorization)
    {
        const string prefix = "Bearer ";
        return authorization is not null
            && authorization.StartsWith(prefix, StringComparison.Ordinal)
            && TokenFile.Matches(authorization[prefix.Length..], Value);
    }
}
