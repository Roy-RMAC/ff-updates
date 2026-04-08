#if TEST_HARNESS
using System.Collections.Generic;
using Inventor;
using RMAC_Efficiency_Addin.DrawingDock.Services;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for SelectionService.TryResolveDrawingView(IReadOnlyList<object>).
    //
    // This is the static overload that takes a pre-captured selection snapshot rather than
    // reading from app state. Pure within the limits of what we can test here — real
    // DrawingView inputs need a fixture, but negative cases and graceful handling of
    // non-view inputs can be covered without a drawing.

    internal sealed class SelectionService_ResolveFromList_NullListReturnsNull : ITestCase
    {
        public string Name => nameof(SelectionService_ResolveFromList_NullListReturnsNull);
        public void Run(Application app)
        {
            IReadOnlyList<object> list = null!;
            var result = SelectionService.TryResolveDrawingView(list);
            TestAssert.Null(result);
        }
    }

    internal sealed class SelectionService_ResolveFromList_EmptyListReturnsNull : ITestCase
    {
        public string Name => nameof(SelectionService_ResolveFromList_EmptyListReturnsNull);
        public void Run(Application app)
        {
            var list = new List<object>();
            var result = SelectionService.TryResolveDrawingView(list);
            TestAssert.Null(result);
        }
    }

    internal sealed class SelectionService_ResolveFromList_IrrelevantObjectsReturnsNull : ITestCase
    {
        public string Name => nameof(SelectionService_ResolveFromList_IrrelevantObjectsReturnsNull);
        public void Run(Application app)
        {
            // Strings are definitely not DrawingViews and don't expose Parent/ParentView
            // in a way that would yield one. The resolver should degrade gracefully.
            var list = new List<object> { "foo", 42, new object() };
            var result = SelectionService.TryResolveDrawingView(list);
            TestAssert.Null(result);
        }
    }
}
#endif
