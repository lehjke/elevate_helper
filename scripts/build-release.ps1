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
$exampleDir = Join-Path $repoRoot '.example'
$publishDir = Join-Path $repoRoot "artifacts/publish/$Runtime"
$releaseDir = Join-Path $repoRoot 'artifacts/release'
$zipPath = Join-Path $releaseDir "ElevateHelper-$Runtime-$Tag.zip"

$platform = switch ($Runtime) {
    'win-x64' { 'x64' }
    'win-x86' { 'x86' }
    'win-arm64' { 'ARM64' }
    default { throw "Unsupported runtime: $Runtime" }
}

foreach ($path in @($publishDir, $releaseDir)) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

if (Test-Path $publishDir) {
    Get-ChildItem -Path $publishDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

if (-not (Test-Path $exampleDir)) {
    throw "Required .example template folder was not found: $exampleDir"
}

$requiredTemplates = @(
    'Office.elvx',
    'Residential.elvx',
    'Hotel.elvx',
    'Office.xlsx',
    'Residential.xlsx',
    'Hotel.xlsx'
)

foreach ($templateName in $requiredTemplates) {
    $templatePath = Join-Path $exampleDir $templateName
    if (-not (Test-Path $templatePath)) {
        throw "Required template file was not found: $templatePath"
    }
}

dotnet restore $projectPath

dotnet publish $projectPath `
    --configuration $Configuration `
    -p:Platform=$platform `
    -p:RuntimeIdentifier=$Runtime `
    -p:WindowsPackageType=None `
    -p:SelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    --output $publishDir

foreach ($fileName in @('README.md', 'LICENSE')) {
    $sourcePath = Join-Path $repoRoot $fileName
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination (Join-Path $publishDir $fileName) -Force
    }
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

Write-Host "Release archive created: $zipPath"
