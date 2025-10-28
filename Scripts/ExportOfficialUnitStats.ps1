param(
    [string]$GameInstallPath = "D:\SteamLibrary\steamapps\common\Hex of Steel",
    [string]$OutputDirectory = $(Join-Path (Split-Path -Parent $PSCommandPath) "..\output"),
    [double]$TileLengthKm = 1.0
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $GameInstallPath)) {
    throw "Game install path '$GameInstallPath' does not exist. Pass -GameInstallPath to point at your Hex of Steel install."
}

$officialUnitsPath = Join-Path $GameInstallPath "Hex of Steel_Data\StreamingAssets\Official units.txt"
if (-not (Test-Path $officialUnitsPath)) {
    throw "Could not locate 'Official units.txt' at '$officialUnitsPath'. Pass -GameInstallPath to point at your Hex of Steel install."
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$assemblyPath = Join-Path (Split-Path -Parent $PSCommandPath) "..\Libraries\Assembly-CSharp.dll"

if (-not (Test-Path $assemblyPath)) {
    throw "Could not locate Assembly-CSharp.dll at '$assemblyPath'."
}

$unitAssembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
$unitAssemblyName = $unitAssembly.FullName

$typeDefinition = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

[Serializable]
public class UnitsCatalogSurrogate : ISerializable
{
    public List<object> Units { get; private set; }

    public UnitsCatalogSurrogate()
    {
        Units = new List<object>();
    }

    protected UnitsCatalogSurrogate(SerializationInfo info, StreamingContext context)
    {
        Units = new List<object>();
        foreach (SerializationEntry entry in info)
        {
            if (entry.Value == null)
                continue;

            if (string.Equals(entry.Name, "<Units>k__BackingField", StringComparison.OrdinalIgnoreCase))
            {
                var enumerable = entry.Value as IEnumerable;
                if (enumerable != null)
                {
                    foreach (var item in enumerable)
                    {
                        Units.Add(item);
                    }
                }
            }
        }
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        throw new NotImplementedException();
    }
}

public sealed class UnitsCatalogBinder : SerializationBinder
{
    public override Type BindToType(string assemblyName, string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeof(object);

        if (typeName.Equals("OfficialUnits") || typeName.EndsWith(".OfficialUnits"))
            return typeof(UnitsCatalogSurrogate);

        if (typeName.Equals("FacingRotations") || typeName.EndsWith(".FacingRotations"))
            return Type.GetType("FacingRotations, __ASSEMBLY__");

        if (typeName.Equals("AllowedRotations") || typeName.EndsWith(".AllowedRotations"))
            return Type.GetType("AllowedRotations, __ASSEMBLY__");

        if (typeName.Equals("AsleepStates") || typeName.EndsWith(".AsleepStates"))
            return Type.GetType("AsleepStates, __ASSEMBLY__");

        if (typeName.Equals("Unit") || typeName.EndsWith(".Unit"))
            return Type.GetType("Unit, __ASSEMBLY__");

        if (typeName.Equals("Unit[]") || typeName.EndsWith(".Unit[]"))
            return Type.GetType("Unit[], __ASSEMBLY__");

        if (typeName.IndexOf("System.Collections.Generic.List`1[[Unit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Type unitType = Type.GetType("Unit, __ASSEMBLY__");
            return typeof(List<>).MakeGenericType(unitType);
        }

        // Resolve enums and other helper types from the original assembly when possible.
        Type resolved = Type.GetType(string.Format("{{0}}, {{1}}", typeName, assemblyName));
        if (resolved != null)
            return resolved;

        return typeof(object);
    }
}
'@

$typeDefinition = $typeDefinition.Replace("__ASSEMBLY__", $unitAssemblyName)

Add-Type -TypeDefinition $typeDefinition -ErrorAction Stop | Out-Null

$fileStream = [System.IO.File]::OpenRead($officialUnitsPath)
try {
    $formatter = New-Object System.Runtime.Serialization.Formatters.Binary.BinaryFormatter
    $formatter.Binder = New-Object UnitsCatalogBinder
    $data = [UnitsCatalogSurrogate]$formatter.Deserialize($fileStream)
} finally {
    $fileStream.Dispose()
}

if ($null -eq $data -or $null -eq $data.Units -or $data.Units.Count -eq 0) {
    throw "Deserialization produced no units. Ensure the game files haven't changed format."
}

$unitType = $unitAssembly.GetType("Unit")
if ($null -eq $unitType) {
    throw "Failed to locate the Unit type after loading Assembly-CSharp."
}

$properties =
    $unitType.GetProperties() |
    Where-Object {
        $propertyType = $_.PropertyType

        if (-not $_.CanRead) {
            return $false
        }

        if ($propertyType.IsArray) {
            return $false
        }

        if ($propertyType.IsGenericType -and $propertyType.GetGenericTypeDefinition() -eq [Nullable`1]) {
            $underlying = [Nullable]::GetUnderlyingType($propertyType)
            return $underlying.IsPrimitive -or
                   $underlying.IsEnum -or
                   $underlying -eq [string] -or
                   $underlying.FullName -in 'System.Decimal','System.Double','System.Single','System.DateTime'
        }

        return $propertyType.IsPrimitive -or
               $propertyType.IsEnum -or
               $propertyType -eq [string] -or
               $propertyType.FullName -in 'System.Decimal','System.Double','System.Single','System.DateTime'
    }

function Get-PropertyValue {
    param(
        [object]$Instance,
        [Reflection.PropertyInfo]$Property
    )

    $raw = $Property.GetValue($Instance, $null)
    if ($null -eq $raw) {
        return $null
    }

    $propertyType = $Property.PropertyType

    if ($propertyType.IsGenericType -and $propertyType.GetGenericTypeDefinition() -eq [Nullable`1]) {
        $underlying = [Nullable]::GetUnderlyingType($propertyType)
        if ($underlying.IsEnum) {
            return $raw.ToString()
        }

        if ($underlying.FullName -eq 'System.Single' -or $underlying.FullName -eq 'System.Double' -or $underlying.FullName -eq 'System.Decimal') {
            return [double]$raw
        }

        return [Convert]::ChangeType($raw, $underlying)
    }

    if ($propertyType.IsEnum) {
        return $raw.ToString()
    }

    if ($propertyType.FullName -eq 'System.Single' -or $propertyType.FullName -eq 'System.Double' -or $propertyType.FullName -eq 'System.Decimal') {
        return [double]$raw
    }

    return $raw
}

function Get-UnitProperty {
    param(
        [object]$Unit,
        [string]$PropertyName
    )

    $property = $unitType.GetProperty($PropertyName)
    if ($null -eq $property) {
        return $null
    }

    return Get-PropertyValue -Instance $Unit -Property $property
}

$tileKm = [double]$TileLengthKm

$units =
    $data.Units |
    Where-Object { $_ -ne $null } |
    Sort-Object { (Get-UnitProperty -Unit $_ -PropertyName "Name") }

$mobilityRecords = @()
foreach ($unit in $units) {
    $vision = [int](Get-UnitProperty -Unit $unit -PropertyName "Visibility")
    $range = [int](Get-UnitProperty -Unit $unit -PropertyName "Range")
    $mobility = [int](Get-UnitProperty -Unit $unit -PropertyName "MaxMP")
    $autonomy = [int](Get-UnitProperty -Unit $unit -PropertyName "MaxAutonomy")

    $mobilityRecords += [pscustomobject]@{
        Name = Get-UnitProperty -Unit $unit -PropertyName "Name"
        Country = Get-UnitProperty -Unit $unit -PropertyName "Country"
        Type = Get-UnitProperty -Unit $unit -PropertyName "Type"
        FilterType = Get-UnitProperty -Unit $unit -PropertyName "FilterType"
        VisionTiles = $vision
        VisionKm = [double]($vision * $tileKm)
        AttackRangeTiles = $range
        AttackRangeKm = [double]($range * $tileKm)
        MobilityTilesPerTurn = $mobility
        MobilityKmPerTurn = [double]($mobility * $tileKm)
        MaxAutonomyTiles = $autonomy
        MaxAutonomyKm = [double]($autonomy * $tileKm)
    }
}

$mobilityCsvPath = Join-Path $OutputDirectory "units.csv"
$mobilityRecords | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $mobilityCsvPath

$propertyNames = $properties | Select-Object -ExpandProperty Name

$fullRecords = @()
foreach ($unit in $units) {
    $row = [ordered]@{}
    foreach ($property in $properties) {
        $value = Get-PropertyValue -Instance $unit -Property $property
        $row[$property.Name] = $value
    }

    $row["VisionKm"] = [double](([int](Get-UnitProperty -Unit $unit -PropertyName "Visibility")) * $tileKm)
    $row["AttackRangeKm"] = [double](([int](Get-UnitProperty -Unit $unit -PropertyName "Range")) * $tileKm)
    $row["MobilityKmPerTurn"] = [double](([int](Get-UnitProperty -Unit $unit -PropertyName "MaxMP")) * $tileKm)
    $row["MaxAutonomyKm"] = [double](([int](Get-UnitProperty -Unit $unit -PropertyName "MaxAutonomy")) * $tileKm)

    $fullRecords += [pscustomobject]$row
}

$fullCsvPath = Join-Path $OutputDirectory "official-units-stats.csv"
$fullJsonPath = Join-Path $OutputDirectory "official-units-stats.json"

$fullRecords | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $fullCsvPath
$fullRecords | ConvertTo-Json -Depth 6 | Set-Content -Path $fullJsonPath -Encoding UTF8

Write-Host "Export complete:"
Write-Host " - Total units        : $($units.Count)"
Write-Host " - Tile length (km)   : $tileKm"
Write-Host " - Vision/range file  : $mobilityCsvPath"
Write-Host " - Full CSV           : $fullCsvPath"
Write-Host " - Full JSON          : $fullJsonPath"
