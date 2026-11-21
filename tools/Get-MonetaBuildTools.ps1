#requires -Version 7.2
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path (Split-Path $PSScriptRoot) 'private/mission18/toolchain'
$null = New-Item -ItemType Directory -Force -Path $toolRoot
$download = Join-Path $toolRoot 'innosetup-6.7.4.exe'
$compilerDirectory = Join-Path $toolRoot 'inno'
Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' -OutFile $download
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or
    $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Pyrsys B.V.') {
    throw 'Signature Authenticode du compilateur Inno Setup invalide.'
}
$tool = Start-Process -FilePath $download -ArgumentList @(
    '/PORTABLE=1', '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="' + $compilerDirectory + '"')
) -Wait -PassThru -WindowStyle Hidden
if ($tool.ExitCode -ne 0) { throw "Extraction du compilateur en échec ($($tool.ExitCode))." }
(Resolve-Path -LiteralPath (Join-Path $compilerDirectory 'ISCC.exe')).Path
