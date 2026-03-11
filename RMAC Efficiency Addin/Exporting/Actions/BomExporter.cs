// Exporting/Actions/BomExporter.cs
using Inventor;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;

namespace RMAC_Efficiency_Addin.Exporting.Actions
{
    internal sealed class BomExporter
    {
        private readonly Inventor.Application _app;

        public BomExporter(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        internal sealed class BomFilterSpec
        {
            public string OutputPath { get; set; } = "";
            public string[] Categories { get; set; } = Array.Empty<string>();
        }

        /// <summary>
        /// Exports Parts Only BOM to Excel via COM automation with configurable columns,
        /// then saves filtered copies per category using AutoFilter on the Comments column.
        /// Comments is always appended as the last column for filtering.
        /// </summary>
        public bool ExportAllBoms(
            AssemblyDocument topAssembly,
            string? globalBomPath,
            List<BomFilterSpec> filters,
            IReadOnlyList<BomColumnDefinition> columns,
            string thicknessParamName,
            bool alwaysOverwrite,
            out List<string> createdFiles,
            out List<string> errors)
        {
            createdFiles = new List<string>();
            errors = new List<string>();

            dynamic? excelApp = null;
            dynamic? workbook = null;
            dynamic? sheet = null;

            try
            {
                // ---- Enable Parts Only BOM ----
                var bom = topAssembly.ComponentDefinition.BOM;
                try { bom.StructuredViewFirstLevelOnly = false; } catch { }
                try { bom.PartsOnlyViewEnabled = true; } catch { }

                BOMView? bomView = null;
                foreach (BOMView v in bom.BOMViews)
                {
                    if (string.Equals(v.Name, "Parts Only", StringComparison.OrdinalIgnoreCase))
                    { bomView = v; break; }
                }
                if (bomView == null && bom.BOMViews.Count >= 1)
                    bomView = bom.BOMViews[1];

                if (bomView == null)
                {
                    errors.Add("No BOM view available to export.");
                    return false;
                }

                // ---- Create Excel ----
                var excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    errors.Add("Microsoft Excel is not installed.");
                    return false;
                }

                excelApp = Activator.CreateInstance(excelType)
                    ?? throw new InvalidOperationException("Failed to create Excel instance.");
                excelApp.Visible = false;
                excelApp.DisplayAlerts = false;

                workbook = excelApp.Workbooks.Add();
                sheet = workbook.Sheets[1];

                int totalCols = columns.Count + 1; // +1 for auto-appended Comments

                // ---- Headers ----
                for (int c = 0; c < columns.Count; c++)
                    sheet.Cells[1, c + 1].Value = columns[c].Header;
                sheet.Cells[1, totalCols].Value = "COMMENTS";

                // ---- Collect BOM rows ----
                var rows = new List<string[]>();
                foreach (BOMRow bomRow in bomView.BOMRows)
                {
                    try
                    {
                        dynamic compDef = bomRow.ComponentDefinitions[1];
                        _Document doc = compDef.Document;

                        var values = new string[totalCols];

                        // Resolve each configured column
                        for (int c = 0; c < columns.Count; c++)
                        {
                            var col = columns[c];
                            string rawValue;

                            if (col.TokenName.Equals("Qty", StringComparison.OrdinalIgnoreCase)
                                || col.TokenName.Equals("Item Qty", StringComparison.OrdinalIgnoreCase))
                            {
                                // Quantity comes from BOM row, not iProperties
                                double qty = 1;
                                try { qty = bomRow.ItemQuantity; } catch { }
                                rawValue = ((int)Math.Round(qty, MidpointRounding.AwayFromZero)).ToString();
                            }
                            else
                            {
                                rawValue = GlobalExporter.ResolveProperty(doc, col.TokenName, thicknessParamName) ?? "";
                            }

                            // Apply formatting if enabled
                            if (col.FormattingEnabled && !string.IsNullOrWhiteSpace(rawValue))
                                rawValue = ApplyColumnFormatting(rawValue, col.NumberFormat, col.StripUnit);

                            values[c] = rawValue;
                        }

                        // Comments always last (for AutoFilter)
                        values[totalCols - 1] = GlobalExporter.SafeGetProp(
                            doc, "Inventor Summary Information", "Comments") ?? "";

                        rows.Add(values);
                    }
                    catch (Exception exRow)
                    {
                        errors.Add($"BOM row error: {exRow.Message}");
                    }
                }

                // ---- Sort alphabetically by first column ----
                if (rows.Count > 0)
                    rows.Sort((a, b) => string.Compare(a[0], b[0], StringComparison.OrdinalIgnoreCase));

                // ---- Write sorted rows to Excel ----
                int rowIndex = 2;
                foreach (var vals in rows)
                {
                    for (int c = 0; c < vals.Length; c++)
                        sheet.Cells[rowIndex, c + 1].Value = vals[c];
                    rowIndex++;
                }

                int lastRow = rowIndex - 1;
                if (lastRow < 2)
                {
                    errors.Add("BOM contains no rows.");
                    return false;
                }

                // ---- Apply Excel number format to formatted columns ----
                for (int c = 0; c < columns.Count; c++)
                {
                    if (columns[c].FormattingEnabled)
                    {
                        dynamic? range = null;
                        try
                        {
                            range = sheet.Range[
                                sheet.Cells[2, c + 1],
                                sheet.Cells[lastRow, c + 1]];
                            range.NumberFormat = columns[c].NumberFormat;
                        }
                        catch { }
                        finally
                        {
                            if (range != null) try { Marshal.ReleaseComObject(range); } catch { }
                        }
                    }
                }

                // Column letter for range address
                string lastColLetter = GetExcelColumnLetter(totalCols);
                string rangeAddr = $"A1:{lastColLetter}{lastRow}";

                // ---- Enable AutoFilter on Comments column (last column) ----
                try { sheet.Range[rangeAddr].AutoFilter(totalCols); } catch { }

                // ---- Save global BOM (unfiltered) ----
                if (!string.IsNullOrWhiteSpace(globalBomPath))
                {
                    try
                    {
                        var dir = IOPath.GetDirectoryName(globalBomPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);

                        if (alwaysOverwrite || !IOFile.Exists(globalBomPath))
                        {
                            workbook.SaveAs(globalBomPath);
                            createdFiles.Add(globalBomPath);
                        }
                    }
                    catch (Exception exSave)
                    {
                        errors.Add($"Failed to save global BOM: {exSave.Message}");
                    }
                }

                // ---- Save per-category filtered BOMs ----
                foreach (var spec in filters)
                {
                    try
                    {
                        if (!alwaysOverwrite && IOFile.Exists(spec.OutputPath))
                            continue;

                        var dir = IOPath.GetDirectoryName(spec.OutputPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);

                        // Clear existing filter
                        try { sheet.AutoFilterMode = false; } catch { }

                        // Apply filter on Comments column (last column)
                        if (spec.Categories.Length == 1)
                        {
                            sheet.Range[rangeAddr].AutoFilter(totalCols, $"={spec.Categories[0]}");
                        }
                        else if (spec.Categories.Length > 1)
                        {
                            // Operator 7 = xlFilterValues (multi-value filter)
                            sheet.Range[rangeAddr].AutoFilter(totalCols, spec.Categories, 7);
                        }

                        workbook.SaveAs(spec.OutputPath);
                        createdFiles.Add(spec.OutputPath);
                    }
                    catch (Exception exFilter)
                    {
                        errors.Add($"Failed to save filtered BOM for [{string.Join(", ", spec.Categories)}]: {exFilter.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"BOM export failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { workbook?.Close(false); } catch { }
                try { excelApp?.Quit(); } catch { }

                if (sheet != null) try { Marshal.ReleaseComObject(sheet); } catch { }
                if (workbook != null) try { Marshal.ReleaseComObject(workbook); } catch { }
                if (excelApp != null) try { Marshal.ReleaseComObject(excelApp); } catch { }
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Applies numeric formatting to a BOM cell value.
        /// Strips unit suffix if flagged, parses as double, formats with the given number format.
        /// Returns the raw value unchanged if it cannot be parsed as a number.
        /// </summary>
        private static string ApplyColumnFormatting(string rawValue, string numberFormat, bool stripUnit)
        {
            string numericPart = rawValue.Trim();

            // Strip unit suffix (e.g. "1.600mm" → "1.600", "12.5 in" → "12.5")
            if (stripUnit)
            {
                var unitMatch = Regex.Match(numericPart, @"^(.*?)\s*(mm|cm|in|m|deg|rad|ul)$", RegexOptions.IgnoreCase);
                if (unitMatch.Success)
                    numericPart = unitMatch.Groups[1].Value.Trim();
            }

            // Try extract leading numeric portion even if unit wasn't stripped
            var numMatch = Regex.Match(numericPart, @"^[0-9.,\-+eE]+");
            if (!numMatch.Success) return rawValue;

            if (double.TryParse(numMatch.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val.ToString(numberFormat, CultureInfo.InvariantCulture);

            return rawValue;
        }

        /// <summary>
        /// Converts a 1-based column number to Excel column letter (1=A, 26=Z, 27=AA, etc.)
        /// </summary>
        private static string GetExcelColumnLetter(int colNumber)
        {
            string result = "";
            while (colNumber > 0)
            {
                colNumber--;
                result = (char)('A' + colNumber % 26) + result;
                colNumber /= 26;
            }
            return result;
        }
    }
}
