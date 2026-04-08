#if TEST_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;
using RMAC_Efficiency_Addin.DrawingDock.Services;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Programmatic end-to-end test for the drawing dock part-insertion workflow.
    //
    // No mouse clicks required — the test bypasses the dock-pane button handlers
    // (which pop MessageBoxes on completion) and calls the underlying services
    // directly. The dock pane wrappers are mostly UI orchestration around these
    // services; the actual logic lives in the services so calling them directly
    // covers the same behaviour without the dialogs.
    //
    // Workflow:
    //   1. Open A-01 CONSTRUCTION DRAWINGS.idw
    //   2. Resolve the master assembly programmatically (walk first view)
    //   3. Activate sheet whose name contains "A-10B" (sheet 21 in your dataset)
    //   4. ViewCoverageService.Run #1 → expect MissingPartNumbers > 0
    //      (you've deleted some parts so coverage flags them)
    //   5. BomTraversalService.GetBomEntries from view 1 of the active sheet
    //   6. Add a new parts sheet (replicates AddPartsSheet logic)
    //   7. For each part in the BOM:
    //        ViewPlacementService.Place a base view
    //        For the second part: also insert a flat pattern via DrawingViews.AddBaseView
    //   8. ViewCoverageService.Run #2 → expect MissingPartNumbers == 0
    //   9. SheetRenamerService.Run → expect Success
    //  10. PartsListSortService.Run → expect Success
    //
    // Marked IFixtureTestCase + IInteractiveTestCase. Although fully programmatic,
    // it shares the workspace with other interactive tests so the same skip rules
    // apply (TestMode.RunInteractiveTests = false → skipped).

    internal sealed class DrawingTools_PartInsertionWorkflow : IFixtureTestCase, IInteractiveTestCase
    {
        public string Name => nameof(DrawingTools_PartInsertionWorkflow);

        private const string FixturePath = @"Drawings\A-01 CONSTRUCTION DRAWINGS.idw";
        private const string TargetSheetNameContains = "A-10B";

        public void Run(Application app)
        {
            var doc = (DrawingDocument)FixtureWorkspace.OpenFile(app, FixturePath, visible: true);
            try { doc.Activate(); } catch { }

            InteractivePrompt.Toast(
                "Drawing Tools — Part Insertion Workflow",
                "Programmatic test. No clicks required. Watch Inventor work through the sequence.",
                4000);

            // ----- Resolve master assembly programmatically -----
            var asm = ResolveAssemblyFromDrawing(doc);
            TestAssert.NotNull(asm);

            // ----- Step 1: Move to target sheet (A-10B) -----
            var targetSheet = FindSheetByNameContains(doc, TargetSheetNameContains);
            TestAssert.NotNull(targetSheet);
            try { targetSheet!.Activate(); } catch { }
            try { doc.Activate(); } catch { }

            // ----- Step 2: View coverage check #1 — should report missing parts -----
            //
            // Load the same SkipComments set the production button uses. Without this,
            // coverage flags all content-center fasteners (washers, bolts, nuts) because
            // nothing tells it to ignore parts commented as "FASTENERS"/"FASTENER".
            var viewCoverage = new ViewCoverageService();
            var skipComments = LoadViewCoverageSkipComments();
            var coverageOptions = new ViewCoverageService.Options
            {
                IncludeStructuredTopLevel = true,
                IncludePartsOnly = true,
                StructuredFirstLevelOnly = true,
                EnablePartsOnlyView = true,
                IncludeBlankPartNumbers = true,
                SkipComments = skipComments
            };

            var coverage1 = viewCoverage.Run(doc, asm!, coverageOptions);
            TestAssert.True(coverage1.Success,
                $"View coverage #1 should have succeeded. Warnings: {string.Join("; ", coverage1.Warnings)}");
            int initialMissing = coverage1.MissingPartNumbers.Count;
            TestAssert.True(initialMissing > 0,
                $"Expected at least one missing part on first coverage check, got {initialMissing}. " +
                "Did the source dataset still have parts deleted from sheet A-10B?");

            // ----- Step 3: Load BOM from current drawing view -----
            // Replicates LoadBomFromCurrentDrawing: take view 1 of the active sheet,
            // resolve to the referenced assembly, and walk its structured BOM.
            TestAssert.True(targetSheet!.DrawingViews.Count > 0,
                "Target sheet should have at least one drawing view to load the BOM from.");

            var view1 = targetSheet.DrawingViews[1];
            object? refDocObj = null;
            try { refDocObj = view1.ReferencedDocumentDescriptor?.ReferencedDocument; } catch { }
            TestAssert.True(refDocObj is AssemblyDocument,
                "View 1 of the target sheet should reference an AssemblyDocument.");
            var asmFromView = (AssemblyDocument)refDocObj!;

            var bomSvc = new BomTraversalService();
            var entries = bomSvc.GetBomEntries(
                asmFromView,
                BomTraversalService.BomViewKind.Structured,
                new BomTraversalService.Options
                {
                    StructuredFirstLevelOnly = true,
                    DeduplicateByPartNumber = true
                });

            var sortedParts = entries
                .OrderBy(e => e.PartNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            TestAssert.True(sortedParts.Count >= 1,
                $"BOM should have at least 1 part, got {sortedParts.Count}.");

            // ----- Step 4: Add a new parts sheet (replicates AddPartsSheet) -----
            int sheetCountBefore = doc.Sheets.Count;
            string title = GetSafeProperty(asmFromView, "Design Tracking Properties", "Part Number")
                           ?? asmFromView.DisplayName ?? "Parts";
            int nextIndex = sheetCountBefore + 1;
            string newSheetName = $"{title} Parts {nextIndex}";

            var oldActive = doc.ActiveSheet;
            var newSheet = doc.Sheets.Add(
                oldActive.Size,
                oldActive.Orientation,
                newSheetName,
                oldActive.Width,
                oldActive.Height);

            // Copy border + title block from the previously active sheet
            try
            {
                var borderDef = oldActive.Border?.Definition;
                if (borderDef != null) newSheet.AddBorder(borderDef);
            }
            catch { }

            try
            {
                var tbDef = ResolveTitleBlockDefinition(
                    doc,
                    AddinSettings.Current.DrawingPartsTitleBlockName,
                    "RMAC Parts Title");
                if (tbDef != null) newSheet.AddTitleBlock(tbDef);
            }
            catch { }

            try { newSheet.Activate(); } catch { }
            try { doc.Activate(); } catch { }

            TestAssert.Equal(sheetCountBefore + 1, doc.Sheets.Count);

            // ----- Step 5: Insert all parts. Second part also gets a flat pattern. -----
            var viewPlacer = new ViewPlacementService(app);
            int viewsBefore = newSheet.DrawingViews.Count;
            int flatPatternsCreated = 0;

            for (int i = 0; i < sortedParts.Count; i++)
            {
                var part = sortedParts[i];
                var modelDoc = part.ComponentDefinition.Document as Document;
                TestAssert.NotNull(modelDoc);

                var placeRes = viewPlacer.Place(new ViewPlacementService.PlaceViewsRequest
                {
                    Drawing = doc,
                    Sheet = newSheet,
                    ModelDocument = modelDoc!,
                    StartX = -10,
                    StartY = 10,
                    CreateTop = false,
                    CreateRight = false,
                    CreateIso = false,
                    BaseViewOrientation = ViewPlacementService.BaseOrientation.Front,
                    ViewStyle = DrawingViewStyleEnum.kHiddenLineDrawingViewStyle,
                    Scale = 1.0,
                    ClearExistingViews = false
                });

                TestAssert.True(placeRes.Success,
                    $"Place base view for part '{part.PartNumber}' (index {i}) failed: " +
                    string.Join("; ", placeRes.Warnings));

                // Second part (i == 1) also gets a flat pattern view
                if (i == 1)
                {
                    var partDoc = part.ComponentDefinition.Document as PartDocument;
                    TestAssert.NotNull(partDoc);

                    var smcd = partDoc!.ComponentDefinition as SheetMetalComponentDefinition;
                    TestAssert.True(smcd != null,
                        $"Second part '{part.PartNumber}' should be a sheet metal part for flat pattern insertion.");

                    var tgeom = app.TransientGeometry;
                    var pt = tgeom.CreatePoint2d(20, 10);

                    var options = app.TransientObjects.CreateNameValueMap();
                    options.Add("SheetMetalFoldedModel", false);

                    if (!smcd!.HasFlatPattern)
                        smcd.Unfold();

                    newSheet.DrawingViews.AddBaseView(
                        (_Document)partDoc,
                        pt,
                        1,
                        ViewOrientationTypeEnum.kDefaultViewOrientation,
                        DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle,
                        null,
                        null,
                        options);

                    flatPatternsCreated++;
                }
            }

            int viewsAfter = newSheet.DrawingViews.Count;
            int expectedNewViews = sortedParts.Count + flatPatternsCreated;
            TestAssert.Equal(viewsBefore + expectedNewViews, viewsAfter);

            // ----- Step 6: View coverage check #2 — should now be clean -----
            // Force the drawing to update so the new views are reflected in coverage analysis
            try { doc.Update(); } catch { }
            try { ((_Document)doc).Update(); } catch { }

            var coverage2 = viewCoverage.Run(doc, asm!, coverageOptions);
            TestAssert.True(coverage2.Success,
                $"View coverage #2 should have succeeded. Warnings: {string.Join("; ", coverage2.Warnings)}");

            // With the skip-comments list loaded (matching production), coverage should
            // now report exactly zero missing parts after we've inserted the previously
            // missing ones on the new sheet. If it isn't zero, the diagnostic tells us
            // which parts are still flagged so we can figure out why.
            if (coverage2.MissingPartNumbers.Count != 0)
            {
                var insertedPartNumbers = sortedParts.Select(p => p.PartNumber).ToList();
                var insertedStillMissing = coverage2.MissingPartNumbers
                    .Where(m => insertedPartNumbers.Contains(m, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                var otherMissing = coverage2.MissingPartNumbers
                    .Where(m => !insertedPartNumbers.Contains(m, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                throw new TestAssertException(
                    $"View coverage after insertion: expected 0 missing, got {coverage2.MissingPartNumbers.Count}. " +
                    $"Initial missing: {initialMissing}, BOM: {sortedParts.Count}, skip list size: {skipComments.Count}. " +
                    $"Of the {insertedPartNumbers.Count} inserted parts, still missing: {insertedStillMissing.Count} " +
                    $"[{string.Join(", ", insertedStillMissing.Take(10))}]. " +
                    $"Other still missing: {otherMissing.Count} [{string.Join(", ", otherMissing.Take(10))}].");
            }

            // ----- Step 7: Rename Sheets -----
            var sheetRenamer = new SheetRenamerService();
            var renameResult = sheetRenamer.Run(doc, new SheetRenamerService.Options
            {
                PartsTitleBlockName = AddinSettings.Current.DrawingPartsTitleBlockName,
                EnableLog = true,
                ReorderSheets = true,
                OrderingMode = AddinSettings.Current.SheetOrderingMode
            });
            TestAssert.True(renameResult.Success,
                $"Rename Sheets should have succeeded. Status: {renameResult.Status}");

            // ----- Step 8: Sort BOMs (Parts Lists) -----
            var partsListSort = new PartsListSortService();
            var sortResult = partsListSort.Run(doc, new PartsListSortService.Options
            {
                SortColumnContainsText = "Part Number",
                SortAscending = true,
                OnlyAssemblyPartsLists = true,
                RenumberAfterSort = true
            });
            TestAssert.True(sortResult.Success,
                $"Sort BOMs should have succeeded. Warnings: {string.Join("; ", sortResult.Warnings)}");

            InteractivePrompt.Toast(
                "Part Insertion Workflow Complete",
                $"Inserted {expectedNewViews} views ({sortedParts.Count} base + {flatPatternsCreated} flat pattern). " +
                $"Coverage went {initialMissing}→0. Sort processed {sortResult.Processed} parts lists.",
                5000);
        }

        // ============================================================
        // Helpers
        // ============================================================

        /// <summary>
        /// Builds the same SkipComments set the production "Check Views Coverage" button
        /// uses (via <c>DrawingDockPane.LoadViewCoverageSkipComments</c>). Without this,
        /// the coverage check flags every content-center fastener because nothing tells
        /// it to ignore parts commented as "FASTENERS" / "FASTENER" / etc.
        ///
        /// Mirrors the exact production logic:
        ///   1. Read from AddinSettings.Current.CommentsOptions.SkipCommentsForViewCoverage
        ///   2. Sanitise + dedupe
        ///   3. Fall back to the hardcoded { "FASTENERS", "FASTENER" } pair on any read failure
        /// </summary>
        private static HashSet<string> LoadViewCoverageSkipComments()
        {
            try
            {
                AddinSettings.EnsureLoaded();
                var opts = AddinSettings.Current.CommentsOptions;
                opts.SanitizeInPlace();

                return new HashSet<string>(
                    (opts.SkipCommentsForViewCoverage ?? Enumerable.Empty<string>())
                        .Select(x => (x ?? "").Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FASTENERS", "FASTENER" };
            }
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

        private static string? GetSafeProperty(object docOrDef, string propSetName, string propName)
        {
            try
            {
                dynamic d = docOrDef;
                var ps = d.PropertySets[propSetName];
                var p = ps[propName];
                return p.Value?.ToString();
            }
            catch { return null; }
        }

        private static TitleBlockDefinition? ResolveTitleBlockDefinition(DrawingDocument dd, string? preferred, string fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    foreach (TitleBlockDefinition def in dd.TitleBlockDefinitions)
                        if (string.Equals(def.Name, preferred, StringComparison.OrdinalIgnoreCase))
                            return def;
                }
                foreach (TitleBlockDefinition def in dd.TitleBlockDefinitions)
                    if (string.Equals(def.Name, fallback, StringComparison.OrdinalIgnoreCase))
                        return def;

                if (dd.TitleBlockDefinitions.Count > 0)
                    return dd.TitleBlockDefinitions[1];
            }
            catch { }
            return null;
        }
    }
}
#endif
