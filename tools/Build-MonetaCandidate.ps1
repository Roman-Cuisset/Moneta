#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$')]
    [string]$Version = '0.1.0',
    [Parameter(Mandatory)][string]$InnoCompiler,
    [string]$LabPublicKey,
    [string]$Notes
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
if ([version]$Version -gt [version]'65535.65535.65535' -or ($Version.Split('.') | Where-Object { [int]$_ -gt 65535 })) { throw 'Version hors limites Windows.' }
$compiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
if (!$Notes) { $Notes = Join-Path $repo "docs/releases/$Version.md" }
$notesFile = (Resolve-Path -LiteralPath $Notes).Path
$notesText = [IO.File]::ReadAllText($notesFile).Trim()
if ($notesText.Length -eq 0 -or $notesText.Length -gt 24000) { throw 'Notes de publication absentes ou trop longues.' }
$mode = if ($LabPublicKey) { 'lab' } else { 'candidate' }
$root = Join-Path $repo "private/mission18/$mode/$Version"
$null = New-Item -ItemType Directory -Force -Path $root
$run = Join-Path $root ([guid]::NewGuid().ToString('N'))
$app = Join-Path $run 'app'
$hostBuild = Join-Path $run 'host'
$out = Join-Path $run 'artifacts'
$null = New-Item -ItemType Directory -Force -Path $out
$properties = @("-p:Version=$Version", "-p:FileVersion=$Version.0", '-p:ContinuousIntegrationBuild=true')
$labDefine = @()
if ($LabPublicKey) {
    $keyPath = (Resolve-Path -LiteralPath $LabPublicKey).Path
    if ([IO.File]::ReadAllText($keyPath).Trim() -ceq [IO.File]::ReadAllText((Join-Path $repo 'src/Moneta.Updates/release-key.spki')).Trim()) {
        throw 'Le laboratoire doit utiliser une clé distincte de la production.'
    }
    $properties += @('-p:MonetaLab=true', "-p:MonetaLabPublicKey=$keyPath")
    $labDefine += '/DMonetaLab'
}
function Publish-App([string]$project, [string]$target, [string[]]$options) {
    & dotnet publish (Join-Path $repo $project) -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false --artifacts-path (Join-Path $run 'intermediates') -o $target @properties @options
    if ($LASTEXITCODE -ne 0) { throw "Échec dotnet publish ($project)." }
}
Publish-App 'src/Moneta.App/Moneta.App.csproj' $app @('-p:DebugType=None','-p:DebugSymbols=false')
Publish-App 'src/Moneta.UpdateHost/Moneta.UpdateHost.csproj' $hostBuild @(
    '-p:PublishSingleFile=true', '-p:PublishTrimmed=true', '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=None', '-p:DebugSymbols=false'
)
Copy-Item -LiteralPath (Join-Path $hostBuild 'Moneta.UpdateHost.exe') -Destination $app
$notices = Join-Path $app 'ThirdPartyNotices'
$null = New-Item -ItemType Directory -Path $notices
Get-ChildItem -LiteralPath (Join-Path $repo 'packaging/notices') -File | Copy-Item -Destination $notices
$restore = Get-Content -Raw -LiteralPath (Join-Path $run 'intermediates/obj/Moneta.App/project.assets.json') | ConvertFrom-Json -AsHashtable
$runtime = Get-Content -Raw -LiteralPath (Join-Path $app 'Moneta.App.runtimeconfig.json') | ConvertFrom-Json
$frameworks = $runtime.runtimeOptions.includedFrameworks
$net = ($frameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$desktop = ($frameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').version
$sqlite = ($restore.libraries.Keys | Where-Object { $_ -match '^SQLite/' }) -replace '^SQLite/',''
foreach ($notice in @(
    @{ Path = "microsoft.netcore.app.runtime.win-x64/$net/LICENSE.TXT"; Name = 'DotNet.LICENSE' },
    @{ Path = "microsoft.netcore.app.runtime.win-x64/$net/THIRD-PARTY-NOTICES.TXT"; Name = 'DotNet.NOTICES' },
    @{ Path = "microsoft.windowsdesktop.app.runtime.win-x64/$desktop/LICENSE"; Name = 'WindowsDesktop.LICENSE' },
    @{ Path = "sqlite/$sqlite/LICENSE.txt"; Name = 'SQLite.LICENSE' }
)) {
    $sources = @($restore.packageFolders.Keys | ForEach-Object { Join-Path $_ $notice.Path } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if (!$sources.Count) { throw "Notice de redistribution manquante ($($notice.Name))." }
    Copy-Item -LiteralPath $sources[0] -Destination (Join-Path $notices $notice.Name)
}
$appExe = Get-Item -LiteralPath (Join-Path $app 'Moneta.App.exe')
if ($appExe.VersionInfo.FileVersion -ne "$Version.0") { throw 'Version inattendue du programme publié.' }
$pe = [IO.File]::OpenRead($appExe.FullName)
try {
    $reader = [IO.BinaryReader]::new($pe)
    $pe.Position = 0x3c
    $pe.Position = $reader.ReadUInt32()
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "L’application publiée doit être x64." }
} finally { $pe.Dispose() }
$forbidden = @(Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object Name -Match '\.(moneta|bp[0-9]*|pfx|pem|key|bak|sqlite|db|log|pdb)$')
if ($forbidden.Count) { throw 'Le dossier de publication contient des fichiers hors distribution.' }
& $compiler /Qp "/DReleaseVersion=$Version" "/DAppSource=$app" "/DCandidateOutput=$out" @labDefine (Join-Path $repo 'packaging/Moneta.iss')
if ($LASTEXITCODE -ne 0) { throw 'Échec de la compilation Inno Setup.' }
$setup = Get-Item -LiteralPath (Join-Path $out "Moneta-$Version-win-x64.exe")
$info = $setup.VersionInfo
$digits = "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart).$($info.FilePrivatePart)"
if ($info.ProductName.TrimEnd() -ne 'Moneta' -or $digits -ne "$Version.0") { throw "Version inattendue de l’installateur." }
$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $setup.FullName).Hash.ToLowerInvariant()
$metadata = [ordered]@{
    format = 1
    version = $Version
    architecture = 'win-x64'
    installer = $setup.Name
    size = $setup.Length
    sha256 = $sha256
    releaseNotes = $notesText
}
[IO.File]::WriteAllText((Join-Path $out 'moneta-update.unsigned.json'), ($metadata | ConvertTo-Json -Compress))
[IO.File]::WriteAllText((Join-Path $out 'CHANNEL.txt'), $mode)
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), "$sha256  $($setup.Name)" + [Environment]::NewLine)
Copy-Item -LiteralPath $notesFile -Destination (Join-Path $out 'NOTES-DE-VERSION.md')
Write-Host "Moneta $Version | $mode | $($setup.Length) octets | SHA-256 $sha256"
$out
