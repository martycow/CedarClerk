param([switch] $Uninstall)

$ErrorActionPreference = 'Stop'
$cliRepo = Split-Path -Parent $PSScriptRoot
$cliBinDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.dotnet\tools'
$cliTarget = Join-Path $cliBinDir 'cedar.exe'
$cliRelease = Join-Path $cliRepo 'cedar-cli\target\release\cedar.exe'

function Remove-LegacyCedar {
    $toolList = (& dotnet tool list --global 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect .NET tools: $toolList" }
    if ($toolList -match '(?im)^cedarclerk\.cli\s') {
        & dotnet tool uninstall --global CedarClerk.Cli
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the legacy tool. Close all cedar terminals and retry.' }
    }
}

if (@(Get-Process -Name cedar -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Close running cedar processes before replacing the executable.'
}

if ($Uninstall) {
    Remove-LegacyCedar
    if (Test-Path -LiteralPath $cliTarget) { Remove-Item -LiteralPath $cliTarget -Force }
    Write-Host 'cedar removed. JSON configurations are preserved.'
    return
}

& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'rust-cli.ps1') test --locked
if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed; the installed CLI was not changed.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'rust-cli.ps1') build --release --locked
if ($LASTEXITCODE -ne 0) { throw 'Rust release build failed; the installed CLI was not changed.' }
& $cliRelease --version
if ($LASTEXITCODE -ne 0) { throw 'The new executable did not start; the installed CLI was not changed.' }

New-Item -ItemType Directory -Path $cliBinDir -Force | Out-Null
$cliStaged = Join-Path $cliBinDir 'cedar.next.exe'
Copy-Item -LiteralPath $cliRelease -Destination $cliStaged -Force
if ((Get-FileHash -LiteralPath $cliStaged).Hash -ne (Get-FileHash -LiteralPath $cliRelease).Hash) {
    throw 'Staged executable checksum differs; the installed CLI was not changed.'
}
Remove-LegacyCedar
Move-Item -LiteralPath $cliStaged -Destination $cliTarget -Force

$cliConfig = Join-Path $env:APPDATA 'cedar\config.json'
if (-not (Test-Path -LiteralPath $cliConfig)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $cliConfig) -Force | Out-Null
    & $cliTarget --repo $cliRepo config init --output $cliConfig
    if ($LASTEXITCODE -ne 0) { throw 'The executable is installed but configuration initialization failed.' }
}

$cliUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (@($cliUserPath -split ';' | Where-Object { $_.TrimEnd('\') -ieq $cliBinDir }).Count -eq 0) {
    [Environment]::SetEnvironmentVariable('Path', ($cliBinDir + ';' + $cliUserPath), 'User')
}
& $cliTarget --version
if ($LASTEXITCODE -ne 0) { throw 'Installed executable verification failed.' }
Write-Host "Installed Rust CLI: $cliTarget" -ForegroundColor Green
Write-Host 'Run cedar for the animated dashboard. Existing JSON settings are preserved.'
