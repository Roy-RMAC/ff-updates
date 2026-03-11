// Exporting/GlobalExporter.cs
using Inventor;
using RMAC_Efficiency_Addin.Exporting.Actions;
using RMAC_Efficiency_Addin.Exporting.Model;
using RMAC_Efficiency_Addin.Exporting.Services;
using RMAC_Efficiency_Addin.Infrastructure;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using IODir = System.IO.Directory;
using IOFile = System.IO.File;
// Avoid Inventor name collisions
using IOPath = System.IO.Path;
using SysEnv = System.Environment;
using WF = System.Windows.Forms;

namespace RMAC_Efficiency_Addin.Exporting
{
    internal static class GlobalExporter
    {
        /// <summary>Tracks tokens that have already shown a formatting warning this run.</summary>
        private static readonly HashSet<string> _formatWarnings = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// When true, suppresses dock-panel visibility updates in AppEvents_OnActivateDocument.
        /// Set during export to prevent Inventor's queued activation events from showing/hiding
        /// dockable windows as documents are opened behind the scenes.
        /// </summary>
        internal static bool SuppressDockUpdates { get; set; }

        public static void Run(Inventor.Application app, DrawingDocument drawingDoc, object? selectedAssemblyObj, Action<string>? status = null)
        {
            status ??= (_ => { });
            _formatWarnings.Clear();

            AddinSettings.EnsureLoaded();
            var exporting = AddinSettings.Current.Exporting;

            // ---- Determine export folder (chosen at run-time; NOT a setting) ----
            string exportRoot = ChooseExportFolder(app, drawingDoc);
            if (string.IsNullOrWhiteSpace(exportRoot))
                return; // user cancelled

            try { IODir.CreateDirectory(exportRoot); } catch { }

            // ---- Suppress screen updates + silent mode for speed ----
            bool prevScreenUpdating = true;
            bool prevSilent = false;
            try { prevScreenUpdating = app.ScreenUpdating; } catch { }
            try { prevSilent = app.SilentOperation; } catch { }

            ExportProgressForm? progressForm = null;

            try
            {
                SuppressDockUpdates = true;   // prevent dock panels toggling during export
                try { app.ScreenUpdating = false; } catch { }
                try { app.SilentOperation = true; } catch { }

                // ---- Progress dialog ----
                try
                {
                    progressForm = new ExportProgressForm();
                    progressForm.Show();
                    WF.Application.DoEvents();
                }
                catch { progressForm = null; }

                // Augment the status callback to also update the progress form text
                // (step count is updated separately in ExecutePlan via UpdateProgress)
                var originalStatus = status;
                status = msg =>
                {
                    try { originalStatus(msg); } catch { }
                    try { progressForm?.UpdateStatusText(msg); } catch { }
                };

                status($"Export folder: {exportRoot}");

                // ---- Resolve top-level assembly ----
                var asmDoc = ResolveTopAssembly(app, drawingDoc, selectedAssemblyObj, out bool openedAsmHere);
                if (asmDoc == null)
                {
                    WF.MessageBox.Show(
                        "Could not resolve the top-level assembly to export.\n\n" +
                        "Select an assembly (Part Numbering section) or ensure Sheet 1 View 1 references the top assembly.");
                    return;
                }

                // ---- Context + services ----
                var ctx = new ExportContext(app, asmDoc, exporting)
                {
                    BaseOutputDir = exportRoot
                };

                var classifier = new CommentClassifier();
                var resolver = new ProfileResolver(ctx, classifier);
                var router = new FolderRouter(ctx);
                router.EnsureBaseFolder(out _);

                var walker = new BomWalker(app);

                var bomExporter = new BomExporter(app);
                var dxfExporter = new DxfExporter(app);
                var stepExporter = new StepExporter(app);
                var drawingPackager = new DrawingPackager(app);
                var masterPdfBuilder = new MasterPdfBuilder();

                // ---- Read common properties (for naming) ----
                string proj = SafeGetProp(drawingDoc as _Document, "Design Tracking Properties", "Project") ?? "";
                string rev = SafeGetProp(drawingDoc as _Document, "Inventor Summary Information", "Revision Number") ?? "";
                if (string.IsNullOrWhiteSpace(rev))
                    rev = SafeGetProp(asmDoc as _Document, "Inventor Summary Information", "Revision Number") ?? "";

                // ---- Walk BOM (Parts Only) ----
                status("Reading Parts Only BOM...");
                var walk = walker.WalkPartsOnly(asmDoc);
                foreach (var w in walk.Warnings) ctx.AddWarning(w);
                foreach (var e in walk.Errors) ctx.AddError(e);

                if (walk.Errors.Count > 0)
                {
                    WF.MessageBox.Show("BOM walk failed:\n\n" + string.Join("\n", walk.Errors));
                    SafeCloseAssemblyIfNeeded(asmDoc, openedAsmHere);
                    return;
                }

                // ---- Build plan ----
                var plan = BuildPlan(ctx, classifier, resolver, router, walk.Items, asmDoc, drawingDoc, proj, rev, status);
                try { progressForm?.SetTotalSteps(plan.Jobs.Count); } catch { }

                // ---- Execute plan ----
                status("Executing export plan...");
                ExecutePlan(ctx, plan, drawingDoc, proj, rev, status,
                    bomExporter, dxfExporter, stepExporter, drawingPackager, masterPdfBuilder,
                    progressForm);

                // ---- BOM Export (Excel COM) ----
                ExportBoms(ctx, bomExporter, asmDoc, plan, resolver, router, status);

                // ---- Close assembly if we opened it ----
                SafeCloseAssemblyIfNeeded(asmDoc, openedAsmHere);

                try { progressForm?.SetComplete(); } catch { }
                status("Export complete.");
            }
            finally
            {
                // ---- Dispose progress dialog ----
                try { progressForm?.Close(); } catch { }
                try { progressForm?.Dispose(); } catch { }

                // ---- Restore screen updates + silent mode ----
                try { app.SilentOperation = prevSilent; } catch { }
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }

                // Clear suppression AFTER ScreenUpdating is restored so any
                // queued OnActivateDocument events fired during restoration are still suppressed.
                SuppressDockUpdates = false;
            }

