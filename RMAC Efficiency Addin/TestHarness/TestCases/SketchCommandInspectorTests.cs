#if TEST_HARNESS
using Inventor;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for SketchCommandInspector.FindMatches.
    //
    // Walks Inventor's CommandManager.ControlDefinitions and filters by keyword. Needs a
    // live Application but no specific document — Inventor's command catalog is available
    // from startup.
    //
    // Assertions are intentionally loose: we don't pin a specific count because Autodesk
    // can rename/add/remove commands between versions. We assert "more than zero" and
    // "respects the cap" — both are meaningful sanity checks without being brittle.

    internal sealed class SketchCommandInspector_FindMatches_ReturnsResults : ITestCase
    {
        public string Name => nameof(SketchCommandInspector_FindMatches_ReturnsResults);
        public void Run(Application app)
        {
            var hits = SketchCommandInspector.FindMatches(app);
            TestAssert.NotNull(hits);
            TestAssert.True(hits.Count > 0,
                "Expected at least one matching sketch command in Inventor's ControlDefinitions.");
        }
    }

    internal sealed class SketchCommandInspector_FindMatches_RespectsMaxResults : ITestCase
    {
        public string Name => nameof(SketchCommandInspector_FindMatches_RespectsMaxResults);
        public void Run(Application app)
        {
            var hits = SketchCommandInspector.FindMatches(app, maxResults: 5);
            TestAssert.NotNull(hits);
            TestAssert.True(hits.Count <= 5,
                $"Expected at most 5 results, got {hits.Count}.");
        }
    }

    internal sealed class SketchCommandInspector_FindMatches_EntryFormat : ITestCase
    {
        public string Name => nameof(SketchCommandInspector_FindMatches_EntryFormat);
        public void Run(Application app)
        {
            var hits = SketchCommandInspector.FindMatches(app, maxResults: 1);
            TestAssert.True(hits.Count > 0, "Need at least one entry to verify format.");

            // Format from FindMatches: "{name} | {disp} | {desc}"
            // Two pipe separators means three components.
            var entry = hits[0];
            int pipeCount = 0;
            foreach (var c in entry)
                if (c == '|') pipeCount++;

            TestAssert.True(pipeCount >= 2,
                $"Expected at least 2 pipe separators in entry, got {pipeCount}: '{entry}'");
        }
    }
}
#endif
