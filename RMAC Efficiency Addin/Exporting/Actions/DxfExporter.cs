// Exporting/Actions/DxfExporter.cs
using Inventor;
using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.Exporting.Actions
{
    internal sealed class DxfExporter
    {
        private readonly Inventor.Application _app;

        public DxfExporter(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public bool ExportDxf(_Document doc, string dxfPath, bool alwaysOverwrite, out string? error)
        {
            error = null;

            try
            {
                if (doc == null) { error = "Document is null."; return false; }
                if (string.IsNullOrWhiteSpace(dxfPath)) { error = "DXF path is empty."; return false; }

                var dir = IOPath.GetDirectoryName(dxfPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (!alwaysOverwrite && IOFile.Exists(dxfPath))
                    return false;

                if (doc.DocumentType != DocumentTypeEnum.kPartDocumentObject)
                {
                    error = $"DXF export supports Part documents only. Found: {doc.DocumentType}";
                    return false;
                }

                var partDoc = doc as PartDocument;
                if (partDoc == null)
                {
                    error = "Failed to cast to PartDocument.";
                    return false;
                }

                if (partDoc.ComponentDefinition is not SheetMetalComponentDefinition smcd)
                {
                    error = $"Not sheet metal: {SafeName(doc)}";
                    return false;
                }

                return ExportSheetMetalFlatPatternDxf(smcd, dxfPath, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AddinSettings.Log($"DXF export error: {ex}");
                return false;
            }
        }

        public bool WriteDxfListFile(IEnumerable<string> dxfPaths, string listFilePath, out string? error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(listFilePath))
                {
                    error = "DXF list path is empty.";
                    return false;
                }

                var dir = IOPath.GetDirectoryName(listFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var lines = new List<string>();
                foreach (var p in dxfPaths ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    lines.Add(p.Trim());
                }

                IOFile.WriteAllLines(listFilePath, lines);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool ExportSheetMetalFlatPatternDxf(SheetMetalComponentDefinition smcd, string dxfPath, out string? error)
        {
            error = null;

            FlatPattern? fp = null;

            try
            {
                // Ensure flat pattern exists
                if (!smcd.HasFlatPattern)
                    smcd.Unfold();

                fp = smcd.FlatPattern;
                if (fp == null)
                {
                    error = "Flat pattern not available.";
                    return false;
                }

                // Enter flat pattern edit mode (no Active property in your PIA)
                fp.Edit();

                // DataIO export string - adjust later to match your exact legacy string.
                const string dataIoFormat =
                    "FLAT PATTERN DXF?AcadVersion=R12&OuterProfileLayer=IV_OUTER&" +
                    "InnerProfileLayer=IV_INNER&BendUpLayer=IV_BEND_UP&BendDownLayer=IV_BEND_DOWN";

                fp.DataIO.WriteDataToFile(dataIoFormat, dxfPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { fp?.ExitEdit(); } catch { }
            }
        }

        private static string SafeName(_Document doc)
        {
            try { return doc.DisplayName ?? "(unknown)"; } catch { return "(unknown)"; }
        }
    }
}
