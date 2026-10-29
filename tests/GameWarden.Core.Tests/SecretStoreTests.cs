using GameWarden.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Tests;

/// <summary>DPAPI as the current user; as the service the same code runs as LocalSystem (ADR-0009 §4). Windows only (ADR-0016).</summary>
[Trait("Category", "Windows")]
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "secrets-" + Guid.NewGuid().ToString("N")[..10]);

    private DpapiSecretStore Store => new(Options.Create(new GameWardenOptions { DataRoot = _dataRoot }), NullLogger<DpapiSecretStore>.Instance);

    [WindowsFact]
    public void A_secret_round_trips_and_is_not_stored_in_plain_text()
    {
        Store.Set("instances/vh", "password", "hunter2-plain");

        Assert.Equal("hunter2-plain", Store.Get("instances/vh", "password"));
        Assert.Equal(["password"], Store.Names("instances/vh"));
        var file = Directory.GetFiles(Path.Combine(_dataRoot, "secrets"), "*", SearchOption.AllDirectories).Single();
        Assert.DoesNotContain("hunter2", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
    }

    [WindowsFact]
    public void A_blob_moved_to_another_secret_does_not_decrypt()
    {
        Store.Set("instances/vh", "password", "one");
        Store.Set("instances/other", "password", "two");
        var from = Path.Combine(_dataRoot, "secrets", "instances", "vh", "password.dpapi");
        var to = Path.Combine(_dataRoot, "secrets", "instances", "other", "password.dpapi");
        File.Copy(from, to, overwrite: true);

        Assert.Null(Store.Get("instances/other", "password"));
    }

    [WindowsFact]
    public void Names_and_scopes_cannot_escape_the_secrets_folder()
    {
        Assert.Throws<ArgumentException>(() => Store.Set("instances/vh", @"..\..\evil", "x"));
        Assert.Throws<ArgumentException>(() => Store.Set("instances/../db", "x", "x"));
    }

    [WindowsFact]
    public void Deleting_an_instance_scope_removes_its_secrets()
    {
        Store.Set("instances/vh", "password", "x");

        Store.DeleteScope("instances/vh");

        Assert.Null(Store.Get("instances/vh", "password"));
        Assert.Empty(Store.Names("instances/vh"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

/// <summary>The development platform's store (ADR-0016): the same layout as DPAPI's, plain files only their owner can read.</summary>
[Trait("Category", "Unit")]
public sealed class DevelopmentSecretStoreTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "dev-secrets-" + Guid.NewGuid().ToString("N")[..10]);

    private DevelopmentSecretStore Store => new(Options.Create(new GameWardenOptions { DataRoot = _dataRoot }), NullLogger<DevelopmentSecretStore>.Instance);

    private string FileOf(string scope, string name) => Path.Combine([_dataRoot, "secrets", .. scope.Split('/'), name + ".secret"]);

    [Fact]
    public void A_secret_round_trips_in_its_own_file()
    {
        Store.Set("instances/vh", "password", "hunter2");
        Store.Set("instances/vh", "password", "hunter3"); // replaced, not appended

        Assert.Equal("hunter3", Store.Get("instances/vh", "password"));
        Assert.Equal(["password"], Store.Names("instances/vh"));
        Assert.Equal("hunter3", File.ReadAllText(FileOf("instances/vh", "password")));
        Assert.Null(Store.Get("instances/vh", "missing"));
    }

    [Fact]
    public void It_never_reads_the_files_of_the_dpapi_store()
    {
        var dpapiFile = Path.Combine(_dataRoot, "secrets", "system", "run-as-password.dpapi");
        Directory.CreateDirectory(Path.GetDirectoryName(dpapiFile)!);
        File.WriteAllBytes(dpapiFile, [1, 2, 3]);

        Assert.Empty(Store.Names("system"));
        Assert.Null(Store.Get("system", "run-as-password"));
    }

    [Fact]
    public void Names_and_scopes_cannot_escape_the_secrets_folder()
    {
        Assert.Throws<ArgumentException>(() => Store.Set("instances/vh", @"..\..\evil", "x"));
        Assert.Throws<ArgumentException>(() => Store.Set("instances/vh", "../evil", "x"));
        Assert.Throws<ArgumentException>(() => Store.Set("instances/../db", "x", "x"));
    }

    [Fact]
    public void Deleting_removes_a_secret_or_a_whole_instance_scope()
    {
        Store.Set("instances/vh", "password", "x");
        Store.Set("instances/vh", "admin", "y");

        Assert.True(Store.Delete("instances/vh", "admin"));
        Assert.False(Store.Delete("instances/vh", "admin"));
        Store.DeleteScope("instances/vh");

        Assert.Null(Store.Get("instances/vh", "password"));
        Assert.Empty(Store.Names("instances/vh"));
    }

    [LinuxFact]
    public void Only_the_owner_can_read_a_secret()
    {
        Store.Set("instances/vh", "password", "x");

        if (!OperatingSystem.IsWindows()) // [LinuxFact] skips elsewhere; this tells the platform analyzer
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FileOf("instances/vh", "password")));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(FileOf("instances/vh", "password"))!));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
