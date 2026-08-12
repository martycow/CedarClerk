# Cedar Clerk deploy.
#
#   .\Scripts\deploy.ps1                 build, ship, swap, verify
#   .\Scripts\deploy.ps1 -SkipBuild      ship what is already in publish/ (re-run after a dropped upload)
#   .\Scripts\deploy.ps1 -Desktop        also build the installer and publish it for self-update
#   .\Scripts\deploy.ps1 -Rollback       put the previous release back and start it
#   .\Scripts\deploy.ps1 -Force          run the git guard as a warning instead of a stop
#   .\Scripts\deploy.ps1 -Ascii          plain glyphs for a console that cannot draw box characters
#
# WHAT THIS IS NOW (ADR-119, 12.08.2026)
# The 893 lines that used to live here are C#: CedarClerk.Cli/Pipelines/DeployPipeline.cs. Same
# order, same guards, same scars — resumable upload because scp left production down twice
# (ADR-113), checksum on the far side because a truncated upload looks like a success, the service
# stopped only for two renames, app.prev kept for -Rollback, and the .exe -> .blockmap -> latest.yml
# order the desktop updater reads (ADR-116).
#
# This file stays because the name is in CLAUDE.md, in docs/ARCHITECTURE.md and in muscle memory —
# and because one implementation with two entrances beats two implementations.
#
# -Force is passed straight through: it turns the branch and clean-tree refusals into warnings that
# say what is being overridden. -Yes is implied here, since running this script IS the decision;
# `cedar deploy` on its own asks first.
param(
    [string] $CloudHost = "",
    [switch] $Force,
    [switch] $SkipBuild,
    [switch] $Desktop,
    [switch] $Rollback,
    [switch] $Ascii,
    [int]    $Retries = 5
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_cedar.ps1')

$arguments = @('deploy', '--yes')
if ($SkipBuild)                 { $arguments += '--skip-build' }
if ($Desktop)                   { $arguments += '--desktop' }
if ($Rollback)                  { $arguments += '--rollback' }
if ($Force)                     { $arguments += '--force' }
if ($Ascii)                     { $arguments += '--no-unicode' }
if ($CloudHost)                 { $arguments += @('--host', $CloudHost) }
if ($Retries -ne 5)             { $arguments += @('--retries', "$Retries") }

Invoke-Cedar $arguments
