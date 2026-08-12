# Cedar Clerk deploy.
#
#   .\Scripts\deploy.ps1                 build, ship, swap, verify
#   .\Scripts\deploy.ps1 -SkipBuild      ship what is already in publish/ (re-run after a dropped upload)
#   .\Scripts\deploy.ps1 -Desktop        also build the installer and publish it for self-update
#   .\Scripts\deploy.ps1 -Rollback       put the previous release back and start it
#   .\Scripts\deploy.ps1 -Force          run the git guard as a warning instead of a stop
#   .\Scripts\deploy.ps1 -Ascii          plain glyphs for a console that cannot draw box characters
#
# WHY IT LOOKS LIKE THIS (rewritten 11.08.2026, ADR-113)
# The old pipeline was: stop the service -> scp -r 174 loose files -> start it. Two things were
# wrong with that. (1) The upload kept dying near the end ("client_loop: send disconnect:
# Connection reset") somewhere inside wwwroot, and since the service had been stopped FIRST, every
# failure left production down with a half-copied app directory - which is exactly the state this
# rewrite was written in. (2) scp -r cannot resume, so a re-run started the whole 50 MB again.
#
# Now: everything slow happens while the old version is still serving. The build is packed into one
# tarball, uploaded as a single resumable stream (a dropped connection continues from the byte it
# reached, not from zero), checksummed on the far side, and unpacked into app.new. Only then is the
# service stopped, and the downtime is two directory renames - about a second. The previous release
# stays as app.prev, so -Rollback is instant.
#
# What this deletes on the server: app.prev (the release before last) and stale tarballs in
# staging/. Of ~/cedarclerk/data it writes exactly one subdirectory - downloads/, the desktop
# installer and its manifest (ADR-116, -Desktop only) - and never anything else there.
param(
    [string] $CloudHost = "martycow@periwinkle.mooexe.dev",
    [switch] $Force,
    [switch] $SkipBuild,
    [switch] $Desktop,
    [switch] $Rollback,
    [switch] $Ascii,
    [int]    $Retries = 5
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$RepoRoot    = Split-Path $PSScriptRoot -Parent
$WebDir      = Join-Path $RepoRoot 'cedarclerk-web'
$PublishDir  = Join-Path $RepoRoot 'publish'
$DesktopDir  = Join-Path $RepoRoot 'CedarClerk.Desktop'
$CacheDir    = Join-Path $env:TEMP 'cedarclerk-deploy'

$RemoteRoot  = '/home/martycow/cedarclerk'
$AppDir      = "$RemoteRoot/app"
$NewDir      = "$RemoteRoot/app.new"
$PrevDir     = "$RemoteRoot/app.prev"
$Staging     = "$RemoteRoot/staging"
# ADR-116 - under data/ because app/ is replaced wholesale on every deploy, and an installer there
# would disappear on the next ordinary release together with the manifest pointing at it.
$Downloads   = "$RemoteRoot/data/downloads"
$HealthUrl   = 'https://cedarclerk.mooexe.dev/api/health'
$DownloadUrl = 'https://cedarclerk.mooexe.dev/downloads'

# Keepalives, because the failure this script exists for is a connection that goes quiet and gets
# reset. BatchMode makes a key problem fail immediately instead of hanging on a hidden prompt.
$SshOpts = @('-o', 'ServerAliveInterval=15', '-o', 'ServerAliveCountMax=6', '-o', 'ConnectTimeout=20', '-o', 'BatchMode=yes')

#region ----------------------------------------------------------------- output

if ($Ascii) {
    $G = @{ TL = '+'; TR = '+'; BL = '+'; BR = '+'; ML = '+'; MR = '+'; H = '-'; V = '|'
            OK = 'OK'; NO = 'XX'; WARN = '!!'; ARR = '->'; FULL = '#'; EMPTY = '.'; DOT = '*' }
}
else {
    $G = @{ TL = "$([char]0x256D)"; TR = "$([char]0x256E)"; BL = "$([char]0x2570)"; BR = "$([char]0x256F)"
            ML = "$([char]0x251C)"; MR = "$([char]0x2524)"; H = "$([char]0x2500)"; V = "$([char]0x2502)"
            OK = "$([char]0x2714)"; NO = "$([char]0x2718)"; WARN = "$([char]0x25B2)"; ARR = "$([char]0x2192)"
            FULL = "$([char]0x2588)"; EMPTY = "$([char]0x2591)"; DOT = "$([char]0x25CF)" }
}

$BoxWidth = 72

function Write-Box {
    param(
        [string]   $Title,
        [object[]] $Rows,
        [string]   $Color = 'Cyan',
        [int]      $LabelWidth = 11
    )
    $inner = $BoxWidth - 2
    $pad = $inner - 2
    Write-Host ''
    Write-Host ('  {0}{1}{2}' -f $G.TL, ($G.H * $inner), $G.TR) -ForegroundColor $Color
    if ($Title) {
        Write-Host ('  {0} ' -f $G.V) -ForegroundColor $Color -NoNewline
        Write-Host $Title.PadRight($pad) -ForegroundColor White -NoNewline
        Write-Host (' {0}' -f $G.V) -ForegroundColor $Color
        Write-Host ('  {0}{1}{2}' -f $G.ML, ($G.H * $inner), $G.MR) -ForegroundColor $Color
    }
    foreach ($row in $Rows) {
        $label = ''
        $value = ''
        $color = 'Gray'
        if ($row -is [array]) {
            $label = [string]$row[0]
            $value = [string]$row[1]
            if ($row.Count -gt 2) { $color = [string]$row[2] }
        }
        else { $value = [string]$row }

        $room = $pad
        Write-Host ('  {0} ' -f $G.V) -ForegroundColor $Color -NoNewline
        if ($label) {
            if ($label.Length -gt $LabelWidth - 1) { $label = $label.Substring(0, $LabelWidth - 1) }
            Write-Host $label.PadRight($LabelWidth) -ForegroundColor DarkGray -NoNewline
            $room = $pad - $LabelWidth
        }
        if ($value.Length -gt $room) { $value = $value.Substring(0, $room) }
        Write-Host $value.PadRight($room) -ForegroundColor $color -NoNewline
        Write-Host (' {0}' -f $G.V) -ForegroundColor $Color
    }
    Write-Host ('  {0}{1}{2}' -f $G.BL, ($G.H * $inner), $G.BR) -ForegroundColor $Color
}

function Format-Size {
    param([double] $Bytes)
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N0} KB' -f ($Bytes / 1KB)) }
    return ('{0:N0} B' -f $Bytes)
}

