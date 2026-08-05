# -PiHost lets a deploy run by IP when mDNS drops (05.08.2026: raspberrypi.local stopped
# resolving mid-deploy and the script "succeeded" against the still-running old version).
param([string] $PiHost = "martycow@raspberrypi.local")

$AppDir = "/home/martycow/cedarclerk/app"
$HealthUrl = "https://cedarclerk.mooexe.dev/api/health"

$ErrorActionPreference = "Stop"

$CedarClerkDir = Split-Path $PSScriptRoot -Parent
$WebDir = Join-Path $CedarClerkDir "cedarclerk-web"
$PublishDir = Join-Path $CedarClerkDir "publish"

### ANGULAR APP
Write-Host "`n=== [1/6] Building Angular APP ===" -ForegroundColor Cyan
Push-Location $WebDir
npm run build
if ($LASTEXITCODE -ne 0)
{
     Write-Host "Angular APP build failed" -ForegroundColor Red; exit 1
}
Pop-Location

### .NET SERVER
Write-Host "`n=== [2/6] Building .NET Server ===" -ForegroundColor Cyan
if (Test-Path $PublishDir)
{
    Remove-Item $PublishDir -Recurse -Force
}

dotnet publish (Join-Path $CedarClerkDir "CedarClerk.Server") -c Release -o $PublishDir

if ($LASTEXITCODE -ne 0)
{
     Write-Host ".NET Server build failed" -ForegroundColor Red; exit 1
}

### BUNDLING FRONTEND
Write-Host "`n=== [3/6] Bundling frontend into wwwroot ===" -ForegroundColor Cyan
Copy-Item (Join-Path $WebDir "dist\cedarclerk-web\browser") (Join-Path $PublishDir "wwwroot") -Recurse

### Stopping service
# Every remote step checks its exit code: a failed ssh/scp used to fall through to the health
# check, which the still-running OLD version answered — a green "DEPLOYED OK" over no deploy.
Write-Host "`n=== [4/6] Stopping CedarClerk service on Raspberry Pi ===" -ForegroundColor Cyan
ssh $PiHost "sudo systemctl stop cedarclerk"
if ($LASTEXITCODE -ne 0)
{
    Write-Host "Could not reach the Pi to stop the service - nothing was changed" -ForegroundColor Red; exit 1
}

### Copying files to Raspberry Pi
Write-Host "`n=== [5/6] Copying files to Raspberry Pi ===" -ForegroundColor Cyan
scp -r (Join-Path $PublishDir "*") "${PiHost}:${AppDir}/"
if ($LASTEXITCODE -ne 0)
{
    Write-Host "Copy failed - the service is STOPPED; fix connectivity and re-run, or start it: ssh $PiHost 'sudo systemctl start cedarclerk'" -ForegroundColor Red; exit 1
}

### Launching service
Write-Host "`n=== [6/6] Starting CedarClerk service on Raspberry Pi ===" -ForegroundColor Cyan
ssh $PiHost "sudo systemctl start cedarclerk"
if ($LASTEXITCODE -ne 0)
{
    Write-Host "Start command failed - start it by hand: ssh $PiHost 'sudo systemctl start cedarclerk'" -ForegroundColor Red; exit 1
}

### Checking health
Write-Host "`nWaiting for server..." -ForegroundColor Cyan
# 90s, not 30: a start that applies a migration took ~40s on the Pi (05.08.2026) and the old
# window declared a healthy deploy failed.
$resp = $null
for ($i = 0; $i -lt 30; $i++)
{
    Start-Sleep -Seconds 3
    try
    {
        $resp = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 5;
        break
    }
    catch {}
}

# The health check proves the DEPLOYED version answers, not merely that something does — the
# version comes from the same Consts.cs that was just built.
$expected = (Select-String -Path (Join-Path $CedarClerkDir "CedarClerk.Core\Consts.cs") -Pattern 'CurrentVersion = "([^"]+)"').Matches[0].Groups[1].Value

if ($resp -and $resp.version -eq $expected)
{
    Write-Host "`nDEPLOYED OK:" -ForegroundColor Green
    $resp | ConvertTo-Json
}
elseif ($resp)
{
    Write-Host "`nVERSION MISMATCH - the server answers v$($resp.version), the build is v${expected}: the old binaries are still running" -ForegroundColor Red
    exit 1
}
else
{
    Write-Host "`nHealth check FAILED - check: ssh $PiHost 'journalctl -u cedarclerk -n 30'" -ForegroundColor Red
    exit 1
}