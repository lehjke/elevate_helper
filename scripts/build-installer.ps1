[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'ElevateHelperWinUI.csproj'
$installerScriptPath = Join-Path $repoRoot 'installer/ElevateHelper.iss'
$publishDir = Join-Path $repoRoot "artifacts/publish/$Runtime"
$releaseDir = Join-Path $repoRoot 'artifacts/release'
$appExePath = Join-Path $publishDir 'ElevateHelperWinUI.exe'
$outputInstallerPath = Join-Path $releaseDir "ElevateHelper-$Runtime-$Tag-setup.exe"

if (-not (Test-Path $appExePath)) {
    & (Join-Path $PSScriptRoot 'build-release.ps1') -Tag $Tag -Runtime $Runtime -Configuration $Configuration
}

if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

if (Test-Path $outputInstallerPath) {
    Remove-Item -Path $outputInstallerPath -Force
}

[xml]$projectXml = Get-Content -Path $projectPath
$appVersion = $projectXml.Project.PropertyGroup |
    Where-Object { $_.Version } |
    Select-Object -ExpandProperty Version -First 1

if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "Could not resolve <Version> from $projectPath"
}

$candidatePaths = @()
$command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if ($command) {
    $candidatePaths += $command.Source
}

foreach ($basePath in @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6'),
    ${env:ProgramFiles(x86)},
    $env:ProgramFiles
)) {
    if ([string]::IsNullOrWhiteSpace($basePath)) {
        continue
    }

    if ($basePath -like '*Inno Setup 6') {
        $candidatePaths += Join-Path $basePath 'ISCC.exe'
        continue
    }

    $candidatePaths += Join-Path $basePath 'Inno Setup 6\ISCC.exe'
}

$isccPath = $candidatePaths |
    Where-Object { $_ -and (Test-Path $_) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw 'ISCC.exe was not found. Install Inno Setup 6 before building the installer.'
}

& $isccPath `
    "/DAppVersion=$appVersion" `
    "/DReleaseTag=$Tag" `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$releaseDir" `
    "/DRuntime=$Runtime" `
    $installerScriptPath

if (-not (Test-Path $outputInstallerPath)) {
    throw "Installer was not created: $outputInstallerPath"
}

Write-Host "Installer created: $outputInstallerPath"
