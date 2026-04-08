#if TEST_HARNESS
using Inventor;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for RmacPartNumberingRenumberer.MapSkipSelection.
    //
    // Pure string parser: splits a "Set.Prop" selection string into a property set name
    // and a property name, with some special-case friendly → canonical mappings:
    //   "Project.X"                        -> "Design Tracking Properties", "X"
    //   "Summary Information.X"            -> "Summary Information",        "X"
    //   "Document Summary Information.X"   -> "Inventor Document Summary Information", "X"
    //   "Design Tracking Properties.X"     -> "Design Tracking Properties", "X"  (pass-through)
    //   "" / null                          -> "", ""
    //   "X"  (no dot)                      -> "", "X"  (treated as "search all sets")
    //
    // MapSkipSelection was made internal in Phase 7 so tests can reach it directly.

    internal sealed class MapSkipSelection_NullReturnsEmpty : ITestCase
    {
        public string Name => nameof(MapSkipSelection_NullReturnsEmpty);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection(null!, out var set, out var prop);
            TestAssert.Equal("", set);
            TestAssert.Equal("", prop);
        }
    }

    internal sealed class MapSkipSelection_EmptyReturnsEmpty : ITestCase
    {
        public string Name => nameof(MapSkipSelection_EmptyReturnsEmpty);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection("", out var set, out var prop);
            TestAssert.Equal("", set);
            TestAssert.Equal("", prop);
        }
    }

    internal sealed class MapSkipSelection_NoDotTreatsAsPropertyName : ITestCase
    {
        public string Name => nameof(MapSkipSelection_NoDotTreatsAsPropertyName);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection("SomeProp", out var set, out var prop);
            TestAssert.Equal("", set);
            TestAssert.Equal("SomeProp", prop);
        }
    }

    internal sealed class MapSkipSelection_ProjectMapsToDesignTracking : ITestCase
    {
        public string Name => nameof(MapSkipSelection_ProjectMapsToDesignTracking);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection("Project.Description", out var set, out var prop);
            TestAssert.Equal("Design Tracking Properties", set);
            TestAssert.Equal("Description", prop);
        }
    }

    internal sealed class MapSkipSelection_SummaryInformationPassesThrough : ITestCase
    {
        public string Name => nameof(MapSkipSelection_SummaryInformationPassesThrough);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection("Summary Information.Title", out var set, out var prop);
            TestAssert.Equal("Summary Information", set);
            TestAssert.Equal("Title", prop);
        }
    }

    internal sealed class MapSkipSelection_DocumentSummaryRemaps : ITestCase
    {
        public string Name => nameof(MapSkipSelection_DocumentSummaryRemaps);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection("Document Summary Information.Category", out var set, out var prop);
            TestAssert.Equal("Inventor Document Summary Information", set);
            TestAssert.Equal("Category", prop);
        }
    }

    internal sealed class MapSkipSelection_DesignTrackingPassesThrough : ITestCase
    {
        public string Name => nameof(MapSkipSelection_DesignTrackingPassesThrough);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection("Design Tracking Properties.Stock Number", out var set, out var prop);
            TestAssert.Equal("Design Tracking Properties", set);
            TestAssert.Equal("Stock Number", prop);
        }
    }

    internal sealed class MapSkipSelection_CaseInsensitiveSetMatch : ITestCase
    {
        public string Name => nameof(MapSkipSelection_CaseInsensitiveSetMatch);
        public void Run(Application app)
        {
            RmacPartNumberingRenumberer.MapSkipSelection("project.Description", out var set, out var prop);
            TestAssert.Equal("Design Tracking Properties", set);
            TestAssert.Equal("Description", prop);
        }
    }
}
#endif