function Format-Duration {
    param([TimeSpan] $Span)
    if ($Span.TotalSeconds -lt 1) { return ('{0:N0}ms' -f $Span.TotalMilliseconds) }
    if ($Span.TotalSeconds -lt 60) { return ('{0:N1}s' -f $Span.TotalSeconds) }
    return ('{0}m {1:00}s' -f [int]$Span.TotalMinutes, $Span.Seconds)
}

$script:StepNo     = 0
$script:TotalSteps = 9
$script:Steps      = @()
$script:Current    = $null

function Start-Step {
    param([string] $Name, [string] $Detail)
    $script:StepNo++
    $script:Current = @{ Name = $Name; Watch = [Diagnostics.Stopwatch]::StartNew() }
    Write-Host ''
    Write-Host ('  {0} ' -f $G.DOT) -ForegroundColor DarkCyan -NoNewline
    Write-Host ('[{0}/{1}] ' -f $script:StepNo, $script:TotalSteps) -ForegroundColor DarkGray -NoNewline
    Write-Host $Name -ForegroundColor Cyan -NoNewline
    if ($Detail) { Write-Host ("  $Detail") -ForegroundColor DarkGray } else { Write-Host '' }
}

function Complete-Step {
    param([string] $Note)
    $script:Current.Watch.Stop()
    $script:Steps += [pscustomobject]@{
        Name    = $script:Current.Name
        Elapsed = $script:Current.Watch.Elapsed
        Note    = $Note
    }
    Write-Host ('      {0}  ' -f $G.OK) -ForegroundColor Green -NoNewline
    Write-Host (Format-Duration $script:Current.Watch.Elapsed) -ForegroundColor DarkGray -NoNewline
    if ($Note) { Write-Host ("   $Note") -ForegroundColor DarkGray } else { Write-Host '' }
}

# For a step that failed without endangering anything - the desktop installer after the site is
# already live. A green tick there would be a lie, and Stop-Deploy would throw away the report of a
# deploy that actually worked.
function Stop-Step {
    param([string] $Note)
    $script:Current.Watch.Stop()
    $script:Steps += [pscustomobject]@{
        Name    = $script:Current.Name
        Elapsed = $script:Current.Watch.Elapsed
        Note    = $Note
    }
    Write-Host ('      {0}  ' -f $G.NO) -ForegroundColor Yellow -NoNewline
    Write-Host (Format-Duration $script:Current.Watch.Elapsed) -ForegroundColor DarkGray -NoNewline
    Write-Host ("   $Note") -ForegroundColor Yellow
}

function Write-Note { param([string] $Text) Write-Host "      $Text" -ForegroundColor DarkGray }
function Write-Warn { param([string] $Text) Write-Host ('      {0}  {1}' -f $G.WARN, $Text) -ForegroundColor Yellow }

function Stop-Deploy {
    param([string] $Message, [string[]] $Hints, [string[]] $State)
    Write-Host ''
    Write-Host ('  {0}  {1}' -f $G.NO, $Message) -ForegroundColor Red
    foreach ($line in $State) { Write-Host "      $line" -ForegroundColor Gray }
    if ($Hints) {
        Write-Host ''
        Write-Host '      what to do:' -ForegroundColor DarkGray
        foreach ($hint in $Hints) { Write-Host "        $hint" -ForegroundColor Yellow }
    }
    Write-Host ''
    exit 1
}

function Write-Bar {
    param([double] $Fraction, [string] $Right, [int] $Width = 20)
    if ($Fraction -gt 1) { $Fraction = 1 }
    $filled = [int][math]::Round($Width * $Fraction)
    Write-Host "`r      " -NoNewline
    Write-Host (($G.FULL * $filled) + ($G.EMPTY * ($Width - $filled))) -ForegroundColor Cyan -NoNewline
    Write-Host ('  {0,4:0}%  {1}' -f ($Fraction * 100), $Right).PadRight(46) -ForegroundColor DarkGray -NoNewline
}

#endregion
#region ----------------------------------------------------------------- remote

function Invoke-Remote {
    param([string] $Command)
    # ssh writing anything to stderr must not abort the script: with $ErrorActionPreference = 'Stop'
    # a redirected native stderr line becomes a terminating error, and a stray host-key notice would
    # then read as "the deploy crashed".
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & ssh @SshOpts $CloudHost $Command 2>&1 }
    finally { $ErrorActionPreference = $previous }

    return [pscustomobject]@{
        Code   = $LASTEXITCODE
        Output = (($output | ForEach-Object { [string]$_ }) -join "`n").Trim()
    }
}

