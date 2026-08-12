# Makes `cedar` a real command on this machine (ADR-118).
#
#   .\Scripts\install-cli.ps1              build, pack and (re)install as a .NET global tool
#   .\Scripts\install-cli.ps1 -Uninstall    remove it again
#
# Why a global tool and not a published folder on PATH: the SDK already owns
# %USERPROFILE%\.dotnet\tools and already put it on PATH, so nothing about the environment has to
# be edited by hand — and nothing has to be un-edited later. Re-running this script is the whole
# update procedure.
#
# The install is per-user and touches no system state. It does NOT deploy anything: `cedar deploy`
# is still a preflight that prints the command and stops (ADR-118 decision 2).
param(
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'CedarClerk.Cli\CedarClerk.Cli.csproj'
$packageId = 'CedarClerk.Cli'
$nupkgDir = Join-Path $repoRoot 'publish\cli-nupkg'

# `dotnet tool uninstall` exits non-zero when the tool is not installed, which is not a failure
# here — both the -Uninstall path and the reinstall below tolerate it deliberately.
function Remove-CedarTool {
    dotnet tool uninstall --global $packageId 2>&1 | Out-Null
    $global:LASTEXITCODE = 0
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

dotnet tool install --global $packageId --version $version --add-source $nupkgDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool install failed' }

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
Write-Host 'Open a new terminal, then: cedar        (menu)' -ForegroundColor Gray
Write-Host '                          cedar status  (dashboard)' -ForegroundColor Gray
Write-Host '                          cedar deploy  (preflight only)' -ForegroundColor Gray