            WF.MessageBox.Show("Export Process has been completed");
        }

        private static ExportPlan BuildPlan(
            ExportContext ctx,
            CommentClassifier classifier,
            ProfileResolver resolver,
            FolderRouter router,
            List<ExportItem> items,
            AssemblyDocument asmDoc,
            DrawingDocument drawingDoc,
            string proj,
            string rev,
            Action<string> status)
        {
            var plan = new ExportPlan();
            var asmDocTyped = (_Document)asmDoc;

            // Top-level STEP (global)
            if (ctx.Settings.ExportTopLevelAssemblySTEP)
            {
                var stepTmpl = ctx.Settings.TopLevelStepTemplate ?? "<Top.Part Number>";
                var stepName = BuildNameFromTemplate(asmDocTyped, 0, "", stepTmpl, AddinSettings.Current.SheetMetalThicknessParamName, asmDocTyped);

                var stepPath = IOPath.Combine(ctx.BaseOutputDir, SanitizeFileStem(stepName) + ".step");
                plan.AddJob(ExportJob.CreateStep(asmDocTyped, category: "", stepPath, ctx.AlwaysOverwrite, "Top-level Assembly STEP"));
            }

            // Per-item actions
            status("Planning per-item exports...");
            foreach (var it in items)
            {
                var cat = classifier.Normalize(it.Category);

                if (!resolver.TryGetProfile(cat, out var profile, out var reason))
                {
                    ctx.AddWarning($"{reason} ({DocumentHelpers.SafeDisplayName(it.Doc)})");
                    continue;
                }

                // Ensure category folder exists and record targets for category-level operations
                var catDir = router.GetCategoryFolder(cat, profile);
                plan.SetCategoryOutputFolder(cat, catDir);
                plan.AddCategoryTarget(cat, it.Doc);

                // DXF
                if (profile.ExportDXF)
                {
                    var dxfDir = router.GetCategorySubFolder(cat, profile, "DXF");

                    int qty = NormalizeQty(it.Quantity);

                    var tmpl = profile.Naming?.DxfTemplate ?? "<Part.Part Number>";
                    string dxfName = BuildNameFromTemplate(it.Doc, qty, cat, tmpl, AddinSettings.Current.SheetMetalThicknessParamName);

                    string dxfPath = IOPath.Combine(dxfDir, dxfName + ".dxf");
                    plan.AddJob(ExportJob.CreateDxf(it.Doc, cat, dxfPath, ctx.AlwaysOverwrite, "DXF"));
                }


                // STEP
                if (profile.ExportSTEP)
                {
                    var stepDir = router.GetCategorySubFolder(cat, profile, "STEP");

                    int qty = NormalizeQty(it.Quantity);

                    var tmpl = profile.Naming?.StepTemplate ?? "<Part.Part Number>";
                    string stepName = BuildNameFromTemplate(it.Doc, qty, cat, tmpl, AddinSettings.Current.SheetMetalThicknessParamName);

                    var stepPath = IOPath.Combine(stepDir, stepName + ".step");
                    plan.AddJob(ExportJob.CreateStep(it.Doc, cat, stepPath, ctx.AlwaysOverwrite, "STEP"));
                }

            }

            // Category drawing packages (sheet-based from active drawing)
            status("Planning drawing packages...");
            foreach (var kv in plan.CategoryTargets)
            {
                var cat = kv.Key;
                if (!resolver.TryGetProfile(cat, out var profile, out _)) continue;
                if (!profile.PackageDrawings) continue;

                var folder = plan.TryGetCategoryOutputFolder(cat, out var d) ? d : router.GetCategoryFolder(cat, profile);

                // Collect file paths of targets in this category
                var targetsDoc = plan.GetCategoryTargetsArray(cat);
                var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var td in targetsDoc)
                {
                    var fp = DocumentHelpers.SafeFullFileName(td);
                    if (!string.IsNullOrWhiteSpace(fp)) targetPaths.Add(fp);
                }

                // Find sheets in the active drawing that reference these targets
                var sheetIndices = FindSheetsForCategory(drawingDoc, targetPaths);
                if (sheetIndices.Length == 0) continue;

                // Build the output PDF name from the DrawingTemplate
                var drawTmpl = profile.Naming?.DrawingTemplate ?? "<Part.Part Number>";
                string drawName = BuildNameFromTemplate(asmDocTyped, 0, cat, drawTmpl, AddinSettings.Current.SheetMetalThicknessParamName, asmDocTyped);

                var pdfPath = IOPath.Combine(folder, SanitizeFileStem(drawName) + ".pdf");
                plan.AddJob(ExportJob.CreateCategoryDrawingPdf(cat, sheetIndices, pdfPath, ctx.AlwaysOverwrite));
            }

            // Construction drawings PDF (global)
            if (ctx.Settings.ExportMasterPDF)
            {
                var drawTmpl = ctx.Settings.TopLevelDrawingTemplate ?? "<Top.Project> A-01 REV<Top.Revision Number> Construction Drawings";
                var masterName = BuildNameFromTemplate(asmDocTyped, 0, "", drawTmpl, AddinSettings.Current.SheetMetalThicknessParamName, asmDocTyped);

                var masterPath = IOPath.Combine(ctx.BaseOutputDir, SanitizeFileStem(masterName) + ".pdf");
                plan.AddJob(ExportJob.CreateMasterPdf(masterPath, Array.Empty<string>(), ctx.AlwaysOverwrite, "Construction Drawings PDF"));
            }