function Get-RemoteSize {
    param([string] $Path)
    $result = Invoke-Remote "stat -c %s '$Path' 2>/dev/null || echo 0"
    if ($result.Code -ne 0) { return [int64] -1 }
    $value = [int64] 0
    $digits = ($result.Output -split "`n" | Where-Object { $_ -match '^\d+$' } | Select-Object -Last 1)
    if ($digits -and [int64]::TryParse($digits.Trim(), [ref] $value)) { return $value }
    return [int64] -1
}

# One attempt at streaming the tail of a local file into `cat >>` on the far side. Not scp, because
# scp restarts a dropped transfer from byte zero and this file keeps dying at 90%.
function Send-Tail {
    param([string] $LocalPath, [int64] $Offset, [int64] $Total, [string] $RemotePath)

    $sshArgs = ($SshOpts -join ' ') + " $CloudHost `"cat >> '$RemotePath'`""
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = 'ssh'
    $psi.Arguments              = $sshArgs
    $psi.RedirectStandardInput  = $true
    $psi.UseShellExecute        = $false
    $psi.CreateNoWindow         = $true

    $proc   = [System.Diagnostics.Process]::Start($psi)
    $stream = [System.IO.File]::OpenRead($LocalPath)
    $null   = $stream.Seek($Offset, [System.IO.SeekOrigin]::Begin)

    $buffer   = New-Object byte[] 524288
    $sent     = $Offset
    $watch    = [Diagnostics.Stopwatch]::StartNew()
    $lastDraw = -1000.0
    $broke    = $false

    try {
        while ($true) {
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -le 0) { break }
            $proc.StandardInput.BaseStream.Write($buffer, 0, $read)
            $sent += $read

            if (($watch.Elapsed.TotalMilliseconds - $lastDraw) -gt 120) {
                $lastDraw = $watch.Elapsed.TotalMilliseconds
                $speed = ($sent - $Offset) / [math]::Max(0.001, $watch.Elapsed.TotalSeconds)
                $eta = '--'
                if ($speed -gt 1024) { $eta = Format-Duration ([TimeSpan]::FromSeconds(($Total - $sent) / $speed)) }
                Write-Bar -Fraction ($sent / $Total) -Right ('{0} / {1}  {2}/s  ETA {3}' -f `
                    (Format-Size $sent), (Format-Size $Total), (Format-Size $speed), $eta)
            }
        }
        $proc.StandardInput.BaseStream.Flush()
    }
    catch { $broke = $true }
    finally {
        $stream.Dispose()
        try { $proc.StandardInput.Close() } catch { }
        $proc.WaitForExit()
    }

    $speed = ($sent - $Offset) / [math]::Max(0.001, $watch.Elapsed.TotalSeconds)
    Write-Bar -Fraction ($sent / $Total) -Right ('{0} / {1}  {2}/s' -f `
        (Format-Size $sent), (Format-Size $Total), (Format-Size $speed))
    Write-Host ''

    return (-not $broke) -and ($proc.ExitCode -eq 0)
}

# The retry-and-resume wrapper around Send-Tail, for the desktop artifacts (ADR-116). The release
# tarball keeps its own copy of this loop on purpose: that path is what production rides on and it
# has been proven in anger, and sharing code with a feature added later would put a change to the
# installer in the way of the site going out.
function Send-Artifact {
    param([string] $LocalPath, [string] $RemotePath, [switch] $Fresh)

    $total = (Get-Item $LocalPath).Length
    # A small file whose content changes under a fixed name (latest.yml) must never be resumed: the
    # bytes already there are a different manifest of the same length, and appending nothing to it
    # would look like success.
    if ($Fresh) { Invoke-Remote "rm -f '$RemotePath'" | Out-Null }

    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        $offset = Get-RemoteSize $RemotePath
        if ($offset -lt 0) { return $false }
        if ($offset -gt $total) {
            Write-Warn 'the file on the server is longer than the one being sent - starting it over'
            Invoke-Remote "rm -f '$RemotePath'" | Out-Null
            $offset = 0
        }
        if ($offset -eq $total) { return $true }
        if ($offset -gt 0) {
            Write-Note ("resuming at {0} ({1}% was already there)" -f (Format-Size $offset), [int](100 * $offset / $total))
        }

        Send-Tail -LocalPath $LocalPath -Offset $offset -Total $total -RemotePath $RemotePath | Out-Null
        if ((Get-RemoteSize $RemotePath) -eq $total) { return $true }

        if ($attempt -lt $Retries) {
            $wait = [math]::Min(20, 3 * $attempt)
            Write-Warn "the connection dropped - retrying in ${wait}s (attempt $attempt of $Retries)"
            Start-Sleep -Seconds $wait
        }
    }
    return $false
}

#endregion
#region ----------------------------------------------------------------- header

. (Join-Path $PSScriptRoot '_git-guard.ps1')
$version = Get-CedarVersion -RepoRoot $RepoRoot

$branch = (git rev-parse --abbrev-ref HEAD 2>$null)
$commit = (git log -1 --format='%h %s' 2>$null)
if ($commit -and $commit.Length -gt 44) { $commit = $commit.Substring(0, 44) + '...' }

$liveShort = 'down'
try {
    $live = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 8
    $liveShort = "v$($live.version)"
}
catch { }

$headerRows = @(
    @('version', ('{0}   {1}   v{2}' -f $liveShort, $G.ARR, $version), 'White'),
    @('branch', ('{0}   {1}' -f $branch, $commit)),
    @('target', "${CloudHost}:$AppDir"),
    @('health', $HealthUrl)
)
if ($liveShort -eq 'down') {
    $headerRows += , @('', "$($G.WARN) production is not answering right now - this run brings it back", 'Yellow')
}

Write-Box -Title ("CEDAR CLERK  {0}  DEPLOY" -f $G.DOT) -Rows $headerRows

if ($Rollback) {
    $script:TotalSteps = 2
    Start-Step 'Rollback' 'putting app.prev back'

    # app.prev is whatever was in app/ at the last swap, and that is not automatically a good release:
    # the first run after a failed scp deploy files it away half-copied. Rolling back onto that would
    # be a step backwards dressed as a recovery, so it has to look like a whole app first.
    $probe = Invoke-Remote @"
test -d '$PrevDir' || { echo NOPREV; exit 0; }
test -f '$PrevDir/CedarClerk.Server.dll' || echo NODLL
test -f '$PrevDir/wwwroot/index.html' || echo NOWWWROOT
echo "FILES=`$(find '$PrevDir' -type f | wc -l)"
"@
    if ($probe.Output -match 'NOPREV') {
        Stop-Deploy 'There is no previous release to roll back to.' `
            -State @("$PrevDir does not exist on the server.") `
            -Hints @('Deploy a known-good commit instead:  git checkout <tag>; .\Scripts\deploy.ps1')
    }
    if ($probe.Output -match 'NODLL|NOWWWROOT') {
        $missing = @()
        if ($probe.Output -match 'NODLL') { $missing += 'CedarClerk.Server.dll' }
        if ($probe.Output -match 'NOWWWROOT') { $missing += 'wwwroot/index.html' }
        if (-not $Force) {
            Stop-Deploy 'The previous release is incomplete - rolling back to it would break the site.' `
                -State @("$PrevDir is missing: $($missing -join ', ')",
                         'It is most likely the half-copied directory a failed deploy left behind.') `
                -Hints @('git checkout <a tag that worked>; .\Scripts\deploy.ps1',
                         '.\Scripts\deploy.ps1 -Rollback -Force    # if you know better')
        }
        Write-Warn "-Force: rolling back onto app.prev even though it is missing $($missing -join ', ')"
    }
    Write-Note "app.prev holds $([regex]::Match($probe.Output, 'FILES=(\d+)').Groups[1].Value) files and looks complete"

    $swap = Invoke-Remote @"
set -e
sudo systemctl stop cedarclerk
rm -rf '$RemoteRoot/app.broken'
mv '$AppDir' '$RemoteRoot/app.broken'
mv '$PrevDir' '$AppDir'
sudo systemctl start cedarclerk
echo SWAPPED
"@
    if ($swap.Code -ne 0 -or $swap.Output -notmatch 'SWAPPED') {
        Stop-Deploy 'Rollback failed halfway.' -State ($swap.Output -split "`n") `
            -Hints @("ssh $CloudHost 'ls -la $RemoteRoot'", "ssh $CloudHost 'sudo systemctl start cedarclerk'")
    }
    Complete-Step 'previous release is back; the bad one is kept as app.broken'

    Start-Step 'Health check'
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        try {
            $resp = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 5
            Complete-Step "server answers v$($resp.version)"

            # The tags follow the same swap the server just performed: LIVE-PREV becomes LIVE and
            # nothing is kept behind it, because the server kept nothing behind app.prev either.
            $restored = Restore-LiveTag
            $rollbackRows = @(@('running', "v$($resp.version)", 'Green'))
            $rollbackRows += , @('LIVE', $(if ($restored) { $restored } else { 'removed - nothing here knows what is running now' }),
                                 $(if ($restored) { 'Green' } else { 'Yellow' }))

            Write-Box -Title "$($G.OK) ROLLED BACK" -Rows $rollbackRows -Color Green
            exit 0
        }
        catch { }
    }
    Stop-Deploy 'Rolled back, but the server still does not answer.' `
        -Hints @("ssh $CloudHost 'systemctl status cedarclerk'")
}

if ($SkipBuild) { $script:TotalSteps = 6 }
# Building the installer and shipping it are two steps, and they run after the site is already live.
if ($Desktop) { $script:TotalSteps += 2 }

#endregion
#region ----------------------------------------------------------------- 1. preflight

Start-Step 'Preflight' 'git state, local tools, server state'

Assert-Branch -Allowed 'master' -Action 'Deploy' -Force:$Force
Assert-CleanTree -Action 'Deploy' -Force:$Force
Test-VersionTag -Version $version
# Only reports; a stale LIVE is bookkeeping, and stopping here would leave production on the older
# code with the tag still wrong. $liveShort carries a 'v' prefix, which the comparison does not want.
Test-LiveTag -LiveVersion $(if ($liveShort -eq 'down') { '' } else { $liveShort.TrimStart('v') })

if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
    Stop-Deploy 'tar is not on PATH - the whole transfer is one tarball, so it is required.' `
        -Hints @('Windows 10/11 ships it at C:\Windows\System32\tar.exe; Git for Windows also has one.')
}

if ($SkipBuild -and -not (Test-Path (Join-Path $PublishDir 'CedarClerk.Server.dll'))) {
    Stop-Deploy '-SkipBuild was given, but publish/ holds no build.' -Hints @('Run without -SkipBuild.')
}

if ($Desktop) {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Stop-Deploy '-Desktop needs npm on PATH - electron-builder is what makes the installer.'
    }
    if (-not (Test-Path (Join-Path $WebDir 'dist\cedarclerk-web\browser'))) {
        Stop-Deploy 'The installer bundles the Angular output, and cedarclerk-web/dist is empty.' `
            -Hints @('Run without -SkipBuild, or build the frontend first: cd cedarclerk-web; npm run build')
    }

    # The version an installed copy reports comes from package.json, and the tag on this commit is
    # what says which source it was built from. Letting the build sync them mid-deploy would ship an
    # installer whose version exists in no commit - so the mismatch is fixed here and the run stops,
    # rather than leaving a modified file behind after production has already moved.
    $pkgPath = Join-Path $DesktopDir 'package.json'
    $pkgVersion = (Get-Content $pkgPath -Raw | ConvertFrom-Json).version
    if ($pkgVersion -ne $version) {
        (Get-Content $pkgPath -Raw) -replace '"version":\s*"[^"]*"', "`"version`": `"$version`"" |
            Set-Content $pkgPath -NoNewline
        Stop-Deploy "The desktop shell says $pkgVersion, this build is $version - nothing has been touched." `
            -State @('package.json has just been corrected; it needs to be part of the tagged commit.') `
            -Hints @('git add CedarClerk.Desktop/package.json; git commit -m "Sync shell version"',
                     'then re-run the same deploy command')
    }
}

