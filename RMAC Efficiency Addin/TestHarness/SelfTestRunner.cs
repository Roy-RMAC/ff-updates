#if TEST_HARNESS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Inventor;
using RMAC_Efficiency_Addin.TestHarness.TestCases;
using IOFile = System.IO.File;
using IODir = System.IO.Directory;
using IOPath = System.IO.Path;
using SysEnv = System.Environment;

namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Self-test harness entry point. Runs a curated set of tests against the loaded add-in,
    /// writes results to <c>%APPDATA%\FabFlow\TestResults\results.csv</c>, and signals completion
    /// via a <c>done.flag</c> sentinel file. The <c>.bat</c> orchestrator polls for the flag,
    /// archives the CSV with a version suffix, and kills Inventor.
    ///
    /// Only compiled when the <c>Debug-Test</c> configuration defines <c>TEST_HARNESS</c>.
    /// </summary>
    internal static class SelfTestRunner
    {
        private static string ResultsDir =>
            IOPath.Combine(SysEnv.GetFolderPath(SysEnv.SpecialFolder.ApplicationData),
                           "FabFlow", "TestResults");

        public static void Run(Application app)
        {
            if (app == null) return;

            try { IODir.CreateDirectory(ResultsDir); } catch { }

            string version = "unknown";
            try { version = app.SoftwareVersion.DisplayVersion ?? "unknown"; } catch { }

            string csvPath = IOPath.Combine(ResultsDir, "results.csv");
            string flagPath = IOPath.Combine(ResultsDir, "done.flag");

            // Clean stale output from prior runs (defensive — the .bat also cleans before launch)
            try { if (IOFile.Exists(csvPath)) IOFile.Delete(csvPath); } catch { }
            try { if (IOFile.Exists(flagPath)) IOFile.Delete(flagPath); } catch { }

            var sb = new StringBuilder();
            sb.AppendLine("Test,Status,DurationMs,Message");
            sb.AppendLine($"# InventorVersion: {version}");
            sb.AppendLine($"# Started: {DateTime.Now:o}");

            int passed = 0, failed = 0;

            // ---------------------------------------------------------------------------
            // TEST REGISTRY
            //
            // Add new tests here. Order matters: fastest/purest first, live-app tests next,
            // fixture-dependent tests last — so a hang in a slow test doesn't starve
            // quick-feedback tests.
            //
            // Keep the list sorted by wave (see TESTING_INVENTORY.md / this conversation's
            // 5-wave plan).
            // ---------------------------------------------------------------------------
            var tests = new List<ITestCase>
            {
                // ---- Wave 1: Pure logic ----

                // Test #1 — SheetMetalThicknessCore.SanitizeUserParameterName
                new SanitizeParamName_NullReturnsDefault(),
                new SanitizeParamName_EmptyReturnsDefault(),
                new SanitizeParamName_WhitespaceReturnsDefault(),
                new SanitizeParamName_ValidNameUnchanged(),
                new SanitizeParamName_SpacesBecomeUnderscores(),
                new SanitizeParamName_LeadingDigitGetsPrefix(),
                new SanitizeParamName_OnlyIllegalCharsReturnsDefault(),

                // Test #2 — CommentClassifier.Normalize / .EqualsCategory
                new CommentClassifier_Normalize_NullReturnsEmpty(),
                new CommentClassifier_Normalize_TrimsWhitespace(),
                new CommentClassifier_Normalize_PreservesCaseByDefault(),
                new CommentClassifier_Normalize_ForceUpperInvariantUpperCases(),
                new CommentClassifier_EqualsCategory_IdenticalMatches(),
                new CommentClassifier_EqualsCategory_CaseInsensitive(),
                new CommentClassifier_EqualsCategory_IgnoresSurroundingWhitespace(),
                new CommentClassifier_EqualsCategory_DifferentValuesDoNotMatch(),
                new CommentClassifier_EqualsCategory_BothNullMatch(),
                new CommentClassifier_EqualsCategory_NullEqualsEmpty(),

                // Test #3 — FrameMemberMatcher.IsMatch
                new FrameMemberMatcher_IsMatch_IdenticalDataMatches(),
                new FrameMemberMatcher_IsMatch_DifferentMaterialFails(),
                new FrameMemberMatcher_IsMatch_DifferentStockFails(),
                new FrameMemberMatcher_IsMatch_DifferentLengthFails(),
                new FrameMemberMatcher_IsMatch_MassWithinToleranceStillMatches(),
                new FrameMemberMatcher_IsMatch_MassBeyondToleranceFails(),
                new FrameMemberMatcher_IsMatch_InertiaBeyondToleranceFails(),

                // Test #4 — MachineFingerprint.Get
                new MachineFingerprint_Get_ReturnsNonEmpty(),
                new MachineFingerprint_Get_Is64HexChars(),
                new MachineFingerprint_Get_StableAcrossCalls(),

                // Test #5 — SelectionService.TryResolveDrawingView(IReadOnlyList<object>)
                new SelectionService_ResolveFromList_NullListReturnsNull(),
                new SelectionService_ResolveFromList_EmptyListReturnsNull(),
                new SelectionService_ResolveFromList_IrrelevantObjectsReturnsNull(),

                // Test #6 — RmacPartNumberingRenumberer.MapSkipSelection
                new MapSkipSelection_NullReturnsEmpty(),
                new MapSkipSelection_EmptyReturnsEmpty(),
                new MapSkipSelection_NoDotTreatsAsPropertyName(),
                new MapSkipSelection_ProjectMapsToDesignTracking(),
                new MapSkipSelection_SummaryInformationPassesThrough(),
                new MapSkipSelection_DocumentSummaryRemaps(),
                new MapSkipSelection_DesignTrackingPassesThrough(),
                new MapSkipSelection_CaseInsensitiveSetMatch(),

                // Test #7 — DEFERRED. LicenseData round-trip needs an injectable path on
                // LicenseStore.Save/Load to avoid clobbering the dev license. Revisit when
                // licensing gets a small refactor.

                // Test #8 — UpdateInfo JSON deserialization
                new UpdateInfo_Parse_ValidJsonPopulatesAllFields(),
                new UpdateInfo_Parse_MissingFieldsDefaultToEmpty(),
                new UpdateInfo_Parse_EmptyObjectAllDefaults(),
                new UpdateInfo_Parse_RoundTripPreservesValues(),

                // Test #9 — DEFERRED to Wave 3. FolderRouter requires an ExportContext that
                // requires a real AssemblyDocument; needs a fixture file to construct.

                // Test #10 — Infrastructure smoke (verifies the harness itself works)
                new Infra_App_IsNotNull(),
                new Infra_App_HasSoftwareVersion(),
                new Infra_TestAssert_EqualThrowsOnMismatch(),
                new Infra_TestAssert_TrueThrowsOnFalse(),
                new Infra_ResultObjects_Constructible(),

                // ---- Wave 2: Live app, no fixture ----

                // Test #11 — SketchCommandInspector.FindMatches
                new SketchCommandInspector_FindMatches_ReturnsResults(),
                new SketchCommandInspector_FindMatches_RespectsMaxResults(),
                new SketchCommandInspector_FindMatches_EntryFormat(),

                // Test #12 — DrawingContextResolver.GetContext(null)
                new DrawingContextResolver_GetContext_NullDocReturnsInvalid(),
                new DrawingContextResolver_GetContext_NullDocPopulatesEmptySelection(),
                new DrawingContextResolver_GetContext_NullDocLeavesViewsNull(),

                // Test #13 — SelectionService null-doc overloads
                new SelectionService_GetSnapshot_NullDocReturnsEmpty(),
                new SelectionService_TryResolveFromSelection_NullDocReturnsNull(),

                // Test #14 — DEFERRED. Coverage already provided by Infra_App_HasSoftwareVersion.

                // ---- Wave 3: Fixture-based tests (need FixtureWorkspace) ----

                // Test #21 — InventorTransaction.RunFast (smallest possible fixture test;
                // proves the workspace pipeline works end-to-end before we add destructive tests)
                new RunFast_OpensFixturePart(),
                new RunFast_ActionIsInvoked(),
                new RunFast_ScreenUpdatingFalseDuringAction(),
                new RunFast_ScreenUpdatingRestoredAfterAction(),
                new RunFast_ExceptionStillRestoresScreenUpdating(),

                // Tests #15-19 (consolidated) — LAYOUT_01.ipt as shared sketch fixture.
                // Order:
                //   sanity → visibility (hide/show) → dim mode (visible/hidden/named)
                //   → color sweep (palette/random)
                new LayoutSketch_HasContent(),
                new LayoutSketch_HideAllMakesAllInvisible(),
                new LayoutSketch_ShowAllMakesAllVisible(),
                new LayoutSketch_DimMode_AllVisible_ShowsEveryDim(),
                new LayoutSketch_DimMode_AllHidden_HidesEveryDim(),
                new LayoutSketch_DimMode_NamedOnly_ShowsOnlyNamedDims(),
                new LayoutSketch_LegacySweep_PaletteMode(),
                new LayoutSketch_LegacySweep_RandomMode(),

                // Show/Hide Dims + Highlight/Clear Highlight — INTERACTIVE test
                // (user picks 2 sketches by clicking lines, then watches the cycle).
                // Skipped automatically when TestMode.RunInteractiveTests = false.
                new LayoutSketch_DimsAndHighlight_Workflow(),

                // Sheet Metal Thickness From Line — INTERACTIVE test
                // (user clicks a sketch line in TEST_THK_FROM_LINE.ipt; production code
                // creates the driven dim, user param, and binds the SM thickness).
                new SheetMetalThickness_FromLine_Workflow(),

                // Drawing Tools — INTERACTIVE test
                // (user picks a drawing view in A-01 CONSTRUCTION DRAWINGS.idw; test then
                // runs Load Assembly + Renumber + Check Comments via the production code).
                new DrawingTools_LoadRenumberCheckComments(),

                // Drawing Tools Part Insertion Workflow — fully programmatic
                // (no clicks; bypasses dock pane wrappers and calls services directly).
                // Activates sheet A-10B, runs view coverage, loads BOM, adds new sheet,
                // inserts all parts + a flat pattern, re-runs coverage, renames + sorts.
                new DrawingTools_PartInsertionWorkflow(),

                // Exporter — fully programmatic
                // (no clicks; calls GlobalExporter.RunCore with a temp output folder).
                // Slow — 30-60s against a real assembly.
                new Exporter_RunCoreWorkflow(),

                // Custom Derive — INTERACTIVE test (calls real production methods, requires user clicks).
                // Skipped automatically when TestMode.RunInteractiveTests = false.
                new CustomDerive_FullProductionWorkflow(),
            };

            int skipped = 0;

            // ---------------------------------------------------------------------------
            // Phase 1: non-fixture tests (Wave 1 + Wave 2)
            // These don't need the test project on disk and run cheaply.
            // Interactive non-fixture tests (rare) get skipped if RunInteractiveTests is false.
            // ---------------------------------------------------------------------------
            foreach (var test in tests)
            {
                if (test is IFixtureTestCase) continue;
                if (test is IInteractiveTestCase && !TestMode.RunInteractiveTests)
                {
                    sb.AppendLine($"{test.Name},SKIP,0,Interactive test skipped (TestMode.RunInteractiveTests = false).");
                    skipped++;
                    continue;
                }
                RunOne(sb, test, app, ref passed, ref failed);
            }

            // ---------------------------------------------------------------------------
            // Phase 2: fixture tests (Wave 3+)
            // Set up the working copy of the test project, run all fixture tests, tear down.
            // If the source dataset is missing on this machine, mark every fixture test SKIP.
            // ---------------------------------------------------------------------------
            var fixtureTests = new List<IFixtureTestCase>();
            foreach (var t in tests)
                if (t is IFixtureTestCase ft) fixtureTests.Add(ft);

            if (fixtureTests.Count > 0)
            {
                if (!FixtureWorkspace.IsAvailable)
                {
                    foreach (var test in fixtureTests)
                    {
                        sb.AppendLine($"{test.Name},SKIP,0,FixtureWorkspace source dataset not present on this machine.");
                        skipped++;
                    }
                }
                else
                {
                    string? setupError = null;
                    try
                    {
                        sb.AppendLine($"# FixtureWorkspace.Setup starting at {DateTime.Now:o}");
                        FixtureWorkspace.Setup(app);
                        sb.AppendLine($"# FixtureWorkspace.Setup completed at {DateTime.Now:o}");
                    }
                    catch (Exception ex)
                    {
                        setupError = ex.Message;
                    }

                    if (setupError != null)
                    {
                        foreach (var test in fixtureTests)
                        {
                            var msg = setupError.Replace(",", ";").Replace("\r", " ").Replace("\n", " ");
                            sb.AppendLine($"{test.Name},SKIP,0,FixtureWorkspace setup failed: {msg}");
                            skipped++;
                        }
                    }
                    else
                    {
                        try
                        {
                            foreach (var test in fixtureTests)
                            {
                                if (test is IInteractiveTestCase && !TestMode.RunInteractiveTests)
                                {
                                    sb.AppendLine($"{test.Name},SKIP,0,Interactive test skipped (TestMode.RunInteractiveTests = false).");
                                    skipped++;
                                    continue;
                                }
                                RunOne(sb, test, app, ref passed, ref failed);
                            }
                        }
                        finally
                        {
                            try
                            {
                                sb.AppendLine($"# FixtureWorkspace.Teardown starting at {DateTime.Now:o}");
                                FixtureWorkspace.Teardown(app);
                                sb.AppendLine($"# FixtureWorkspace.Teardown completed at {DateTime.Now:o}");
                            }
                            catch (Exception ex)
                            {
                                sb.AppendLine($"# FixtureWorkspace.Teardown error: {ex.Message}");
                            }
                        }
                    }
                }
            }

            sb.AppendLine($"# Finished: {DateTime.Now:o}");
            sb.AppendLine($"# Summary: {passed} passed, {failed} failed, {skipped} skipped");

            // Write CSV first, flag last — the .bat only reads the CSV after seeing the flag,
            // so flag-last avoids a race where it reads a half-written CSV.
            try { IOFile.WriteAllText(csvPath, sb.ToString()); } catch { }
            try { IOFile.WriteAllText(flagPath, DateTime.Now.ToString("o")); } catch { }

            // NOTE: We deliberately do NOT call app.Quit() from here. The .bat orchestrator
            // polls for done.flag, reads the CSV, and calls taskkill. That's simpler and more
            // reliable than trying to coordinate Application.Quit() from within an add-in
            // callback (which can fail on dirty documents, pending transactions, etc.).
        }

        private static void RunOne(StringBuilder sb, ITestCase test, Application app, ref int passed, ref int failed)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                test.Run(app);
                sw.Stop();
                sb.AppendLine($"{test.Name},PASS,{sw.ElapsedMilliseconds},");
                passed++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var msg = (ex.Message ?? "")
                    .Replace(",", ";")
                    .Replace("\r", " ")
                    .Replace("\n", " ");
                sb.AppendLine($"{test.Name},FAIL,{sw.ElapsedMilliseconds},{msg}");
                failed++;
            }
        }
    }
}
#endif
