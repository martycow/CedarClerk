param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'check', 'test', 'clippy', 'fmt')]
    [string] $Task = 'build',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CargoArguments
)

$ErrorActionPreference = 'Stop'
$cliRepo = Split-Path -Parent $PSScriptRoot

if ($IsWindows -and $Task -ne 'fmt') {
    if (-not (Get-Command link.exe -ErrorAction SilentlyContinue)) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        $vsPath = if (Test-Path -LiteralPath $vswhere) {
            & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        }
        if (-not $vsPath) {
            $vsRoot = Join-Path $env:ProgramFiles 'Microsoft Visual Studio'
            $vsShell = Get-ChildItem -LiteralPath $vsRoot -Filter Launch-VsDevShell.ps1 -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        } else {
            $vsShell = Get-Item -LiteralPath (Join-Path $vsPath 'Common7\Tools\Launch-VsDevShell.ps1')
        }
        if (-not $vsShell) { throw 'Install Visual Studio C++ Build Tools and a Windows SDK, then retry.' }
        & $vsShell.FullName -SkipAutomaticLocation -Arch amd64 -HostArch amd64 | Out-Null
    }

    $hasSdk = @($env:LIB -split ';' | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'kernel32.lib')) }).Count -gt 0
    if (-not $hasSdk) {
        $sdkCache = Join-Path $cliRepo 'cedar-cli\target\windows-sdk'
        $sdkKernel = Get-ChildItem -LiteralPath $sdkCache -Filter kernel32.lib -Recurse -ErrorAction SilentlyContinue | Where-Object FullName -Match '[\\/]x64[\\/]' | Select-Object -First 1
        $sdkUcrt = Get-ChildItem -LiteralPath $sdkCache -Filter ucrt.lib -Recurse -ErrorAction SilentlyContinue | Where-Object FullName -Match '[\\/]x64[\\/]' | Select-Object -First 1
        $sdkHeader = Get-ChildItem -LiteralPath $sdkCache -Filter Windows.h -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $sdkKernel -or -not $sdkUcrt -or -not $sdkHeader) {
            throw 'Windows SDK libraries are missing. Install the Windows SDK C++ x64 component. The MSVC compiler alone is insufficient.'
        }
        $env:LIB = "$($sdkKernel.DirectoryName);$($sdkUcrt.DirectoryName);$env:LIB"
        $sdkInclude = Split-Path -Parent $sdkHeader.DirectoryName
        foreach ($part in @('ucrt', 'shared', 'um', 'winrt')) {
            $include = Join-Path $sdkInclude $part
            if (Test-Path -LiteralPath $include) { $env:INCLUDE = "$include;$env:INCLUDE" }
        }
    }
}

if ($Task -eq 'test') {
    # Windows locks a running cedar.exe; tests must not rebuild the invoking debug executable.
    & cargo test --manifest-path (Join-Path $cliRepo 'cedar-cli\Cargo.toml') --target-dir (Join-Path $cliRepo 'cedar-cli\target\verification') @CargoArguments
} elseif ($Task -eq 'clippy') {
    & cargo clippy --manifest-path (Join-Path $cliRepo 'cedar-cli\Cargo.toml') @CargoArguments -- -D warnings
} else {
    & cargo $Task --manifest-path (Join-Path $cliRepo 'cedar-cli\Cargo.toml') @CargoArguments
}
exit $LASTEXITCODE