# One round trip for everything that decides whether it is worth starting.
$probe = Invoke-Remote @"
mkdir -p '$Staging'
echo "ACTIVE=`$(systemctl is-active cedarclerk 2>/dev/null)"
echo "FREEKB=`$(df -Pk '$RemoteRoot' | awk 'NR==2{print `$4}')"
echo "APPFILES=`$(find '$AppDir' -type f 2>/dev/null | wc -l)"
echo "PREV=`$(test -d '$PrevDir' && echo yes || echo no)"
"@
if ($probe.Code -ne 0) {
    Stop-Deploy "Cannot reach $CloudHost - nothing has been touched." -State ($probe.Output -split "`n") `
        -Hints @("ssh $CloudHost 'echo ok'   # check the connection first")
}

$active   = ([regex]::Match($probe.Output, 'ACTIVE=(\S*)')).Groups[1].Value
$freeKb   = [int64]([regex]::Match($probe.Output, 'FREEKB=(\d+)')).Groups[1].Value
$appFiles = [int]([regex]::Match($probe.Output, 'APPFILES=(\d+)')).Groups[1].Value
$hasPrev  = ([regex]::Match($probe.Output, 'PREV=(\S+)')).Groups[1].Value -eq 'yes'

Write-Note ("server   service $active, $appFiles files in app/, $(Format-Size ($freeKb * 1KB)) free" + $(if ($hasPrev) { ', app.prev present' } else { '' }))
if ($freeKb -lt 400000) { Write-Warn 'less than 400 MB free on the server - the swap keeps two copies of the app' }
if ($active -ne 'active') { Write-Warn "the service is $active right now, so production is already down - this deploy will bring it back" }

Complete-Step

#endregion
#region ----------------------------------------------------------------- 2-4. build

if (-not $SkipBuild) {
    Start-Step 'Angular' 'npm run build'
    Push-Location $WebDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { Stop-Deploy 'The Angular build failed - nothing was shipped.' }
    }
    finally { Pop-Location }
    Complete-Step

    Start-Step 'Server' 'dotnet publish -c Release'
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    dotnet publish (Join-Path $RepoRoot 'CedarClerk.Server') -c Release -o $PublishDir
    if ($LASTEXITCODE -ne 0) { Stop-Deploy 'The .NET publish failed - nothing was shipped.' }
    Complete-Step

    Start-Step 'Frontend bundle' 'Angular output into publish/wwwroot'
    Copy-Item (Join-Path $WebDir 'dist\cedarclerk-web\browser') (Join-Path $PublishDir 'wwwroot') -Recurse
    Complete-Step
}
else {
    Write-Host ''
    Write-Host '  -SkipBuild: shipping whatever is in publish/ already' -ForegroundColor Yellow
}

$publishFiles = @(Get-ChildItem $PublishDir -Recurse -File)
$publishBytes = ($publishFiles | Measure-Object -Property Length -Sum).Sum
$newest = ($publishFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
$signature = '{0}|{1}|{2:o}' -f $publishFiles.Count, $publishBytes, $newest

if (-not (Test-Path (Join-Path $PublishDir 'wwwroot\index.html'))) {
    Stop-Deploy 'publish/wwwroot/index.html is missing - that build would serve an API with no app.' `
        -Hints @('Run without -SkipBuild so the Angular output is bundled in.')
}