            return plan;
        }

        private static void ExecutePlan(
            ExportContext ctx,
            ExportPlan plan,
            DrawingDocument drawingDoc,
            string proj,
            string rev,
            Action<string> status,
            BomExporter bomExporter,
            DxfExporter dxfExporter,
            StepExporter stepExporter,
            DrawingPackager drawingPackager,
            MasterPdfBuilder masterPdfBuilder,
            ExportProgressForm? progressForm = null)
        {
            dynamic? pb = null;
            try { pb = TryCreateProgressBar(ctx.App, plan.Jobs.Count, "RMAC Export"); } catch { }

            var createdDxfsByCategory = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < plan.Jobs.Count; i++)
            {
                var job = plan.Jobs[i];

                // Update progress (both custom dialog and Inventor's built-in bar)
                var progressMsg = $"{job.Type} {i + 1} / {plan.Jobs.Count}";
                try { progressForm?.UpdateProgress(i + 1, progressMsg); } catch { }
                try
                {
                    try { pb!.Message = progressMsg; } catch { }
                    TryProgressUpdate(pb, i + 1);
                }
                catch { }

                // Check for user cancellation
                if (progressForm?.CancellationRequested == true)
                    break;

                try
                {
                    switch (job.Type)
                    {
                        case ExportJobType.ExportDXF:
                            {
                                if (job.Doc == null) break;

                                status($"DXF: {DocumentHelpers.SafeDisplayName(job.Doc)}");
                                using var opened = OpenReadOnly(ctx.App, job.Doc, out var openedDoc);
                                if (openedDoc == null) break;

                                if (dxfExporter.ExportDxf(openedDoc, job.OutputPathOrFolder, job.AlwaysOverwrite, out var err))
                                {
                                    ctx.TrackCreatedFile(job.OutputPathOrFolder);

                                    var catKey = (job.Category ?? "").Trim();
                                    if (!createdDxfsByCategory.TryGetValue(catKey, out var list))
                                    {
                                        list = new List<string>();
                                        createdDxfsByCategory[catKey] = list;
                                    }
                                    list.Add(job.OutputPathOrFolder);
                                }
                                else if (!string.IsNullOrWhiteSpace(err))
                                {
                                    ctx.AddWarning(err);
                                }
                                break;
                            }

                        case ExportJobType.ExportSTEP:
                            {
                                if (job.Doc == null) break;

                                status($"STEP: {DocumentHelpers.SafeDisplayName(job.Doc)}");
                                using var opened = OpenReadOnly(ctx.App, job.Doc, out var openedDoc);
                                if (openedDoc == null) break;

                                if (stepExporter.ExportStep(openedDoc, job.OutputPathOrFolder, job.AlwaysOverwrite, out var err))
                                {
                                    ctx.TrackCreatedFile(job.OutputPathOrFolder);
                                }
                                else if (!string.IsNullOrWhiteSpace(err))
                                {
                                    ctx.AddWarning(err);
                                }
                                break;
                            }

                        case ExportJobType.ExportDrawingPackagePdf:
                            {
                                if (job.SheetIndices != null && job.SheetIndices.Length > 0)
                                {
                                    // Sheet-based export from the active drawing
                                    status($"Drawing package: {job.Category}");

                                    if (ExportSheetsToPdf(ctx.App, drawingDoc, job.SheetIndices, job.OutputPathOrFolder, job.AlwaysOverwrite, masterPdfBuilder, out var err))
                                        ctx.TrackCreatedFile(job.OutputPathOrFolder);
                                    else if (!string.IsNullOrWhiteSpace(err))
                                        ctx.AddWarning(err);
                                }
                                else if (job.DrawingTargetPaths != null && job.DrawingTargetPaths.Length > 0)
                                {
                                    // Legacy file-path-based export (fallback)
                                    status($"Drawing package: {job.Category}");
                                    var created = drawingPackager.ExportCategoryPdfPackage(
                                        job.DrawingTargetPaths,
                                        job.OutputPathOrFolder,
                                        job.AlwaysOverwrite,
                                        out var errors);
                                    foreach (var p in created) ctx.TrackCreatedFile(p);
                                    foreach (var e in errors) ctx.AddWarning(e);
                                }
                                break;
                            }

                        case ExportJobType.ExportMasterPdf:
                            {
                                status(job.Label ?? "Master PDF...");

                                // SourcePdfPaths empty => export all sheets of the active drawing
                                if (job.SourcePdfPaths == null || job.SourcePdfPaths.Length == 0)
                                {
                                    if (TryExportAllSheetsToPdf(ctx.App, drawingDoc, job.OutputPathOrFolder, job.AlwaysOverwrite, out var err))
                                        ctx.TrackCreatedFile(job.OutputPathOrFolder);
                                    else if (!string.IsNullOrWhiteSpace(err))
                                        ctx.AddWarning(err);
                                }
                                else
                                {
                                    if (masterPdfBuilder.BuildMasterPdf(job.SourcePdfPaths, job.OutputPathOrFolder, job.AlwaysOverwrite, out var err))
                                        ctx.TrackCreatedFile(job.OutputPathOrFolder);
                                    else if (!string.IsNullOrWhiteSpace(err))
                                        ctx.AddWarning(err);
                                }

                                break;
                            }

                        case ExportJobType.RunDxfPostProcessor:
                            {
                                // Legacy job type (kept for compatibility). We now post-process per-category below.
                                break;
                            }
                    }
                }
                catch (Exception exJob)
                {
                    ctx.AddWarning($"Job failed ({job.Type}): {exJob.Message}");
                }
            }

            // ---- DXF Post Processing (per category) ----
            // Temporary diagnostic log — always writes regardless of Debug flag
            var _ppLogPath = IOPath.Combine(IOPath.GetTempPath(), "RMAC_DxfPostProcess.log");
            void ppLog(string msg) { try { IOFile.AppendAllText(_ppLogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); } catch { } }

            try
            {
                ppLog($"DXF post-process START — {createdDxfsByCategory.Count} categories with DXFs");
                foreach (var dbg in createdDxfsByCategory)
                    ppLog($"  category=\"{dbg.Key}\" files={dbg.Value?.Count ?? 0}");

                var internalProcessor = new DxfPostProcessorInternal();
                var allUnhandledLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in createdDxfsByCategory)
                {
                    var cat = (kv.Key ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(cat))
                    {
                        ppLog("skipping blank category key");
                        continue;
                    }

                    if (!ctx.Settings.ProfilesByComment.TryGetValue(cat, out var prof) || prof == null)
                    {
                        ppLog($"no profile found for category \"{cat}\"");
                        continue;
                    }

                    prof.SanitizeInPlace();
                    if (!prof.DxfPostProcessing)
                    {
                        ppLog($"DxfPostProcessing=false for \"{cat}\" — skipping");
                        continue;
                    }

                    var dxfs = kv.Value?.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray() ?? Array.Empty<string>();
                    if (dxfs.Length == 0)
                    {
                        ppLog($"no DXF paths for \"{cat}\"");
                        continue;
                    }

                    ppLog($"processing {dxfs.Length} files for \"{cat}\"");
                    foreach (var dp in dxfs) ppLog($"  → {dp}");
                    status($"DXF post processing: {cat} ({dxfs.Length})");

                    bool ppOk = internalProcessor.ProcessFiles(dxfs, out var ppProcessed, out var ppErrors, out var unhandled);
                    ppLog($"result: ok={ppOk}, processed={ppProcessed.Count}, errors={ppErrors.Count}, unhandled={unhandled.Count}");

                    foreach (var e in ppErrors)
                    {
                        ppLog($"ERROR: {e}");
                        ctx.AddWarning(e);
                    }
                    foreach (var layer in unhandled)
                        allUnhandledLayers.Add(layer);
                }

                if (allUnhandledLayers.Count > 0)
                {
                    var msg = $"Unhandled DXF layers: {string.Join(", ", allUnhandledLayers.OrderBy(l => l))}";
                    ppLog(msg);
                    ctx.AddWarning(msg);
                }

                ppLog("DXF post-process END");
            }
            catch (Exception exPP)
            {
                ppLog($"EXCEPTION: {exPP}");
                ctx.AddWarning($"DXF post processing failed: {exPP.Message}");
            }

            try { pb?.Close(); } catch { }
        }

        // ---------------------------
        // Helpers: assembly resolution
        // ---------------------------

        private static AssemblyDocument? ResolveTopAssembly(Inventor.Application app, DrawingDocument drawingDoc, object? selectedAssemblyObj, out bool openedAsmHere)
        {
            openedAsmHere = false;

            AssemblyDocument? asmDoc = selectedAssemblyObj as AssemblyDocument;
            if (asmDoc == null)
                asmDoc = TryGetAssemblyFromSheet1View1(drawingDoc);

            if (asmDoc == null) return null;

            // Ensure assembly is open (read-only)
            try
            {
                var fullName = DocumentHelpers.SafeFullFileName(asmDoc);
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    var docObj = app.Documents.Open(fullName, true);
                    if (docObj is AssemblyDocument opened)
                    {
                        openedAsmHere = !ReferenceEquals(opened, asmDoc);
                        asmDoc = opened;
                    }
                }
            }
            catch { }

            return asmDoc;
        }

        private static AssemblyDocument? TryGetAssemblyFromSheet1View1(DrawingDocument drawingDoc)
        {
            try
            {
                var s = drawingDoc.Sheets[1];
                var v = s.DrawingViews[1];
                var refDocObj = v.ReferencedDocumentDescriptor?.ReferencedDocument;
                return refDocObj as AssemblyDocument;
            }
            catch
            {
                return null;
            }
        }

        private static void SafeCloseAssemblyIfNeeded(AssemblyDocument asm, bool openedHere)
        {
            try
            {
                if (openedHere)
                    asm.Close(true);
            }
            catch { }
        }

        // ---------------------------
        // Helpers: BOM export (Excel COM)
        // ---------------------------

        private static void ExportBoms(
            ExportContext ctx,
            BomExporter bomExporter,
            AssemblyDocument asmDoc,
            ExportPlan plan,
            ProfileResolver resolver,
            FolderRouter router,
            Action<string> status)
        {
            var asmDocTyped = (_Document)asmDoc;
            var thickParam = AddinSettings.Current.SheetMetalThicknessParamName;

            // Build global BOM path
            string? globalBomPath = null;
            if (ctx.Settings.ExportBOMExcel)
            {
                var bomTmpl = ctx.Settings.TopLevelBomTemplate ?? "<Top.Part Number>_Rev<Top.Revision Number>_BOM";
                var bomName = BuildNameFromTemplate(asmDocTyped, 0, "", bomTmpl, thickParam, asmDocTyped);
                globalBomPath = IOPath.Combine(ctx.BaseOutputDir, SanitizeFileStem(bomName) + ".xlsx");
            }

            // Build per-category filter specs.
            // Categories that share the same output folder are grouped into one filtered BOM
            // (e.g. PROFILE + PRESS → one file with AutoFilter matching both).
            var filtersByFolder = new Dictionary<string, BomExporter.BomFilterSpec>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in plan.CategoryTargets)
            {
                var cat = kv.Key;
                if (!resolver.TryGetProfile(cat, out var profile, out _)) continue;
                if (!profile.ExportBOM) continue;

                var folder = plan.TryGetCategoryOutputFolder(cat, out var d) ? d : router.GetCategoryFolder(cat, profile);

                if (filtersByFolder.TryGetValue(folder, out var existing))
                {
                    // Add this category to the existing spec (shared folder → combined filter)
                    var cats = new List<string>(existing.Categories) { cat };
                    existing.Categories = cats.ToArray();
                }
                else
                {
                    // Build the output file name from the first category's BOM template
                    var bomTmpl = profile.Naming?.BomTemplate ?? "<Part.Part Number> BOM";
                    string bomName = BuildNameFromTemplate(asmDocTyped, 0, cat, bomTmpl, thickParam, asmDocTyped);
                    var bomPath = IOPath.Combine(folder, SanitizeFileStem(bomName) + ".xlsx");

                    filtersByFolder[folder] = new BomExporter.BomFilterSpec
                    {
                        OutputPath = bomPath,
                        Categories = new[] { cat }
                    };
                }
            }

            var filters = new List<BomExporter.BomFilterSpec>(filtersByFolder.Values);

            // Skip if nothing to export
            if (string.IsNullOrWhiteSpace(globalBomPath) && filters.Count == 0)
                return;

            status("Exporting BOMs (Excel)...");

            try
            {
                var bomColumns = ctx.Settings.BomColumns ?? ExportingSettings.CreateDefaultBomColumns();

                bool ok = bomExporter.ExportAllBoms(
                    asmDoc,
                    globalBomPath,
                    filters,
                    bomColumns,
                    thickParam,
                    ctx.AlwaysOverwrite,
                    out var createdFiles,
                    out var errors);

                foreach (var f in createdFiles) ctx.TrackCreatedFile(f);
                foreach (var e in errors) ctx.AddWarning(e);
            }
            catch (Exception ex)
            {
                ctx.AddWarning($"BOM export failed: {ex.Message}");
            }
        }

        // ---------------------------
        // Helpers: folder picker
        // ---------------------------

        private static string ChooseExportFolder(Inventor.Application app, DrawingDocument drawingDoc)
        {
            string projectDir = "";
            try
            {
                var pm = app.DesignProjectManager;
                var activeProj = pm.ActiveDesignProject;
                projectDir = IOPath.GetDirectoryName(activeProj.FullFileName) ?? "";
            }
            catch { }

            string defaultFolderPath;
            try
            {
                defaultFolderPath = IOPath.Combine(projectDir, "Drawings");
                if (!IODir.Exists(defaultFolderPath))
                    defaultFolderPath = projectDir;

                if (string.IsNullOrWhiteSpace(defaultFolderPath))
                    defaultFolderPath = SysEnv.GetFolderPath(SysEnv.SpecialFolder.DesktopDirectory);
            }
            catch
            {
                defaultFolderPath = SysEnv.GetFolderPath(SysEnv.SpecialFolder.DesktopDirectory);
            }

            using var dlg = new WF.FolderBrowserDialog
            {
                SelectedPath = defaultFolderPath
            };

            return dlg.ShowDialog() == WF.DialogResult.OK ? dlg.SelectedPath : "";
        }

        // ---------------------------
        // Helpers: open docs read-only
        // ---------------------------

        private sealed class DocCloser : IDisposable
        {
            private _Document? _doc;
            private readonly bool _close;

            public DocCloser(_Document? doc, bool close)
            {
                _doc = doc;
                _close = close;
            }

            public void Dispose()
            {
                if (_doc == null) return;
                if (!_close) return;

                try { _doc.Close(true); } catch { }
                _doc = null;
            }
        }

        private static IDisposable OpenReadOnly(Inventor.Application app, _Document doc, out _Document? opened)
        {
            opened = null;

            try
            {
                var full = DocumentHelpers.SafeFullFileName(doc);
                if (string.IsNullOrWhiteSpace(full))
                {
                    opened = doc;
                    return new DocCloser(null, false);
                }

                var openedDocObj = app.Documents.Open(full, true);
                var openedDoc = openedDocObj as _Document;

                if (openedDoc == null)
                {
                    opened = doc;
                    return new DocCloser(null, false);
                }

                opened = openedDoc;

                // Close the document when the using block ends.
                // Drawing targets are stored as file paths, so closing here is safe.
                return new DocCloser(openedDoc, true);
            }
            catch
            {
                opened = doc;
                return new DocCloser(null, false);
            }
        }

        // ---------------------------
        // Helpers: PDF export (all sheets)
        // ---------------------------

        private static bool TryExportAllSheetsToPdf(Inventor.Application app, DrawingDocument doc, string exportPath, bool alwaysOverwrite, out string? error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    error = "PDF export path is empty.";
                    return false;
                }

                var dir = IOPath.GetDirectoryName(exportPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    IODir.CreateDirectory(dir);

                if (!alwaysOverwrite && IOFile.Exists(exportPath))
                    return true;

                var pdfAddin = GetTranslatorAddin(app, "{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}");
                if (pdfAddin == null)
                {
                    error = "PDF translator add-in not found.";
                    return false;
                }

                var ctx = app.TransientObjects.CreateTranslationContext();
                ctx.Type = IOMechanismEnum.kFileBrowseIOMechanism;

                var opts = app.TransientObjects.CreateNameValueMap();
                try { _ = pdfAddin.HasSaveCopyAsOptions[doc, ctx, opts]; } catch { }

                try { opts.Value["Vector_Resolution"] = 400; } catch { }
                try { opts.Value["All_Color_AS_Black"] = 0; } catch { }
                try { opts.Value["Remove_Line_Weights"] = 0; } catch { }
                try { opts.Value["Sheet_Range"] = PrintRangeEnum.kPrintAllSheets; } catch { }

                var data = app.TransientObjects.CreateDataMedium();
                data.FileName = exportPath;

                pdfAddin.SaveCopyAs(doc, ctx, opts, data);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static TranslatorAddIn? GetTranslatorAddin(Inventor.Application app, string classId)
        {
            try
            {
                foreach (ApplicationAddIn a in app.ApplicationAddIns)
                {
                    try
                    {
                        if (string.Equals(a.ClassIdString, classId, StringComparison.OrdinalIgnoreCase))
                            return a as TranslatorAddIn;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        // ---------------------------
        // Helpers: naming
        // ---------------------------

        private static string BuildLegacyDxfName(_Document doc, double? qty, string thicknessPropName)
        {
            string pno = SafeGetProp(doc, "Design Tracking Properties", "Part Number") ?? "NO-PN";
            string mat = SafeGetProp(doc, "Design Tracking Properties", "Material") ?? "NO-MAT";

            string thkRaw = SafeGetProp(doc, "Inventor User Defined Properties", thicknessPropName) ?? "0";
            double thk = ParseFirstNumber(thkRaw);
            string thkFmt = thk.ToString("0.0");

            int q = 1;
            try
            {
                if (qty.HasValue)
                    q = Math.Max(1, (int)Math.Round(qty.Value));
            }
            catch { }

            var name = $"{mat} - {thkFmt} mm, {pno} - {q} REQ'D";
            return SanitizeFileStem(name);
        }

        private static double ParseFirstNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;

            double val;
            var token = new string(s.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
            if (double.TryParse(token, out val)) return val;

            var curr = "";
            foreach (var ch in s)
            {
                if (char.IsDigit(ch) || ch == '.' || ch == '-')
                    curr += ch;
                else if (curr.Length > 0)
                    break;
            }

            if (double.TryParse(curr, out val)) return val;
            return 0;
        }

        private static string SanitizeFileStem(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "EXPORT";

            foreach (var c in IOPath.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            // Strip only TRAILING extensions (not mid-string occurrences)
            string[] extensionsToStrip = { ".pdf", ".step", ".stp", ".dxf", ".xlsx" };
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (var ext in extensionsToStrip)
                {
                    if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        s = s.Substring(0, s.Length - ext.Length);
                        stripped = true;
                    }
                }
            }

            return s.Trim();
        }

        // ---------------------------
        // Helpers: iProps
        // ---------------------------

        internal static string? SafeGetProp(_Document? doc, string propSetName, string propName)
        {
            if (doc == null) return null;

            try
            {
                var sets = doc.PropertySets;
                if (sets == null) return null;

                PropertySet? ps = null;
                try { ps = sets[propSetName]; } catch { ps = null; }
                if (ps == null) return null;

                Inventor.Property? p = null;
                try { p = ps[propName]; } catch { p = null; }
                if (p == null) return null;

                return p.Value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------
        // Helpers: progress bar
        // ---------------------------

        private static dynamic? TryCreateProgressBar(Inventor.Application app, int max, string title)
        {
            try
            {
                // Some PIAs provide CreateProgressBar. Keep it late-bound.
                var pb = app.CreateProgressBar(true, max, title);
                try { pb.Message = title; } catch { }
                TryProgressUpdate(pb, 0);
                return pb;
            }
            catch
            {
                return null;
            }
        }

        private static void TryProgressUpdate(dynamic? pb, int current)
        {
            if (pb == null) return;

            // Different PIAs expose different methods; try the common ones.
            try { pb.UpdateProgress(); return; } catch { }
            try { pb.UpdateProgress(current); return; } catch { }
            try { pb.Update(); return; } catch { }
        }

        private static readonly Regex TopTokenPattern = new(@"<Top\.([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ScopedTokenPattern = new(@"<Part\.([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LegacyTokenPattern = new(@"<([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string BuildNameFromTemplate(_Document doc, int qty, string category, string template, string thicknessParamName, _Document? topDoc = null)
        {
            template ??= "<Part.Part Number>";

            string result = template;

            // 0) Resolve top-level assembly tokens: <Top.PropertyName> or <Top.PropertyName|Format|Multiplier|StripUnit>
            if (topDoc != null)
            {
                result = TopTokenPattern.Replace(result, m =>
                {
                    var (propName, fmt, mult, keepUnit) = ParseTokenSpec(m.Groups[1].Value);
                    var raw = ResolveProperty(topDoc, propName, thicknessParamName) ?? "";
                    return ApplyTokenFormatting(raw, fmt, mult, keepUnit, "Top." + propName);
                });
            }

            // 1) Resolve scoped tokens: <Part.PropertyName|...>
            result = ScopedTokenPattern.Replace(result, m =>
            {
                var (propName, fmt, mult, keepUnit) = ParseTokenSpec(m.Groups[1].Value);
                var raw = ResolveProperty(doc, propName, thicknessParamName) ?? "";
                return ApplyTokenFormatting(raw, fmt, mult, keepUnit, "Part." + propName);
            });

            // 2) Resolve legacy unscoped tokens: <PropertyName|...> (backward compat)
            result = LegacyTokenPattern.Replace(result, m =>
            {
                var (tokenName, fmt, mult, keepUnit) = ParseTokenSpec(m.Groups[1].Value);

                // Context tokens (not iProperties)
                if (tokenName.Equals("Qty", StringComparison.OrdinalIgnoreCase))
                    return ApplyTokenFormatting(qty.ToString(), fmt, mult, keepUnit, tokenName);
                if (tokenName.Equals("Process", StringComparison.OrdinalIgnoreCase)
                    || tokenName.Equals("Category", StringComparison.OrdinalIgnoreCase))
                    return ApplyTokenFormatting(category, fmt, mult, keepUnit, tokenName);

                var raw = ResolveProperty(doc, tokenName, thicknessParamName) ?? "";
                return ApplyTokenFormatting(raw, fmt, mult, keepUnit, tokenName);
            });

            return SanitizeFileStem(result);
        }

        /// <summary>
        /// Splits a token spec like "THICKNESS|0.0|254|N" into name, format, multiplier, keepUnit.
        /// Format: TokenName[|NumberFormat[|Multiplier[|StripUnit]]]
        ///   NumberFormat: C# number format string (e.g. "0.0", "0.00", "G")
        ///   Multiplier:  numeric factor (e.g. 254 for mm→inches)
        ///   StripUnit:   N = strip unit suffix, Y = keep (default: Y)
        /// </summary>
        private static (string name, string? format, double? multiplier, bool keepUnit) ParseTokenSpec(string raw)
        {
            var parts = raw.Split('|');
            string name = parts[0].Trim();
            string? format = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : null;
            double? multiplier = null;
            if (parts.Length > 2 && double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mult))
                multiplier = mult;
            bool keepUnit = true;
            if (parts.Length > 3)
                keepUnit = !parts[3].Trim().Equals("N", StringComparison.OrdinalIgnoreCase);
            return (name, format, multiplier, keepUnit);
        }

        /// <summary>
        /// Applies numeric formatting to a resolved token value.
        /// If the value is non-numeric and formatting is requested, shows a one-time warning.
        /// </summary>
        private static string ApplyTokenFormatting(string rawValue, string? format, double? multiplier, bool keepUnit, string tokenName)
        {
            bool hasFormatting = format != null || multiplier.HasValue || !keepUnit;
            if (!hasFormatting) return rawValue;

            // Try to separate numeric part from unit suffix
            string valueStr = rawValue.Trim();
            string unitSuffix = "";

            var unitMatch = Regex.Match(valueStr, @"^(.*?)\s*(mm|cm|in|m|deg|rad|ul)$", RegexOptions.IgnoreCase);
            if (unitMatch.Success)
            {
                valueStr = unitMatch.Groups[1].Value.Trim();
                unitSuffix = unitMatch.Groups[2].Value;
            }

            if (!double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double numVal))
            {
                // Non-numeric value with formatting requested — warn once per run
                if (_formatWarnings.Add(tokenName))
                {
                    WF.MessageBox.Show(
                        $"Token <{tokenName}> resolved to \"{rawValue}\" which is not numeric.\n" +
                        "Numeric formatting (number format, multiplier, unit stripping) will be ignored.",
                        "Export Warning", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
                }
                return rawValue;
            }

            if (multiplier.HasValue)
                numVal *= multiplier.Value;

            string formatted;
            if (!string.IsNullOrEmpty(format))
                formatted = numVal.ToString(format, CultureInfo.InvariantCulture);
            else
                formatted = numVal.ToString("G", CultureInfo.InvariantCulture);

            // Re-attach unit suffix if keepUnit is true
            if (keepUnit && !string.IsNullOrEmpty(unitSuffix))
                formatted += " " + unitSuffix;

            return formatted;
        }

        /// <summary>
        /// Resolves a property name to its value from the document.
        /// Uses the IPropertyTokenRegistry to find the correct property set,
        /// falls back to User Defined Properties for custom tokens.
        /// </summary>
        internal static string? ResolveProperty(_Document doc, string propName, string thicknessParamName)
        {
            // Look up in registry to get the correct property set
            var token = IPropertyTokenRegistry.FindByName(propName);
            if (token != null)
            {
                var val = SafeGetProp(doc, token.PropertySet, token.Name);

                // Part Number fallback: use filename if property is empty
                if (string.IsNullOrEmpty(val)
                    && propName.Equals("Part Number", StringComparison.OrdinalIgnoreCase))
                {
                    val = IOPath.GetFileNameWithoutExtension(DocumentHelpers.SafeFullFileName(doc))
                          ?? DocumentHelpers.SafeDisplayName(doc)
                          ?? "Part";
                }

                return val;
            }

            // Not in registry — try User Defined Properties (custom tokens)
            var udp = SafeGetProp(doc, IPropertyTokenRegistry.UserDefined, propName);
            if (udp != null) return udp;

            // Special case: thickness may be a parameter rather than an iProperty
            if (propName.Equals("THICKNESS", StringComparison.OrdinalIgnoreCase)
                || propName.Equals(thicknessParamName, StringComparison.OrdinalIgnoreCase))
            {
                return TryGetParameterValue(doc, thicknessParamName);
            }

            return null;
        }

        internal static string? TryGetParameterValue(_Document doc, string paramName)
        {
            try
            {
                if (doc is PartDocument pd && pd.ComponentDefinition != null)
                {
                    var p = pd.ComponentDefinition.Parameters[paramName];
                    if (p == null) return null;

                    // Try Expression first (e.g. "1.600mm"), clean it up
                    try
                    {
                        var expr = p.Expression;
                        if (!string.IsNullOrWhiteSpace(expr))
                            return CleanParameterExpression(expr);
                    }
                    catch { }

                    // Fallback: internal value (cm) → mm
                    try
                    {
                        double valCm = (double)p.Value; // Inventor internal units = cm
                        double valMm = valCm * 10.0;
                        return valMm.ToString("G");
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Cleans a parameter expression like "1.600mm" → "1.6".
        /// Strips unit suffix, parses numeric value, formats with G to remove trailing zeros.
        /// </summary>
        internal static string CleanParameterExpression(string expr)
        {
            // Strip common unit suffixes (mm, cm, in, deg, etc.)
            var numeric = Regex.Replace(expr.Trim(), @"\s*(mm|cm|in|m|deg|rad)\s*$", "", RegexOptions.IgnoreCase);

            if (double.TryParse(numeric, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double val))
            {
                return val.ToString("G");
            }

            // If parsing failed, return the original stripped of unit
            return numeric;
        }

        private static int NormalizeQty(double? qty)
        {
            try
            {
                if (!qty.HasValue) return 1;
                return Math.Max(1, (int)Math.Round(qty.Value, MidpointRounding.AwayFromZero));
            }
            catch
            {
                return 1;
            }
        }

        // ---------------------------
        // Helpers: sheet-based drawing export
        // ---------------------------

        /// <summary>
        /// Finds 1-based sheet indices in the drawing that contain views referencing any of the target file paths.
        /// </summary>
        private static int[] FindSheetsForCategory(DrawingDocument drawingDoc, HashSet<string> targetPaths)
        {
            var indices = new List<int>();
            try
            {
                for (int s = 1; s <= drawingDoc.Sheets.Count; s++)
                {
                    var sheet = drawingDoc.Sheets[s];
                    try
                    {
                        foreach (DrawingView view in sheet.DrawingViews)
                        {
                            try
                            {
                                var refDesc = view.ReferencedDocumentDescriptor;
                                if (refDesc == null) continue;

                                var refDoc = refDesc.ReferencedDocument as _Document;
                                if (refDoc == null) continue;

                                var refPath = DocumentHelpers.SafeFullFileName(refDoc);
                                if (!string.IsNullOrWhiteSpace(refPath) && targetPaths.Contains(refPath))
                                {
                                    indices.Add(s);
                                    break; // one match is enough for this sheet
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return indices.ToArray();
        }

        /// <summary>
        /// Exports specific sheets from a drawing to a single merged PDF.
        /// Exports each sheet individually to a temp PDF, then merges.
        /// </summary>
        private static bool ExportSheetsToPdf(
            Inventor.Application app,
            DrawingDocument drawingDoc,
            int[] sheetIndices,
            string outputPath,
            bool alwaysOverwrite,
            MasterPdfBuilder merger,
            out string? error)
        {
            error = null;
            if (sheetIndices == null || sheetIndices.Length == 0)
            {
                error = "No sheets specified.";
                return false;
            }

            try
            {
                var dir = IOPath.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    IODir.CreateDirectory(dir);

                if (!alwaysOverwrite && IOFile.Exists(outputPath))
                    return true;

                var pdfAddin = GetTranslatorAddin(app, "{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}");
                if (pdfAddin == null)
                {
                    error = "PDF translator add-in not found.";
                    return false;
                }

                if (sheetIndices.Length == 1)
                {
                    // Single sheet — export directly
                    return ExportSingleSheetToPdf(app, drawingDoc, pdfAddin, sheetIndices[0], outputPath, out error);
                }

                // Multiple sheets — export each to temp, then merge
                var tempDir = IOPath.Combine(IOPath.GetTempPath(), "RMAC_DrawingPkg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                IODir.CreateDirectory(tempDir);

                var tempPdfs = new List<string>();
                try
                {
                    foreach (var idx in sheetIndices)
                    {
                        var tempPath = IOPath.Combine(tempDir, $"Sheet_{idx}.pdf");
                        if (ExportSingleSheetToPdf(app, drawingDoc, pdfAddin, idx, tempPath, out var sheetErr))
                        {
                            tempPdfs.Add(tempPath);
                        }
                        else if (!string.IsNullOrWhiteSpace(sheetErr))
                        {
                            error = (error ?? "") + sheetErr + "; ";
                        }
                    }

                    if (tempPdfs.Count == 0)
                    {
                        error = "No sheets were exported successfully.";
                        return false;
                    }

                    if (tempPdfs.Count == 1)
                    {
                        IOFile.Copy(tempPdfs[0], outputPath, true);
                        return true;
                    }

                    if (merger.BuildMasterPdf(tempPdfs.ToArray(), outputPath, true, out var mergeErr))
                        return true;

                    error = mergeErr;
                    return false;
                }
                finally
                {
                    try { IODir.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                error = $"Sheet PDF export failed: {ex.Message}";
                return false;
            }
        }

        private static bool ExportSingleSheetToPdf(
            Inventor.Application app,
            DrawingDocument drawingDoc,
            TranslatorAddIn pdfAddin,
            int sheetIndex,
            string outputPath,
            out string? error)
        {
            error = null;
            try
            {
                // Activate the target sheet
                var sheet = drawingDoc.Sheets[sheetIndex];
                sheet.Activate();

                var ctx = app.TransientObjects.CreateTranslationContext();
                ctx.Type = IOMechanismEnum.kFileBrowseIOMechanism;

                var opts = app.TransientObjects.CreateNameValueMap();
                try { _ = pdfAddin.HasSaveCopyAsOptions[drawingDoc, ctx, opts]; } catch { }

                try { opts.Value["Sheet_Range"] = PrintRangeEnum.kPrintCurrentSheet; } catch { }
                try { opts.Value["Vector_Resolution"] = 400; } catch { }
                try { opts.Value["All_Color_AS_Black"] = 0; } catch { }
                try { opts.Value["Remove_Line_Weights"] = 0; } catch { }

                var data = app.TransientObjects.CreateDataMedium();
                data.FileName = outputPath;

                pdfAddin.SaveCopyAs(drawingDoc, ctx, opts, data);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to export sheet {sheetIndex}: {ex.Message}";
                return false;
            }
        }

    }
}
