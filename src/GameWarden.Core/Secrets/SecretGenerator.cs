using System.Security.Cryptography;

namespace GameWarden.Core.Secrets;

/// <summary>
/// Values for secrets an adapter marks <c>generate</c> (ADR-0022 decision 5). The alphabet leaves out characters
/// that are easy to confuse when the Owner types a password in a game console (0/O, 1/l/I).
/// </summary>
public static class SecretGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string New(int length = 24)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 8);
        return string.Create(length, Alphabet, static (span, alphabet) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}
