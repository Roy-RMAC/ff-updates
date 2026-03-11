// Exporting/Actions/DxfPostProcessorInternal.cs
// Built-in DXF post-processor using IxMilia.Dxf — replaces the external AutoCAD-based EXE.
// Performs two operations on Inventor flat-pattern DXF exports:
//   1. Trim bend lines: replace long bend-down lines with short etch segments at each end
//   2. Normalize layers: map Inventor layer names to CUT / ETCH, delete everything else

using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;

namespace RMAC_Efficiency_Addin.Exporting.Actions
{
    internal sealed class DxfPostProcessorInternal
    {
        // ---- Bend-line trimming parameters (match legacy VB.NET post-processor) ----
        private const double TrimLength = 75;   // minimum original length to split into two segments
        private const double EtchLength = 25;   // length of each etch segment
        private const double Inset = 3;          // inset from each end before etch starts
        private const double Epsilon = 0.000001; // degenerate line threshold

        // ---- Layer mapping ----
        // Inventor flat-pattern DXF layers (both custom DataIO names and Inventor defaults)

        private static readonly HashSet<string> CutLayers = new(StringComparer.OrdinalIgnoreCase)
        {
            "IV_OUTER",              // addin DataIO export
            "IV_INNER",              // addin DataIO export
            "IV_OUTER_PROFILE",      // Inventor default
            "IV_INTERIOR_PROFILE",   // Inventor default
            "IV_INTERIOR_PROFILES",  // Inventor default (plural variant)
        };

        private static readonly HashSet<string> EtchLayers = new(StringComparer.OrdinalIgnoreCase)
        {
            "IV_BEND_DOWN",      // addin DataIO export / Inventor default
            "IV_BEND_UP",        // addin DataIO export
            "IV_BEND",           // Inventor default (generic)
            "IV_MARK_SURFACE",   // Inventor default (surface marks)
        };

        /// <summary>
        /// Processes a batch of DXF files in-place: trims bend lines, normalizes layers,
        /// sanitizes entity properties, and saves as R12 for maximum CNC compatibility.
        /// Returns the list of unhandled layer names encountered (for warning/logging).
        /// </summary>
        public bool ProcessFiles(
            IReadOnlyList<string> dxfPaths,
            out List<string> processedFiles,
            out List<string> errors,
            out HashSet<string> unhandledLayers)
        {
            processedFiles = new List<string>();
            errors = new List<string>();
            unhandledLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in dxfPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!File.Exists(path))
                {
                    errors.Add($"DXF file not found: {path}");
                    continue;
                }

                try
                {
                    ProcessSingleFile(path, unhandledLayers);
                    processedFiles.Add(path);
                }
                catch (Exception ex)
                {
                    errors.Add($"DXF post-process failed [{Path.GetFileName(path)}]: {ex.Message}");
                    AddinSettings.Log($"DXF post-process error: {path} — {ex}");
                }
            }

