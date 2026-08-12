# Makes `cedar` a real command on this machine (ADR-118).
#
#   .\Scripts\install-cli.ps1              build, pack and (re)install as a .NET global tool
#   .\Scripts\install-cli.ps1 -Uninstall    remove it again
#
# THIS IS THE FIRST THING TO RUN ON A FRESH CLONE (ADR-119 decision 1, rewritten). The build, the
# test run and the deploy used to be scripts in this folder; they are `cedar build`, `cedar test` and
# `cedar deploy` now, and this script is what makes that name exist. After it, nothing here is needed
# for ordinary work — only `e2e.ps1`, which `cedar test --smoke` calls for you.
#
# If `cedar` is ever broken and cannot install itself, the way round is one line and needs nothing
# from this folder:  dotnet run --project CedarClerk.Cli -- deploy --preflight
#
# Why a global tool and not a published folder on PATH: the SDK already owns
# %USERPROFILE%\.dotnet\tools and already put it on PATH, so nothing about the environment has to
# be edited by hand — and nothing has to be un-edited later. Re-running this script is the whole
# update procedure.
#
# The install is per-user and touches no system state.
#
# This script stays PowerShell and is not going to move (ADR-119 decision 3): installing `cedar` with
# `cedar` is a circle, and this is where it is cut.
param(
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'CedarClerk.Cli\CedarClerk.Cli.csproj'
$packageId = 'CedarClerk.Cli'
$nupkgDir = Join-Path $repoRoot 'publish\cli-nupkg'

# Removing the tool has exactly one acceptable failure: it was not installed. Everything else has to
# stop the script.
#
# This was learned the hard way (12.08.2026): a `cedar` left open in another terminal holds its own
# files in the tool store, the uninstall fails with "Access to the path ... is denied", and the
# install that follows then reports "Tool is already installed" and exits **zero**. The script
# happily printed "installed" over a version that had not changed. A failed install that claims
# success is worse than a loud one, because the next confusing thing you see is a missing command.
function Remove-CedarTool {
    $output = (dotnet tool uninstall --global $packageId 2>&1) -join "`n"
    $failed = $LASTEXITCODE -ne 0
    $global:LASTEXITCODE = 0

    if (-not $failed) { return }
    # Not installed is the normal case on a first run and on a machine being cleaned up twice.
    if ($output -match 'not installed|could not be found|is not found') { return }

    Write-Host ""
    Write-Host $output -ForegroundColor DarkGray
    if ($output -match 'denied|being used|another process') {
        $running = @(Get-Process -Name 'cedar' -ErrorAction SilentlyContinue)
        Write-Host "The tool's files are locked - a running copy is holding them." -ForegroundColor Red
        if ($running.Count -gt 0) {
            Write-Host "  Running now: PID $(($running | ForEach-Object { $_.Id }) -join ', ')" -ForegroundColor Yellow
        }
        Write-Host "  Close every open 'cedar' (the menu counts) and run this again." -ForegroundColor DarkGray
        Write-Host ""
    }
    throw 'Could not remove the installed tool.'
}

if ($Uninstall) {
    Remove-CedarTool
    Write-Host 'cedar removed.' -ForegroundColor Green
    return
}

# The version is not invented here: Consts.CurrentVersion is the single place it lives (CLAUDE.md),
# and packing with anything else would give `dotnet tool list` a number that matches nothing.
$constsPath = Join-Path $repoRoot 'CedarClerk.Core\Consts.cs'
$match = Select-String -Path $constsPath -Pattern 'CurrentVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
if (-not $match) { throw "Could not read CurrentVersion from $constsPath" }
$version = $match.Matches[0].Groups[1].Value

Write-Host "`n=== Packing cedar $version ===" -ForegroundColor Cyan

# A stale package of the same version in the output folder would be picked over the fresh one, so
# the folder is cleared rather than added to.
if (Test-Path $nupkgDir) { Remove-Item $nupkgDir -Recurse -Force }

dotnet pack $project -c Release -p:Version=$version --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }

Write-Host "`n=== Installing ===" -ForegroundColor Cyan

# Uninstall-then-install rather than `dotnet tool update`: update is a no-op when the version has
# not changed, which is the normal case during development — you rebuilt the same 0.11.0 and want
# the new binary anyway.
Remove-CedarTool

$installOutput = (dotnet tool install --global $packageId --version $version --add-source $nupkgDir 2>&1) -join "`n"
Write-Host $installOutput -ForegroundColor DarkGray
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool install failed' }

# `dotnet tool install` says this and still exits zero, so the exit code alone cannot tell a fresh
# install from a no-op. Left unchecked it is the exact path that reported success while leaving the
# old binary in place.
if ($installOutput -match 'already installed') {
    throw 'The old version is still installed - the uninstall above did not take effect.'
}

# ConfigStore finds the repository by walking up from the executable, which worked while the tool
# ran out of bin/. A global tool lives in the SDK's store instead, so outside this folder it has no
# way to know where the repository is. Seeding RepoRoot here is what keeps `cedar deploy` working
# from any directory — an existing config is merged into, never overwritten.
$configPath = Join-Path $env:APPDATA 'cedar\config.json'
$config = if (Test-Path $configPath) { Get-Content $configPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
$config | Add-Member -NotePropertyName 'RepoRoot' -NotePropertyValue $repoRoot -Force
New-Item -ItemType Directory -Force (Split-Path $configPath) | Out-Null
$config | ConvertTo-Json | Set-Content $configPath -Encoding UTF8

Write-Host "`ncedar $version installed." -ForegroundColor Green
Write-Host "repository: $repoRoot" -ForegroundColor Gray
Write-Host 'Open a new terminal, then: cedar                   (menu)' -ForegroundColor Gray
Write-Host '                          cedar status             (dashboard)' -ForegroundColor Gray
Write-Host '                          cedar deploy --preflight (the checks, and stop)' -ForegroundColor Gray
