param(
    [string]$GameInstallPath = "D:\SteamLibrary\steamapps\common\Hex of Steel",
    [string]$OutputDirectory = $(Join-Path (Split-Path -Parent $PSCommandPath) "..\output"),
    [string]$GuidesDirectory = $(Join-Path (Split-Path -Parent $PSCommandPath) "..\guides")
)

$ErrorActionPreference = 'Stop'

$officialUnitsPath = Join-Path $GameInstallPath "Hex of Steel_Data\StreamingAssets\Official units.txt"
if (-not (Test-Path $officialUnitsPath)) {
    throw "Could not locate 'Official units.txt' at '$officialUnitsPath'. Pass -GameInstallPath to point at your Hex of Steel install."
}

foreach ($directory in @($OutputDirectory, $GuidesDirectory)) {
    if ([string]::IsNullOrWhiteSpace($directory)) {
        continue
    }

    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

$jsonOutputPath = Join-Path $OutputDirectory "official-units-export.json"
$csvOutputPath = Join-Path $OutputDirectory "official-units-export.csv"
$guidesJsonPath = if (-not [string]::IsNullOrWhiteSpace($GuidesDirectory)) { Join-Path $GuidesDirectory "official-units-export.json" } else { $null }
$guidesCsvPath = if (-not [string]::IsNullOrWhiteSpace($GuidesDirectory)) { Join-Path $GuidesDirectory "official-units-export.csv" } else { $null }

$typeDefinition = @"
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

[Serializable]
public class LegacyOfficialUnitsSurrogate : ISerializable
{
    public List<LegacyUnitSurrogate> Units { get; private set; }

    public LegacyOfficialUnitsSurrogate()
    {
        Units = new List<LegacyUnitSurrogate>();
    }

    protected LegacyOfficialUnitsSurrogate(SerializationInfo info, StreamingContext context)
    {
        Units = new List<LegacyUnitSurrogate>();
        foreach (SerializationEntry entry in info)
        {
            var list = entry.Value as List<LegacyUnitSurrogate>;
            if (list != null)
            {
                Units = list;
            }

            var array = entry.Value as LegacyUnitSurrogate[];
            if (array != null)
            {
                Units = new List<LegacyUnitSurrogate>(array);
            }
        }
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        throw new NotImplementedException();
    }
}

[Serializable]
public class LegacyUnitSurrogate : ISerializable
{
    public string Name { get; set; }
    public string Country { get; set; }
    public string Type { get; set; }
    public string FilterType { get; set; }
    public bool IsMotorized { get; set; }
    public bool IsMechanized { get; set; }
    public bool IsHorsed { get; set; }
    public bool IsCarrier { get; set; }
    public bool IsRecon { get; set; }
    public bool IsParatrooper { get; set; }
    public bool IsEngineers { get; set; }
    public bool IsCavalry { get; set; }
    public bool IsOnBoat { get; set; }
    public bool IsDropable { get; set; }
    public bool IsTrain { get; set; }
    public bool IsSubmarine { get; set; }
    public bool IsSupplyShip { get; set; }
    public bool IsConvoy { get; set; }
    public bool CanCarryRockets { get; set; }
    public bool CanCarryBombs { get; set; }
    public bool CanCarryTorpedo { get; set; }

    public LegacyUnitSurrogate() { }

    protected LegacyUnitSurrogate(SerializationInfo info, StreamingContext context)
    {
        foreach (SerializationEntry entry in info)
        {
            string name = entry.Name;
            object value = entry.Value;

            if (value == null)
                continue;

            switch (name)
            {
                case "Name":
                case "<Name>k__BackingField":
                    Name = value as string;
                    break;
                case "Country":
                case "<Country>k__BackingField":
                    Country = value as string;
                    break;
                case "Type":
                case "<Type>k__BackingField":
                    Type = value as string;
                    break;
                case "FilterType":
                case "<FilterType>k__BackingField":
                    FilterType = value as string;
                    break;
                case "<IsMotorized>k__BackingField":
                    if (value is bool) IsMotorized = (bool)value;
                    break;
                case "<IsMechanized>k__BackingField":
                    if (value is bool) IsMechanized = (bool)value;
                    break;
                case "<IsHorsed>k__BackingField":
                    if (value is bool) IsHorsed = (bool)value;
                    break;
                case "<IsCarrier>k__BackingField":
                    if (value is bool) IsCarrier = (bool)value;
                    break;
                case "<IsRecon>k__BackingField":
                    if (value is bool) IsRecon = (bool)value;
                    break;
                case "<IsParatrooper>k__BackingField":
                    if (value is bool) IsParatrooper = (bool)value;
                    break;
                case "<IsEngineers>k__BackingField":
                    if (value is bool) IsEngineers = (bool)value;
                    break;
                case "<IsCavalry>k__BackingField":
                    if (value is bool) IsCavalry = (bool)value;
                    break;
                case "<IsOnBoat>k__BackingField":
                    if (value is bool) IsOnBoat = (bool)value;
                    break;
                case "<IsDropable>k__BackingField":
                    if (value is bool) IsDropable = (bool)value;
                    break;
                case "<IsTrain>k__BackingField":
                    if (value is bool) IsTrain = (bool)value;
                    break;
                case "<IsSubmarine>k__BackingField":
                    if (value is bool) IsSubmarine = (bool)value;
                    break;
                case "<IsSupplyShip>k__BackingField":
                    if (value is bool) IsSupplyShip = (bool)value;
                    break;
                case "<IsConvoy>k__BackingField":
                    if (value is bool) IsConvoy = (bool)value;
                    break;
                case "<CanCarryRockets>k__BackingField":
                    if (value is bool) CanCarryRockets = (bool)value;
                    break;
                case "<CanCarryBombs>k__BackingField":
                    if (value is bool) CanCarryBombs = (bool)value;
                    break;
                case "<CanCarryTorpedo>k__BackingField":
                    if (value is bool) CanCarryTorpedo = (bool)value;
                    break;
            }
        }
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        throw new NotImplementedException();
    }
}

public sealed class LegacyBinder : SerializationBinder
{
    public override Type BindToType(string assemblyName, string typeName)
    {
        if (!string.IsNullOrEmpty(assemblyName) &&
            assemblyName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
        {
            if (typeName == "OfficialUnits" || typeName.EndsWith(".OfficialUnits", StringComparison.OrdinalIgnoreCase))
                return typeof(LegacyOfficialUnitsSurrogate);
            if (typeName == "Unit" || typeName.EndsWith(".Unit", StringComparison.OrdinalIgnoreCase))
                return typeof(LegacyUnitSurrogate);
            if (typeName == "Unit[]" || typeName.EndsWith(".Unit[]", StringComparison.OrdinalIgnoreCase))
                return typeof(LegacyUnitSurrogate[]);
            return typeof(object);
        }

        if (!string.IsNullOrEmpty(typeName) &&
            typeName.StartsWith("System.Collections.Generic.List`1[[Unit", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(List<LegacyUnitSurrogate>);
        }

        if (!string.IsNullOrEmpty(typeName) &&
            (typeName.StartsWith("Unit[]", StringComparison.OrdinalIgnoreCase) || typeName.Contains(".Unit[]")))
        {
            return typeof(LegacyUnitSurrogate[]);
        }

        return typeof(object);
    }
}
"@

Add-Type -TypeDefinition $typeDefinition -ErrorAction Stop | Out-Null

$fileStream = [System.IO.File]::OpenRead($officialUnitsPath)
try {
    $formatter = New-Object System.Runtime.Serialization.Formatters.Binary.BinaryFormatter
    $formatter.Binder = New-Object LegacyBinder
    $data = [LegacyOfficialUnitsSurrogate]$formatter.Deserialize($fileStream)
} finally {
    $fileStream.Dispose()
}

if ($data -eq $null -or $data.Units -eq $null -or $data.Units.Count -eq 0) {
    Write-Host "Deserialization diagnostics:"
    if ($data -ne $null) {
        Write-Host (" - Data type: {0}" -f $data.GetType().FullName)
        if ($data.Units -ne $null) {
            Write-Host (" - Units property type: {0}" -f $data.Units.GetType().FullName)
        } else {
            Write-Host " - Units property type: null"
        }
    } else {
        Write-Host " - Data type: null"
    }
    throw "Deserialization produced no units. Ensure the game files haven't changed format."
}

$records =
    $data.Units |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } |
    Sort-Object Name |
    ForEach-Object {
        $unit = $_

        $isInfantry =
            ([string]::IsNullOrWhiteSpace($unit.Type) -eq $false -and $unit.Type.IndexOf("infantry", [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            ([string]::IsNullOrWhiteSpace($unit.FilterType) -eq $false -and $unit.FilterType.IndexOf("infantry", [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            $unit.IsEngineers -or
            $unit.IsCavalry -or
            $unit.IsParatrooper

        $isFootInfantry =
            $isInfantry -and
            -not ($unit.IsMotorized -or $unit.IsMechanized -or $unit.IsHorsed -or $unit.IsCarrier -or $unit.IsOnBoat -or $unit.IsDropable -or $unit.IsTrain -or $unit.IsSubmarine -or $unit.IsSupplyShip -or $unit.IsConvoy)

        $isTransportCandidate =
            $unit.IsCarrier -or
            $unit.IsMotorized -or
            $unit.IsMechanized -or
            $unit.IsHorsed -or
            $unit.IsOnBoat -or
            $unit.IsDropable -or
            $unit.IsSupplyShip -or
            $unit.IsConvoy

        [pscustomobject]@{
            name = $unit.Name
            country = $unit.Country
            type = $unit.Type
            filterType = $unit.FilterType
            isMotorized = $unit.IsMotorized
            isMechanized = $unit.IsMechanized
            isHorsed = $unit.IsHorsed
            isCarrier = $unit.IsCarrier
            isRecon = $unit.IsRecon
            isParatrooper = $unit.IsParatrooper
            isEngineers = $unit.IsEngineers
            isCavalry = $unit.IsCavalry
            isOnBoat = $unit.IsOnBoat
            isDropable = $unit.IsDropable
            isTrain = $unit.IsTrain
            isSubmarine = $unit.IsSubmarine
            isSupplyShip = $unit.IsSupplyShip
            isConvoy = $unit.IsConvoy
            canCarryRockets = $unit.CanCarryRockets
            canCarryBombs = $unit.CanCarryBombs
            canCarryTorpedo = $unit.CanCarryTorpedo
            isInfantry = $isInfantry
            isFootInfantry = $isFootInfantry
            isTransportCandidate = $isTransportCandidate
        }
    }

$payload = [pscustomobject]@{
    exportedAtUtc = [DateTime]::UtcNow.ToString("o")
    source = $officialUnitsPath
    totalSerializedEntries = $records.Count
    distinctNameCount = $records.Count
    units = $records
}

$payload | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonOutputPath -Encoding UTF8
$records | ConvertTo-Csv -NoTypeInformation | Set-Content -Path $csvOutputPath -Encoding UTF8

if ($guidesJsonPath) {
    $payload | ConvertTo-Json -Depth 6 | Set-Content -Path $guidesJsonPath -Encoding UTF8
}

if ($guidesCsvPath) {
    $records | ConvertTo-Csv -NoTypeInformation | Set-Content -Path $guidesCsvPath -Encoding UTF8
}

$sample = $records | Select-Object -First 5

Write-Host "Export complete:"
Write-Host " - Total entries : $($records.Count)"
Write-Host " - JSON output   : $jsonOutputPath"
Write-Host " - CSV output    : $csvOutputPath"
if ($guidesJsonPath) {
    Write-Host " - Guides JSON   : $guidesJsonPath"
    Write-Host " - Guides CSV    : $guidesCsvPath"
}

if ($sample.Count -gt 0) {
    $sampleNames = $sample | ForEach-Object { $_.name }
    Write-Host " - Sample names  : $($sampleNames -join ', ')"
}