            return errors.Count == 0;
        }

        // ---------------------------------------------------------------
        // Core processing
        // ---------------------------------------------------------------

        // Temporary diagnostic log — always writes regardless of Debug flag
        private static readonly string _ppLogPath = IOPath.Combine(IOPath.GetTempPath(), "RMAC_DxfPostProcess.log");
        private static void ppLog(string msg)
        {
            try { IOFile.AppendAllText(_ppLogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); } catch { }
        }

        private static void ProcessSingleFile(string dxfPath, HashSet<string> unhandledLayers)
        {
            DxfFile dxf;

            using (var fs = new FileStream(dxfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                dxf = DxfFile.Load(fs);
            }

            ppLog($"--- {IOPath.GetFileName(dxfPath)} ---");
            ppLog($"  Input version: {dxf.Header.Version}");
            ppLog($"  Layers defined: {string.Join(", ", dxf.Layers.Select(l => l.Name))}");

            // Log entity breakdown by layer
            var layerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in dxf.Entities)
            {
                var ln = e.Layer ?? "(null)";
                layerCounts.TryGetValue(ln, out var c);
                layerCounts[ln] = c + 1;
            }
            foreach (var kv in layerCounts.OrderBy(x => x.Key))
                ppLog($"  Entities on [{kv.Key}]: {kv.Value} ({string.Join(", ", dxf.Entities.Where(e => string.Equals(e.Layer, kv.Key, StringComparison.OrdinalIgnoreCase)).Select(e => e.EntityTypeString).Distinct())})");

            TrimBendLines(dxf);
            NormalizeLayers(dxf, unhandledLayers);
            SanitizeEntityProperties(dxf);

            ppLog($"  After processing — entities: {dxf.Entities.Count}, layers: {string.Join(", ", dxf.Layers.Select(l => l.Name))}");
            ppLog($"  Output version: R12");

            dxf.Header.Version = DxfAcadVersion.R12;

            using (var fs = new FileStream(dxfPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                dxf.Save(fs);
            }

            // IxMilia.Dxf writes some entity group codes that confuse AutoCAD
            // (e.g. empty group 430, group 284/440 with default values).
            // Strip them from the raw text as a final cleanup pass.
            StripProblematicGroupCodes(dxfPath);
        }

        // ---------------------------------------------------------------
        // 1. Trim bend lines
        // ---------------------------------------------------------------

        /// <summary>
        /// Finds lines on bend-down layers and replaces them:
        ///   - Long lines (≥ TrimLength): two short etch segments at each end
        ///   - Short lines: single segment inset from each end
        ///   - Degenerate lines: removed entirely
        /// </summary>
        private static void TrimBendLines(DxfFile dxf)
        {
            // Collect bend-down lines first — don't modify while enumerating
            var bendLines = new List<DxfLine>();
            foreach (var entity in dxf.Entities)
            {
                if (entity is DxfLine line &&
                    !string.IsNullOrEmpty(line.Layer) &&
                    line.Layer.IndexOf("IV_BEND_DOWN", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bendLines.Add(line);
                }
            }

            foreach (var line in bendLines)
            {
                var p1 = line.P1;
                var p2 = line.P2;

                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double dz = p2.Z - p1.Z;
                double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                string layer = line.Layer;

                // Remove the original line
                dxf.Entities.Remove(line);

                if (length <= Epsilon)
                    continue; // degenerate — just delete

                // Unit direction vector
                double ux = dx / length;
                double uy = dy / length;
                double uz = dz / length;

                if (length >= TrimLength)
                {
                    // Two short segments at each end
                    // Segment 1: from (p1 + inset) to (p1 + inset + etchLength)
                    var seg1Start = new DxfPoint(
                        Round(p1.X + Inset * ux),
                        Round(p1.Y + Inset * uy),
                        Round(p1.Z + Inset * uz));
                    var seg1End = new DxfPoint(
                        Round(p1.X + (Inset + EtchLength) * ux),
                        Round(p1.Y + (Inset + EtchLength) * uy),
                        Round(p1.Z + (Inset + EtchLength) * uz));

                    // Segment 2: from (p2 - inset - etchLength) to (p2 - inset)
                    var seg2Start = new DxfPoint(
                        Round(p2.X - (Inset + EtchLength) * ux),
                        Round(p2.Y - (Inset + EtchLength) * uy),
                        Round(p2.Z - (Inset + EtchLength) * uz));
                    var seg2End = new DxfPoint(
                        Round(p2.X - Inset * ux),
                        Round(p2.Y - Inset * uy),
                        Round(p2.Z - Inset * uz));

                    var newLine1 = new DxfLine(seg1Start, seg1End) { Layer = layer };
                    var newLine2 = new DxfLine(seg2Start, seg2End) { Layer = layer };

                    dxf.Entities.Add(newLine1);
                    dxf.Entities.Add(newLine2);
                }
                else if (length > 2 * Inset)
                {
                    // Short line: single segment inset from each end
                    var start = new DxfPoint(
                        Round(p1.X + Inset * ux),
                        Round(p1.Y + Inset * uy),
                        Round(p1.Z + Inset * uz));
                    var end = new DxfPoint(
                        Round(p2.X - Inset * ux),
                        Round(p2.Y - Inset * uy),
                        Round(p2.Z - Inset * uz));

                    var newLine = new DxfLine(start, end) { Layer = layer };
                    dxf.Entities.Add(newLine);
                }
                else
                {
                    // Too short to inset — keep the original line as-is on the same layer
                    dxf.Entities.Add(new DxfLine(p1, p2) { Layer = layer });
                }
            }
        }

        // ---------------------------------------------------------------
        // 2. Normalize layers
        // ---------------------------------------------------------------

        /// <summary>
        /// Remaps all entities to CUT or ETCH layers.
        /// Entities on unknown layers are deleted (matching AutoCAD purge behaviour).
        /// Then removes unused Inventor layer definitions.
        /// </summary>
        private static void NormalizeLayers(DxfFile dxf, HashSet<string> unhandledLayers)
        {
            // Ensure CUT and ETCH layers exist
            if (!dxf.Layers.Any(l => string.Equals(l.Name, "CUT", StringComparison.OrdinalIgnoreCase)))
                dxf.Layers.Add(new DxfLayer("CUT", DxfColor.FromIndex(1))); // red

            if (!dxf.Layers.Any(l => string.Equals(l.Name, "ETCH", StringComparison.OrdinalIgnoreCase)))
                dxf.Layers.Add(new DxfLayer("ETCH", DxfColor.FromIndex(2))); // yellow

            // Remap entity layers — collect entities to remove
            var entitiesToRemove = new List<DxfEntity>();

            foreach (var entity in dxf.Entities)
            {
                var layerName = (entity.Layer ?? "").Trim();
                if (string.IsNullOrEmpty(layerName)) continue;

                // Already on target layer?
                if (string.Equals(layerName, "CUT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(layerName, "ETCH", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Layer 0 — keep as-is
                if (string.Equals(layerName, "0", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (CutLayers.Contains(layerName))
                {
                    entity.Layer = "CUT";
                }
                else if (EtchLayers.Contains(layerName))
                {
                    entity.Layer = "ETCH";
                }
                else
                {
                    // Unknown layer — mark entity for removal and log the layer name
                    unhandledLayers.Add(layerName);
                    entitiesToRemove.Add(entity);
                }
            }

            // Remove entities on unknown layers
            if (entitiesToRemove.Count > 0)
            {
                ppLog($"  Removing {entitiesToRemove.Count} entities on unknown layers");
                foreach (var entity in entitiesToRemove)
                    dxf.Entities.Remove(entity);
            }

            // Remove unused Inventor layers (keep 0, CUT, ETCH)
            var keepLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0", "CUT", "ETCH" };
            var layersToRemove = dxf.Layers
                .Where(l => !keepLayers.Contains(l.Name))
                .ToList();

            foreach (var layer in layersToRemove)
            {
                try { dxf.Layers.Remove(layer); } catch { }
            }
        }

        // ---------------------------------------------------------------
        // 3. Sanitize entity properties
        // ---------------------------------------------------------------

        /// <summary>
        /// Resets entity-level properties that IxMilia.Dxf carries forward from the
        /// Inventor DXF but which can confuse AutoCAD or other viewers:
        ///   - Lineweight → ByLayer (-1) instead of hairline (0)
        ///   - Color24Bit → reset to 0 (forces color-by-layer, avoids empty group 430)
        ///   - Transparency → 0
        /// </summary>
        private static void SanitizeEntityProperties(DxfFile dxf)
        {
            foreach (var entity in dxf.Entities)
            {
                // Force color to ByLayer (index 256) so entities inherit from layer definition
                entity.Color = DxfColor.ByLayer;

                // Lineweight (group 370): IxMilia always writes this field.
                // DXF spec: -2 = ByLayer, -1 = ByBlock, 0 = hairline (invisible at normal zoom)
                entity.LineweightEnumValue = -2; // ByLayer

                // Clear any true-color override (group 420) — 0 suppressed by IxMilia
                entity.Color24Bit = 0;

                // Clear color name (group 430) — null avoids writing empty string
                entity.ColorName = null;

                // Transparency (group 440): 0 = fully opaque
                entity.Transparency = 0;
            }
        }

        // ---------------------------------------------------------------
        // 4. Strip problematic group codes from raw DXF text
        // ---------------------------------------------------------------

        /// <summary>
        /// IxMilia.Dxf always writes certain entity group codes that AutoCAD
        /// does not expect (empty 430, default 284, default 440).
        /// This method reads the saved file line-by-line and removes those
        /// group-code/value pairs so the output is clean.
        /// </summary>
        private static void StripProblematicGroupCodes(string dxfPath)
        {
            // Group codes to strip — but ONLY within the ENTITIES section.
            // These same codes have legitimate meanings in HEADER/TABLES/OBJECTS.
            var codesToStrip = new HashSet<string> { "430", "440", "284" };

            var lines = IOFile.ReadAllLines(dxfPath);
            var output = new List<string>(lines.Length);

            bool inEntities = false;

            int i = 0;
            while (i < lines.Length)
            {
                var trimmed = lines[i].Trim();

                // Track which section we're in by watching for SECTION/ENDSEC pairs.
                // DXF sections: "  2\nENTITIES" follows "  0\nSECTION"
                if (trimmed == "ENTITIES")
                    inEntities = true;
                else if (trimmed == "ENDSEC")
                    inEntities = false;

                // Only strip within ENTITIES section
                if (inEntities && i + 1 < lines.Length && codesToStrip.Contains(trimmed))
                {
                    // Skip this code + its value line
                    i += 2;
                }
                else
                {
                    output.Add(lines[i]);
                    i++;
                }
            }

            IOFile.WriteAllLines(dxfPath, output);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static double Round(double value, int precision = 5)
            => Math.Round(value, precision);
    }
}