#endregion
#region ----------------------------------------------------------------- 5. pack

Start-Step 'Packing' "$($publishFiles.Count) files, $(Format-Size $publishBytes)"

if (-not (Test-Path $CacheDir)) { New-Item -ItemType Directory -Path $CacheDir | Out-Null }
$tarPath  = Join-Path $CacheDir "cedar-$version.tar.gz"
$metaPath = "$tarPath.meta"

# Reusing a byte-identical tarball is what makes a resumed upload possible: the half-uploaded file
# on the server is only a valid prefix if this side did not repack in between (gzip stamps a time,
# so repacking the same input still produces different bytes).
$reused = $false
if ((Test-Path $tarPath) -and (Test-Path $metaPath) -and ((Get-Content $metaPath -Raw).Trim() -eq $signature)) {
    $reused = $true
    Write-Note "reusing the artifact packed at $((Get-Item $tarPath).LastWriteTime.ToString('HH:mm')) - an interrupted upload can continue"
}
else {
    if (Test-Path $tarPath) { Remove-Item $tarPath -Force }
    $tarFlags = @()
    # GNU tar reads "C:\..." as host "C" path "\...". bsdtar (System32\tar.exe) does not.
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $tarBanner = (& tar --version 2>&1) -join ' ' } finally { $ErrorActionPreference = $previousEap }
    if ($tarBanner -match 'GNU tar') { $tarFlags += '--force-local' }
    & tar @tarFlags -czf $tarPath -C $PublishDir .
    if ($LASTEXITCODE -ne 0) { Stop-Deploy 'Packing the tarball failed.' }
    Set-Content -Path $metaPath -Value $signature -NoNewline
}

