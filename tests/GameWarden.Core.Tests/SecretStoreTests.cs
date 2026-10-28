using GameWarden.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Tests;

/// <summary>DPAPI as the current user; as the service the same code runs as LocalSystem (ADR-0009 §4).</summary>
[Trait("Category", "Windows")]
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "secrets-" + Guid.NewGuid().ToString("N")[..10]);

    private DpapiSecretStore Store => new(Options.Create(new GameWardenOptions { DataRoot = _dataRoot }), NullLogger<DpapiSecretStore>.Instance);

    [Fact]
    public void A_secret_round_trips_and_is_not_stored_in_plain_text()
    {
        Store.Set("instances/vh", "password", "hunter2-plain");

        Assert.Equal("hunter2-plain", Store.Get("instances/vh", "password"));
        Assert.Equal(["password"], Store.Names("instances/vh"));
        var file = Directory.GetFiles(Path.Combine(_dataRoot, "secrets"), "*", SearchOption.AllDirectories).Single();
        Assert.DoesNotContain("hunter2", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_blob_moved_to_another_secret_does_not_decrypt()
    {
        Store.Set("instances/vh", "password", "one");
        Store.Set("instances/other", "password", "two");
        var from = Path.Combine(_dataRoot, "secrets", "instances", "vh", "password.dpapi");
        var to = Path.Combine(_dataRoot, "secrets", "instances", "other", "password.dpapi");
        File.Copy(from, to, overwrite: true);

        Assert.Null(Store.Get("instances/other", "password"));
    }

    [Fact]
    public void Names_and_scopes_cannot_escape_the_secrets_folder()
    {
        Assert.Throws<ArgumentException>(() => Store.Set("instances/vh", @"..\..\evil", "x"));
        Assert.Throws<ArgumentException>(() => Store.Set("instances/../db", "x", "x"));
    }

    [Fact]
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
