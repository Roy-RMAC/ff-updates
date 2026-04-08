#if TEST_HARNESS
using System;
using System.Collections.Generic;
using Inventor;
using RMAC_Efficiency_Addin.Layouts;
using IOPath = System.IO.Path;
using WF = System.Windows.Forms;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Interactive test for the custom derive feature.
    //
    // This test calls the REAL production methods (LayoutsController.RunCustomDerive and
    // RunEditDerive) so the user goes through the actual selection session UI from the
    // production code path. There's no shortcut around the InteractionEvents — what the
    // user clicks is what gets passed downstream.
    //
    // FLOW:
    //   1. Open LAYOUT_01.ipt (the source layout) — silent setup
    //   2. Create a new empty target part — silent setup
    //   3. Show MessageBox A: "About to test CUSTOM DERIVE..."
    //   4. Call controller.RunCustomDerive(target, layoutPath)
    //      → production selection session opens
    //      → user clicks 2+ sketches in LAYOUT_01
    //      → user clicks Finish
    //      → production code adds the derived component
    //   5. Hard-verify: 1 DPC, included IDs match what's in the derive
    //   6. Show MessageBox B: "Verify the count looks right..."
    //   7. Record original ID set
    //   8. Call controller.RunEditDerive(target, layoutPath)
    //      → production edit-derive session opens, pre-populated with the original
    //      → user adds at least one new sketch (so count > original)
    //      → user clicks Finish
    //   9. Hard-verify: still 1 DPC (not duplicated), included IDs is a strict superset
    //  10. Show MessageBox C: "Edit complete..."
    //
    // Marked IFixtureTestCase + IInteractiveTestCase: needs the workspace AND user input.
    // Skipped automatically when TestMode.RunInteractiveTests is false.

    internal sealed class CustomDerive_FullProductionWorkflow : IFixtureTestCase, IInteractiveTestCase
    {
        public string Name => nameof(CustomDerive_FullProductionWorkflow);

        public void Run(Application app)
        {
            // ---- Setup: open source layout (visible so user can see/click sketches) ----
            var layoutDoc = (PartDocument)FixtureWorkspace.OpenFile(
                app, LayoutSketchHelpers.FixturePath, visible: true);

            string layoutPath = layoutDoc.FullFileName;
            TestAssert.True(System.IO.File.Exists(layoutPath),
                $"Layout file should exist on disk: {layoutPath}");

            // ---- Setup: create new empty target part ----
            // Empty template path → use the active project's default template.
            // The workspace setup activated the test project, so this should resolve.
            PartDocument target;
            try
            {
                target = (PartDocument)app.Documents.Add(
                    DocumentTypeEnum.kPartDocumentObject, "", true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to create new target part via Documents.Add. " +
                    "The active project may not have a default template configured. Error: " + ex.Message, ex);
            }
            FixtureWorkspace.RegisterCreatedDoc(target);

            // Make the target the active document so RunCustomDerive's checks pass
            try { target.Activate(); } catch { }

            // ---- Get the live LayoutsController instance from the running add-in ----
            var controller = LayoutsController.Instance;
            TestAssert.NotNull(controller);

            // ===== STEP 1: Custom Derive =====

            // Non-blocking heads-up. The selection session opens immediately after.
            InteractivePrompt.Toast(
                "Testing Custom Derive",
                "Selection dialog opens in LAYOUT_01. Pick 2+ sketches and click Finish on the toolbar.",
                4000);

            controller!.RunCustomDerive(target, layoutPath);

            // After the production code returns, verify the derive landed correctly.
            var dpc1 = CustomDeriveCore.FindDerivedPartComponent(target, layoutPath);
            TestAssert.NotNull(dpc1);

            int dpcCount1 = CountDerivedPartComponents(target);
            TestAssert.Equal(1, dpcCount1);

            var includedIds1 = CustomDeriveCore.ReadIncludedEntityIds(dpc1!, (Inventor._Document)layoutDoc);
            TestAssert.True(includedIds1.Count >= 1,
                $"Expected at least 1 included entity after custom derive, got {includedIds1.Count}.");

            // Stricter check: the number of derived sketches in the target should match
            // exactly the number of entity IDs in the DPC. No human confirmation needed.
            int sketchCount1 = target.ComponentDefinition.Sketches.Count;
            TestAssert.Equal(includedIds1.Count, sketchCount1);

            // ===== STEP 2: Edit Derive =====

            // Non-blocking heads-up. The edit-derive session opens immediately after.
            InteractivePrompt.Toast(
                "Testing Edit Derive",
                "Edit-derive dialog opens with your previous picks pre-highlighted. " +
                "Add at least one new sketch and click Finish.",
                4000);

            controller.RunEditDerive(target, layoutPath);

            // Verify the edit landed correctly. Critical checks:
            //   1. Still exactly 1 DPC (catches duplication bugs)
            //   2. New included set is a strict superset of the original (user added at least one)
            var dpc2 = CustomDeriveCore.FindDerivedPartComponent(target, layoutPath);
            TestAssert.NotNull(dpc2);

            int dpcCount2 = CountDerivedPartComponents(target);
            TestAssert.Equal(1, dpcCount2);

            var includedIds2 = CustomDeriveCore.ReadIncludedEntityIds(dpc2!, (Inventor._Document)layoutDoc);
            TestAssert.True(includedIds2.Count > includedIds1.Count,
                $"Expected the edit to ADD sketches (count {includedIds2.Count}) to the original ({includedIds1.Count}). " +
                "Did the user remove sketches instead, or click Finish without adding any?");

            // Every original ID should still be present (the user was told to keep them)
            foreach (var origId in includedIds1)
            {
                TestAssert.True(includedIds2.Contains(origId),
                    $"Original entity ID '{origId}' is missing from the edited derive. " +
                    "Did the user accidentally deselect an original sketch?");
            }

            // Stricter: derived sketch count must match the included ID count exactly,
            // AND it must have grown from the original (the user added at least one).
            int sketchCount2 = target.ComponentDefinition.Sketches.Count;
            TestAssert.Equal(includedIds2.Count, sketchCount2);
            TestAssert.True(sketchCount2 > sketchCount1,
                $"Target part should now have MORE derived sketches than before edit " +
                $"(was {sketchCount1}, now {sketchCount2}).");

            // No final Confirm — the strict API checks above (DPC count == 1, included IDs
            // is a strict superset of original, every original ID still present, derived
            // sketch count == included count) are sufficient. A short toast lets the user
            // know the test finished cleanly without requiring a key press.
            InteractivePrompt.Toast(
                "Custom Derive Test Complete",
                $"Custom: {includedIds1.Count} sketches → Edit: {includedIds2.Count} sketches. All checks passed.",
                3000);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static int CountDerivedPartComponents(PartDocument targetPart)
        {
            int n = 0;
            try
            {
                dynamic dpcs = targetPart.ComponentDefinition.ReferenceComponents.DerivedPartComponents;
                foreach (object _ in dpcs) n++;
            }
            catch { }
            return n;
        }
    }
}
#endif
