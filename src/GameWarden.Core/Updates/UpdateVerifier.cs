using System.Security.Cryptography;
using System.Text;

namespace GameWarden.Core.Updates;

/// <summary>
/// Checks an update before anything runs from it (ADR-0013 §3): <c>SHA256SUMS.sig</c> is an ECDSA P-256 signature
/// (SHA-256, DER) of <c>SHA256SUMS</c> by the release key; <c>SHA256SUMS</c> lists the hash of each asset.
/// </summary>
public static class UpdateVerifier
{
    public const string SumsFile = "SHA256SUMS";
    public const string SignatureFile = "SHA256SUMS.sig";

    /// <summary>True when one of <paramref name="publicKeys"/> (base64 SubjectPublicKeyInfo) signed <paramref name="sums"/>.</summary>
    public static bool IsSignedBy(byte[] sums, byte[] signature, IEnumerable<string> publicKeys)
    {
        foreach (var key in publicKeys)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            if (ecdsa.VerifyData(sums, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The hash listed for <paramref name="fileName"/> in <c>sha256sum</c> format (<c>&lt;hex&gt;  &lt;name&gt;</c>).</summary>
    public static string? ExpectedHash(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space == 64 && line[(space + 1)..].TrimStart(' ', '*') == fileName)
            {
                return line[..64].ToLowerInvariant();
            }
        }

        return null;
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Throws unless the signature is valid and <paramref name="zipPath"/> has the listed hash.</summary>
    public static void Verify(string zipPath, string sumsPath, string signaturePath, IEnumerable<string> publicKeys)
    {
        var sums = File.ReadAllBytes(sumsPath);
        if (!IsSignedBy(sums, File.ReadAllBytes(signaturePath), publicKeys))
        {
            throw new CryptographicException($"{SignatureFile} is not signed by a GameWarden release key; the update is refused.");
        }

        var expected = ExpectedHash(Encoding.UTF8.GetString(sums), Path.GetFileName(zipPath))
            ?? throw new CryptographicException($"{SumsFile} does not list {Path.GetFileName(zipPath)}.");
        var actual = Sha256Of(zipPath);
        if (actual != expected)
        {
            throw new CryptographicException($"{Path.GetFileName(zipPath)} does not match {SumsFile} (got {actual}).");
        }
    }
}
