param(
    [string]$GameInstallPath
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

if ([string]::IsNullOrWhiteSpace($GameInstallPath)) {
    $installCandidates = @(
        (Join-Path ${Env:ProgramFiles(x86)} "Steam\steamapps\common\Hex of Steel"),
        "D:\SteamLibrary\steamapps\common\Hex of Steel"
    )

    $GameInstallPath =
        $installCandidates |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

Assert-True (-not [string]::IsNullOrWhiteSpace($GameInstallPath)) "Could not locate Hex of Steel."
Assert-True (Test-Path -LiteralPath $GameInstallPath) "Game install path '$GameInstallPath' does not exist."

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$managedPath = Join-Path $GameInstallPath 'Hex of Steel_Data\Managed'
$librariesPath = Join-Path $repoRoot 'Libraries'
$modAssemblyPath = Join-Path $repoRoot 'output\net48\SquadOfSteel.dll'
$harnessPath = Join-Path $repoRoot 'Tests\CompatibilityHarness\bin\Release\net8.0\SquadOfSteel.CompatibilityHarness.dll'
$testOutputPath = Join-Path $repoRoot 'output\compatibility-test'

Assert-True (Test-Path -LiteralPath $managedPath) "Managed assembly directory was not found at '$managedPath'."

Write-Host "Building release solution..."
& dotnet build (Join-Path $repoRoot 'SquadOfSteelMod.sln') -c Release
Assert-True ($LASTEXITCODE -eq 0) "Release build failed."

Write-Host "Checking HoS reference assemblies..."
$referenceMap = [ordered]@{
    'Assembly-CSharp.dll' = 'Assembly-CSharp.dll'
    'Newtonsoft.Json.dll' = 'Newtonsoft.Json.dll'
    'PhotonUnityNetworking.dll' = 'PhotonUnityNetworking.dll'
    'TranslucentImage.dll' = 'LeTai.TranslucentImage.dll'
    'Unity.TextMeshPro.dll' = 'Unity.TextMeshPro.dll'
    'UnityEngine.AudioModule.dll' = 'UnityEngine.AudioModule.dll'
    'UnityEngine.CoreModule.dll' = 'UnityEngine.CoreModule.dll'
    'UnityEngine.ImageConversionModule.dll' = 'UnityEngine.ImageConversionModule.dll'
    'UnityEngine.TextRenderingModule.dll' = 'UnityEngine.TextRenderingModule.dll'
    'UnityEngine.UI.dll' = 'UnityEngine.UI.dll'
    'UnityEngine.UIModule.dll' = 'UnityEngine.UIModule.dll'
}

foreach ($entry in $referenceMap.GetEnumerator()) {
    $localPath = Join-Path $librariesPath $entry.Key
    $gamePath = Join-Path $managedPath $entry.Value
    Assert-True (Test-Path -LiteralPath $localPath) "Missing local reference '$($entry.Key)'."
    Assert-True (Test-Path -LiteralPath $gamePath) "Missing HoS reference '$($entry.Value)'."

    $localHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $localPath).Hash
    $gameHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $gamePath).Hash
    Assert-True ($localHash -eq $gameHash) "Reference '$($entry.Key)' does not match the installed HoS assembly."
}

$harmonyPath = Join-Path $librariesPath '0Harmony.dll'
$harmonyVersion = (Get-Item -LiteralPath $harmonyPath).VersionInfo.FileVersion
Assert-True ($harmonyVersion -eq '2.4.2.0') "Expected Harmony 2.4.2.0, found '$harmonyVersion'."

Write-Host "Resolving Harmony patch targets..."
& dotnet $harnessPath $modAssemblyPath $managedPath
Assert-True ($LASTEXITCODE -eq 0) "Harmony target compatibility test failed."

