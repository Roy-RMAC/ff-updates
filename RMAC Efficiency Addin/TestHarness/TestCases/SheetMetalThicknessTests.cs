#if TEST_HARNESS
using System;
using Inventor;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Interactive test for the "Set Sheet Metal Thickness From Line" command.
    //
    // Calls the REAL production method SheetMetalThicknessTools.SetSheetMetalThicknessFromSelectedLine
    // which opens an interactive pick session. The user clicks a sketch line in the
    // viewport (the test fixture has a single sketch ready with a line). Production
    // code then:
    //   1. Creates a driven dimension between the line endpoints
    //   2. Renames the dim parameter to RMAC_SM_THK_DIM
    //   3. Creates a UserParameter (named per AddinSettings.SheetMetalThicknessParamName)
    //      that references the dim parameter
    //   4. Sets the sheet metal thickness expression to the user parameter
    //
    // Verification (all API, no human eyeballing):
    //   - The expected UserParameter exists with the configured (sanitised) name
    //   - smDef.Thickness.Expression references that parameter name
    //   - A dimension constraint named RMAC_SM_THK_DIM exists in some sketch
    //
    // Marked IFixtureTestCase + IInteractiveTestCase. Skipped when
    // TestMode.RunInteractiveTests = false.

    internal sealed class SheetMetalThickness_FromLine_Workflow : IFixtureTestCase, IInteractiveTestCase
    {
        public string Name => nameof(SheetMetalThickness_FromLine_Workflow);

        private const string FixturePath = @"3D CAD\TEST_THK_FROM_LINE.ipt";

        public void Run(Application app)
        {
            // Open the fixture visibly. FixtureWorkspace auto-fits the view.
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, FixturePath, visible: true);

            // Sanity: must be a sheet metal part for the command to apply
            TestAssert.True(doc.ComponentDefinition is SheetMetalComponentDefinition,
                "TEST_THK_FROM_LINE.ipt should be a sheet metal part.");
            var smDef = (SheetMetalComponentDefinition)doc.ComponentDefinition;

            try { doc.Activate(); } catch { }

            // Compute the expected user parameter name from current settings, sanitised
            // the same way the production code does.
            AddinSettings.EnsureLoaded();
            string expectedParamName = SheetMetalThicknessCore.SanitizeUserParameterName(
                AddinSettings.Current.SheetMetalThicknessParamName);

            InteractivePrompt.Toast(
                "Testing Thickness From Line",
                "Click a sketch LINE in the part to drive the sheet metal thickness.",
                5000);

            // Call the real production method. Opens the pick session; user clicks a line.
            // After this call returns, production has done all the work.
            SheetMetalThicknessTools.SetSheetMetalThicknessFromSelectedLine(app);

            // ---- Verification ----

            // 1. The user parameter should exist with the expected name
            UserParameter? foundParam = null;
            foreach (UserParameter p in doc.ComponentDefinition.Parameters.UserParameters)
            {
                if (string.Equals(p.Name, expectedParamName, StringComparison.OrdinalIgnoreCase))
                {
                    foundParam = p;
                    break;
                }
            }
            TestAssert.NotNull(foundParam);

            // 2. The sheet metal thickness expression should reference our parameter
            string thicknessExpr = "";
            try { thicknessExpr = smDef.Thickness.Expression ?? ""; } catch { }
            TestAssert.True(
                thicknessExpr.IndexOf(expectedParamName, StringComparison.OrdinalIgnoreCase) >= 0,
                $"Sheet metal thickness expression should reference '{expectedParamName}', got '{thicknessExpr}'.");

            // 3. A dimension constraint named RMAC_SM_THK_DIM should exist somewhere
            //    (production code creates this on the parent sketch of the picked line)
            bool foundDimParam = FindDimensionConstraintByParameterName(
                doc, SheetMetalThicknessCore.RefDimParamName);
            TestAssert.True(foundDimParam,
                $"Expected to find a dimension constraint with parameter name '{SheetMetalThicknessCore.RefDimParamName}' " +
                $"in some sketch on the part.");

            InteractivePrompt.Toast(
                "Thickness From Line Test Complete",
                $"Sheet metal thickness now driven by '{expectedParamName}'. All checks passed.",
                3000);
        }

        /// <summary>
        /// Walks every 2D sketch in the part looking for a DimensionConstraint whose
        /// underlying Parameter has the given name. Returns true if found.
        /// </summary>
        private static bool FindDimensionConstraintByParameterName(PartDocument doc, string paramName)
        {
            try
            {
                var sketches = doc.ComponentDefinition.Sketches;
                for (int i = 1; i <= sketches.Count; i++)
                {
                    PlanarSketch sk;
                    try { sk = sketches[i]; } catch { continue; }

                    var dims = sk.DimensionConstraints;
                    int dimCount = 0;
                    try { dimCount = dims.Count; } catch { continue; }

                    for (int d = 1; d <= dimCount; d++)
                    {
                        try
                        {
                            object dc = dims[d];
                            dynamic dyn = dc;
                            Parameter? p = null;
                            try { p = dyn.Parameter; } catch { }
                            if (p == null) continue;

                            string pn = "";
                            try { pn = p.Name ?? ""; } catch { }

                            if (string.Equals(pn, paramName, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
#endif
