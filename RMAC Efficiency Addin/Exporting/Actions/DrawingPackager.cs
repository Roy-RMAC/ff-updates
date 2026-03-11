// Exporting/Actions/DrawingPackager.cs
using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;

namespace RMAC_Efficiency_Addin.Exporting.Actions
{
    internal sealed class DrawingPackager
    {
        private readonly Inventor.Application _app;

        public DrawingPackager(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public IReadOnlyList<string> ExportCategoryPdfPackage(
            _Document[] targetDocs,
            string outputFolder,
            bool alwaysOverwrite,
            out List<string> errors)
        {
            errors = new List<string>();

            targetDocs ??= Array.Empty<_Document>();
            var targets = targetDocs.Where(d => d != null).Distinct((IEqualityComparer<_Document>)new DocComparer<_Document>()).ToArray();
            if (targets.Length == 0) return Array.Empty<string>();

            try { Directory.CreateDirectory(outputFolder); }
            catch (Exception ex)
            {
                errors.Add($"Failed to create folder '{outputFolder}': {ex.Message}");
                return Array.Empty<string>();
            }

            var drawings = FindCandidateDrawings(targets);

            var created = new List<string>();
            foreach (var dwgPath in drawings)
            {
                try
                {
                    if (!IOFile.Exists(dwgPath)) continue;

                    var doc = _app.Documents.Open(dwgPath, true);
                    if (doc is not DrawingDocument ddoc)
                    {
                        try { doc.Close(true); } catch { }
                        continue;
                    }

                    if (!DrawingReferencesAny(ddoc, targets))
                    {
                        try { ddoc.Close(true); } catch { }
                        continue;
                    }

                    var pdfPath = BuildPdfPath(dwgPath, outputFolder);

                    if (!alwaysOverwrite && IOFile.Exists(pdfPath))
                    {
                        try { ddoc.Close(true); } catch { }
                        continue;
                    }

                    if (ExportDrawingToPdf(ddoc, pdfPath, out var err))
                    {
                        created.Add(pdfPath);
                    }
                    else if (!string.IsNullOrWhiteSpace(err))
                    {
                        errors.Add(err);
                    }

                    try { ddoc.Close(true); } catch { }
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed packaging drawing '{dwgPath}': {ex.Message}");
                }
            }

            return created;
        }

        private HashSet<string> FindCandidateDrawings(_Document[] targets)
        {
            // Minimal, safe heuristic: same folder + same stem as model.
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in targets)
            {
                var modelPath = DocumentHelpers.SafeFullFileName(t);
                if (string.IsNullOrWhiteSpace(modelPath)) continue;

                var dir = IOPath.GetDirectoryName(modelPath);
                var stem = IOPath.GetFileNameWithoutExtension(modelPath);
                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem)) continue;

                set.Add(IOPath.Combine(dir, stem + ".idw"));
                set.Add(IOPath.Combine(dir, stem + ".dwg"));
            }

