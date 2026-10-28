namespace GameWarden.Core.Hosting;

/// <summary>Values from requests, packages or the file system are logged without line breaks, so they cannot forge log lines.</summary>
internal static class LogText
{
    public static string Clean(string? value) => (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}
