param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$Clean,
    [switch]$Test,
    [switch]$Publish,
    [switch]$NoRestore,
    [switch]$IgnoreFailedSources
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot 'ElevateHelperWinUI.csproj'
$testProjectPath = Join-Path $repoRoot 'tests\ElevateHelper.Tests\ElevateHelper.Tests.csproj'
$runtime = switch ($Platform) {
    'x64' { 'win-x64' }
    'x86' { 'win-x86' }
    'ARM64' { 'win-arm64' }
}
$buildRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'ElevateHelperLocalBuild'
$artifactsPath = Join-Path $buildRoot 'artifacts'

function Resolve-DotNetPath {
    $command = Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidatePaths = @()

    foreach ($root in @($env:DOTNET_ROOT, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }

        if ($root -eq $env:LOCALAPPDATA) {
            $candidatePaths += Join-Path $root 'Microsoft\dotnet\dotnet.exe'
        }
        else {
            $candidatePaths += Join-Path (Join-Path $root 'dotnet') 'dotnet.exe'
        }
    }

    $resolvedPath = $candidatePaths |
        Where-Object { $_ -and (Test-Path $_) } |
        Select-Object -First 1

    if ($resolvedPath) {
        return $resolvedPath
    }

    throw @'
dotnet.exe was not found in this Windows environment.

If you are running through Parallels, install the .NET SDK inside the Windows VM, not only on macOS.

Check from Windows PowerShell:
  where.exe dotnet
  dotnet --info

If it is not installed, install .NET SDK 10 for Windows and open a new terminal window.
'@
}

$dotnetPath = Resolve-DotNetPath

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $dotnetPath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Set-Location $repoRoot

Write-Host "Elevate Helper local Windows build"
Write-Host "Configuration: $Configuration"
Write-Host "Platform:      $Platform"
Write-Host "Runtime:       $runtime"
Write-Host "dotnet:        $dotnetPath"
Write-Host "Artifacts:     $artifactsPath"
Write-Host "Restore:       $(if ($NoRestore) { 'Skipped' } else { 'Enabled' })"
Write-Host ""

if ($Clean) {
    if (Test-Path $buildRoot) {
        Remove-Item $buildRoot -Recurse -Force
    }

    $cleanArgs = @(
        'clean',
        $projectPath,
        '-c',
        $Configuration,
        '--artifacts-path',
        $artifactsPath,
        "-p:Platform=$Platform"
    )
    Invoke-DotNet -Arguments $cleanArgs
}

if (-not $NoRestore) {
    $restoreArgs = @(
        'restore',
        $projectPath,
        '--artifacts-path',
        $artifactsPath,
        "-p:Platform=$Platform"
    )

    if ($IgnoreFailedSources) {
        $restoreArgs += '--ignore-failed-sources'
    }

    Invoke-DotNet -Arguments $restoreArgs
}

if ($Publish) {
    $publishDir = Join-Path $buildRoot "publish\$runtime\$Configuration"

    $publishArgs = @(
        'publish',
        $projectPath,
        '-c',
        $Configuration,
        '--artifacts-path',
        $artifactsPath,
        "-p:Platform=$Platform",
        "-p:RuntimeIdentifier=$runtime",
        '-p:WindowsPackageType=None',
        '-p:SelfContained=true',
        '-p:PublishSingleFile=false',
        '-p:PublishReadyToRun=true',
        '-p:PublishTrimmed=false',
        '-o',
        $publishDir
    )
    Invoke-DotNet -Arguments $publishArgs

    Write-Host ""
    Write-Host "Published to: $publishDir"
}
else {
    $buildArgs = @(
        'build',
        $projectPath,
        '-c',
        $Configuration,
        '--artifacts-path',
        $artifactsPath,
        "-p:Platform=$Platform",
        '--no-restore'
    )
    Invoke-DotNet -Arguments $buildArgs
}

if ($Test) {
    if (-not $NoRestore) {
        $testRestoreArgs = @(
            'restore',
            $testProjectPath,
            '--artifacts-path',
            $artifactsPath
        )

        if ($IgnoreFailedSources) {
            $testRestoreArgs += '--ignore-failed-sources'
        }

        Invoke-DotNet -Arguments $testRestoreArgs
    }

    $testArgs = @(
        'test',
        $testProjectPath,
        '-c',
        $Configuration,
        '--artifacts-path',
        $artifactsPath,
        '--no-restore'
    )

    $installedRuntimes = & $dotnetPath --list-runtimes
    $hasNet8Runtime = $installedRuntimes |
        Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' } |
        Select-Object -First 1
    if (-not $hasNet8Runtime) {
        Write-Host "Microsoft.NETCore.App 8.x is not installed; running tests on net10.0 only."
        $testArgs += @('--framework', 'net10.0')
    }

    Invoke-DotNet -Arguments $testArgs
}

Write-Host ""
Write-Host "Build script finished successfully."