$tarBytes = (Get-Item $tarPath).Length
$localHash = (Get-FileHash $tarPath -Algorithm SHA256).Hash.ToLower()
$ratio = 100 - [int](100 * $tarBytes / [math]::Max(1, $publishBytes))
Complete-Step ("$(Format-Size $tarBytes) on the wire ({0}% smaller){1}" -f $ratio, $(if ($reused) { ', reused' } else { '' }))

#endregion
#region ----------------------------------------------------------------- 6. upload

Start-Step 'Uploading' 'the old version keeps serving while this runs'

$remoteTar = "$Staging/cedar-$version.tar.gz"
Invoke-Remote "find '$Staging' -maxdepth 1 -name 'cedar-*.tar.gz' ! -name 'cedar-$version.tar.gz' -delete 2>/dev/null; echo done" | Out-Null

$uploadWatch = [Diagnostics.Stopwatch]::StartNew()
$done = $false
for ($attempt = 1; $attempt -le $Retries; $attempt++) {
    $offset = Get-RemoteSize $remoteTar
    if ($offset -lt 0) {
        Stop-Deploy 'Lost the connection to the server while checking the upload.' `
            -Hints @('Re-run the same command - it will continue from where it stopped.')
    }
    if ($offset -gt $tarBytes) {
        Write-Warn 'the file on the server is longer than the one being sent - starting it over'
        Invoke-Remote "rm -f '$remoteTar'" | Out-Null
        $offset = 0
    }
    if ($offset -eq $tarBytes) { $done = $true; break }

    if ($offset -gt 0) {
        Write-Note ("resuming at {0} ({1}% was already there)" -f (Format-Size $offset), [int](100 * $offset / $tarBytes))
    }
    elseif ($attempt -gt 1) { Write-Note 'starting over from the beginning' }

    $ok = Send-Tail -LocalPath $tarPath -Offset $offset -Total $tarBytes -RemotePath $remoteTar
    if ($ok) { continue }   # the size check at the top of the loop is the real verdict

    if ($attempt -lt $Retries) {
        $wait = [math]::Min(20, 3 * $attempt)
        Write-Warn "the connection dropped - retrying in ${wait}s (attempt $attempt of $Retries)"
        Start-Sleep -Seconds $wait
    }
}
$uploadWatch.Stop()

if (-not $done -and (Get-RemoteSize $remoteTar) -ne $tarBytes) {
    Stop-Deploy "The upload could not finish after $Retries attempts - production is untouched and still running." `
        -State @("$(Format-Size (Get-RemoteSize $remoteTar)) of $(Format-Size $tarBytes) made it across.") `
        -Hints @('.\Scripts\deploy.ps1 -SkipBuild    # continues from that byte, does not rebuild')
}

$avg = $tarBytes / [math]::Max(0.001, $uploadWatch.Elapsed.TotalSeconds)
Complete-Step ("$(Format-Size $avg)/s average")

#endregion
#region ----------------------------------------------------------------- 7. verify and unpack

Start-Step 'Verify + unpack' 'still no downtime'

$check = Invoke-Remote "sha256sum '$remoteTar' | cut -d' ' -f1"
$remoteHash = $check.Output.Trim().ToLower()
if ($remoteHash -ne $localHash) {
    Invoke-Remote "rm -f '$remoteTar'" | Out-Null
    Stop-Deploy 'The uploaded file does not match the local one - it was deleted, production is untouched.' `
        -State @("local  $localHash", "server $remoteHash") `
        -Hints @('.\Scripts\deploy.ps1 -SkipBuild    # sends it again from scratch')
}
Write-Note "checksum matches ($($localHash.Substring(0,16))...)"

$unpack = Invoke-Remote @"
set -e
rm -rf '$NewDir'
mkdir -p '$NewDir'
tar -xzf '$remoteTar' -C '$NewDir'
echo "FILES=`$(find '$NewDir' -type f | wc -l)"
test -f '$NewDir/CedarClerk.Server.dll' || { echo MISSING_DLL; exit 3; }
test -f '$NewDir/wwwroot/index.html' || { echo MISSING_WWWROOT; exit 4; }
echo UNPACKED
"@
if ($unpack.Code -ne 0 -or $unpack.Output -notmatch 'UNPACKED') {
    Stop-Deploy 'Unpacking on the server failed - production is untouched and still running.' `
        -State ($unpack.Output -split "`n") `
        -Hints @("ssh $CloudHost 'ls -la $NewDir'")
}

$unpacked = [int]([regex]::Match($unpack.Output, 'FILES=(\d+)')).Groups[1].Value
if ($unpacked -ne $publishFiles.Count) {
    Stop-Deploy "The server unpacked $unpacked files, but the build has $($publishFiles.Count) - production is untouched." `
        -Hints @('.\Scripts\deploy.ps1 -SkipBuild')
}
Complete-Step "$unpacked files staged in app.new, both sides agree"

#endregion
#region ----------------------------------------------------------------- 8. swap

Start-Step 'Swap' 'the only moment the service is down'

# Everything here is renames, so the window is a second or so. It is one ssh call on purpose: a
# connection lost between two calls is what left production stopped in the first place.
$swap = Invoke-Remote @"
set -e
T0=`$(date +%s%3N)
sudo systemctl stop cedarclerk
rm -rf '$PrevDir'
mv '$AppDir' '$PrevDir'
mv '$NewDir' '$AppDir'
sudo systemctl start cedarclerk
echo "DOWNMS=`$((`$(date +%s%3N) - `$T0))"
echo SWAPPED
"@

if ($swap.Code -ne 0 -or $swap.Output -notmatch 'SWAPPED') {
    $state = Invoke-Remote "systemctl is-active cedarclerk; ls -d $RemoteRoot/app* 2>/dev/null"
    if ($state.Output -match '^inactive') {
        Write-Warn 'the swap broke halfway and the service is down - starting it back up'
        Invoke-Remote 'sudo systemctl start cedarclerk' | Out-Null
    }
    Stop-Deploy 'The swap failed.' -State (($swap.Output + "`n" + $state.Output) -split "`n") `
        -Hints @("ssh $CloudHost 'ls -la $RemoteRoot'",
                 '.\Scripts\deploy.ps1 -Rollback    # if app.prev is the good one')
}

$downMs = [int]([regex]::Match($swap.Output, 'DOWNMS=(\d+)')).Groups[1].Value
Complete-Step "down for $($downMs)ms; the old release is kept as app.prev"

#endregion
#region ----------------------------------------------------------------- 9. health

Start-Step 'Health check' 'waiting for the new version to answer'

# 120s, because a start that applies an EF migration has taken ~40s before now.
$resp = $null
$lastError = 'no answer yet'
$healthWatch = [Diagnostics.Stopwatch]::StartNew()
for ($i = 1; $i -le 40; $i++) {
    Write-Bar -Fraction ($i / 40) -Right ("waiting {0}   {1}" -f (Format-Duration $healthWatch.Elapsed), $lastError) -Width 20
    Start-Sleep -Seconds 3
    try {
        $resp = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 5
        break
    }
    catch {
        $lastError = $_.Exception.Message
        if ($lastError.Length -gt 24) { $lastError = $lastError.Substring(0, 24) }
    }
}
Write-Host "`r".PadRight(80) -NoNewline
Write-Host "`r" -NoNewline

if (-not $resp) {
    Complete-Step 'no answer'
    Stop-Deploy 'The new version never answered the health check.' `
        -State @("Tried $HealthUrl for $(Format-Duration $healthWatch.Elapsed).") `
        -Hints @("ssh $CloudHost 'systemctl status cedarclerk'",
                 '.\Scripts\deploy.ps1 -Rollback    # puts the previous release back in seconds')
}

if ($resp.version -ne $version) {
    Complete-Step "answers v$($resp.version)"
    Stop-Deploy "The server answers v$($resp.version), but v$version was shipped." `
        -State @('Something other than this build is serving that URL.') `
        -Hints @("ssh $CloudHost 'ls -la $AppDir | head'")
}

Complete-Step "v$($resp.version) is answering"

# Only now: production has answered with the version that was just shipped, which is the first
# moment "this commit is live" is a fact rather than an intention.
$liveTag = Set-LiveTag

#endregion
#region ----------------------------------------------------- 10-11. desktop installer (-Desktop)

# Everything below runs with the site already live, so nothing here can take production down. A
# failure costs the desktop update, not the deploy - which is why it reports instead of aborting.
$desktopNote = $null
$desktopColor = 'Green'

if ($Desktop) {
    Start-Step 'Desktop build' "electron-builder; the site is already serving v$version"

    $installer = Join-Path $DesktopDir "dist\CedarClerk-Setup-$version.exe"
    $manifest  = Join-Path $DesktopDir 'dist\latest.yml'
    $blockmap  = "$installer.blockmap"

    # build.ps1 owns how the shell is built; this only asks for it. -DesktopOnly reuses the Angular
    # output the steps above produced instead of building the frontend a second time.
    & (Join-Path $PSScriptRoot 'build.ps1') -DesktopOnly -Installer
    $builderCode = $LASTEXITCODE

    $missing = @(
        @{ Path = $installer; Name = 'the installer' },
        @{ Path = $manifest;  Name = 'latest.yml (electron-builder writes it from the publish block)' }
    ) | Where-Object { -not (Test-Path $_.Path) } | ForEach-Object { $_.Name }

    if ($builderCode -ne 0 -or $missing) {
        $desktopColor = 'Yellow'
        $desktopNote = 'not published - the installer was not built'
        Write-Warn 'the site is deployed and running; only the desktop update did not happen'
        foreach ($item in $missing) { Write-Note "missing: $item" }
        Stop-Step $desktopNote
    }
    else {
        $installerBytes = (Get-Item $installer).Length
        Complete-Step "$(Format-Size $installerBytes)$(if (Test-Path $blockmap) { ' + blockmap' })"

        Start-Step 'Publishing installer' "into data/downloads, manifest written last"

        # Staged first, moved second, manifest third. latest.yml is the only file an installed copy
        # reads, so as long as it appears after the file it names, a half-finished publish is
        # invisible rather than broken: clients keep seeing the previous version.
        $staging = "$Downloads/.staging"
        Invoke-Remote "mkdir -p '$staging'" | Out-Null

        $name = Split-Path $installer -Leaf
        $localHash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLower()
        $published = $false

        foreach ($pass in 1, 2) {
            if (-not (Send-Artifact -LocalPath $installer -RemotePath "$staging/$name")) { break }

            $remoteHash = (Invoke-Remote "sha256sum '$staging/$name' | cut -d' ' -f1").Output.Trim().ToLower()
            if ($remoteHash -eq $localHash) { $published = $true; break }

            # Same file name, different bytes: a partial upload of an earlier build of this same
            # version. Resuming spliced two builds together, so the only cure is starting over.
            Write-Warn "checksum mismatch on pass $pass - discarding the staged file and sending it again"
            Invoke-Remote "rm -f '$staging/$name'" | Out-Null
        }

        if (-not $published) {
            $desktopColor = 'Yellow'
            $desktopNote = 'not published - the upload did not verify'
            Stop-Step $desktopNote
        }
        else {
            Write-Note "checksum matches ($($localHash.Substring(0,16))...)"

            # Both are small, but a truncated one is worse than a missing one: a half-written
            # manifest is still a manifest as far as a client is concerned, and a half-written
            # blockmap breaks the differential download it exists to enable. So neither moves into
            # place unless its upload reported success.
            $blockmapOk = (Test-Path $blockmap) -and
                (Send-Artifact -LocalPath $blockmap -RemotePath "$staging/$name.blockmap" -Fresh)
            if ((Test-Path $blockmap) -and -not $blockmapOk) {
                Write-Warn 'the blockmap did not upload - updates will download the whole installer'
                Invoke-Remote "rm -f '$staging/$name.blockmap'" | Out-Null
            }

            $manifestOk = Send-Artifact -LocalPath $manifest -RemotePath "$staging/latest.yml" -Fresh

            # One call: move the payload in, then the manifest, then drop everything older than the
            # last two releases. Keeping one previous installer is what makes a bad build recoverable
            # by hand without a rebuild.
            $place = if (-not $manifestOk) { [pscustomobject]@{ Code = 1; Output = 'latest.yml did not upload' } } else { Invoke-Remote @"
set -e
mv -f '$staging/$name' '$Downloads/$name'
if [ -f '$staging/$name.blockmap' ]; then mv -f '$staging/$name.blockmap' '$Downloads/$name.blockmap'; fi
mv -f '$staging/latest.yml' '$Downloads/latest.yml'
ls -1t '$Downloads'/CedarClerk-Setup-*.exe 2>/dev/null | tail -n +3 | xargs -r -I{} rm -f {} {}.blockmap
echo "KEPT=`$(ls -1 '$Downloads'/CedarClerk-Setup-*.exe 2>/dev/null | wc -l)"
echo PLACED
"@
            }

            if ($place.Output -notmatch 'PLACED') {
                $desktopColor = 'Yellow'
                $desktopNote = 'uploaded, but moving it into place failed'
                Write-Warn $desktopNote
                foreach ($line in ($place.Output -split "`n")) { Write-Note $line }
                Stop-Step $desktopNote
            }
            else {
                $kept = [regex]::Match($place.Output, 'KEPT=(\d+)').Groups[1].Value
                $desktopNote = "v$version   $DownloadUrl/latest"

                # The one thing a file on disk cannot prove: that the route serves it. Worth a check
                # of its own - a manifest nobody can read is exactly as useless as no manifest.
                try {
                    $servedManifest = Invoke-RestMethod -Uri "$DownloadUrl/latest.yml" -TimeoutSec 15
                    if ("$servedManifest" -notmatch [regex]::Escape("version: $version")) {
                        throw "the served manifest does not name $version"
                    }
                    Complete-Step "$kept installer(s) kept; $DownloadUrl/latest.yml answers v$version"
                }
                catch {
                    $desktopColor = 'Yellow'
                    $desktopNote = "published, but $DownloadUrl/latest.yml did not verify"
                    Write-Warn $_.Exception.Message
                    Write-Note 'the files are on the server; it is the route serving them that did not answer'
                    Stop-Step 'published, manifest not verified'
                }
            }
        }
    }
}

#endregion
#region ----------------------------------------------------------------- summary

$total = [TimeSpan]::Zero
foreach ($step in $script:Steps) { $total += $step.Elapsed }

$rows = @()
foreach ($step in $script:Steps) {
    $bar = ''
    if ($total.TotalSeconds -gt 0) {
        $bar = $G.FULL * [int][math]::Round(18 * $step.Elapsed.TotalSeconds / $total.TotalSeconds)
    }
    $rows += , @($step.Name, ('{0,8}   {1}' -f (Format-Duration $step.Elapsed), $bar))
}
$rows += , @('', '')
$rows += , @('total', (Format-Duration $total), 'White')
$rows += , @('downtime', "$($downMs)ms  (renaming two directories)", 'Green')
$rows += , @('shipped', ('{0} files, {1} packed to {2}' -f $publishFiles.Count, (Format-Size $publishBytes), (Format-Size $tarBytes)))
$rows += , @('running', ("v{0}   {1}" -f $resp.version, $HealthUrl), 'Green')
if ($liveTag) {
    $rows += , @('LIVE', ("{0}{1}" -f $liveTag.Commit,
                          $(if ($liveTag.Previous) { "   (was $($liveTag.Previous), kept as LIVE-PREV)" } else { '' })), 'Green')
}
if ($Desktop) { $rows += , @('desktop', $desktopNote, $desktopColor) }
$rows += , @('rollback', '.\Scripts\deploy.ps1 -Rollback')

Write-Box -Title ("{0}  DEPLOYED   {1} {2} v{3}" -f $G.OK, $liveShort, $G.ARR, $version) `
    -Rows $rows -Color Green -LabelWidth 16

#endregion
