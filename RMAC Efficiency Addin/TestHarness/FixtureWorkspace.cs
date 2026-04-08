#if TEST_HARNESS
using System;
using System.Collections.Generic;
using Inventor;
using IODir = System.IO.Directory;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using SysEnv = System.Environment;

namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Manages the test fixture project: copies the source dataset to a working location
    /// in %TEMP%, opens files from the working copy, closes them without saving, and
    /// deletes the working copy on teardown. The source project is never touched by tests.
    ///
    /// One workspace per harness run, set up before any IFixtureTestCase runs and torn
    /// down after.
    /// </summary>
    internal static class FixtureWorkspace
    {
        // -------------------------------------------------------------------------
        // CONFIGURATION
        // -------------------------------------------------------------------------
        // Source path is hardcoded — Roy confirmed it won't change. If a future dev
        // needs a different path, change it here.
        private const string SourceProjectDir =
            @"D:\RMAC Design Projects\RM240 Titan Feeder Trailer - For Testing";

        // Original .ipj inside the source folder (renamed in the working copy to avoid
        // colliding with whatever is in the user's existing Inventor project list).
        private const string OriginalProjectFileName =
            "RM240 Titan Feeder Trailer - For Testing.ipj";

        // Unique name for the .ipj inside the working copy. Anything will do as long as
        // it's not the same as the original.
        private const string WorkingProjectFileName = "FabFlow_TestRun.ipj";

        // -------------------------------------------------------------------------

        private static string WorkingRoot =>
            IOPath.Combine(IOPath.GetTempPath(), "FabFlow_TestRun");

        // Mirror the source folder name inside WorkingRoot so the working copy looks
        // like a normal Inventor project on disk (recognisable when you go look at it).
        private static string SourceFolderName =>
            IOPath.GetFileName(SourceProjectDir.TrimEnd(
                IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar));

        private static string WorkingProjectDir =>
            IOPath.Combine(WorkingRoot, SourceFolderName);

        /// <summary>Path to the (renamed) .ipj inside the working copy. Only valid after Setup().</summary>
        public static string WorkingProjectFile =>
            IOPath.Combine(WorkingProjectDir, WorkingProjectFileName);

        /// <summary>True if the source dataset exists on this machine. If false, fixture tests will be skipped.</summary>
        public static bool IsAvailable => IODir.Exists(SourceProjectDir);

        /// <summary>True if Setup() has completed successfully and Teardown() has not yet run.</summary>
        public static bool IsSetup { get; private set; }

        // Track every doc opened via OpenFile (or registered via RegisterCreatedDoc) so
        // Teardown can close them all. List<object> because Inventor's PartDocument /
        // AssemblyDocument / DrawingDocument / Document are sibling COM interfaces in C#'s
        // eyes — there's no common base type they all implicitly convert to. We cast to
        // _Document at close time, which always succeeds via COM QueryInterface.
        private static readonly List<object> _openedDocs = new();

        // State to restore on Teardown
        private static DesignProject? _previousProject;
        private static bool _previousSilentOperation;
        private static bool _hadPreviousSilentOperation;

        /// <summary>
        /// Copies the source project to the working location, activates it as the current
        /// Inventor design project (so referenced files resolve against the working copy
        /// rather than whatever project happened to be active before), and sets
        /// SilentOperation = true to suppress modal dialogs. Throws on failure.
        /// </summary>
        public static void Setup(Application app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (!IsAvailable)
                throw new InvalidOperationException(
                    $"Test project source not found: {SourceProjectDir}");

            // ---- Copy ----
            HardDeleteWorkingRoot();
            IODir.CreateDirectory(WorkingProjectDir);
            CopyDirectoryRecursive(SourceProjectDir, WorkingProjectDir);

            // ---- Rename the .ipj to a unique name so it can never collide with the
            //      user's existing project list entry for the original ----
            var copiedOriginalIpj = IOPath.Combine(WorkingProjectDir, OriginalProjectFileName);
            if (!IOFile.Exists(copiedOriginalIpj))
                throw new InvalidOperationException(
                    $"After copy, original .ipj is missing: {copiedOriginalIpj}");

            // If a stale renamed file is somehow already there, delete it first
            if (IOFile.Exists(WorkingProjectFile))
            {
                try { IOFile.Delete(WorkingProjectFile); } catch { }
            }

            IOFile.Move(copiedOriginalIpj, WorkingProjectFile);

            if (!IOFile.Exists(WorkingProjectFile))
                throw new InvalidOperationException(
                    $"After rename, working project file is missing: {WorkingProjectFile}");

            // ---- Save SilentOperation, then enable ----
            try
            {
                _previousSilentOperation = app.SilentOperation;
                _hadPreviousSilentOperation = true;
            }
            catch
            {
                _hadPreviousSilentOperation = false;
            }
            try { app.SilentOperation = true; } catch { }

            // ---- Save current active project, then activate the working copy's .ipj ----
            try { _previousProject = app.DesignProjectManager.ActiveDesignProject; }
            catch { _previousProject = null; }

            try
            {
                var dpm = app.DesignProjectManager;

                // If a project entry with this exact file path is already known to Inventor
                // (e.g. from a previous run), reuse it; otherwise add a new entry.
                DesignProject? testProj = null;
                try
                {
                    foreach (DesignProject p in dpm.DesignProjects)
                    {
                        string pPath = "";
                        try { pPath = p.FullFileName ?? ""; } catch { }
                        if (string.Equals(pPath, WorkingProjectFile, StringComparison.OrdinalIgnoreCase))
                        {
                            testProj = p;
                            break;
                        }
                    }
                }
                catch { }

                if (testProj == null)
                    testProj = dpm.DesignProjects.AddExisting(WorkingProjectFile);

                testProj.Activate();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to activate test project at {WorkingProjectFile}: {ex.Message}", ex);
            }

            IsSetup = true;
        }

        /// <summary>
        /// Maximum seconds an <c>OpenFile</c> call is expected to take. Anything longer
        /// almost certainly means Inventor is blocked on a modal dialog (migration warning,
        /// reference resolution, etc.) that the harness can't see — the open is timed and
        /// the test fails if this threshold is exceeded.
        /// </summary>
        private const double OpenTimeoutSeconds = 20.0;

        /// <summary>
        /// Opens a file from the working copy. <paramref name="relativePath"/> is relative
        /// to the working project root (e.g. "3D CAD\\P-01.ipt"). The opened document is
        /// tracked and will be closed (without saving) on Teardown.
        ///
        /// Strict validation:
        /// 1. The open must complete within <see cref="OpenTimeoutSeconds"/>. A slow open
        ///    is strong evidence that Inventor blocked on a modal dialog the harness can't see.
        /// 2. The returned document's <c>FullFileName</c> must be inside the working copy.
        ///    If Inventor resolved the file to somewhere else (e.g. the original source or
        ///    another project with a matching filename), the test fails.
        /// 3. All referenced documents must also resolve to files inside the working copy.
        ///    Catches the "reference jumped back to the source project" failure mode.
        /// </summary>
        public static Document OpenFile(Application app, string relativePath, bool visible = false)
        {
            // Hidden by default for speed. Pass visible: true when a test contains active
            // VisualConfirm.Step() checkpoints so the user can actually see the viewport.
            bool openVisible = visible;

            if (!IsSetup)
                throw new InvalidOperationException("FixtureWorkspace.Setup() must be called first.");

            var fullPath = IOPath.Combine(WorkingProjectDir, relativePath);
            if (!IOFile.Exists(fullPath))
                throw new System.IO.FileNotFoundException($"Fixture file not found: {fullPath}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = app.Documents.Open(fullPath, openVisible);
            sw.Stop();

            if (sw.Elapsed.TotalSeconds > OpenTimeoutSeconds)
            {
                // Don't track it — we're about to fail. Best effort close.
                try { doc?.Close(true); } catch { }
                throw new InvalidOperationException(
                    $"Opening '{relativePath}' took {sw.Elapsed.TotalSeconds:F1}s " +
                    $"(threshold {OpenTimeoutSeconds}s). Inventor likely blocked on a modal dialog " +
                    "that SilentOperation did not suppress.");
            }

            if (doc == null)
                throw new InvalidOperationException($"Documents.Open returned null for '{relativePath}'.");

            // Track the doc first so Teardown will close it even if validation throws below.
            _openedDocs.Add(doc);

            // Strict path check: doc's full path must be inside the working copy
            string actual = "";
            try { actual = doc.FullFileName ?? ""; } catch { }

            if (!IsInsideWorkingCopy(actual))
            {
                throw new InvalidOperationException(
                    $"Opened document resolved to '{actual}', which is outside the working copy " +
                    $"'{WorkingProjectDir}'. The test project is probably not active, or the file " +
                    "was found via a different project's search paths.");
            }

            // Reference check: every referenced document descriptor must also resolve within
            // the working copy. Catches the "reference jumped to the source project" failure.
            ValidateReferencedDocuments(doc, relativePath);

            // When opened visibly, fit the view so the user can immediately see the geometry.
            // No effect when invisible.
            if (openVisible)
            {
                try { doc.Activate(); } catch { }
                try { app.ActiveView?.Fit(); } catch { }
                try { app.ActiveView?.Update(); } catch { }
            }

            return doc;
        }

        private static void ValidateReferencedDocuments(Document doc, string relativePath)
        {
            ReferencedFileDescriptors? refDescriptors = null;
            try { refDescriptors = doc.ReferencedFileDescriptors; } catch { refDescriptors = null; }

            if (refDescriptors == null) return;

            int count = 0;
            try { count = refDescriptors.Count; } catch { return; }

            for (int i = 1; i <= count; i++)
            {
                ReferencedFileDescriptor? rd = null;
                try { rd = refDescriptors[i]; } catch { continue; }
                if (rd == null) continue;

                string resolvedPath = "";
                try { resolvedPath = rd.FullFileName ?? ""; } catch { resolvedPath = ""; }

                if (string.IsNullOrEmpty(resolvedPath)) continue; // unresolved, skip

                if (!IsInsideWorkingCopy(resolvedPath))
                {
                    throw new InvalidOperationException(
                        $"Document '{relativePath}' has a reference that resolved to " +
                        $"'{resolvedPath}', outside the working copy '{WorkingProjectDir}'. " +
                        "This means Inventor's project search paths are not pointing at the temp workspace.");
                }
            }
        }

        private static bool IsInsideWorkingCopy(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                var full = IOPath.GetFullPath(path);
                var workRoot = IOPath.GetFullPath(WorkingProjectDir);
                return full.StartsWith(workRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Register a document that was CREATED (not opened) during a test, so Teardown
        /// will close it without saving along with the rest. Use this for documents
        /// produced by <c>app.Documents.Add(...)</c> inside a test. Accepts any Inventor
        /// document type (PartDocument, AssemblyDocument, DrawingDocument, Document).
        /// </summary>
        public static void RegisterCreatedDoc(object doc)
        {
            if (doc == null) return;
            _openedDocs.Add(doc);
        }

        /// <summary>
        /// Closes every document opened via OpenFile (without saving), restores the
        /// previously-active design project and SilentOperation state, and deletes the
        /// working copy. Safe to call even if Setup wasn't called or failed partway.
        /// </summary>
        public static void Teardown(Application? app = null)
        {
            // Close opened docs first — Inventor needs the files free before we delete them.
            // Cast each to _Document; Close(true) discards changes without saving.
            foreach (var doc in _openedDocs)
            {
                try { ((_Document)doc).Close(true); } catch { /* best-effort */ }
            }
            _openedDocs.Clear();

            if (app != null)
            {
                // Restore previous active project
                try { _previousProject?.Activate(); } catch { }
                _previousProject = null;

                // Restore previous SilentOperation
                if (_hadPreviousSilentOperation)
                {
                    try { app.SilentOperation = _previousSilentOperation; } catch { }
                }
                _hadPreviousSilentOperation = false;
            }

            HardDeleteWorkingRoot();
            IsSetup = false;
        }

        // -------------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------------

        private static void HardDeleteWorkingRoot()
        {
            if (!IODir.Exists(WorkingRoot)) return;

            try
            {
                // Strip read-only attributes — Inventor sometimes writes them on lock files
                foreach (var f in IODir.GetFiles(WorkingRoot, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { IOFile.SetAttributes(f, System.IO.FileAttributes.Normal); } catch { }
                }

                IODir.Delete(WorkingRoot, recursive: true);
            }
            catch
            {
                // Best-effort. If deletion fails, the next Setup() will try again.
            }
        }

        private static void CopyDirectoryRecursive(string source, string target)
        {
            IODir.CreateDirectory(target);

            foreach (var file in IODir.GetFiles(source))
            {
                var dest = IOPath.Combine(target, IOPath.GetFileName(file));
                try
                {
                    IOFile.Copy(file, dest, overwrite: true);
                }
                catch
                {
                    // Best-effort: skip files that fail (locked, permission denied, etc.)
                    // The harness can still run if a non-essential file is missing.
                }
            }

            foreach (var subDir in IODir.GetDirectories(source))
            {
                var dest = IOPath.Combine(target, IOPath.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, dest);
            }
        }
    }
}
#endif
