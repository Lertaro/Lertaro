#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$CheckSigningKey,
    [string]$PackagePath,
    [string]$TrustSourcePath = (Join-Path $PSScriptRoot 'Core/Services/Update/UpdatePackage.cs')
)

$ErrorActionPreference = 'Stop'
if (-not $CheckSigningKey -and -not $PackagePath) {
    throw 'Specify -CheckSigningKey or -PackagePath.'
}

# Read the same trust anchor the shipped updater uses, never the signing key's own public half.
$source = [IO.File]::ReadAllText($TrustSourcePath)
$declaration = [regex]::Match($source, '(?s)\bconst\s+string\s+PUBLIC_KEY_PEM\s*=\s*(.*?);')
$parts = [regex]::Matches($declaration.Groups[1].Value, '"([A-Za-z0-9+/=]+)\\n"')
if (-not $declaration.Success -or $parts.Count -eq 0) {
    throw 'The updater trust anchor could not be read.'
}
$pem = "-----BEGIN PUBLIC KEY-----`n" + (($parts | ForEach-Object { $_.Groups[1].Value }) -join "`n") + "`n-----END PUBLIC KEY-----"
$trusted = [Security.Cryptography.ECDsa]::Create()
try {
    $trusted.ImportFromPem($pem)
    $trustedPublic = $trusted.ExportSubjectPublicKeyInfo()
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($trustedPublic)).ToLowerInvariant()
    if ($CheckSigningKey) {
        if (-not $env:LERTARO_SIGNING_KEY) { throw 'LERTARO_SIGNING_KEY is not configured.' }
        $signer = [Security.Cryptography.ECDsa]::Create()
        try {
            try {
                $keyBytes = [Convert]::FromBase64String(($env:LERTARO_SIGNING_KEY -replace '\s', ''))
                $signer.ImportFromPem([Text.Encoding]::UTF8.GetString($keyBytes))
            } catch {
                throw 'LERTARO_SIGNING_KEY must contain a base64-encoded ECDSA private-key PEM.'
            }
            $actualPublic = $signer.ExportSubjectPublicKeyInfo()
            if ([Convert]::ToBase64String($actualPublic) -cne [Convert]::ToBase64String($trustedPublic)) {
                throw "The signing key does not match the updater trust anchor (SHA256:$fingerprint)."
            }
            # Prove possession of the private half as well as public-key equality, without exporting it.
            $challenge = [Text.Encoding]::UTF8.GetBytes('Lertaro release signing preflight')
            $signature = $signer.SignData($challenge, [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
            if (-not $trusted.VerifyData($challenge, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) {
                throw 'Signing-key preflight failed.'
            }
            Write-Host "Signing key matches updater trust anchor SHA256:$fingerprint"
        } finally { $signer.Dispose() }
    }

    if ($PackagePath) {
        $package = [IO.File]::ReadAllBytes($PackagePath)
        $signature = [IO.File]::ReadAllBytes("$PackagePath.sig")
        if (-not $trusted.VerifyData($package, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) {
            throw "Release signature verification failed for '$PackagePath' against updater trust anchor SHA256:$fingerprint."
        }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($package)).ToLowerInvariant()
        Write-Host "Verified $([IO.Path]::GetFileName($PackagePath)): SHA256:$hash; signature bytes=$($signature.Length); key SHA256:$fingerprint"
    }
} finally { $trusted.Dispose() }
