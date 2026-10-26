# Installs an unmodified official stable Mihomo release next to built GUI.
# Uses the SHA-256 digest returned by the GitHub release asset API, and fails closed.
param(
    [string]$Version = 'v1.19.33',
    [string]$AppDirectory = ''
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if (-not $AppDirectory) {
    $AppDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\FlClash.Windows\bin\x64\Release\net48'
}
$assetName = "mihomo-windows-amd64-compatible-$Version.zip"
$tag = [uri]::EscapeDataString($Version)
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/$tag" -Headers @{ 'User-Agent' = 'FlClash-Windows-Native-build' }
if ($release.draft -or $release.prerelease) { throw 'Refusing draft or pre-release Mihomo binary.' }
$asset = @($release.assets | Where-Object { $_.name -ceq $assetName })
if ($asset.Count -ne 1) { throw "Missing official release asset: $assetName" }
$expected = [string]$asset[0].digest
if ($expected -cnotmatch '^sha256:[0-9a-f]{64}$') {
    throw 'Official asset digest is missing. Refusing to install an unverified executable.'
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ("flclash-mihomo-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $archive = Join-Path $temp $assetName
    Invoke-WebRequest -Uri $asset[0].browser_download_url -OutFile $archive -UseBasicParsing
    $actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $expected.Substring(7)) { throw "SHA256 mismatch for $assetName" }
    $expanded = Join-Path $temp 'unpacked'
    Expand-Archive -LiteralPath $archive -DestinationPath $expanded
    $executables = @(Get-ChildItem $expanded -File -Recurse -Filter '*.exe' | Where-Object { $_.Name -like 'mihomo*' })
    if ($executables.Count -ne 1) { throw 'Cannot uniquely identify Mihomo executable in verified archive.' }
    $coreDir = Join-Path $AppDirectory 'core'
    New-Item -ItemType Directory -Path $coreDir -Force | Out-Null
    Copy-Item -LiteralPath $executables[0].FullName -Destination (Join-Path $coreDir 'mihomo.exe') -Force
    Write-Host "Installed official Mihomo $Version (verified asset SHA256: $actual)"
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
