# Phase 10 smoke suite runner (ADR-070).
#
# Runs on Marty's machine only — Playwright ships no armhf browsers, so this never enters the
# deploy pipeline and never touches the Pi.
#
# What it does, and why in this order:
#   1. Wipes a scratch CEDAR_DATA_DIR, so every run starts from an empty database. Nothing here
#      can reach the real local dev data, let alone production.
#   2. Starts the server with NO bot token. The environment name is deliberately not
#      "Development": that is the only thing loading appsettings.Development.json, which is where
#      the token lives. No token means TelegramBotService logs "bot is disabled" and never
#      long-polls — which is what keeps this from 409-ing against the Pi
#      (see .claude/rules/telegram-bot.md).
#   3. Registers the test account, then restarts the server. The admin bootstrap in Program.cs
#      only grants rights at startup, so the account must exist before the start that promotes it.
#      This is why there are two starts and not one.
#   4. Runs Playwright, then stops the server whatever happened.

param(
    [switch]$Headed,
    [switch]$KeepData,
    [string]$Grep
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot 'cedarclerk-web'
$dataDir = Join-Path $repoRoot '.e2e-data'

$env:CEDAR_DATA_DIR = $dataDir
$env:ASPNETCORE_ENVIRONMENT = 'E2E'
$env:ASPNETCORE_URLS = 'http://localhost:8080'
$env:Cedar__InviteCode = 'e2e-invite'
$env:Cedar__AdminEmail = 'e2e-admin@local.test'
# blog.localhost resolves to 127.0.0.1 in Chromium without a hosts entry, so the blog branch and
# the app branch can share one Kestrel on one port, exactly as they do in production.
$env:Cedar__BlogHost = 'blog.localhost'
$env:Cedar__MainHost = 'http://localhost:8080'

$server = $null

function Stop-Server {
    if ($script:server -and -not $script:server.HasExited) {
        Write-Host '→ stopping server' -ForegroundColor DarkGray
        Stop-Process -Id $script:server.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $script:server.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
}

function Start-Server {
    # Redirected, not inherited: EF logs every CREATE TABLE of a fresh database, which buries the
    # Playwright output the run actually exists to show. The log stays for diagnosis.
    $log = Join-Path $repoRoot '.e2e-server.log'
    $script:server = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', (Join-Path $repoRoot 'CedarClerk.Server'), '--no-launch-profile') `
        -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError "$log.err"

    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod -Uri 'http://localhost:8080/api/health' -TimeoutSec 3
            Write-Host "→ server up (v$($health.version))" -ForegroundColor DarkGray
            return
        }
        catch { Start-Sleep -Milliseconds 700 }
    }
    throw 'Server did not answer /api/health within 90s'
}

try {
    if (Test-Path $dataDir) {
        if ($KeepData) {
            Write-Host '→ reusing existing scratch data (-KeepData)' -ForegroundColor Yellow
        }
        else {
            Write-Host "→ wiping scratch data: $dataDir" -ForegroundColor DarkGray
            Remove-Item -Recurse -Force $dataDir
        }
    }

    Write-Host '=== 1/4 first start (creates the database) ===' -ForegroundColor Cyan
    Start-Server

    Write-Host '=== 2/4 seeding the test account ===' -ForegroundColor Cyan
    $body = @{
        email      = $env:Cedar__AdminEmail
        password   = 'E2e-passw0rd!'
        inviteCode = $env:Cedar__InviteCode
    } | ConvertTo-Json
    try {
        Invoke-RestMethod -Uri 'http://localhost:8080/api/auth/register' -Method Post `
            -ContentType 'application/json' -Body $body | Out-Null
        Write-Host '→ account registered' -ForegroundColor DarkGray
    }
    catch {
        # Already there when -KeepData reuses a database; anything else is a real failure.
        if (-not $KeepData) { throw }
        Write-Host '→ account already exists, continuing' -ForegroundColor Yellow
    }

    Write-Host '=== 3/4 restart (admin bootstrap promotes the account) ===' -ForegroundColor Cyan
    Stop-Server
    Start-Server

    Write-Host '=== 4/4 Playwright ===' -ForegroundColor Cyan
    Push-Location $webRoot
    try {
        $args = @('playwright', 'test')
        if ($Headed) { $args += '--headed' }
        if ($Grep) { $args += @('--grep', $Grep) }
        & npx @args
        $exit = $LASTEXITCODE
    }
    finally { Pop-Location }

    if ($exit -ne 0) { Write-Host "Playwright exited with $exit" -ForegroundColor Red }
    exit $exit
}
finally {
    Stop-Server
}
