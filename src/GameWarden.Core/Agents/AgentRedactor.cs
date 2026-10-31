using System.Text.RegularExpressions;
using GameWarden.Core.Data;
using GameWarden.Core.Secrets;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Agents;

/// <summary>
/// Removes secrets and common credentials from Agent-visible text (docs/15). Values come from the same secret
/// store Core uses; the redactor never logs them, and only replaces values long enough to be meaningful.
/// </summary>
public sealed partial class AgentRedactor
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<GameWardenDb> _db;
    private readonly ISecretStore _secrets;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly List<string> _values = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public AgentRedactor(IDbContextFactory<GameWardenDb> db, ISecretStore secrets, TimeProvider time)
    {
        _db = db;
        _secrets = secrets;
        _time = time;
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        LoadIfStale();
        var redacted = StripControls(text);
        foreach (var value in _values)
        {
            if (redacted.Contains(value, StringComparison.Ordinal))
            {
                redacted = redacted.Replace(value, "***", StringComparison.Ordinal);
            }
        }

        redacted = PasswordPattern().Replace(redacted, "$1$2***");
        redacted = ArgumentPasswordPattern().Replace(redacted, "$1***");
        redacted = BearerPattern().Replace(redacted, "$1***");
        redacted = AgentTokenPattern().Replace(redacted, "***");
        redacted = DiscordWebhookPattern().Replace(redacted, "***");
        return redacted.Length <= 8192 ? redacted : redacted[..8192] + "…";
    }

    private void LoadIfStale()
    {
        var now = _time.GetUtcNow();
        if (now - _loadedAt < CacheFor)
        {
            return;
        }

        lock (_gate)
        {
            if (now - _loadedAt < CacheFor)
            {
                return;
            }

            var values = new List<string>();
            AddValues(values, SecretScopes.System);
            try
            {
                using var db = _db.CreateDbContext();
                foreach (var id in db.Instances.AsNoTracking().Select(row => row.Id).ToList())
                {
                    AddValues(values, SecretScopes.Instance(id));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A missing database at start-up just means the next request tries again.
            }

            _values.Clear();
            _values.AddRange(values
                .Where(value => value.Length >= 4)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(value => value.Length));
            _loadedAt = now;
        }
    }

    private void AddValues(List<string> values, string scope)
    {
        foreach (var name in _secrets.Names(scope))
        {
            if (_secrets.Get(scope, name) is { Length: >= 4 } value && !value.Contains('\0'))
            {
                values.Add(value);
            }
        }
    }

    private static string StripControls(string text)
    {
        // ANSI escapes and most control characters make logs hard to read and can drive a terminal; keep tabs/newlines.
        text = AnsiPattern().Replace(text, string.Empty);
        var buffer = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\n' or '\r' or '\t' || !char.IsControl(character))
            {
                buffer.Append(character);
            }
        }

        return buffer.ToString();
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|authorization)(\s*[:=]\s*)([""']?)[^\s""',;]+")]
    private static partial Regex PasswordPattern();

    [GeneratedRegex(@"(?i)((?:-|/|--)(?:password|passwd|pwd)\s+)([""']?)[^\s""']+")]
    private static partial Regex ArgumentPasswordPattern();

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9._~+/\-=]+")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"gw_agent_[A-Za-z0-9_-]{8,}")]
    private static partial Regex AgentTokenPattern();

    [GeneratedRegex(@"https://discord(?:app)?\.com/api/webhooks/\S+", RegexOptions.IgnoreCase)]
    private static partial Regex DiscordWebhookPattern();
}
