# Local build of everything, for checking a release before it goes anywhere (Marty, 10.08.2026).
#
#   .\Scripts\build.ps1                 Angular + server (Pi-shaped, linux-arm) + desktop shell
#   .\Scripts\build.ps1 -NoDesktop      skip the desktop (fast: no Electron download)
#   .\Scripts\build.ps1 -DesktopOnly    rebuild the shell against the Angular output already there
#   .\Scripts\build.ps1 -Installer      also produce CedarClerk-Setup-<version>.exe
#   .\Scripts\build.ps1 -RunDesktop     launch the shell when it is built
#
# This does NOT deploy and does NOT check the branch: building a feature branch to look at it is
# the point. The branch rule lives in deploy.ps1, where shipping actually happens.
param(
    [switch] $NoDesktop,
    [switch] $DesktopOnly,
    [switch] $Installer,
    [switch] $RunDesktop
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot 'cedarclerk-web'
$desktopRoot = Join-Path $repoRoot 'CedarClerk.Desktop'
$desktopServerDir = Join-Path $desktopRoot 'server'

. (Join-Path $PSScriptRoot '_git-guard.ps1')
$version = Get-CedarVersion -RepoRoot $repoRoot
Write-Host "Cedar Clerk $version" -ForegroundColor Cyan

function Assert-LastExit {
    param([string] $What)
    if ($LASTEXITCODE -ne 0) { Write-Host "$What failed" -ForegroundColor Red; exit 1 }
}

### 1. Angular
if (-not $DesktopOnly) {
    Write-Host "`n=== [1/3] Angular ===" -ForegroundColor Cyan
    Push-Location $webRoot
    try {
        npm run build
        Assert-LastExit 'Angular build'
    }
    finally { Pop-Location }
}

### 2. Server for the Pi
# armhf/linux-arm, not arm64: the Pi's kernel is 64-bit but its userland is 32-bit
# (.claude/rules/production-environment.md). Framework-dependent, because the Pi has the runtime
# installed and never builds anything.
if (-not $DesktopOnly) {
    Write-Host "`n=== [2/3] Server (linux-arm, framework-dependent - what the Pi runs) ===" -ForegroundColor Cyan
    $publishDir = Join-Path $repoRoot 'publish'
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    dotnet publish (Join-Path $repoRoot 'CedarClerk.Server') -c Release -o $publishDir
    Assert-LastExit 'Server publish'

    Copy-Item (Join-Path $webRoot 'dist\cedarclerk-web\browser') (Join-Path $publishDir 'wwwroot') -Recurse
    Write-Host "  -> $publishDir" -ForegroundColor DarkGray
}

### 3. Desktop
if ($NoDesktop) {
    Write-Host "`n=== [3/3] Desktop: skipped (-NoDesktop) ===" -ForegroundColor DarkGray
}
else {
    Write-Host "`n=== [3/3] Desktop shell ===" -ForegroundColor Cyan

    # The shell's version is checked against /api/health at startup, so a stale one shows up as a
    # dialog rather than as confusing behaviour. Keep it in step with Consts.cs automatically.
    $pkgPath = Join-Path $desktopRoot 'package.json'
    $pkg = Get-Content $pkgPath -Raw | ConvertFrom-Json
    if ($pkg.version -ne $version) {
        Write-Host "  syncing shell version $($pkg.version) -> $version" -ForegroundColor DarkGray
        (Get-Content $pkgPath -Raw) -replace '"version":\s*"[^"]*"', "`"version`": `"$version`"" |
            Set-Content $pkgPath -NoNewline
    }

    # Self-contained win-x64: the machine running this is not expected to have .NET installed.
    # A different runtime identifier from the Pi build above, on purpose — see docs/DESKTOP.md.
    Write-Host "`n  publishing server (win-x64, self-contained)..." -ForegroundColor DarkGray
    if (Test-Path $desktopServerDir) { Remove-Item $desktopServerDir -Recurse -Force }

    dotnet publish (Join-Path $repoRoot 'CedarClerk.Server') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false `
        -o $desktopServerDir
    Assert-LastExit 'Desktop server publish'

    $browserDir = Join-Path $webRoot 'dist\cedarclerk-web\browser'
    if (-not (Test-Path $browserDir)) {
        Write-Host "  Angular output missing - run without -DesktopOnly first" -ForegroundColor Red
        exit 1
    }
    Copy-Item $browserDir (Join-Path $desktopServerDir 'wwwroot') -Recurse
    Write-Host "  -> $desktopServerDir" -ForegroundColor DarkGray

    Push-Location $desktopRoot
    try {
        if (-not (Test-Path (Join-Path $desktopRoot 'node_modules'))) {
            Write-Host "`n  installing Electron (first run only, ~200MB)..." -ForegroundColor DarkGray
            npm install
            Assert-LastExit 'Electron install'
        }

        if ($Installer) {
            Write-Host "`n  packaging installer..." -ForegroundColor DarkGray
            npm run dist
            Assert-LastExit 'electron-builder'
            Write-Host "  -> $(Join-Path $desktopRoot 'dist')" -ForegroundColor DarkGray
        }

        if ($RunDesktop) {
            Write-Host "`n  launching the shell (close the window to return)..." -ForegroundColor DarkGray
            npm start
        }
    }
    finally { Pop-Location }
}

Write-Host "`nBuild OK - $version" -ForegroundColor Green
if (-not $RunDesktop -and -not $NoDesktop) {
    Write-Host "  run it:  cd CedarClerk.Desktop; npm start" -ForegroundColor DarkGray
}
