# Shared git checks for the scripts in this folder (T-138). Dot-source it:
#
#     . (Join-Path $PSScriptRoot '_git-guard.ps1')
#     Assert-Branch -Allowed 'master' -Action 'Deploy' -Force:$Force
#
# The rule this exists for is Marty's, 10.08.2026 (CLAUDE.md, "Branches"): master holds the latest
# stable version, every commit on it is tagged, and **every deploy runs from master and only from
# master**. Until now nothing enforced that — deploy.ps1 would just as happily ship `dev` or a
# feature branch, and the only sign afterwards would be a version that answers but is not the one
# anybody meant to release.

function Get-CurrentBranch {
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }
    return $branch.Trim()
}

<#
.SYNOPSIS
Stops the script unless HEAD is on one of the allowed branches.

.PARAMETER Allowed
Branch names that may run this action.

.PARAMETER Action
What is being attempted, for the message ("Deploy", "Build").

.PARAMETER Force
Proceed anyway, loudly. Exists because a script with no override gets copy-pasted around rather
than fixed — but it says exactly what is being overridden, so it cannot be used by accident.
#>
function Assert-Branch {
    param(
        [Parameter(Mandatory)] [string[]] $Allowed,
        [Parameter(Mandatory)] [string] $Action,
        [switch] $Force
    )

    $branch = Get-CurrentBranch
    if (-not $branch) {
        Write-Host "Not a git repository (or git is unavailable) - cannot verify the branch." -ForegroundColor Red
        exit 1
    }

    # A detached HEAD is never right for a deploy: there is no branch to say what was shipped.
    if ($branch -eq 'HEAD') {
        Write-Host "$Action refused: HEAD is detached, so there is no branch to attribute this to." -ForegroundColor Red
        if (-not $Force) { exit 1 }
    }
    elseif ($Allowed -notcontains $branch) {
        Write-Host ""
        if ($Force) {
            # Says what is being overridden rather than "refused", which would contradict the very
            # next line — a message that argues with itself is a message nobody reads.
            Write-Host "-Force: $Action from '$branch', which is not $($Allowed -join '/')." -ForegroundColor Yellow
            Write-Host "  master holds the latest stable version and is normally the only branch that ships (CLAUDE.md)." -ForegroundColor DarkGray
            Write-Host ""
            return
        }

        Write-Host "$Action refused: you are on '$branch'." -ForegroundColor Red
        Write-Host "  Allowed: $($Allowed -join ', ')" -ForegroundColor Yellow
        Write-Host "  master holds the latest stable version and is the only branch that ships (CLAUDE.md)." -ForegroundColor DarkGray
        Write-Host "  Override with -Force if you genuinely mean to." -ForegroundColor DarkGray
        Write-Host ""
        exit 1
    }

    Write-Host "Branch OK: $branch" -ForegroundColor DarkGray
}

<#
.SYNOPSIS
Stops the script when the working tree has uncommitted changes.

.DESCRIPTION
A deploy from a dirty tree ships binaries that match no commit, which makes "what is running in
production" unanswerable — and answering that is the whole reason master is the only source.
#>
function Assert-CleanTree {
    param(
        [Parameter(Mandatory)] [string] $Action,
        [switch] $Force
    )

    $dirty = git status --porcelain
    if (-not $dirty) {
        Write-Host "Working tree clean" -ForegroundColor DarkGray
        return
    }

    Write-Host ""
    Write-Host "$Action refused: the working tree has uncommitted changes." -ForegroundColor Red
    $dirty -split "`n" | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    if (($dirty -split "`n").Count -gt 10) { Write-Host "  ..." -ForegroundColor DarkGray }
    Write-Host "  What ships would match no commit, so nothing could say what is running." -ForegroundColor DarkGray
    if (-not $Force) {
        Write-Host "  Commit them, or override with -Force." -ForegroundColor DarkGray
        Write-Host ""
        exit 1
    }
    Write-Host "-Force: continuing with a dirty tree." -ForegroundColor Yellow
}

<#
.SYNOPSIS
Warns (does not stop) when HEAD carries no tag matching the version in Consts.cs.

.DESCRIPTION
Marty's rule is that every commit on master is tagged with its version. This only warns: forgetting
the tag is a bookkeeping mistake, not a broken release, and a hard stop here would make the script
refuse a perfectly good hotfix at the worst moment.
#>
function Test-VersionTag {
    param([Parameter(Mandatory)] [string] $Version)

    $tags = git tag --points-at HEAD
    if ($tags -contains $Version) {
        Write-Host "Tag OK: HEAD is tagged $Version" -ForegroundColor DarkGray
        return
    }

    Write-Host "Note: HEAD is not tagged '$Version'." -ForegroundColor Yellow
    if ($tags) { Write-Host "  Tags here: $($tags -join ', ')" -ForegroundColor DarkGray }
    Write-Host "  Every commit on master is meant to carry its version tag: git tag $Version" -ForegroundColor DarkGray
}

<# Reads CurrentVersion out of Consts.cs — the single place the version lives. #>
function Get-CedarVersion {
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $consts = Join-Path $RepoRoot 'CedarClerk.Core\Consts.cs'
    $match = Select-String -Path $consts -Pattern 'CurrentVersion = "([^"]+)"'
    if (-not $match) {
        Write-Host "Could not read CurrentVersion from $consts" -ForegroundColor Red
        exit 1
    }
    return $match.Matches[0].Groups[1].Value
}
