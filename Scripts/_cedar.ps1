# Shared launcher for the three scripts in this folder (ADR-119). Dot-source it:
#
#     . (Join-Path $PSScriptRoot '_cedar.ps1')
#     Invoke-Cedar @('deploy', '--yes')
#
# The direction of the wrapping turned around on 12.08.2026. It used to be `cedar` running these
# scripts; it is now these scripts running `cedar`, so there is exactly one implementation of the
# build, the test run and the deploy — while `.\Scripts\deploy.ps1` keeps working under the name that
# is written in CLAUDE.md, in docs/ARCHITECTURE.md and in muscle memory.
#
# This file replaces _git-guard.ps1, whose only callers were the three scripts that now delegate.
# Those checks live in CedarClerk.Cli/Pipelines/GitGuard.cs, tests and all.

<#
.SYNOPSIS
How to run the CLI on this machine: the installed global tool, or the project in this repository.

.DESCRIPTION
The installed tool is preferred because it is what `cedar` means everywhere else. The fallback
matters more than it looks: without it these scripts would only work after install-cli.ps1 had been
run, and install-cli.ps1 is a script in this same folder — a circle that would be discovered by
whoever cloned the repository fresh, at the worst possible moment.
#>
function Get-CedarCommand {
    param([string] $RepoRoot)

    $installed = Get-Command 'cedar' -CommandType Application -ErrorAction SilentlyContinue
    if ($installed) {
        return [pscustomobject]@{ Exe = $installed.Source; Prefix = @() }
    }

    $project = Join-Path $RepoRoot 'CedarClerk.Cli\CedarClerk.Cli.csproj'
    if (-not (Test-Path $project)) {
        Write-Host "Neither 'cedar' on PATH nor $project - cannot continue." -ForegroundColor Red
        exit 1
    }

    Write-Host "cedar is not installed - running it from source (.\Scripts\install-cli.ps1 makes it a command)." -ForegroundColor DarkGray
    # --nologo -v q so the build output does not land in the middle of the tool's own screen.
    return [pscustomobject]@{
        Exe    = 'dotnet'
        Prefix = @('run', '--project', $project, '-c', 'Release', '--nologo', '-v', 'q', '--')
    }
}

<#
.SYNOPSIS
Runs the CLI and exits with its exit code.

.DESCRIPTION
Exiting with the tool's own code is the whole contract these shims have to keep: everything that
called deploy.ps1 or test.ps1 in a pipeline was reading $LASTEXITCODE, and a wrapper that swallowed
it would turn a failed deploy into a successful-looking one.
#>
function Invoke-Cedar {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    # The one way this arrangement can eat itself: an installed `cedar` from before ADR-119 still
    # runs these scripts, and these scripts now run `cedar`. Left alone that is an infinite ladder of
    # processes, and the symptom - a build that never starts - says nothing about the cause.
    #
    # The marker costs one environment variable and turns it into a sentence naming the fix.
    if ($env:CEDAR_SHIM -eq '1') {
        Write-Host ""
        Write-Host "The installed 'cedar' is older than these scripts and is calling them back." -ForegroundColor Red
        Write-Host "  Since ADR-119 the scripts delegate to the CLI, not the other way round." -ForegroundColor DarkGray
        Write-Host "  Fix it once:  .\Scripts\install-cli.ps1" -ForegroundColor Yellow
        Write-Host ""
        exit 1
    }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $cedar = Get-CedarCommand -RepoRoot $repoRoot

    $env:CEDAR_SHIM = '1'
    try   { & $cedar.Exe @($cedar.Prefix + $Arguments) }
    finally { Remove-Item Env:\CEDAR_SHIM -ErrorAction SilentlyContinue }

    exit $LASTEXITCODE
}
