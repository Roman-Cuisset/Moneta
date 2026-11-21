#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [string]$LabPublicKey,
    [string]$LabKeyName
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$artifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
if ([bool]$LabPublicKey -ne [bool]$LabKeyName) { throw 'Fournissez les deux paramètres de signature de laboratoire.' }
$expectedChannel = if ($LabPublicKey) { 'lab' } else { 'candidate' }
if ([IO.File]::ReadAllText((Join-Path $artifactDirectory 'CHANNEL.txt')).Trim() -cne $expectedChannel) {
    throw 'Le canal de build et le canal de signature doivent correspondre.'
}
$keyFile = if ($LabPublicKey) { (Resolve-Path -LiteralPath $LabPublicKey).Path }
    else { Join-Path $repo 'src/Moneta.Updates/release-key.spki' }
$keyName = if ($LabKeyName) { $LabKeyName } else { 'Moneta.Release.Alpha.P256' }
if ($LabKeyName -and $LabKeyName -notlike 'Moneta.Lab.*') { throw 'Utilisez une clé Moneta.Lab.* pour les essais.' }

$bytes = [IO.File]::ReadAllBytes((Join-Path $artifactDirectory 'moneta-update.unsigned.json'))
$manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
if ($manifest.format -ne 1 -or $manifest.architecture -ne 'win-x64' -or
    $manifest.version -notmatch '^(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})$' -or
    $manifest.installer -cne "Moneta-$($manifest.version)-win-x64.exe" -or
    [string]::IsNullOrWhiteSpace($manifest.releaseNotes)) {
    throw 'Les métadonnées du candidat sont invalides.'
}
$setup = Get-Item -LiteralPath (Join-Path $artifactDirectory $manifest.installer)
if ($setup.Length -ne $manifest.size -or (Get-FileHash -Algorithm SHA256 -LiteralPath $setup.FullName).Hash -ne $manifest.sha256) {
    throw "Les métadonnées ne correspondent plus à l’installateur."
}
$authenticode = Get-AuthenticodeSignature -LiteralPath $setup.FullName
if ($authenticode.Status -notin @('Valid', 'NotSigned')) { throw "Authenticode invalide ($($authenticode.Status))." }
$container = [Security.Cryptography.CngKey]::Open($keyName)
try {
    $signer = [Security.Cryptography.ECDsaCng]::new($container)
    try {
        $actual = [Convert]::ToBase64String($signer.ExportSubjectPublicKeyInfo())
        if ($actual -cne [IO.File]::ReadAllText($keyFile).Trim()) { throw 'La clé ne correspond pas au canal compilé.' }
        $signature = $signer.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256)
        if (!$signer.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256)) { throw 'Autocontrôle de signature en échec.' }
    } finally { $signer.Dispose() }
} finally { $container.Dispose() }
$envelope = [ordered]@{ payload = [Convert]::ToBase64String($bytes); signature = [Convert]::ToBase64String($signature) }
$destination = Join-Path $artifactDirectory 'moneta-update.json'
[IO.File]::WriteAllText($destination, ($envelope | ConvertTo-Json -Compress))
$destination
