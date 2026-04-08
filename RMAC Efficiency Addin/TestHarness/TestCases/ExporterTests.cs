#if TEST_HARNESS
using System;
using System.Linq;
using Inventor;
using RMAC_Efficiency_Addin.Exporting;
using IODir = System.IO.Directory;
using IOPath = System.IO.Path;
using IOSearch = System.IO.SearchOption;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Programmatic test for the exporter.
    //
    // Calls GlobalExporter.RunCore directly (the Phase 5 refactored core — takes the
    // export root as a parameter instead of popping the folder picker). Points it at
    // a fresh folder inside the workspace temp root so teardown cleans it up along
    // with everything else.
    //
    // Verification (minimal for now):
    //   1. ExportResult.Success == true
    //   2. ExportResult.Errors is empty
    //   3. At least one file was written to the output folder (proves the exporter
    //      actually ran to completion and wasn't a silent no-op)
    //
    // DELIBERATELY NOT VERIFIED (future enhancement):
    //   - Specific files (DXF/STEP/PDF/BOM for each expected part)
    //   - File counts per category
    //   - BOM column content
    //   - PDF merge completeness
    //
    // When the exporter grows more test requirements, add them here — the test has
    // the output folder path and a file listing ready to go.
    //
    // Marked IFixtureTestCase + IInteractiveTestCase because it's slow (30-60s against
    // a real assembly) — gated by TestMode.RunInteractiveTests so it skips during
    // quick-feedback runs.

    internal sealed class Exporter_RunCoreWorkflow : IFixtureTestCase, IInteractiveTestCase
    {
        public string Name => nameof(Exporter_RunCoreWorkflow);

        private const string FixturePath = @"Drawings\A-01 CONSTRUCTION DRAWINGS.idw";

        public void Run(Application app)
        {
            var doc = (DrawingDocument)FixtureWorkspace.OpenFile(app, FixturePath, visible: true);
            try { doc.Activate(); } catch { }

            // Resolve top assembly programmatically — no user pick
            var asm = ResolveAssemblyFromDrawing(doc);
            TestAssert.NotNull(asm);

            // Create a fresh output folder INSIDE the workspace temp root so teardown
            // cleans it up automatically with the rest of the fixture.
            string exportFolder = IOPath.Combine(
                IOPath.GetTempPath(),
                "FabFlow_TestRun",
                "Export_Output");

            // If a previous run left an export folder behind, delete it so we start clean
            try { if (IODir.Exists(exportFolder)) IODir.Delete(exportFolder, recursive: true); } catch { }
            IODir.CreateDirectory(exportFolder);

            InteractivePrompt.Toast(
                "Testing Exporter",
                $"Running GlobalExporter.RunCore against A-01 Construction Drawings.\n" +
                "This may take 30-60 seconds depending on assembly size.",
                5000);

            // Suppress the token-format warning dialog (Phase 5 flag) so a bad template
            // token can't block the test with a modal we'd have to dismiss.
            bool prevSuppress = GlobalExporter.SuppressUiDialogs;
            GlobalExporter.SuppressUiDialogs = true;

            ExportResult result;
            try
            {
                result = GlobalExporter.RunCore(
                    app,
                    doc,
                    asm,                  // selectedAssemblyObj — skip the ResolveTopAssembly inference path
                    exportFolder,         // exportRoot — instead of the folder picker
                    status: null,
                    progressForm: null);  // no progress dialog in test mode
            }
            finally
            {
                GlobalExporter.SuppressUiDialogs = prevSuppress;
            }

            // ----- Hard checks -----

            TestAssert.NotNull(result);

            TestAssert.True(result.Success,
                "Exporter should have succeeded. Errors: " +
                (result.Errors.Count == 0 ? "<none>" : string.Join("; ", result.Errors)));

            TestAssert.Equal(0, result.Errors.Count);

            // Minimum sanity check: the exporter should have written *something* to the
            // output folder. We don't verify individual files yet — that's a future
            // enhancement once we know what the expected file layout looks like for
            // your typical export. For now, "some files exist" is enough to catch a
            // silent no-op where the exporter claims success but nothing gets written.
            int fileCount = 0;
            try
            {
                fileCount = IODir.EnumerateFiles(exportFolder, "*", IOSearch.AllDirectories).Count();
            }
            catch { }

            TestAssert.True(fileCount > 0,
                $"Exporter reported success but wrote no files to '{exportFolder}'. " +
                $"Jobs total: {result.JobsTotal}, executed: {result.JobsExecuted}, " +
                $"warnings: {result.Warnings.Count}.");

            InteractivePrompt.Toast(
                "Exporter Test Complete",
                $"Jobs executed: {result.JobsExecuted}/{result.JobsTotal}. " +
                $"Files written: {fileCount}. Warnings: {result.Warnings.Count}.",
                5000);
        }

        private static AssemblyDocument? ResolveAssemblyFromDrawing(DrawingDocument dd)
        {
            try
            {
                foreach (Sheet sheet in dd.Sheets)
                {
                    foreach (DrawingView dv in sheet.DrawingViews)
                    {
                        try
                        {
                            var refDoc = dv.ReferencedDocumentDescriptor?.ReferencedDocument;
                            if (refDoc is AssemblyDocument asm) return asm;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
#endif
