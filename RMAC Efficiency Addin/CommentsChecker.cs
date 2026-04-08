using Inventor;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin
{
    internal static class CommentsChecker
    {
        /// <summary>
        /// Legacy entry point: runs against the active document (must be an Assembly).
        /// </summary>
        public static void Run(Inventor.Application app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (app.ActiveDocument == null) throw new InvalidOperationException("No active document.");

            if (app.ActiveDocument.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject)
                throw new InvalidOperationException("Open an assembly document before running Check Comments.");

            Run(app, (AssemblyDocument)app.ActiveDocument);
        }

        /// <summary>
        /// Public entry point. Runs the checker against a provided assembly and presents result
        /// dialogs on completion. Production callers (ribbon, dock pane) use this overload.
        /// </summary>
        public static void Run(Inventor.Application app, AssemblyDocument topAssembly)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (topAssembly == null) throw new ArgumentNullException(nameof(topAssembly));

            var originalDoc = app.ActiveDocument; // for restoring focus afterwards

            var result = RunCore(app, topAssembly);

            // Return focus to original doc before any modal
            try { originalDoc?.Activate(); } catch { }

            // Present result
            if (result.Errors.Count > 0)
            {
                MessageBox.Show(result.Errors[0], "RMAC Comment Checker");
                return;
            }

            MessageBox.Show(
                $"Completed.\n\nUpdated: {result.UpdatedCount}\nSkipped: {result.SkippedCount}",
                "RMAC Comment Checker"
            );
        }

        /// <summary>
        /// Pure pipeline. Walks the BOM and applies comment checks. Returns a result object
        /// describing what happened. No dialogs are shown from this method directly — but the
        /// inner <c>PromptAndSet*</c> helpers will still open modal pickers if a row has an
        /// invalid comment. Harness tests should use fixtures where every row already has a
        /// valid comment so no prompts fire.
        /// </summary>
        public static CheckCommentsResult RunCore(Inventor.Application app, AssemblyDocument topAssembly)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (topAssembly == null) throw new ArgumentNullException(nameof(topAssembly));

            var result = new CheckCommentsResult();

            // Unified config: read from AddinSettings (single global settings.json)
            AddinSettings.EnsureLoaded();
            var options = AddinSettings.Current.CommentsOptions;
            options.SanitizeInPlace();

            int updated = 0, skipped = 0;

            // Enable Structured BOM view if not already (mirrors renumberer behaviour).
            var bom = topAssembly.ComponentDefinition.BOM;
            try { bom.StructuredViewEnabled = true; } catch { }

            BOMView view;
            try { view = bom.BOMViews["Structured"]; }
            catch
            {
                try { view = bom.BOMViews[1]; }
                catch
                {
                    result.Errors.Add("Unable to access a BOM view.\n\nMake sure the assembly has been saved at least once.");
                    return result;
                }
            }

            if (view.BOMRows.Count == 0)
            {
                result.Errors.Add("The BOM view contains no rows.\n\nCheck that the assembly has components and the BOM Structured view is enabled.");
                return result;
            }

            CheckRowsRecursive(
                app,
                view.BOMRows,
                options,
                topAssemblyFullPath: SafeFullPath(topAssembly),
                ref updated,
                ref skipped
            );

            result.UpdatedCount = updated;
            result.SkippedCount = skipped;
            result.Success = true;
            return result;
        }

        private static void CheckRowsRecursive(
            Inventor.Application app,
            BOMRowsEnumerator rows,
            CommentsOptionsSettings options,
            string? topAssemblyFullPath,
            ref int updated,
            ref int skipped)
        {
            for (int i = 1; i <= rows.Count; i++)
            {
                var row = rows[i];

                if (row.ComponentDefinitions == null || row.ComponentDefinitions.Count < 1)
                {
                    skipped++;
                    continue;
                }

                var compDef = row.ComponentDefinitions[1];

                // In this interop, compDef.Document is an object. Cast it.
                if (compDef.Document is not Inventor.Document doc)
                {
                    skipped++;
                    continue;
                }

                // Skip Skeleton docs
                if (doc.DocumentInterests != null && doc.DocumentInterests.HasInterest("SkeletonDoc"))
                {
                    skipped++;
                    continue;
                }

                // PART DOCUMENTS
                if (doc.IsModifiable &&
                    doc.DocumentType == DocumentTypeEnum.kPartDocumentObject)
                {
                    if (!IsValidPartComment(doc, options))
                    {
                        if (PromptAndSetPartComment(app, doc, options, topAssemblyFullPath))
                            updated++;
                        else
                            skipped++;
                    }
                }

                // INSEPARABLE ASSEMBLIES -> treat like parts (your VB behaviour)
                if (doc.IsModifiable &&
                    doc.DocumentType == DocumentTypeEnum.kAssemblyDocumentObject &&
                    compDef.BOMStructure == BOMStructureEnum.kInseparableBOMStructure)
                {
                    if (!IsValidPartComment(doc, options))
                    {
                        if (PromptAndSetPartComment(app, doc, options, topAssemblyFullPath))
                            updated++;
                        else
                            skipped++;
                    }
                }

                // NORMAL ASSEMBLIES
                if (doc.IsModifiable &&
                    doc.DocumentType == DocumentTypeEnum.kAssemblyDocumentObject &&
                    compDef.BOMStructure != BOMStructureEnum.kInseparableBOMStructure &&
                    !(doc.DocumentInterests != null && doc.DocumentInterests.HasInterest("FrameDoc")))
                {
                    if (!IsValidAssemblyComment(doc, options))
                    {
                        if (PromptAndSetAssemblyComment(app, compDef, doc, options, topAssemblyFullPath))
                            updated++;
                        else
                            skipped++;
                    }
                }

                // Recurse children if not inseparable
                if (row.ChildRows != null && compDef.BOMStructure != BOMStructureEnum.kInseparableBOMStructure)
                {
                    CheckRowsRecursive(app, row.ChildRows, options, topAssemblyFullPath, ref updated, ref skipped);
                }
            }
        }

        private static bool PromptAndSetPartComment(
            Inventor.Application app,
            Document targetDoc,
            CommentsOptionsSettings options,
            string? topAssemblyFullPath)
        {
            var opened = OpenOrActivate(app, SafeFullPath(targetDoc));

            FitView(app);

            var choice = OptionPickerForm.Pick("Select Part Category", options.Part);
            if (choice == null)
            {
                // user cancelled
                CloseIfNotTopLevel(opened, topAssemblyFullPath);
                return false;
            }

            SetComments(opened, choice);
            try { opened.Save(); } catch { }

            CloseIfNotTopLevel(opened, topAssemblyFullPath);
            return true;
        }

        private static bool PromptAndSetAssemblyComment(
            Inventor.Application app,
            ComponentDefinition compDef,
            Document targetDoc,
            CommentsOptionsSettings options,
            string? topAssemblyFullPath)
        {
            var opened = OpenOrActivate(app, SafeFullPath(targetDoc));

            FitView(app);

            var assyChoice = OptionPickerForm.Pick("Select Assembly Category", options.Assembly);
            if (assyChoice == null)
            {
                CloseIfNotTopLevel(opened, topAssemblyFullPath);
                return false;
            }

            // Special action: INSEPERABLE means convert to inseparable and then choose a Part category
            if (assyChoice.Equals("INSEPERABLE", StringComparison.OrdinalIgnoreCase))
            {
                try { compDef.BOMStructure = BOMStructureEnum.kInseparableBOMStructure; } catch { }

                var partChoice = OptionPickerForm.Pick("Select Part Category (Inseparable Assembly)", options.Part);
                if (partChoice == null)
                {
                    CloseIfNotTopLevel(opened, topAssemblyFullPath);
                    return false;
                }

                SetComments(opened, partChoice);
                try { opened.Save(); } catch { }

                CloseIfNotTopLevel(opened, topAssemblyFullPath);
                return true;
            }

            // Standard: set comment to selected assembly category
            SetComments(opened, assyChoice);

            // If selected category implies inseparable, set BOMStructure accordingly
            if (options.TreatAsInseparableWhenAssemblyIs.Any(x => x.Equals(assyChoice, StringComparison.OrdinalIgnoreCase)))
            {
                try { compDef.BOMStructure = BOMStructureEnum.kInseparableBOMStructure; } catch { }
            }

            try { opened.Save(); } catch { }

            CloseIfNotTopLevel(opened, topAssemblyFullPath);
            return true;
        }

        private static bool IsValidPartComment(Document doc, CommentsOptionsSettings options)
        {
            var c = GetComments(doc);
            return !string.IsNullOrWhiteSpace(c) &&
                   options.Part.Any(x => x.Equals(c, StringComparison.OrdinalIgnoreCase));
        }

        // Note: INSEPERABLE is treated as an action, not a valid stored comment.
        private static bool IsValidAssemblyComment(Document doc, CommentsOptionsSettings options)
        {
            var c = GetComments(doc);
            if (string.IsNullOrWhiteSpace(c)) return false;

            if (c.Equals("INSEPERABLE", StringComparison.OrdinalIgnoreCase))
                return false;

            return options.Assembly.Any(x =>
                x.Equals(c, StringComparison.OrdinalIgnoreCase) &&
                !x.Equals("INSEPERABLE", StringComparison.OrdinalIgnoreCase));
        }

        private static string? GetComments(Document doc)
        {
            try
            {
                var set = doc.PropertySets["Inventor Summary Information"];
                var prop = set["Comments"];
                return prop?.Value as string;
            }
            catch
            {
                return null;
            }
        }

        private static void SetComments(Document doc, string value)
        {
            try
            {
                var set = doc.PropertySets["Inventor Summary Information"];
                var prop = set["Comments"];
                prop.Value = value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RMAC] SetComments failed: {ex.Message}");
            }
        }

        private static Document OpenOrActivate(Inventor.Application app, string? fullFileName)
        {
            if (string.IsNullOrWhiteSpace(fullFileName))
                throw new InvalidOperationException("Target document has no file path.");

            // If already open, activate it
            foreach (Document d in app.Documents)
            {
                try
                {
                    if (string.Equals(d.FullFileName, fullFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        d.Activate();
                        return d;
                    }
                }
                catch { }
            }

            // Otherwise open visibly
            return app.Documents.Open(fullFileName, true);
        }

        private static void FitView(Inventor.Application app)
        {
            try { app.ActiveView?.Fit(); } catch { }
        }

        private static void CloseIfNotTopLevel(Document doc, string? topAssemblyFullPath)
        {
            try
            {
                // Never close the top-level assembly passed into Run(app, topAssembly)
                var path = SafeFullPath(doc);
                if (!string.IsNullOrWhiteSpace(topAssemblyFullPath) &&
                    !string.IsNullOrWhiteSpace(path) &&
                    string.Equals(path, topAssemblyFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // If it's not the top-level, we can close it after saving.
                doc.Close(true);
            }
            catch { }
        }

        private static string? SafeFullPath(object? docOrAsm)
        {
            try
            {
                return docOrAsm switch
                {
                    Document d => d.FullFileName,
                    AssemblyDocument a => a.FullFileName,
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class OptionPickerForm : KryptonForm
    {
        private readonly KryptonListBox _list = new() { Dock = DockStyle.Fill };
        private readonly KryptonButton _ok = new() { Text = "OK", Width = 100 };
        private readonly KryptonButton _cancel = new() { Text = "Cancel", Width = 100 };

        private OptionPickerForm(string title, string[] options)
        {
            Text = title;
            Width = 420;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            _list.Items.AddRange(options.Cast<object>().ToArray());
            _list.DoubleClick += (_, __) => Accept();

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                FlowDirection = FlowDirection.RightToLeft
            };
            bottom.Controls.Add(_ok);
            bottom.Controls.Add(_cancel);

            _ok.Click += (_, __) => Accept();
            _cancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_list);
            Controls.Add(bottom);
        }

        public static string? Pick(string title, System.Collections.Generic.IEnumerable<string> options)
        {
            var arr = options
                .Select(x => (x ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            using var f = new OptionPickerForm(title, arr);
            return f.ShowDialog() == DialogResult.OK ? f._list.SelectedItem?.ToString() : null;
        }

        private void Accept()
        {
            if (_list.SelectedItem == null)
            {
                MessageBox.Show("Select an option first.");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
