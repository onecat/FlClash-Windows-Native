$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet restore FlClashWindowsNative.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build FlClashWindowsNative.sln -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & .\tests\FlClash.Windows.SmokeTests\bin\x64\Release\net48\FlClash.Windows.SmokeTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed' }
} finally { Pop-Location }
