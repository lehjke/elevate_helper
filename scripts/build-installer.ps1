[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipPublish
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
$stagingRoot = Join-Path $env:TEMP 'ElevateHelperInstallerStage'
$stagingDir = Join-Path $stagingRoot $Runtime

$requiredTemplates = @(
    'Office.elvx',
    'Residential.elvx',
    'Hotel.elvx',
    'Office.xlsx',
    'Residential.xlsx',
    'Hotel.xlsx'
)

function Assert-NonEmptyFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }

    if ((Get-Item -LiteralPath $Path).Length -le 0) {
        throw "$Description is empty: $Path"
    }
}

function Assert-PublishPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    Assert-NonEmptyFile `
        -Path (Join-Path $Root 'ElevateHelperWinUI.exe') `
        -Description 'Published application executable'

    foreach ($templateName in $requiredTemplates) {
        $templatePath = Join-Path (Join-Path $Root '.example') $templateName
        Assert-NonEmptyFile -Path $templatePath -Description 'Published template file'
    }
}

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'build-release.ps1') -Tag $Tag -Runtime $Runtime -Configuration $Configuration
}

Assert-PublishPayload -Root $publishDir

if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

if (Test-Path $outputInstallerPath) {
    Remove-Item -Path $outputInstallerPath -Force
}

if (Test-Path $stagingDir) {
    Remove-Item -Path $stagingDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $stagingDir -Recurse -Force
Assert-PublishPayload -Root $stagingDir

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
    "/DSourceDir=$stagingDir" `
    "/DOutputDir=$releaseDir" `
    "/DRuntime=$Runtime" `
    $installerScriptPath

if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

Assert-NonEmptyFile -Path $outputInstallerPath -Description 'Installer executable'

if (Test-Path $stagingRoot) {
    Remove-Item -Path $stagingRoot -Recurse -Force
}

Write-Host "Installer created: $outputInstallerPath"
