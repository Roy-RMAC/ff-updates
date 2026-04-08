// RmacPartNumberingRenumberer.cs
using Inventor;
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using RMAC_Efficiency_Addin.PartNumbering;

// Avoid Inventor.Path collisions
using IOPath = System.IO.Path;
using IODir = System.IO.Directory;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Orchestrator: resolves BOM view + BOM rows, sets silent/transaction behavior,
    /// then dispatches to the selected numbering scheme class.
    /// </summary>
    internal static class RmacPartNumberingRenumberer
    {
        /// <summary>
        /// Public UI entry point. Validates the target, creates a progress dialog, delegates the
        /// pipeline to <see cref="RunCore"/>, and presents the outcome as modal dialogs.
        /// Production callers (ribbon button) use this overload.
        /// </summary>
        public static void Run(Inventor.Application app, object? assemblyDocObj)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            if (assemblyDocObj is not AssemblyDocument asmDoc)
            {
                MessageBox.Show("Selected target is not an Assembly document.");
                return;
            }

            var prevDoc = app.ActiveDocument;

            RenumberProgressForm? progressForm = null;
            try
            {
                progressForm = new RenumberProgressForm();
                progressForm.Show();
                progressForm.UpdateStatusText("Starting\u2026");
            }
            catch { progressForm = null; }

            RenumberResult result;
            try
            {
                result = RunCore(app, asmDoc, progressForm);
            }
            finally
            {
                try { progressForm?.CloseAllowed(); } catch { }
                try { progressForm?.Dispose(); } catch { }
            }

            // Restore the previously-active document (typically the drawing) before any modal.
            try { prevDoc?.Activate(); } catch { }

            // ---- Present result ----
            if (result.Errors.Count > 0)
            {
                MessageBox.Show(result.Errors[0], "Renumber - Error");
                return;
            }

            if (result.Skipped)
            {
                string skipMsg = $"Renumber skipped (top-level assembly matched skip rule).\r\n\r\n{result.SkipReason}";
                if (result.DebugMode && !string.IsNullOrWhiteSpace(result.LogPath))
                    skipMsg += $"\r\n\r\nLog:\r\n{result.LogPath}";
                MessageBox.Show(skipMsg);
                return;
            }

            // Non-fatal: missing properties report (shown before the completion dialog)
            if (!string.IsNullOrWhiteSpace(result.MissingPropertiesReport))
            {
                MessageBox.Show(result.MissingPropertiesReport,
                    "Part Numbering - Missing Properties",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            // Success dialog
            if (result.DebugMode)
            {
                if (!string.IsNullOrWhiteSpace(result.LogPath))
                    MessageBox.Show("RMAC Part numbering complete.\n\nLog:\n" + result.LogPath);
                else
                    MessageBox.Show("RMAC Part numbering complete.");
            }
            else
            {
                MessageBox.Show("Process Complete", "RMAC Part Numbering");
            }
        }

        /// <summary>
        /// Pure renumber pipeline. No dialogs — caller supplies a pre-validated assembly document
        /// and optionally a progress form; receives a <see cref="RenumberResult"/> describing the
        /// outcome. Still manages Inventor state (SilentOperation, UserInterfaceManager,
        /// SuppressDockUpdates) since these are intrinsic to the pipeline.
        /// </summary>
        public static RenumberResult RunCore(
            Inventor.Application app,
            AssemblyDocument asmDoc,
            RenumberProgressForm? progressForm = null)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (asmDoc == null) throw new ArgumentNullException(nameof(asmDoc));

            var result = new RenumberResult();

            AddinSettings.EnsureLoaded();
            bool debug = AddinSettings.Current.Debug;
            result.DebugMode = debug;

            // Choose scheme (must exist in RMAC_Efficiency_Addin.PartNumbering)
            IPartNumberingScheme scheme = AddinSettings.Current.PartNumberingMode switch
            {
                PartNumberingMode.Sequential => new SequentialNumberingScheme(),
                PartNumberingMode.FramesOnly => new FramesOnlyNumberingScheme(),
                _ => new StructuredNumberingScheme(),
            };

            bool prevSilent = false;
            bool prevUI = false;
            bool uiSupported = false;

            RenumberingContext? ctx = null;

            try
            {
                try { asmDoc.Activate(); } catch { }

                // Capture current settings
                try { prevSilent = app.SilentOperation; } catch { }
                try
                {
                    prevUI = app.UserInterfaceManager.UserInteractionDisabled;
                    uiSupported = true;
                }
                catch
                {
                    uiSupported = false;
                }

                // Enable silent mode
                try { app.SilentOperation = true; } catch { }
                if (uiSupported)
                {
                    try { app.UserInterfaceManager.UserInteractionDisabled = true; } catch { }
                }

                // Resolve BOM view and BOM rows BEFORE creating context (ctx requires bomView)
                dynamic bomView = GetPreferredBomView(asmDoc, out string chosenViewName);

                object? bomRowsObj = TryGet(bomView, "BOMRows");
                if (bomRowsObj == null) throw new InvalidOperationException("Unable to access BOMRows.");
                dynamic bomRows = bomRowsObj;

                // Create context (now that we have bomView) - IMPORTANT: assign to the outer ctx, do NOT shadow.
                ctx = new RenumberingContext(app, asmDoc, bomView, debug);
                ctx.LogInit(); // only writes to file when Debug is enabled

                // ----------------------------
                // TEMPLATE WIRING
                // ----------------------------
                // Build effective template from Prefix + <PartNumber> + Suffix (preferred UI path)
                // Fall back to legacy PartNumberTemplate if Prefix/Suffix are blank.
                string legacy = AddinSettings.Current.PartNumberTemplate ?? "<PartNumber>";
                string prefix = AddinSettings.Current.PartNumberPrefixTemplate ?? "";
                string suffix = AddinSettings.Current.PartNumberSuffixTemplate ?? "";

                static string NormalizeTemplate(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                    s = s.Trim();

                    for (int i = 0; i < 4; i++)
                    {
                        string before = s;
                        s = s.Replace("<<", "<").Replace(">>", ">");
                        if (s == before) break;
                    }

                    return s;
                }

                legacy = NormalizeTemplate(legacy);
                prefix = NormalizeTemplate(prefix);
                suffix = NormalizeTemplate(suffix);

                string effectiveTemplate =
                    (!string.IsNullOrWhiteSpace(prefix) || !string.IsNullOrWhiteSpace(suffix))
                        ? $"{prefix}<PartNumber>{suffix}"
                        : legacy;

                effectiveTemplate = NormalizeTemplate(effectiveTemplate);

                // Convert any "friendly" strings into internal tokens before schemes run
                ctx.PartNumberTemplate = ConvertFriendlyIPropStringsToTokens(effectiveTemplate);
                ctx.PartNumberTemplate = ctx.PartNumberTemplate
                    .Replace("<<", "<")
                    .Replace(">>", ">");

                if (debug) ctx.Info($"Template tokens (normalized): {ctx.PartNumberTemplate}");

                ctx.PartNumberTemplateUseTopLevelAssemblyProperties =
                    AddinSettings.Current.PartNumberTemplateUseTopLevelAssemblyProperties;

                ctx.KeepTopLevelAssemblyPartNumberFixed =
                    AddinSettings.Current.KeepTopLevelAssemblyPartNumberFixed;

                // ----------------------------
                // SKIP iPROPERTY WIRING
                // ----------------------------
                ctx.SkipRenumberEnabled = AddinSettings.Current.SkipRenumberEnabled;
                ctx.SkipRenumberMatchValue = AddinSettings.Current.SkipRenumberMatchValue ?? string.Empty;

                var selection = AddinSettings.Current.SkipRenumberPropertySelection ?? string.Empty;

                if (string.Equals(selection, "Custom…", StringComparison.Ordinal))
                {
                    ctx.SkipRenumberPropertySet = "User Defined Properties";
                    ctx.SkipRenumberPropertyName = AddinSettings.Current.SkipRenumberCustomPropertyName ?? string.Empty;
                }
                else
                {
                    string setName, propName;
                    MapSkipSelection(selection, out setName, out propName);
                    ctx.SkipRenumberPropertySet = setName;
                    ctx.SkipRenumberPropertyName = propName;
                }

                if (debug && ctx.SkipRenumberEnabled)
                {
                    ctx.Info($"SkipRenumber enabled: {ctx.SkipRenumberPropertySet}\\{ctx.SkipRenumberPropertyName} == \"{ctx.SkipRenumberMatchValue}\"");
                }

                // If the top-level assembly matches the skip rule, abort the entire renumber run.
                string skipReason;
                if (ctx.ShouldSkip(asmDoc, out skipReason))
                {
                    ctx.Warn($"SKIP SUBTREE (top assembly): {ctx.SafeName(asmDoc)} | {skipReason}");
                    result.Skipped = true;
                    result.SkipReason = skipReason;
                    result.LogPath = ctx.LogPath;
                    return result;
                }

                // --- Progress dialog wiring (form is created by the caller if at all) ---
                try { progressForm?.UpdateStatusText("Counting BOM rows\u2026"); } catch { }

                int totalRows = CountBomRowsRecursive(ctx, bomRows);
                try { progressForm?.SetTotalSteps(totalRows); } catch { }

                // Wire context -> progress form (null-safe)
                if (progressForm != null)
                {
                    ctx.OnProgress = (step, text) => progressForm.UpdateProgress(step, text);
                    ctx.OnStatusText = (text) => progressForm.UpdateStatusText(text);
                }

                // Suppress dock panel visibility updates during ScreenUpdating toggle
                Exporting.GlobalExporter.SuppressDockUpdates = true;

                RunFast(app, asmDoc, $"RMAC Part Numbering ({scheme.Name})", () =>
                {
                    ctx.Info("Start RMAC Part Numbering Protocol");
                    ctx.Info($"Scheme: {scheme.Name}");
                    ctx.Info($"Target assembly: {ctx.SafeName(asmDoc)}");
                    ctx.Info($"BOM View selected: {chosenViewName}");

                    // Best-effort preload
                    ctx.UpdateStatusText("Preloading referenced documents\u2026");
                    ctx.Info("Preloading referenced documents (best effort)...");
                    PreloadAllReferencedDocs(app, asmDoc);

                    // Top-level assembly PN behavior:
                    // - If KeepTopLevelAssemblyPartNumberFixed = true, do NOT override it.
                    // - Otherwise set to A-01 (your global behavior).
                    if (!ctx.KeepTopLevelAssemblyPartNumberFixed)
                    {
                        string oldTopPn = ctx.GetIPropString(asmDoc, "Design Tracking Properties", "Part Number") ?? "<no PN>";
                        ctx.SetIProp(asmDoc, "Design Tracking Properties", "Part Number", "A-01");
                        ctx.Info($"TOP PN: {oldTopPn} -> A-01");
                    }
                    else
                    {
                        string existing = ctx.GetIPropString(asmDoc, "Design Tracking Properties", "Part Number") ?? "<no PN>";
                        ctx.Info($"TOP PN kept fixed: {existing}");
                    }

                    // Run selected scheme
                    scheme.Run(ctx, bomRows);

                    ctx.Info("RMAC Part numbering complete");
                });

                // Capture missing template tokens report (if any) into result; caller decides how to present it
                try
                {
                    var report = ctx.BuildMissingPropertiesReport();
                    if (!string.IsNullOrWhiteSpace(report))
                        result.MissingPropertiesReport = report;
                }
                catch { }

                // Refresh drawings after transaction (best effort)
                try { progressForm?.UpdateStatusText("Refreshing drawings\u2026"); } catch { }
                try { RefreshOpenDrawingsReferencingAssembly(app, asmDoc, debug, ctx); } catch { }

                result.LogPath = ctx.LogPath;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                if (ctx != null && result.LogPath == null)
                    result.LogPath = ctx.LogPath;
            }
            finally
            {
                // Ensure renumber log is closed/flushed (no-op if Debug is off)
                try { ctx?.LogClose(); } catch { }

                // Restore silent settings
                try { app.SilentOperation = prevSilent; } catch { }
                if (uiSupported)
                {
                    try { app.UserInterfaceManager.UserInteractionDisabled = prevUI; } catch { }
                }

                // Clear suppression AFTER ScreenUpdating is restored so any
                // queued OnActivateDocument events fired during restoration are still suppressed.
                Exporting.GlobalExporter.SuppressDockUpdates = false;
            }

            return result;
        }

        // -----------------------------
        // BOM view helper
        // -----------------------------
        private static dynamic GetPreferredBomView(AssemblyDocument asmDoc, out string chosenViewName)
        {
            chosenViewName = "<unknown>";

            dynamic dAsm = asmDoc;

            object? compDef0Obj = TryGet(dAsm, "ComponentDefinition");
            if (compDef0Obj == null) throw new InvalidOperationException("Unable to access ComponentDefinition.");

            object? bomObj = TryGet(compDef0Obj, "BOM");
            if (bomObj == null) throw new InvalidOperationException("Unable to access BOM.");

            dynamic bom = bomObj;

            object? bomViewsObj = TryGet(bom, "BOMViews");
            if (bomViewsObj == null) throw new InvalidOperationException("Unable to access BOMViews.");
            dynamic bomViews = bomViewsObj;

            dynamic bomView = null!;
            try
            {
                bomView = bomViews["Model Data"];
                try { chosenViewName = (string)bomView.Name; } catch { chosenViewName = "Model Data"; }
            }
            catch
            {
                bomView = ComItem(bomViews, 1);
                try { chosenViewName = (string)bomView.Name; } catch { chosenViewName = "Item(1)"; }
            }

            if (bomView == null) throw new InvalidOperationException("Unable to access BOM view.");
            return bomView;
        }

        // -----------------------------
        // Transaction wrapper
        // -----------------------------
        private static void RunFast(Inventor.Application app, object doc, string name, Action action)
        {
            bool oldSU = app.ScreenUpdating;
            Transaction? tx = null;

            try
            {
                app.ScreenUpdating = false;
                tx = app.TransactionManager.StartTransaction((dynamic)doc, name);

                action();

                tx.End();
            }
            catch
            {
                try { tx?.Abort(); } catch { }
                throw;
            }
            finally
            {
                app.ScreenUpdating = oldSU;
                try { app.ActiveView.Update(); } catch { }
            }
        }

        // -----------------------------
        // BOM row counter (for progress bar)
        // -----------------------------
        private static int CountBomRowsRecursive(RenumberingContext ctx, dynamic bomRows)
        {
            int count = 0;
            int rowCount = ctx.GetCount(bomRows);

            for (int i = 1; i <= rowCount; i++)
            {
                count++;

                dynamic row = ctx.ComItem(bomRows, i);
                if (row == null) continue;

                object? childRowsObj = ctx.TryGet(row, "ChildRows");
                if (childRowsObj != null)
                    count += CountBomRowsRecursive(ctx, childRowsObj);
            }

            return count;
        }

        // -----------------------------
        // Preload referenced docs (best effort)
        // -----------------------------
        private static void PreloadAllReferencedDocs(Inventor.Application app, AssemblyDocument asmDoc)
        {
            try
            {
                foreach (Document d in asmDoc.AllReferencedDocuments)
                    _ = d;
            }
            catch { }

            try
            {
                dynamic docs = asmDoc.AllReferencedDocuments;
                int n = 0;
                try { n = (int)docs.Count; } catch { n = 0; }

                for (int i = 1; i <= n; i++)
                {
                    dynamic d = ComItem(docs, i);
                    if (d == null) continue;

                    string path = "";
                    try { path = (string)d.FullFileName; } catch { path = ""; }
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    bool alreadyOpen = false;
                    try
                    {
                        foreach (Document od in app.Documents)
                        {
                            try
                            {
                                if (string.Equals(od.FullFileName, path, StringComparison.OrdinalIgnoreCase))
                                {
                                    alreadyOpen = true;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    if (!alreadyOpen)
                    {
                        try { app.Documents.Open(path, false); } catch { }
                    }
                }
            }
            catch { }
        }

        // -----------------------------
        // Drawing refresh (best effort)
        // -----------------------------
        private static void RefreshOpenDrawingsReferencingAssembly(Inventor.Application app, AssemblyDocument asmDoc, bool debug, RenumberingContext ctx)
        {
            string asmPath = "";
            try { asmPath = asmDoc.FullFileName ?? ""; } catch { }

            foreach (Document d in app.Documents)
            {
                if (d is not DrawingDocument dd) continue;

                if (!DrawingReferencesAssembly(dd, asmPath))
                    continue;

                if (debug) ctx.Info($"DRAWING REFRESH: Updating drawing '{ctx.SafeName(dd)}'");

                try { dd.Update(); } catch { }
                try { dd.Update2(true); } catch { }

                try
                {
                    foreach (Sheet sh in dd.Sheets)
                    {
                        foreach (PartsList pl in sh.PartsLists)
                        {
                            try { pl.Update(); } catch { }
                        }
                    }
                }
                catch { }

                try { app.ActiveView.Update(); } catch { }
            }
        }

        private static bool DrawingReferencesAssembly(DrawingDocument dd, string asmFullFileName)
        {
            if (string.IsNullOrWhiteSpace(asmFullFileName)) return false;

            try
            {
                foreach (Sheet sh in dd.Sheets)
                {
                    foreach (DrawingView v in sh.DrawingViews)
                    {
                        try
                        {
                            var rd = v.ReferencedDocumentDescriptor?.ReferencedDocument;
                            if (rd is AssemblyDocument ad)
                            {
                                string p = "";
                                try { p = ad.FullFileName ?? ""; } catch { }
                                if (string.Equals(p, asmFullFileName, StringComparison.OrdinalIgnoreCase))
                                    return true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return false;
        }

        // -----------------------------
        // Friendly iProp string -> internal token conversion
        // -----------------------------
        private static string ConvertFriendlyIPropStringsToTokens(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            // Normalize spacing around dots (user input can be "Project. Checked Date")
            string s = Regex.Replace(input, @"\b(Project|Summary Information)\s*\.\s*", "$1.", RegexOptions.IgnoreCase);

            // Project.* -> Design Tracking Properties
            s = Regex.Replace(
                s,
                @"\bProject\.(?<name>[A-Za-z0-9 \-_]+)\b",
                m => $"<iProp:Design Tracking Properties:{m.Groups["name"].Value.Trim()}>",
                RegexOptions.IgnoreCase);

            // Summary Information.* -> Summary Information
            s = Regex.Replace(
                s,
                @"\bSummary Information\.(?<name>[A-Za-z0-9 \-_]+)\b",
                m => $"<iProp:Summary Information:{m.Groups["name"].Value.Trim()}>",
                RegexOptions.IgnoreCase);

            // Allow explicit Design Tracking Properties.* if someone types it
            s = Regex.Replace(
                s,
                @"\bDesign Tracking Properties\.(?<name>[A-Za-z0-9 \-_]+)\b",
                m => $"<iProp:Design Tracking Properties:{m.Groups["name"].Value.Trim()}>",
                RegexOptions.IgnoreCase);

            return s;
        }

        // -----------------------------
        // COM / reflection helpers
        // -----------------------------
        private static dynamic ComItem(dynamic collection, object index)
        {
            try { return collection[index]; } catch { }
            try { return collection.Item(index); } catch { }
            return null!;
        }

        private static object? TryGet(dynamic obj, string prop)
        {
            try
            {
                return ((object)obj).GetType().InvokeMember(
                    prop,
                    BindingFlags.GetProperty,
                    null,
                    (object)obj,
                    null
                );
            }
            catch
            {
                try { return obj[prop]; }
                catch { return null; }
            }
        }

        internal static void MapSkipSelection(string selection, out string setName, out string propName)
        {
            setName = string.Empty;
            propName = string.Empty;

            if (string.IsNullOrWhiteSpace(selection))
                return;

            int dot = selection.IndexOf('.');
            if (dot <= 0 || dot >= selection.Length - 1)
            {
                // Treat as property name with "search all sets"
                propName = selection;
                return;
            }

            setName = selection.Substring(0, dot).Trim();
            propName = selection.Substring(dot + 1).Trim();

            // IMPORTANT: match the same semantics as ConvertFriendlyIPropStringsToTokens()
            // "Project.*" is really "Design Tracking Properties:*"
            if (string.Equals(setName, "Project", StringComparison.OrdinalIgnoreCase))
                setName = "Design Tracking Properties";

            // Allow "Summary Information.*" as-is (it is a real property set)
            if (string.Equals(setName, "Summary Information", StringComparison.OrdinalIgnoreCase))
                setName = "Summary Information";

            // "Document Summary Information" → Inventor COM expects "Inventor Document Summary Information"
            if (string.Equals(setName, "Document Summary Information", StringComparison.OrdinalIgnoreCase))
                setName = "Inventor Document Summary Information";

            // Allow explicit "Design Tracking Properties.*" as-is
            if (string.Equals(setName, "Design Tracking Properties", StringComparison.OrdinalIgnoreCase))
                setName = "Design Tracking Properties";
        }

    }
}
