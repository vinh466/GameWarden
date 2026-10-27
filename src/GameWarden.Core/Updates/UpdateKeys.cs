namespace GameWarden.Core.Updates;

/// <summary>
/// Public keys of the release signing key (ECDSA P-256, base64 SubjectPublicKeyInfo). The private key exists only as
/// the <c>UPDATE_SIGNING_KEY</c> secret of the <c>release</c> environment on GitHub (ADR-0013 §3). A list, so a new key
/// can be added a release before the old one is retired.
/// </summary>
public static class UpdateKeys
{
    public static readonly IReadOnlyList<string> Release = [];
}
