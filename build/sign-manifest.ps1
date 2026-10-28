# Signs SHA256SUMS with the release key (ADR-0013 §3): ECDSA P-256, SHA-256, DER signature -> SHA256SUMS.sig.
# The key comes from the UPDATE_SIGNING_KEY secret of the "release" environment (PKCS#8 PEM) and never touches disk.
# Refuses to sign unless the key's public half is one of the keys compiled into Core (src/GameWarden.Core/Updates/UpdateKeys.cs),
# so a release can never carry a signature that installed versions cannot check. Needs PowerShell 7 (.NET 5+).
param([string]$Sums = "artifacts/SHA256SUMS")
$ErrorActionPreference = "Stop"
$pem = $env:UPDATE_SIGNING_KEY
if (-not $pem) { throw "UPDATE_SIGNING_KEY is not set." }

$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem($pem)
    $public = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
    $keysFile = Join-Path (Split-Path $PSScriptRoot -Parent) "src/GameWarden.Core/Updates/UpdateKeys.cs"
    if (-not (Select-String -Path $keysFile -SimpleMatch $public -Quiet)) {
        throw "The signing key's public key is not in UpdateKeys.cs; installed versions could not verify this release."
    }

    $bytes = [IO.File]::ReadAllBytes((Resolve-Path $Sums))
    $signature = $key.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
    [IO.File]::WriteAllBytes("$((Resolve-Path $Sums).Path).sig", $signature)
    Write-Host "Signed $Sums with key $($public.Substring(0, 24))..."
}
finally {
    $key.Dispose()
}
