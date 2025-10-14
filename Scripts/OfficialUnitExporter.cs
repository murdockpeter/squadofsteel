// =============================================================================
// Official unit catalog exporter.
// Emits a JSON snapshot of all units (with a foot-infantry subset) once on load.
// Toggle the ExportOfficialUnitsOnLoad flag to enable/disable.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace SquadOfSteelMod
{
    static class OfficialUnitExporter
    {
        public const bool ExportOfficialUnitsOnLoad = false; // Set true to export once on next load.

        const string ExportFileName = "official-units-export.json";

        static bool s_exportAttempted;

        public static void TryExport()
        {
            if (!ExportOfficialUnitsOnLoad || s_exportAttempted)
                return;

            s_exportAttempted = true;

            try
            {
                var official = OfficialUnits.GetInstance();
                var units = official?.Units;
                if (units == null || units.Count == 0)
                {
                    Debug.LogWarning("[SquadOfSteel] Official unit export skipped - catalog not populated.");
                    return;
                }

                var payload = BuildPayload(units);
                string assemblyDir = Path.GetDirectoryName(typeof(SquadMovementRuntime).Assembly.Location);
                if (string.IsNullOrEmpty(assemblyDir))
                {
                    assemblyDir = Application.persistentDataPath;
                }

                string baseDir = Directory.GetParent(assemblyDir)?.FullName ?? assemblyDir;
                string assetsDir = Path.Combine(baseDir, "Assets");
                assetsDir = Path.GetFullPath(assetsDir);
                Directory.CreateDirectory(assetsDir);

                string outputPath = Path.Combine(assetsDir, ExportFileName);
                string json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                File.WriteAllText(outputPath, json);

                Debug.Log($"[SquadOfSteel] Exported {payload.TotalUnits} official units ({payload.FootInfantryCount} infantry) to {outputPath}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SquadOfSteel] Failed to export official units: {ex.Message}");
            }
        }

        static ExportPayload BuildPayload(List<Unit> units)
        {
            var ordered = units.Where(u => u != null).OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();

            var infantry = ordered
                .Where(IsFootInfantry)
                .Select(u => new InfantryEntry
                {
                    Name = u.Name,
                    Country = u.Country,
                    SuggestedCarrier = "M3 Halftrack",
                    IsMotorized = u.IsMotorized || u.IsMechanized || u.IsHorsed
                })
                .ToList();

            return new ExportPayload
            {
                ExportedAtUtc = DateTime.UtcNow,
                TotalUnits = ordered.Count,
                FootInfantryCount = infantry.Count,
                FootInfantry = infantry,
                SuggestedTransportMappings = infantry.ToDictionary(
                    entry => entry.Name,
                    entry => entry.SuggestedCarrier,
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        static bool IsFootInfantry(Unit unit)
        {
            if (unit == null)
                return false;

            string filter = unit.FilterType ?? string.Empty;
            string type = unit.Type ?? string.Empty;

            bool matches = filter.IndexOf("Infantry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           type.IndexOf("Infantry", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!matches)
                return false;

            return !unit.IsRecon && !unit.IsMissile;
        }

        sealed class ExportPayload
        {
            public DateTime ExportedAtUtc { get; set; }
            public int TotalUnits { get; set; }
            public int FootInfantryCount { get; set; }
            public List<InfantryEntry> FootInfantry { get; set; }
            public Dictionary<string, string> SuggestedTransportMappings { get; set; }
        }

        sealed class InfantryEntry
        {
            public string Name { get; set; }
            public string Country { get; set; }
            public bool IsMotorized { get; set; }
            public string SuggestedCarrier { get; set; }
        }
    }

    [HarmonyPatch(typeof(OfficialUnits), nameof(OfficialUnits.Load))]
    static class SquadOfSteelOfficialUnitsLoadPatch
    {
        static void Postfix()
        {
            OfficialUnitExporter.TryExport();
        }
    }
}
