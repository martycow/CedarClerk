# One command for "is everything still green".
#
#   .\Scripts\test.ps1              backend + frontend units + the contrast contract  (~40s)
#   .\Scripts\test.ps1 -Smoke       ...plus the Playwright smoke suite                 (~2min)
#   .\Scripts\test.ps1 -Backend     backend only
#   .\Scripts\test.ps1 -Frontend    frontend only
#
# The phases live in CedarClerk.Cli/Pipelines/TestPipeline.cs since ADR-119, and each one's exit code
# is still the whole verdict. No git check here on purpose: running tests on a feature branch is the
# normal case, and a guard that fires on the thing you do twenty times a day teaches people to pass
# -Force by reflex.
#
# -Detailed is accepted and ignored. It existed to make dotnet test name every test so `cedar test`
# could draw a tick per result; that is now the only behaviour, so the flag has nothing left to turn
# on. It stays as a parameter so an old command line does not fail on an unknown argument.
param(
    [switch] $Smoke,
    [switch] $Backend,
    [switch] $Frontend,
    [switch] $Detailed
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_cedar.ps1')

$arguments = @('test')
if ($Smoke)    { $arguments += '--smoke' }
if ($Backend)  { $arguments += '--backend' }
if ($Frontend) { $arguments += '--frontend' }

Invoke-Cedar $arguments