Write-Host "Refreshing and validating official-unit exports..."
$namesOutputPath = Join-Path $testOutputPath 'names'
$statsOutputPath = Join-Path $testOutputPath 'stats'
& (Join-Path $scriptRoot 'ExportOfficialUnitNames.ps1') `
    -GameInstallPath $GameInstallPath `
    -OutputDirectory $namesOutputPath `
    -GuidesDirectory ''
Assert-True ($LASTEXITCODE -eq 0) "Official-unit names export failed."

& (Join-Path $scriptRoot 'ExportOfficialUnitStats.ps1') `
    -GameInstallPath $GameInstallPath `
    -OutputDirectory $statsOutputPath
Assert-True ($LASTEXITCODE -eq 0) "Official-unit stats export failed."

$utf8 = New-Object System.Text.UTF8Encoding($false)
$namesJsonPath = Join-Path $namesOutputPath 'official-units-export.json'
$statsJsonPath = Join-Path $statsOutputPath 'official-units-stats.json'
$mappingPath = Join-Path $repoRoot 'Assets\transport-mappings.json'
$scaleProfilePath = Join-Path $repoRoot 'Assets\scale-profiles.json'
$builtScaleProfilePath = Join-Path $repoRoot 'output\net48\Assets\scale-profiles.json'
$guidesNamesPath = Join-Path $repoRoot 'guides\official-units-export.json'
$namesPayload = [IO.File]::ReadAllText($namesJsonPath, $utf8) | ConvertFrom-Json
$statsPayload = [IO.File]::ReadAllText($statsJsonPath, $utf8) | ConvertFrom-Json
$mappingPayload = [IO.File]::ReadAllText($mappingPath, $utf8) | ConvertFrom-Json
$scaleProfilePayload = [IO.File]::ReadAllText($scaleProfilePath, $utf8) | ConvertFrom-Json
$guidesPayload = [IO.File]::ReadAllText($guidesNamesPath, $utf8) | ConvertFrom-Json

Assert-True ($namesPayload.totalSerializedEntries -gt 0) "Official-unit names export was empty."
Assert-True ($namesPayload.totalSerializedEntries -eq $statsPayload.Count) "Names and stats exports contain different unit counts."
Assert-True ($namesPayload.totalSerializedEntries -eq $guidesPayload.totalSerializedEntries) "Committed official-unit snapshot is stale."

$officialNames = @($namesPayload.units | ForEach-Object { $_.name } | Sort-Object -Unique)
$guidesNames = @($guidesPayload.units | ForEach-Object { $_.name } | Sort-Object -Unique)
$snapshotDifferences = @(Compare-Object $officialNames $guidesNames)
Assert-True ($snapshotDifferences.Count -eq 0) "Committed official-unit name snapshot does not match the installed game."

$carrierNames =
    $mappingPayload.PSObject.Properties |
    Where-Object { $_.Name -notlike '_*' } |
    ForEach-Object {
        if ($_.Value -is [string]) {
            $_.Value
        }
        else {
            $_.Value.PSObject.Properties | ForEach-Object { $_.Value }
        }
    } |
    Sort-Object -Unique

$missingCarriers = @($carrierNames | Where-Object { $_ -notin $officialNames })
Assert-True ($missingCarriers.Count -eq 0) "Transport mappings reference missing carriers: $($missingCarriers -join ', ')"
Assert-True ($officialNames -contains 'Panzergrenadiers') "Expected HoS 8.4.11 unit 'Panzergrenadiers' was not found."
Assert-True ($null -ne $mappingPayload.Panzergrenadiers) "No transport mapping exists for HoS 8.4.11 'Panzergrenadiers'."

$scaleProfileIds = @($scaleProfilePayload.profiles | ForEach-Object { $_.id })
Assert-True (Test-Path -LiteralPath $builtScaleProfilePath) "Scale profile configuration was not copied to the build output."
Assert-True ($scaleProfileIds.Count -eq ($scaleProfileIds | Sort-Object -Unique).Count) "Scale profile ids must be unique."
Assert-True ($scaleProfileIds -contains 'default') "Scale profiles do not contain the Default interpretation."
Assert-True ($scaleProfileIds -contains 'operational-5km') "Scale profiles do not contain the Operational interpretation."
Assert-True ($scaleProfileIds -contains 'company-1km') "Scale profiles do not contain the Company interpretation."
Assert-True ($scaleProfileIds -contains 'platoon-250m') "Scale profiles do not contain the Platoon interpretation."
Assert-True ($scaleProfileIds -contains 'squad-50m') "Scale profiles do not contain the Squad interpretation."

$defaultScaleProfile = $scaleProfilePayload.profiles | Where-Object { $_.id -eq 'default' } | Select-Object -First 1
Assert-True ($defaultScaleProfile.distanceModel -eq 'perHex') "Default scale must retain per-hex distance falloff."
Assert-True ([double]$defaultScaleProfile.accuracyPenaltyPerHex -eq 0.10) "Default scale accuracy falloff changed."
Assert-True ([double]$defaultScaleProfile.damageLossPerHex -eq 0.08) "Default scale damage falloff changed."
Assert-True ([int]$defaultScaleProfile.passiveSuppressionRecovery -eq 15) "Default suppression recovery changed."

Write-Host "Checking release metadata..."
$manifest = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'ModPackage\Manifest.json') | ConvertFrom-Json
$infoLines = Get-Content -LiteralPath (Join-Path $repoRoot 'ModPackage\info.txt')
[xml]$project = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'SquadOfSteel.csproj')
$projectVersion = [string](
    $project.Project.PropertyGroup |
    Where-Object { $null -ne $_.Version } |
    Select-Object -ExpandProperty Version -First 1
)
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($modAssemblyPath).Version.ToString()

Assert-True ($manifest.modVersion -eq $infoLines[1]) "Manifest and info.txt mod versions differ."
Assert-True ($manifest.modVersion -eq $projectVersion) "Manifest and project versions differ."
Assert-True ($assemblyVersion -eq "$projectVersion.0") "Built assembly version '$assemblyVersion' does not match '$projectVersion.0'."
Assert-True ($manifest.supportedGameVersion -eq '8.4.11+') "Manifest is not aligned to HoS 8.4.11+."

Write-Host ""
Write-Host "Compatibility checks passed:"
Write-Host " - Mod version       : $($manifest.modVersion)"
Write-Host " - HoS support       : $($manifest.supportedGameVersion)"
Write-Host " - Harmony           : $harmonyVersion"
Write-Host " - Official units    : $($namesPayload.totalSerializedEntries)"
Write-Host " - Distinct names    : $($officialNames.Count)"
