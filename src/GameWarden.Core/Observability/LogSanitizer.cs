using System.Text.RegularExpressions;

namespace GameWarden.Core.Observability;

/// <summary>
/// The dependency-free part of log redaction: passwords, bearer tokens, Agent tokens, Discord webhook URLs and control
/// characters. The secret-store-backed AgentRedactor is still used by the Agent read path; the logger provider cannot
/// depend on it because it would create an ILoggerFactory cycle.
/// </summary>
public static partial class LogSanitizer
{
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var value = AnsiPattern().Replace(text, string.Empty);
        value = ControlPattern().Replace(value, string.Empty);
        value = AgentTokenPattern().Replace(value, "***");
        value = DiscordWebhookPattern().Replace(value, "***");
        value = BearerPattern().Replace(value, "$1***");
        value = PasswordPattern().Replace(value, "$1$2***");
        value = ArgumentPasswordPattern().Replace(value, "$1***");
        value = CookiePattern().Replace(value, "$1***");
        value = UrlCredentialsPattern().Replace(value, "$1***@");
        return value;
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]")]
    private static partial Regex ControlPattern();

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

    [GeneratedRegex(@"(?i)\b(cookie|set-cookie)\s*:\s*[^\r\n]+")]
    private static partial Regex CookiePattern();

    [GeneratedRegex(@"(?i)\b(https?://)[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlCredentialsPattern();
}
