using GameWarden.Core.Notifications;
using GameWarden.Core.Observability;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ObservabilityTests
{
    [Fact]
    public void The_sanitizer_removes_credentials_before_storage()
    {
        var text = LogSanitizer.Clean(
            "\u001b[31mpassword=hunter2\u001b[0m Authorization: Bearer abc.def.ghi " +
            "https://user:pass@example.test/path " +
            "Cookie: session=secret-value " +
            "https://discord.com/api/webhooks/123456/token-value");

        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.ghi", text, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pass", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/x", true)]
    [InlineData("https://discordapp.com/api/webhooks/1/x", true)]
    [InlineData("http://discord.com/api/webhooks/1/x", false)]
    [InlineData("https://example.com/api/webhooks/1/x", false)]
    [InlineData("https://discord.com/channels/1", false)]
    [InlineData(null, false)]
    public void Only_discord_webhook_urls_are_accepted(string? url, bool accepted) =>
        Assert.Equal(accepted, NotificationSettings.IsWebhookUrl(url));

    [Fact]
    public void Routing_honours_topics_severity_and_instances()
    {
        var destination = new NotificationDestination(
            "alerts", "Alerts", "discord-webhook", "alerts",
            MinimumSeverity: "warning",
            Topics: ["instance.*", "job.failed"],
            Instances: ["valheim-main"]);

        Assert.True(NotificationSettings.Matches(destination, "instance.crashed", "error", "valheim-main"));
        Assert.True(NotificationSettings.Matches(destination, "job.failed", "error", null));
        Assert.False(NotificationSettings.Matches(destination, "instance.running", "info", "valheim-main"));
        Assert.False(NotificationSettings.Matches(destination, "instance.crashed", "error", "palworld-main"));
        Assert.False(NotificationSettings.Matches(destination, "player.joined", "info", "valheim-main"));

        Assert.True(NotificationSettings.Matches(
            destination with { Instances = [], Topics = [], MinimumSeverity = "info" }, "player.joined", "info", "anything"));
        Assert.False(NotificationSettings.Matches(destination with { Enabled = false }, "instance.crashed", "error", "valheim-main"));
    }

    [Fact]
    public void Severity_order_matches_the_documented_scale()
    {
        Assert.True(NotificationSettings.Rank("critical") > NotificationSettings.Rank("error"));
        Assert.True(NotificationSettings.Rank("error") > NotificationSettings.Rank("warning"));
        Assert.True(NotificationSettings.Rank("warning") > NotificationSettings.Rank("success"));
        Assert.True(NotificationSettings.Rank("success") > NotificationSettings.Rank("info"));
    }
}
