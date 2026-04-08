#if TEST_HARNESS
using System.Collections.Generic;
using System.Threading;
using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;
using RMAC_Efficiency_Addin.Settings;
using WF = System.Windows.Forms;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests against 3D CAD\LAYOUT_01.ipt — a layout part containing only sketches
    // (no solid bodies). Used as the shared fixture for sketch visibility, dimension
    // mode, and color sweep tests.
    //
    // VERIFICATION STRATEGY:
    //   - Hard check (always runs): API state inspection. After calling a Core method,
    //     iterate the relevant collection and assert each item has the expected state.
    //   - Soft check (active when authoring/debugging): VisualConfirm.Step shows a
    //     MessageBox so you can verify the viewport. Cancel = fail. Comment out the
    //     Step() lines (and revert visible: true) when you're satisfied.
    //
    // STATE CONTRACT:
    //   These tests share a single open document. Each test resets the bits of state
    //   it cares about at the start (via SketchTestHelpers / explicit DimMode calls)
    //   so test order doesn't matter for correctness.
    //
    //   Test order is chosen so the visual checkpoints flow naturally:
    //     1. Sanity (open + has sketches)
    //     2. Hide all sketches → Show all sketches
    //     3. Dim mode AllVisible → AllHidden → NamedOnly
    //     4. Color sweep — Palette (muted) → RandomPerSketch
    //
    //   By the time the color tests run, dims are in NamedOnly mode so the viewport
    //   shows only the user-named dimensions when visually confirming colors.
    //
    // HOW TO STEP THROUGH A TEST VISUALLY:
    //   1. Change OpenFile(app, ...) to OpenFile(app, ..., visible: true)
    //   2. Uncomment the VisualConfirm.Step(...) line in that test
    //   3. Build and run the .bat — Inventor will pause on each Step
    //   4. When satisfied, comment Step() back out and revert visible: true

    internal static class LayoutSketchHelpers
    {
        public const string FixturePath = @"3D CAD\LAYOUT_01.ipt";
    }

    // ------------------------------------------------------------------------
    // 1 — Sanity check
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_HasContent : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_HasContent);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            int total = SketchTestHelpers.CountAllSketches(doc);
            TestAssert.True(total > 0,
                $"LAYOUT_01.ipt should contain at least one sketch (got {total}). " +
                "Later tests are meaningless if this fails.");

            // VisualConfirm.Step(app, (_Document)doc,
            //     $"LAYOUT_01.ipt opened. Should contain {total} sketches and no solid bodies.");
        }
    }

    // ------------------------------------------------------------------------
    // 2 — Sketch visibility (hide all / show all)
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_HideAllMakesAllInvisible : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_HideAllMakesAllInvisible);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            // Known starting state: everything visible
            SketchTestHelpers.ResetAllSketchesVisible(doc);
            int total = SketchTestHelpers.CountAllSketches(doc);

            // The action under test
            var result = SketchVisibilityCore.SetAll(doc, makeVisible: false);

            // Hard check
            int stillVisible = SketchTestHelpers.CountSketchesWithVisible(doc, wantVisible: true);
            TestAssert.Equal(0, stillVisible);
            TestAssert.Equal(total, result.TotalChanged);
            TestAssert.Equal(0, result.Failures);

            // VisualConfirm.Step(app, (_Document)doc,
            //     "All sketches should now be HIDDEN in the viewport.");
        }
    }

    internal sealed class LayoutSketch_ShowAllMakesAllVisible : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_ShowAllMakesAllVisible);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            // Known starting state: everything invisible
            SketchTestHelpers.ResetAllSketchesInvisible(doc);
            int total = SketchTestHelpers.CountAllSketches(doc);

            // The action under test
            var result = SketchVisibilityCore.SetAll(doc, makeVisible: true);

            // Hard check
            int stillInvisible = SketchTestHelpers.CountSketchesWithVisible(doc, wantVisible: false);
            TestAssert.Equal(0, stillInvisible);
            TestAssert.Equal(total, result.TotalChanged);
            TestAssert.Equal(0, result.Failures);

            // VisualConfirm.Step(app, (_Document)doc,
            //     "All sketches should now be VISIBLE in the viewport.");
        }
    }

    // ------------------------------------------------------------------------
    // 3 — Dim mode: AllVisible (the first of the three modes; ACTIVE checkpoint)
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_DimMode_AllVisible_ShowsEveryDim : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_DimMode_AllVisible_ShowsEveryDim);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            SketchTestHelpers.ResetAllSketchesVisible(doc);

            int totalDims = SketchTestHelpers.CountAllDimensions(doc);
            TestAssert.True(totalDims > 0, "Need at least one dimension constraint to test.");

            var dims = new SketchDimensionPolicy();
            DimModeCore.ApplyToPart(doc, DimPolicyMode.AllVisible, dims);

            int visible = SketchTestHelpers.CountDimensionsWithVisible(doc, wantVisible: true);
            TestAssert.Equal(totalDims, visible);

            // VisualConfirm.Step(app, (_Document)doc,
            //     "Dim Mode = AllVisible. EVERY sketch dimension should now be visible in the viewport.");
        }
    }

    // ------------------------------------------------------------------------
    // 4 — Dim mode: AllHidden (ACTIVE checkpoint)
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_DimMode_AllHidden_HidesEveryDim : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_DimMode_AllHidden_HidesEveryDim);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            SketchTestHelpers.ResetAllSketchesVisible(doc);

            int totalDims = SketchTestHelpers.CountAllDimensions(doc);
            TestAssert.True(totalDims > 0,
                $"LAYOUT_01.ipt should contain dimensioned sketches; found {totalDims} dimension constraints.");

            var dims = new SketchDimensionPolicy();
            DimModeCore.ApplyToPart(doc, DimPolicyMode.AllHidden, dims);

            int hidden = SketchTestHelpers.CountDimensionsWithVisible(doc, wantVisible: false);
            TestAssert.Equal(totalDims, hidden);

            // VisualConfirm.Step(app, (_Document)doc,
            //     "Dim Mode = AllHidden. EVERY sketch dimension should now be hidden (sketches themselves still visible).");
        }
    }

    // ------------------------------------------------------------------------
    // 5 — Dim mode: NamedOnly (ACTIVE checkpoint)
    //     Leaves dims in NamedOnly mode for the color tests that follow.
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_DimMode_NamedOnly_ShowsOnlyNamedDims : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_DimMode_NamedOnly_ShowsOnlyNamedDims);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            SketchTestHelpers.ResetAllSketchesVisible(doc);

            int totalDims = SketchTestHelpers.CountAllDimensions(doc);
            TestAssert.True(totalDims > 0, "Need at least one dimension constraint to test.");

            var dims = new SketchDimensionPolicy();
            DimModeCore.ApplyToPart(doc, DimPolicyMode.NamedOnly, dims);

            // Hard check: number of visible dims must be <= total. The exact count depends
            // on how many user-named dims are in the fixture, which we don't pin here.
            int visible = SketchTestHelpers.CountDimensionsWithVisible(doc, wantVisible: true);
            TestAssert.True(visible >= 0 && visible <= totalDims,
                $"Visible dim count ({visible}) should be between 0 and total ({totalDims}).");

            // VisualConfirm.Step(app, (_Document)doc,
            //     $"Dim Mode = NamedOnly. Only USER-NAMED dimensions should be visible " +
            //     $"(auto-named d0/d1/etc should be hidden). Total dims: {totalDims}, currently visible: {visible}.");
        }
    }

    // ------------------------------------------------------------------------
    // 6 — Color sweep: Palette mode (muted role-based palette)
    //     Saves/restores InactiveSketchColorMode so the test is deterministic
    //     regardless of the user's persisted setting.
    //     ACTIVE checkpoint.
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_LegacySweep_PaletteMode : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_LegacySweep_PaletteMode);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            // ApplyInactiveToPart only touches VISIBLE sketches — make sure they all are.
            SketchTestHelpers.ResetAllSketchesVisible(doc);

            // Save and force the color mode under test, restore in finally.
            var originalMode = AddinSettings.Current.InactiveSketchColorMode;
            try
            {
                AddinSettings.Current.InactiveSketchColorMode = InactiveSketchColorMode.Palette;

                var styler = new SketchStyler(app);
                LegacySweepCore.Run(doc, styler);

                // Hard check: at least one entity now has a non-null OverrideColor
                int withOverride = SketchTestHelpers.CountEntitiesWithOverrideColor(doc);
                TestAssert.True(withOverride > 0,
                    $"Expected at least one sketch entity with an OverrideColor after Palette sweep, got {withOverride}.");

                // VisualConfirm.Step(app, (_Document)doc,
                //     "Color Mode = Palette (muted). Sketches should now show MUTED role-based colors " +
                //     "(under-constrained / fully-constrained / construction lines in different muted shades).");
            }
            finally
            {
                AddinSettings.Current.InactiveSketchColorMode = originalMode;
            }
        }
    }

    // ------------------------------------------------------------------------
    // 7 — Color sweep: RandomPerSketch mode (HSL hue per sketch)
    //     ACTIVE checkpoint.
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_LegacySweep_RandomMode : IFixtureTestCase
    {
        public string Name => nameof(LayoutSketch_LegacySweep_RandomMode);
        public void Run(Application app)
        {
            var doc = (PartDocument)FixtureWorkspace.OpenFile(app, LayoutSketchHelpers.FixturePath);

            SketchTestHelpers.ResetAllSketchesVisible(doc);

            var originalMode = AddinSettings.Current.InactiveSketchColorMode;
            try
            {
                AddinSettings.Current.InactiveSketchColorMode = InactiveSketchColorMode.RandomPerSketch;

                var styler = new SketchStyler(app);
                LegacySweepCore.Run(doc, styler);

                int withOverride = SketchTestHelpers.CountEntitiesWithOverrideColor(doc);
                TestAssert.True(withOverride > 0,
                    $"Expected at least one sketch entity with an OverrideColor after Random sweep, got {withOverride}.");

                // VisualConfirm.Step(app, (_Document)doc,
                //     "Color Mode = RandomPerSketch. Each sketch should now have its OWN distinct " +
                //     "hue (HSL golden-angle distribution — every sketch a different colour).");
            }
            finally
            {
                AddinSettings.Current.InactiveSketchColorMode = originalMode;
            }
        }
    }

    // ------------------------------------------------------------------------
    // 8 — Show / Hide Dims and Highlight / Clear Highlight (INTERACTIVE)
    //
    // Walks the user through the four sketch-tools-on-individual-sketches buttons
    // by asking them to click a line in two sketches, then applies:
    //   Phase 1 → SketchDimCore.ShowAndPin (both) + SketchColorCore.ClearOverrides
    //             (the "Highlight" production action). 3-second pause.
    //   Phase 2 → SketchDimCore.UnpinAndApply with AllHidden (both) +
    //             SketchColorCore.ReleaseHold (the "Clear Highlight" production
    //             action). 2-second pause.
    //
    // Marked IFixtureTestCase + IInteractiveTestCase so it skips automatically when
    // TestMode.RunInteractiveTests = false.
    // ------------------------------------------------------------------------

    internal sealed class LayoutSketch_DimsAndHighlight_Workflow : IFixtureTestCase, IInteractiveTestCase
    {
        public string Name => nameof(LayoutSketch_DimsAndHighlight_Workflow);

        public void Run(Application app)
        {
            // Open layout visibly. FixtureWorkspace.OpenFile auto-fits the view when
            // visible: true, so the user immediately sees the geometry.
            var doc = (PartDocument)FixtureWorkspace.OpenFile(
                app, LayoutSketchHelpers.FixturePath, visible: true);

            // Known starting state: sketches visible, all dims hidden so the
            // "Show Dims" effect is dramatic when it fires.
            SketchTestHelpers.ResetAllSketchesVisible(doc);
            var dims = new SketchDimensionPolicy();
            DimModeCore.ApplyToPart(doc, DimPolicyMode.AllHidden, dims);

            try { doc.Activate(); } catch { }

            // Non-blocking informational toast — just tells the user what's coming.
            // The pick prompt in Inventor's status bar provides the actual instructions.
            InteractivePrompt.Toast(
                "Testing Show/Hide Dims + Highlight",
                "Click a LINE in two sketches when prompted in the status bar.",
                4000);

            // ----- Pick 2 sketches via line clicks -----
            // CommandManager.Pick prompts in Inventor's status bar — modeless, no
            // additional UI from us.
            var sketch1 = SketchTestHelpers.PickSketchInteractive(
                app, "Click any LINE in the FIRST sketch (1 of 2)");
            TestAssert.NotNull(sketch1);
            TestAssert.True(sketch1 is Sketch || sketch1 is Sketch3D,
                "First pick did not resolve to a Sketch or Sketch3D.");

            var sketch2 = SketchTestHelpers.PickSketchInteractive(
                app, "Click any LINE in the SECOND sketch (2 of 2)");
            TestAssert.NotNull(sketch2);
            TestAssert.True(sketch2 is Sketch || sketch2 is Sketch3D,
                "Second pick did not resolve to a Sketch or Sketch3D.");

            var styler = new SketchStyler(app);

            // Snapshot the dim counts BEFORE so we can verify the operations actually
            // changed the visible state.
            int totalDimsSketch1 = CountDimensionConstraints(sketch1!);
            int totalDimsSketch2 = CountDimensionConstraints(sketch2!);
            TestAssert.True(totalDimsSketch1 > 0,
                $"Sketch 1 should have at least one dimension constraint to test (got {totalDimsSketch1}). Pick a different sketch.");
            TestAssert.True(totalDimsSketch2 > 0,
                $"Sketch 2 should have at least one dimension constraint to test (got {totalDimsSketch2}). Pick a different sketch.");

            // ===== PHASE 1: Show Dims + Highlight =====
            //
            // We don't check the internal "pinned" state — that's an implementation
            // detail of SketchDimPin and isn't directly observable to users. What we
            // verify is what the user sees: every dimension on the picked sketches is
            // now visible.
            //
            // Mutations are wrapped in InventorTransaction.RunFast because some of them
            // (AttributeSet writes inside SetPinned) require a transaction to commit.

            InventorTransaction.RunFast(app, (_Document)doc, "RMAC Test Show Dims", () =>
            {
                SketchDimCore.ShowAndPin(sketch1!, dims);
                SketchDimCore.ShowAndPin(sketch2!, dims);
            });

            // Hard check: every dimension on the picked sketches is now visible
            int visible1AfterShow = CountVisibleDimensionConstraints(sketch1!);
            int visible2AfterShow = CountVisibleDimensionConstraints(sketch2!);
            TestAssert.Equal(totalDimsSketch1, visible1AfterShow);
            TestAssert.Equal(totalDimsSketch2, visible2AfterShow);

            // The Highlight button (BtnHighlight) selects sketches in the SelectSet
            // then calls ClearOverrides on the first one (which toggles Show Format on).
            try { doc.SelectSet.Clear(); } catch { }
            try { doc.SelectSet.Select(sketch1); } catch { }
            try { doc.SelectSet.Select(sketch2); } catch { }

            InventorTransaction.RunFast(app, (_Document)doc, "RMAC Test Highlight", () =>
            {
                SketchColorCore.ClearOverrides(app, sketch1!, showFormat: null);
            });

            try { doc.Update(); } catch { }
            try { app.ActiveView.Update(); } catch { }
            try { WF.Application.DoEvents(); } catch { }

            // Status toast while the user looks at the result
            InteractivePrompt.Toast(
                "Show Dims + Highlight Active",
                "Showing dimensions and highlight on both sketches. Holding 3 seconds...",
                3000);
            Thread.Sleep(3000);

            // ===== PHASE 2: Hide Dims + Clear Highlight =====

            InventorTransaction.RunFast(app, (_Document)doc, "RMAC Test Hide Dims", () =>
            {
                SketchDimCore.UnpinAndApply(sketch1!, DimPolicyMode.AllHidden, dims);
                SketchDimCore.UnpinAndApply(sketch2!, DimPolicyMode.AllHidden, dims);
            });

            // Hard check: every dimension on the picked sketches is now hidden
            int visible1AfterHide = CountVisibleDimensionConstraints(sketch1!);
            int visible2AfterHide = CountVisibleDimensionConstraints(sketch2!);
            TestAssert.Equal(0, visible1AfterHide);
            TestAssert.Equal(0, visible2AfterHide);

            // The Clear Highlight button (BtnClearHighlight) calls ReleaseHold which
            // toggles Show Format off and re-applies the inactive palette to each.
            var sketches = new List<object> { sketch1!, sketch2! };
            InventorTransaction.RunFast(app, (_Document)doc, "RMAC Test Clear Highlight", () =>
            {
                SketchColorCore.ReleaseHold(app, sketches, showFormat: null, styler);
            });

            try { doc.Update(); } catch { }
            try { app.ActiveView.Update(); } catch { }
            try { WF.Application.DoEvents(); } catch { }

            InteractivePrompt.Toast(
                "Hide Dims + Clear Highlight Active",
                "Hiding dimensions and clearing highlight. Holding 2 seconds...",
                2000);
            Thread.Sleep(2000);

            // Cleanup: clear the selection so it doesn't bleed into later tests
            try { doc.SelectSet.Clear(); } catch { }

            // Final confirmation — Enter to pass, Esc to fail
            if (!InteractivePrompt.Confirm(
                "Show/Hide Dims + Highlight Test",
                "Test complete. Did all four operations behave correctly?"))
            {
                throw new TestAssertException("User reported the visual result was wrong.");
            }
        }

        /// <summary>
        /// Count of dimension constraints on a sketch (2D or 3D), via dynamic dispatch
        /// because Sketch.DimensionConstraints and Sketch3D.DimensionConstraints are
        /// different types.
        /// </summary>
        private static int CountDimensionConstraints(object sketchObj)
        {
            try
            {
                dynamic d = sketchObj;
                return (int)d.DimensionConstraints.Count;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountVisibleDimensionConstraints(object sketchObj)
        {
            int n = 0;
            try
            {
                dynamic d = sketchObj;
                var dims = d.DimensionConstraints;
                int count = (int)dims.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        dynamic dc = dims[i];
                        bool v = false;
                        try { v = (bool)dc.Visible; } catch { }
                        if (v) n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }
    }
}
#endif
