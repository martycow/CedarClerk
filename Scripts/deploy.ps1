$PiHost = "martycow@raspberrypi.local"
$AppDir = "/home/martycow/cedarclerk/app"
$HealthUrl = "https://cedarclerk.mooexe.dev/api/health"

$ErrorActionPreference = "Stop"

$CedarClerkDir = Join-Path (Split-Path $PSScriptRoot -Parent) "CedarClerk"
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
Write-Host "`n=== [4/6] Stopping CedarClerk service on Raspberry Pi ===" -ForegroundColor Cyan
ssh $PiHost "sudo systemctl stop cedarclerk"

### Copying files to Raspberry Pi
Write-Host "`n=== [5/6] Copying files to Raspberry Pi ===" -ForegroundColor Cyan
scp -r (Join-Path $PublishDir "*") "${PiHost}:${AppDir}/"

### Launching service
Write-Host "`n=== [6/6] Starting CedarClerk service on Raspberry Pi ===" -ForegroundColor Cyan
ssh $PiHost "sudo systemctl start cedarclerk"

### Checking health
Write-Host "`nWaiting for server..." -ForegroundColor Cyan
$resp = $null
for ($i = 0; $i -lt 10; $i++) 
{
    Start-Sleep -Seconds 3
    try 
    { 
        $resp = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 5; 
        break 
    } 
    catch {}
}

if ($resp) 
{
    Write-Host "`nDEPLOYED OK:" -ForegroundColor Green
    $resp | ConvertTo-Json
}
else 
{
    Write-Host "`nHealth check FAILED - check: ssh $PiHost 'journalctl -u cedarclerk -n 30'" -ForegroundColor Red
    exit 1
}