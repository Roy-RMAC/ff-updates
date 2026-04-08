#if TEST_HARNESS
using Inventor;
using RMAC_Efficiency_Addin.DrawingDock.Services;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for SelectionService.GetSelectionSnapshot(Document?) and
    // .TryResolveDrawingViewFromSelection(Document?) — the static, document-explicit overloads
    // added in Phase 4.
    //
    // These verify graceful behaviour with a null document. The "with a real drawing"
    // positive cases live in Wave 5.

    internal sealed class SelectionService_GetSnapshot_NullDocReturnsEmpty : ITestCase
    {
        public string Name => nameof(SelectionService_GetSnapshot_NullDocReturnsEmpty);
        public void Run(Application app)
        {
            var snapshot = SelectionService.GetSelectionSnapshot((Document?)null);
            TestAssert.NotNull(snapshot);
            TestAssert.Equal(0, snapshot.Count);
        }
    }

    internal sealed class SelectionService_TryResolveFromSelection_NullDocReturnsNull : ITestCase
    {
        public string Name => nameof(SelectionService_TryResolveFromSelection_NullDocReturnsNull);
        public void Run(Application app)
        {
            var view = SelectionService.TryResolveDrawingViewFromSelection((Document?)null);
            TestAssert.Null(view);
        }
    }
}
#endif