            return set;
        }

        private static bool DrawingReferencesAny(DrawingDocument drawingDoc, _Document[] targets)
        {
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetInternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in targets)
            {
                var p = DocumentHelpers.SafeFullFileName(t);
                if (!string.IsNullOrWhiteSpace(p)) targetPaths.Add(p);

                var i = DocumentHelpers.SafeInternalName(t);
                if (!string.IsNullOrWhiteSpace(i)) targetInternal.Add(i);
            }

            try
            {
                foreach (ReferencedFileDescriptor rfd in drawingDoc.ReferencedFileDescriptors)
                {
                    var refPath = "";
                    try { refPath = rfd.FullFileName; } catch { }

                    if (!string.IsNullOrWhiteSpace(refPath) && targetPaths.Contains(refPath))
                        return true;

                    // In some PIAs ReferencedDocument is object
                    try
                    {
                        var o = rfd.ReferencedDocument;
                        var rd = o as _Document;
                        if (rd != null)
                        {
                            var iname = DocumentHelpers.SafeInternalName(rd);
                            if (!string.IsNullOrWhiteSpace(iname) && targetInternal.Contains(iname))
                                return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        private bool ExportDrawingToPdf(DrawingDocument drawingDoc, string pdfPath, out string? error)
        {
            error = null;

            try
            {
                var pdfAddIn = GetPdfTranslator();
                if (pdfAddIn == null)
                {
                    error = "PDF translator add-in not found.";
                    return false;
                }

                var ctx = _app.TransientObjects.CreateTranslationContext();
                ctx.Type = IOMechanismEnum.kFileBrowseIOMechanism;

                var opts = _app.TransientObjects.CreateNameValueMap();
                try { _ = pdfAddIn.HasSaveCopyAsOptions[drawingDoc, ctx, opts]; } catch { }

                var data = _app.TransientObjects.CreateDataMedium();
                data.FileName = pdfPath;

                pdfAddIn.SaveCopyAs(drawingDoc, ctx, opts, data);
                return true;
            }
            catch (Exception ex)
            {
                error = $"PDF export failed: {ex.Message}";
                return false;
            }
        }

        private TranslatorAddIn? GetPdfTranslator()
        {
            try
            {
                const string pdfGuid = "{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}";
                foreach (ApplicationAddIn a in _app.ApplicationAddIns)
                {
                    try
                    {
                        if (string.Equals(a.ClassIdString, pdfGuid, StringComparison.OrdinalIgnoreCase))
                            return a as TranslatorAddIn;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static string BuildPdfPath(string drawingPath, string outputFolder)
        {
            var stem = IOPath.GetFileNameWithoutExtension(drawingPath) ?? "Drawing";
            return IOPath.Combine(outputFolder, stem + ".pdf");
        }

        public IReadOnlyList<string> ExportCategoryPdfPackage(
            string[] targetModelPaths,
            string outputFolder,
            bool alwaysOverwrite,
            out List<string> errors)
        {
            errors = new List<string>();
            targetModelPaths ??= Array.Empty<string>();

            var validPaths = targetModelPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (validPaths.Length == 0) return Array.Empty<string>();

            try { Directory.CreateDirectory(outputFolder); }
            catch (Exception ex)
            {
                errors.Add($"Failed to create folder '{outputFolder}': {ex.Message}");
                return Array.Empty<string>();
            }

            // Build candidate drawing paths from model file paths
            var drawingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var modelPath in validPaths)
            {
                var dir = IOPath.GetDirectoryName(modelPath);
                var stem = IOPath.GetFileNameWithoutExtension(modelPath);
                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem)) continue;

                drawingPaths.Add(IOPath.Combine(dir, stem + ".idw"));
                drawingPaths.Add(IOPath.Combine(dir, stem + ".dwg"));
            }

            var created = new List<string>();
            var targetPathSet = new HashSet<string>(validPaths, StringComparer.OrdinalIgnoreCase);

            foreach (var dwgPath in drawingPaths)
            {
                try
                {
                    if (!IOFile.Exists(dwgPath)) continue;

                    var doc = _app.Documents.Open(dwgPath, true);
                    if (doc is not DrawingDocument ddoc)
                    {
                        try { doc.Close(true); } catch { }
                        continue;
                    }

                    // Check if this drawing references any of our target models
                    if (!DrawingReferencesAnyByPath(ddoc, targetPathSet))
                    {
                        try { ddoc.Close(true); } catch { }
                        continue;
                    }

                    var pdfPath = BuildPdfPath(dwgPath, outputFolder);

                    if (!alwaysOverwrite && IOFile.Exists(pdfPath))
                    {
                        try { ddoc.Close(true); } catch { }
                        continue;
                    }

                    if (ExportDrawingToPdf(ddoc, pdfPath, out var err))
                    {
                        created.Add(pdfPath);
                    }
                    else if (!string.IsNullOrWhiteSpace(err))
                    {
                        errors.Add(err);
                    }

                    try { ddoc.Close(true); } catch { }
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed packaging drawing '{dwgPath}': {ex.Message}");
                }
            }

            return created;
        }

        private static bool DrawingReferencesAnyByPath(DrawingDocument drawingDoc, HashSet<string> targetPaths)
        {
            try
            {
                foreach (ReferencedFileDescriptor rfd in drawingDoc.ReferencedFileDescriptors)
                {
                    var refPath = "";
                    try { refPath = rfd.FullFileName; } catch { }

                    if (!string.IsNullOrWhiteSpace(refPath) && targetPaths.Contains(refPath))
                        return true;
                }
            }
            catch { }

            return false;
        }

    }
}
