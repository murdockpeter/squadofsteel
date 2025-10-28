param(
    [string]$ModLibraryPath = $(Join-Path (Join-Path $Env:LOCALAPPDATA '..\LocalLow') 'War Frogs Studio\Hex of Steel\MODS\Squad Of Steel Beta 1.0\Libraries')
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        return (Convert-Path -Path $Path)
    }
    catch {
        return [System.IO.Path]::GetFullPath($Path)
    }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-FullPath (Join-Path $scriptRoot '..')
$outputRoot = Join-Path $repoRoot 'output\net48'
$dllSource = Join-Path $outputRoot 'SquadOfSteel.dll'
$assetDirectory = Join-Path $outputRoot 'Assets'
$mappingSourcePattern = 'transport-mappings*.json'
$manifestSource = Join-Path $repoRoot 'ModPackage\Manifest.json'
$infoSource = Join-Path $repoRoot 'ModPackage\info.txt'
$iconSource = Join-Path $repoRoot 'Libraries\SmallImage.png'

if (-not (Test-Path $dllSource)) {
    throw "Could not find compiled DLL at '$dllSource'. Build the project first."
}

if (-not (Test-Path $assetDirectory)) {
    throw "Could not find transport asset directory at '$assetDirectory'. Ensure the build output is up to date."
}

$mappingSources = Get-ChildItem -Path $assetDirectory -Filter $mappingSourcePattern -File -ErrorAction Stop
if (-not $mappingSources) {
    throw "No transport mapping files matching '$mappingSourcePattern' were found under '$assetDirectory'."
}

$resolvedLibraryPath = Resolve-FullPath $ModLibraryPath
$modRootPath = Resolve-FullPath (Join-Path $resolvedLibraryPath '..')

if (-not (Test-Path $modRootPath)) {
    New-Item -ItemType Directory -Path $modRootPath -Force | Out-Null
}

if (-not (Test-Path $resolvedLibraryPath)) {
    New-Item -ItemType Directory -Path $resolvedLibraryPath -Force | Out-Null
}

$dllDestination = Join-Path $resolvedLibraryPath 'SquadOfSteel.dll'
$manifestDestination = Join-Path $modRootPath 'Manifest.json'
$infoDestination = Join-Path $modRootPath 'info.txt'
$iconDestination = Join-Path $modRootPath 'SmallImage.png'

$deployments = New-Object System.Collections.Generic.List[string]

Copy-Item -Path $dllSource -Destination $dllDestination -Force
$deployments.Add($dllDestination)

foreach ($mappingSource in $mappingSources) {
    $destination = Join-Path $resolvedLibraryPath $mappingSource.Name
    Copy-Item -Path $mappingSource.FullName -Destination $destination -Force
    $deployments.Add($destination)
}

if (Test-Path $manifestSource) {
    Copy-Item -Path $manifestSource -Destination $manifestDestination -Force
    $deployments.Add($manifestDestination)
}

if (Test-Path $infoSource) {
    Copy-Item -Path $infoSource -Destination $infoDestination -Force
    $deployments.Add($infoDestination)
}

if (Test-Path $iconSource) {
    Copy-Item -Path $iconSource -Destination $iconDestination -Force
    $deployments.Add($iconDestination)
}

$dependencySources = @(
    @{ Source = Join-Path $repoRoot 'Libraries\0Harmony.dll'; Destination = Join-Path $resolvedLibraryPath '0Harmony.dll' },
    @{ Source = Join-Path $repoRoot 'Libraries\Newtonsoft.Json.dll'; Destination = Join-Path $resolvedLibraryPath 'Newtonsoft.Json.dll' }
)

foreach ($dependency in $dependencySources) {
    $source = $dependency.Source
    $destination = $dependency.Destination

    if (-not (Test-Path $source)) {
        continue
    }

    Copy-Item -Path $source -Destination $destination -Force
    $deployments.Add($destination)
}

Write-Host "Deployed:"
foreach ($path in $deployments) {
    Write-Host " - $path"
}
