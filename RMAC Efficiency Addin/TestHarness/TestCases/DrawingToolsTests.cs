#if TEST_HARNESS
using System;
using System.Runtime.InteropServices;
using Inventor;
using RMAC_Efficiency_Addin.DrawingDock;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Interactive test for the drawing dock command pipeline.
    //
    // Walks the user through the three commands they'd run in real life:
    //   1. LOAD ASSEMBLY  — pick a drawing view, resolve to its referenced assembly
    //                       via DrawingDockPane.TryGetAssemblyFromDrawingView (the same
    //                       static helper the production "Load Assembly" button calls)
    //   2. RENUMBER       — RmacPartNumberingRenumberer.RunCore against the loaded asm
    //   3. CHECK COMMENTS — CommentsChecker.RunCore against the same asm
    //
    // Verification is via the result objects (RenumberResult / CheckCommentsResult)
    // returned by the production RunCore methods. No human verification needed.
    //
    // Marked IFixtureTestCase + IInteractiveTestCase. Skipped when
    // TestMode.RunInteractiveTests = false.
    //
    // CAVEAT: CommentsChecker still has per-row OptionPickerForm prompts when it finds
    // an invalid comment. If the loaded assembly's parts/subassemblies have any invalid
    // comments, those dialogs will pop and you'll have to dismiss them.

    internal sealed class DrawingTools_LoadRenumberCheckComments : IFixtureTestCase, IInteractiveTestCase
    {
        public string Name => nameof(DrawingTools_LoadRenumberCheckComments);

        private const string FixturePath = @"Drawings\A-01 CONSTRUCTION DRAWINGS.idw";

        public void Run(Application app)
        {
            // Open the drawing visibly so the user can click a view
            var doc = (DrawingDocument)FixtureWorkspace.OpenFile(app, FixturePath, visible: true);
            try { doc.Activate(); } catch { }

            // ===== STEP 1: LOAD ASSEMBLY =====
            //
            // Navigate to the sheet that has the master assembly view before prompting
            // for the pick. Otherwise the user gets stuck on whatever sheet happened to
            // be active and has to hunt for a valid view.
            try
            {
                var assemblySheet = FindSheetByNameContains(doc, "A-01A");
                if (assemblySheet != null)
                {
                    assemblySheet.Activate();
                    doc.Activate();
                }
            }
            catch { /* best effort */ }

            InteractivePrompt.Toast(
                "Step 1 of 3: Load Assembly",
                "Click any DRAWING VIEW that references the assembly you want to load. " +
                "The test has navigated to sheet A-01A for you.",
                5000);

            AssemblyDocument? asm = PickAssemblyFromDrawingView(app);
            TestAssert.NotNull(asm);
            TestAssert.True(asm is AssemblyDocument,
                $"Picked view did not resolve to an AssemblyDocument. Got: {asm?.GetType().FullName ?? "null"}");

            // ===== STEP 2: RENUMBER =====
            //
            // Navigate to sheet 1 before running the renumberer — that's where the
            // global BOM is defined, and the renumberer needs to reach it from the
            // active sheet context.
            try
            {
                if (doc.Sheets.Count >= 1)
                {
                    doc.Sheets[1].Activate();
                    doc.Activate();
                }
            }
            catch { /* best effort */ }

            InteractivePrompt.Toast(
                "Step 2 of 3: Renumber",
                $"Running RmacPartNumberingRenumberer.RunCore against the loaded assembly. " +
                "This may take a few seconds.",
                4000);

            var renumberResult = RmacPartNumberingRenumberer.RunCore(app, asm!, progressForm: null);

            TestAssert.NotNull(renumberResult);
            TestAssert.True(renumberResult.Success,
                $"Renumber should have succeeded. Errors: " +
                (renumberResult.Errors.Count == 0 ? "<none>" : string.Join("; ", renumberResult.Errors)));
            TestAssert.False(renumberResult.Skipped,
                $"Renumber should not have been skipped. Reason: {renumberResult.SkipReason ?? "<none>"}");
            TestAssert.Equal(0, renumberResult.Errors.Count);

            // ===== STEP 3: CHECK COMMENTS =====

            InteractivePrompt.Toast(
                "Step 3 of 3: Check Comments",
                "Running CommentsChecker.RunCore against the same assembly. If any parts " +
                "have invalid comments you'll see per-row picker dialogs to dismiss.",
                4000);

            var commentsResult = CommentsChecker.RunCore(app, asm!);

            TestAssert.NotNull(commentsResult);
            TestAssert.True(commentsResult.Success,
                $"Check Comments should have succeeded. Errors: " +
                (commentsResult.Errors.Count == 0 ? "<none>" : string.Join("; ", commentsResult.Errors)));
            TestAssert.Equal(0, commentsResult.Errors.Count);

            // Final non-blocking toast — no Confirm needed because the API checks
            // are deterministic. Shows the result counts so the user can see what
            // happened at a glance.
            InteractivePrompt.Toast(
                "Drawing Tools Test Complete",
                $"Renumber: success. Comments: {commentsResult.UpdatedCount} updated, " +
                $"{commentsResult.SkippedCount} skipped. All checks passed.",
                4000);
        }

        /// <summary>
        /// Mirrors what the production "Load Assembly" button does:
        /// pick a drawing view via CommandManager.Pick, then resolve to the referenced
        /// assembly via the same static helper the production code uses.
        /// Returns null if the user cancels (presses Escape during the pick).
        /// </summary>
        private static AssemblyDocument? PickAssemblyFromDrawingView(Application app)
        {
            object picked;
            try
            {
                picked = app.CommandManager.Pick(
                    SelectionFilterEnum.kDrawingViewFilter,
                    "Click a drawing view that references the target assembly");
            }
            catch (COMException)
            {
                throw new TestAssertException("User cancelled the drawing view pick.");
            }

            if (picked is not DrawingView dv)
                throw new TestAssertException(
                    $"Pick returned a {picked?.GetType().FullName ?? "null"}, expected a DrawingView.");

            // Use the same internal helper the production button calls
            var asm = DrawingDockPane.TryGetAssemblyFromDrawingView(dv);
            if (asm == null)
                throw new TestAssertException(
                    "The picked drawing view does not reference an AssemblyDocument. " +
                    "Pick a different view (one that shows your assembly).");

            return asm;
        }

        private static Sheet? FindSheetByNameContains(DrawingDocument dd, string substring)
        {
            try
            {
                foreach (Sheet s in dd.Sheets)
                {
                    string n = "";
                    try { n = s.Name ?? ""; } catch { }
                    if (n.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s;
                }
            }
            catch { }
            return null;
        }
    }
}
#endif
