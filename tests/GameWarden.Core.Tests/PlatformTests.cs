using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using GameWarden.Core.Access;
using GameWarden.Core.Checks;
using GameWarden.Core.Hosting;
using GameWarden.Core.Tests.Unit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameWarden.Core.Tests;

/// <summary>Windows adapters that need no admin rights (ADR-0016, tier <c>Windows</c>): token file ACL, prerequisite and Toolhelp probes.</summary>
[Trait("Category", "Windows")]
public sealed class WindowsAdapterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "windows-" + Guid.NewGuid().ToString("N")[..10]);

    [WindowsFact]
    public void A_token_file_admits_only_system_administrators_and_cores_account()
    {
        var path = Path.Combine(_folder, "cli.token");

        var token = TokenFile.ReadOrCreate(path, IsolationMode.Off, NullLogger.Instance);

        Assert.Equal(token, TokenFile.ReadOrCreate(path, IsolationMode.Off, NullLogger.Instance));
        var security = new FileInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected); // nothing inherited from the folder
        string[] expected =
        [
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
            WindowsIdentity.GetCurrent().User!.Value,
        ];
        var allowed = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => rule.IdentityReference.Value);
        Assert.Equal(expected.Order(StringComparer.Ordinal), allowed.Order(StringComparer.Ordinal));
    }

    [WindowsFact]
    public void The_prerequisite_checker_answers_every_known_prerequisite()
    {
        var checker = new WindowsPrerequisiteChecker();

        foreach (var id in Prerequisites.Known)
        {
            var status = checker.Check(id);
            Assert.Equal(id, status.Id);
            Assert.False(string.IsNullOrWhiteSpace(status.Hint));
        }

        Assert.Equal(Prerequisites.Unknown("nope"), checker.Check("nope"));
        // GitHub's Windows runners come with the VC++ runtime (ADR-0012), so the registry probe must find it.
        Assert.True(checker.Check(Prerequisites.VcRedistX64).Installed);
    }

    [WindowsFact]
    public Task The_toolhelp_probe_finds_the_children_a_game_starts() =>
        MetricsTests.AssertTreeHasChildAsync(new WindowsMachine(), new ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 >nul"), "PING", "conhost");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}

/// <summary>Choosing the platform and the development platform's file modes (ADR-0016).</summary>
[Trait("Category", "Unit")]
public sealed class PlatformTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "platform-" + Guid.NewGuid().ToString("N")[..10]);

    [Theory]
    [InlineData("off")]
    [InlineData("auto")] // a test never runs as LocalSystem
    [InlineData(null)]
    public void Tests_and_development_run_without_isolation(string? value) =>
        Assert.False(IsolationMode.From(Isolation(value)).Enabled);

    [LinuxFact]
    public void Isolation_on_needs_windows()
    {
        var error = Assert.Throws<PlatformNotSupportedException>(() => IsolationMode.From(Isolation("on")));

        Assert.Contains("ADR-0016", error.Message, StringComparison.Ordinal);
    }

    [LinuxFact]
    public void A_token_file_is_readable_by_its_owner_only()
    {
        var path = Path.Combine(_folder, "cli.token");

        var token = TokenFile.ReadOrCreate(path, IsolationMode.Off, NullLogger.Instance);

        Assert.Equal(token, TokenFile.ReadOrCreate(path, IsolationMode.Off, NullLogger.Instance));
        if (!OperatingSystem.IsWindows()) // [LinuxFact] skips elsewhere; this tells the platform analyzer
        {
            Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
        }
    }

    [LinuxFact]
    public void An_existing_token_file_that_others_can_read_is_tightened()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "cli.token");
        File.WriteAllText(path, "made-by-hand");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            Assert.Equal("made-by-hand", TokenFile.ReadOrCreate(path, IsolationMode.Off, NullLogger.Instance));

            Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static IConfiguration Isolation(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GameWarden:Isolation"] = value })
            .Build();
}
