#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v[0-9][A-Za-z0-9._+-]*$')][string]$Tag,
    [string]$RepositoryPath = $PSScriptRoot,
    [string]$ArtifactDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$artifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$output = Join-Path $artifacts 'release-notes.md'
# A failed regeneration must not leave yesterday's checksums beside today's files.
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
$files = @('Lertaro-Setup.exe', 'Lertaro-Portable.zip', 'Lertaro-Portable.zip.sig',
    'Lertaro-Setup-arm64.exe', 'Lertaro-Portable-arm64.zip', 'Lertaro-Portable-arm64.zip.sig')
foreach ($name in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $artifacts $name) -PathType Leaf)) {
        throw "Missing release artifact: $name"
    }
}

Push-Location -LiteralPath $repository
try {
    $commit = git rev-parse --verify "refs/tags/$Tag^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Release tag not found: $Tag" }
    $headCommit = git rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0 -or $headCommit -ne $commit) {
        throw "HEAD must match release tag $Tag before generating its notes."
    }
    $previous = git describe --tags --abbrev=0 "$Tag^" 2>$null
    $range = if ($LASTEXITCODE -eq 0 -and $previous) { "$previous..$Tag" } else { $Tag }
    $commits = @(git -c core.quotepath=false log --encoding=UTF-8 --pretty=format:'* %s (%h)' --no-merges $range)
    if ($LASTEXITCODE -ne 0) { throw "Could not read changelog for $Tag." }
} finally { Pop-Location }

$trustSource = Join-Path $repository 'Core/Services/Update/UpdatePackage.cs'
$source = [IO.File]::ReadAllText($trustSource)
$declaration = [regex]::Match($source, '(?s)\bconst\s+string\s+PUBLIC_KEY_PEM\s*=\s*(.*?);')
$parts = [regex]::Matches($declaration.Groups[1].Value, '"([A-Za-z0-9+/=]+)\\n"')
if (-not $declaration.Success -or $parts.Count -eq 0) { throw 'The updater trust anchor could not be read.' }

# Use the verifier as a child script: its local variables cannot overwrite this script's state.
foreach ($zip in @('Lertaro-Portable.zip', 'Lertaro-Portable-arm64.zip')) {
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -PackagePath (Join-Path $artifacts $zip) -TrustSourcePath $trustSource
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add("# $Tag")
$lines.Add('')
$lines.Add("Commit: ``$commit``")
$lines.Add('')
$lines.Add('## Changelog (Commits)')
$lines.Add('')
foreach ($line in $commits) { $lines.Add($line) }
$lines.Add('')
$lines.Add('## Security Verification')
$lines.Add('')
$lines.Add('### SHA-256 Checksums')
foreach ($name in $files) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $artifacts $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines.Add("* **$name**: ``$hash``")
}
$lines.Add('')
$lines.Add('### Signature Verification (ECDsa NistP256)')
$lines.Add('The portable ZIPs are signed with the release key. Save the public key below as `public_key.pem`, then run:')
$lines.Add('')
$lines.Add('```bash')
foreach ($zip in @('Lertaro-Portable.zip', 'Lertaro-Portable-arm64.zip')) {
    $lines.Add("openssl dgst -sha256 -verify public_key.pem -signature $zip.sig $zip")
}
$lines.Add('# Expected output for each archive: Verified OK')
$lines.Add('```')
$lines.Add('')
$lines.Add('```pem')
$lines.Add('-----BEGIN PUBLIC KEY-----')
foreach ($part in $parts) { $lines.Add($part.Groups[1].Value) }
$lines.Add('-----END PUBLIC KEY-----')
$lines.Add('```')
[IO.File]::WriteAllText($output, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Release notes saved to: $output"
