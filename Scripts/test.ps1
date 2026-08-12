# One command for "is everything still green" (Marty, 10.08.2026).
#
#   .\Scripts\test.ps1              backend + frontend units + the contrast contract  (~40s)
#   .\Scripts\test.ps1 -Smoke       ...plus the 53-scenario Playwright suite          (~2min)
#   .\Scripts\test.ps1 -Backend     backend only
#   .\Scripts\test.ps1 -Frontend    frontend only
#   .\Scripts\test.ps1 -Detailed    print a line per test instead of only the summary
#
# No git check here on purpose: running tests on a feature branch is the normal case, and a guard
# that fires on the thing you do twenty times a day teaches people to pass -Force by reflex.
#
# -Detailed exists for `cedar test` (ADR-118), which draws a tick per test as the results arrive.
# It changes nothing but console verbosity, and it is opt-in so an ordinary run keeps the short
# output everyone is used to.
param(
    [switch] $Smoke,
    [switch] $Backend,
    [switch] $Frontend,
    [switch] $Detailed
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot 'cedarclerk-web'

# No selector means everything except the slow suite.
$runBackend = $Backend -or -not ($Backend -or $Frontend)
$runFrontend = $Frontend -or -not ($Backend -or $Frontend)

$results = [ordered]@{}
$failed = $false

function Invoke-Step {
    param([string] $Name, [scriptblock] $Body)

    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        $script:results[$Name] = 'FAILED'
        $script:failed = $true
        Write-Host "$Name FAILED" -ForegroundColor Red
    }
    else {
        $script:results[$Name] = 'ok'
    }
}

if ($runBackend) {
    # SchemaDriftGuardTests lives in here: it fails when Entities.cs has moved without a migration,
    # which is the one mistake that breaks the app at startup rather than at build time.
    if ($Detailed) {
        Invoke-Step 'Backend (dotnet test)' { dotnet test $repoRoot --nologo --logger 'console;verbosity=normal' }
    }
    else {
        Invoke-Step 'Backend (dotnet test)' { dotnet test $repoRoot --nologo }
    }
}

if ($runFrontend) {
    Push-Location $webRoot
    try {
        Invoke-Step 'Frontend units (vitest)' { npm run test }
        # Reads the tokens straight out of styles.scss and scores every pair the app renders, in
        # both themes. Cheap, and it is the only check that catches a colour choice going unreadable.
        Invoke-Step 'Contrast contract' { npm run check:contrast }
    }
    finally { Pop-Location }
}

if ($Smoke) {
    # Wipes a scratch CEDAR_DATA_DIR and runs with no bot token, so it can never touch real data or
    # knock the production bot off its token (.claude/rules/telegram-bot.md).
    Invoke-Step 'Smoke (Playwright, isolated database)' { & (Join-Path $PSScriptRoot 'e2e.ps1') }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
foreach ($entry in $results.GetEnumerator()) {
    $colour = if ($entry.Value -eq 'ok') { 'Green' } else { 'Red' }
    Write-Host ("  {0,-40} {1}" -f $entry.Key, $entry.Value) -ForegroundColor $colour
}

if (-not $Smoke) {
    Write-Host "`n  (smoke suite not run - add -Smoke)" -ForegroundColor DarkGray
}

if ($failed) { Write-Host "" ; exit 1 }
Write-Host "`nAll green." -ForegroundColor Green
