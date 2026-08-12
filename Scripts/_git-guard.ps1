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

# --------------------------------------------------------------------------- the LIVE tag
#
# `LIVE` names the commit production is currently running (Marty, 12.08.2026). "Only one commit may
# carry it" needs no policing: a tag name points at exactly one object by definition, so the tag
# *moves* rather than accumulates, and `git tag -f` is the whole of that guarantee.
#
# What does need policing is the tag being **wrong** — pointing at something production is not
# running. That happens through a deploy from another machine, a hand-edit on the server, or a
# rollback nobody told git about. So: the tag is only moved once the health check has confirmed the
# new version answering, and Test-LiveTag compares it against what production actually reports.
#
# LIVE-PREV mirrors the server's app.prev exactly — one level deep, no more. The server keeps one
# previous release, so keeping two would let the tags promise a rollback the server cannot perform.

<# The commit a tag points at, or $null when the tag does not exist. #>
function Get-TagCommit {
    param([Parameter(Mandatory)] [string] $Tag)

    $sha = git rev-parse -q --verify "refs/tags/$Tag^{commit}" 2>$null
    $global:LASTEXITCODE = 0
    if (-not $sha) { return $null }
    return $sha.Trim()
}

<# CurrentVersion as it was at a given commit — what the tag claims is running. #>
function Get-VersionAtCommit {
    param([Parameter(Mandatory)] [string] $Commit)

    $text = (git show "${Commit}:CedarClerk.Core/Consts.cs" 2>$null) -join "`n"
    $global:LASTEXITCODE = 0
    if (-not $text) { return $null }

    $match = [regex]::Match($text, 'CurrentVersion = "([^"]+)"')
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value
}

<#
.SYNOPSIS
Moves LIVE onto HEAD, keeping the tag it replaces as LIVE-PREV.

.DESCRIPTION
Called only after production has answered with the new version. Tagging earlier would let LIVE name
a commit that never finished shipping, which is worse than no tag at all: a wrong answer to "what is
running" is acted on, an absent one is investigated.

Never fatal. By the time this runs the site is already live, and failing a finished deploy over
bookkeeping would be the tail wagging the dog.
#>
function Set-LiveTag {
    $head = (git rev-parse -q --verify HEAD 2>$null)
    $global:LASTEXITCODE = 0
    if (-not $head) {
        Write-Host "      Could not read HEAD - LIVE was not moved." -ForegroundColor Yellow
        return $null
    }
    $head = $head.Trim()

    $previous = Get-TagCommit 'LIVE'

    # The outgoing LIVE becomes LIVE-PREV even when it is the same commit as HEAD: re-deploying the
    # same commit makes the server's app.prev that commit too, and a LIVE-PREV left pointing further
    # back would describe a release the server can no longer roll back to.
    if ($previous) {
        git tag -f 'LIVE-PREV' $previous 2>$null | Out-Null
    }
    else {
        # No LIVE means nothing here knows what the server is replacing, so any LIVE-PREV lying
        # around is a guess. Removing it makes a later rollback say "I do not know" instead.
        git tag -d 'LIVE-PREV' 2>$null | Out-Null
    }
    git tag -f 'LIVE' $head 2>$null | Out-Null
    $global:LASTEXITCODE = 0

    return [pscustomobject]@{
        Commit   = $head.Substring(0, 7)
        Previous = if ($previous) { $previous.Substring(0, 7) } else { $null }
    }
}

<#
.SYNOPSIS
Puts LIVE back where it was before the last deploy, for -Rollback.

.DESCRIPTION
The server's rollback moves app.prev into app and keeps nothing behind it; this does the same to the
tags, which is why LIVE-PREV is deleted rather than chained. With no LIVE-PREV to return to, LIVE is
removed: after a rollback the running commit is genuinely unknown here, and no tag is the honest way
to say so.
#>
function Restore-LiveTag {
    $previous = Get-TagCommit 'LIVE-PREV'

    if ($previous) {
        git tag -f 'LIVE' $previous 2>$null | Out-Null
        git tag -d 'LIVE-PREV' 2>$null | Out-Null
        $global:LASTEXITCODE = 0
        return $previous.Substring(0, 7)
    }

    git tag -d 'LIVE' 2>$null | Out-Null
    $global:LASTEXITCODE = 0
    return $null
}

<#
.SYNOPSIS
Reports where LIVE points and whether it agrees with the version production reports.

.PARAMETER LiveVersion
What the health endpoint answers right now, or an empty string when production is down.

.DESCRIPTION
Warns, never stops. A stale tag is a bookkeeping problem, and refusing to deploy over one would
leave production on the older code *and* the tag still wrong.
#>
function Test-LiveTag {
    param([string] $LiveVersion)

    $live = Get-TagCommit 'LIVE'
    if (-not $live) {
        Write-Host "Note: no LIVE tag yet - a successful deploy will create it." -ForegroundColor DarkGray
        return
    }

    $head = (git rev-parse -q --verify HEAD 2>$null)
    $global:LASTEXITCODE = 0
    $short = $live.Substring(0, 7)
    $tagged = Get-VersionAtCommit $live

    if ($head -and $head.Trim() -eq $live) {
        Write-Host "LIVE is already on HEAD ($short) - this deploy will leave it there." -ForegroundColor DarkGray
        return
    }

    if ($LiveVersion -and $tagged -and $tagged -ne $LiveVersion) {
        Write-Host "Note: LIVE points at $short (v$tagged), but production answers v$LiveVersion." -ForegroundColor Yellow
        Write-Host "  The tag is stale - something shipped without moving it. This deploy corrects it." -ForegroundColor DarkGray
        return
    }

    Write-Host "LIVE: $short$(if ($tagged) { " (v$tagged)" })" -ForegroundColor DarkGray
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
