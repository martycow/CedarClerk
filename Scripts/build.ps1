# Local build of everything, for checking a release before it goes anywhere.
#
#   .\Scripts\build.ps1                 Angular + server (the publish that ships) + desktop shell
#   .\Scripts\build.ps1 -NoDesktop      skip the desktop (fast: no Electron download)
#   .\Scripts\build.ps1 -DesktopOnly    rebuild the shell against the Angular output already there
#   .\Scripts\build.ps1 -Installer      also produce CedarClerk-Setup-<version>.exe
#   .\Scripts\build.ps1 -RunDesktop     launch the shell when it is built
#
# The build itself is CedarClerk.Cli/Pipelines/BuildPipeline.cs since ADR-119; this is the entrance
# that keeps working. It still does NOT deploy and does NOT check the branch — building a feature
# branch to look at it is the point, and the branch rule belongs where shipping happens.
param(
    [switch] $NoDesktop,
    [switch] $DesktopOnly,
    [switch] $Installer,
    [switch] $RunDesktop
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_cedar.ps1')

# --yes because running the script is the answer to "may I delete publish/ and rebuild".
$arguments = @('build', '--yes')
if ($NoDesktop)   { $arguments += '--no-desktop' }
if ($DesktopOnly) { $arguments += '--desktop-only' }
if ($Installer)   { $arguments += '--installer' }
if ($RunDesktop)  { $arguments += '--run' }

Invoke-Cedar $arguments
