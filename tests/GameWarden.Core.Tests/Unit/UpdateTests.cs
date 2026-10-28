extern alias setup;

using System.Security.Cryptography;
using GameWarden.Contracts;
using GameWarden.Core.Updates;
using setup::GameWarden.Setup;

namespace GameWarden.Core.Tests.Unit;

/// <summary>Versions, update verification and the Setup engine's plan (ADR-0013); the real install runs on the VM.</summary>
[Trait("Category", "Unit")]
public sealed class UpdateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "update-" + Guid.NewGuid().ToString("N")[..10]);

    public UpdateTests() => Directory.CreateDirectory(_folder);

    [Theory]
    [InlineData("0.1.0-rc.1", "0.1.0")]
    [InlineData("0.1.0-dev", "0.1.0-rc.1")]
    [InlineData("0.1.0-rc.2", "0.1.0-rc.10")]
    [InlineData("0.1.0", "0.1.1-rc.1")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("v1.2.3-rc.1", "1.2.3")]
    public void Versions_sort_like_semver(string older, string newer)
    {
        Assert.True(SemanticVersion.Parse(older) < SemanticVersion.Parse(newer), $"{older} < {newer}");
    }

    [Fact]
    public void Build_metadata_is_ignored_and_junk_is_rejected()
    {
        Assert.Equal(SemanticVersion.Parse("0.1.0-dev"), SemanticVersion.Parse("0.1.0-dev+abc123"));
        Assert.False(SemanticVersion.TryParse("1.2", out _));
        Assert.False(SemanticVersion.TryParse("1.2.3-", out _));
    }

    [Fact]
    public void A_package_signed_by_the_release_key_passes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (zip, sums, signature) = WritePackage(key, "GameWarden-0.2.0-win-x64.zip");

        UpdateVerifier.Verify(zip, sums, signature, [PublicKey(key)]);
    }

    [Fact]
    public void Another_key_or_a_changed_zip_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (zip, sums, signature) = WritePackage(key, "GameWarden-0.2.0-win-x64.zip");

        Assert.Throws<CryptographicException>(() => UpdateVerifier.Verify(zip, sums, signature, [PublicKey(otherKey)]));

        File.AppendAllText(zip, "tampered");
        var error = Assert.Throws<CryptographicException>(() => UpdateVerifier.Verify(zip, sums, signature, [PublicKey(otherKey), PublicKey(key)]));
        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("v0.2.0-rc.01+abc", "0.2.0-rc.1")]
    [InlineData("0.2.0-dev.42", "0.2.0-dev.42")]
    public void Update_versions_are_rebuilt_from_numbers_and_known_words(string input, string expected)
    {
        Assert.Equal(expected, UpdateService.Canonical(input));
    }

    [Theory]
    [InlineData("0.2.0-rc.1\" & calc")]
    [InlineData("0.2.0-..\\..\\x")]
    [InlineData("0.2.0-evil")]
    public void Anything_else_is_refused_before_it_reaches_a_folder_or_a_command_line(string input)
    {
        Assert.Throws<UpdateRefusedException>(() => UpdateService.Canonical(input));
    }

    [Fact]
    public void Core_knows_its_install_root_only_when_it_runs_from_a_version_folder()
    {
        Assert.Equal(@"C:\Program Files\GameWarden", UpdateService.DetectInstallRoot(@"C:\Program Files\GameWarden\versions\0.1.0\"));
        Assert.Null(UpdateService.DetectInstallRoot(@"D:\src\GameWarden.Core\bin\Debug\net10.0-windows\"));
    }

    [Theory]
    [InlineData(@"C:\Windows;%SystemRoot%\system32", true, @"C:\Windows;%SystemRoot%\system32;C:\Program Files\GameWarden")]
    [InlineData(@"C:\Windows;C:\Program Files\GameWarden\", true, @"C:\Windows;C:\Program Files\GameWarden\")]
    [InlineData(@"C:\Windows;C:\Program Files\GameWarden;C:\Tools", false, @"C:\Windows;C:\Tools")]
    [InlineData(@"C:\Windows;", true, @"C:\Windows;C:\Program Files\GameWarden")]
    [InlineData(@"C:\Windows", false, @"C:\Windows")]
    public void The_install_folder_goes_on_and_off_the_machine_path_once(string path, bool present, string expected)
    {
        Assert.Equal(expected, SetupPlan.WithPathEntry(path, @"C:\Program Files\GameWarden", present));
    }

    [Fact]
    public void The_shim_runs_the_current_versions_cli()
    {
        Assert.Contains("\"%~dp0versions\\0.2.0\\gamewarden.exe\" %*", SetupPlan.ShimText("0.2.0"), StringComparison.Ordinal);
    }

    [Fact]
    public void Old_versions_go_unless_current_previous_or_used_by_a_runner()
    {
        string[] installed = ["0.1.0", "0.2.0", "0.3.0", "0.4.0"];

        Assert.Equal(["0.1.0"], SetupPlan.VersionsToDelete(installed, "0.4.0", "0.3.0", new HashSet<string> { "0.2.0" }));
        Assert.Empty(SetupPlan.VersionsToDelete(installed, "0.4.0", "0.3.0", inUse: null)); // unknown: keep everything
        Assert.Equal("0.2.0", SetupPlan.VersionOf(@"C:\Program Files\GameWarden\versions\0.2.0\GameWarden.Runner.exe", @"C:\Program Files\GameWarden\versions"));
        Assert.Null(SetupPlan.VersionOf(@"C:\Other\GameWarden.Runner.exe", @"C:\Program Files\GameWarden\versions"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private (string Zip, string Sums, string Signature) WritePackage(ECDsa key, string zipName)
    {
        var zip = Path.Combine(_folder, zipName);
        File.WriteAllText(zip, "pretend this is a zip");
        var sums = Path.Combine(_folder, UpdateVerifier.SumsFile);
        File.WriteAllText(sums, $"{UpdateVerifier.Sha256Of(zip)}  {zipName}\n{new string('0', 64)}  GameWarden-Setup-0.2.0.exe\n");
        var signature = Path.Combine(_folder, UpdateVerifier.SignatureFile);
        File.WriteAllBytes(signature, key.SignData(File.ReadAllBytes(sums), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        return (zip, sums, signature);
    }

    private static string PublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
}
