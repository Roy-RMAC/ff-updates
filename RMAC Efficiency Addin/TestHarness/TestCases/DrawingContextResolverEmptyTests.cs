#if TEST_HARNESS
using Inventor;
using RMAC_Efficiency_Addin.DrawingDock.Services;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for DrawingContextResolver.GetContext(Document?) — null/invalid input paths.
    //
    // The "with a real drawing" cases are Wave 5 (need DrawingSingleSheet.idw fixture).
    // What we can verify here without a fixture: graceful failure when the doc is null
    // or doesn't make sense as a drawing.

    internal sealed class DrawingContextResolver_GetContext_NullDocReturnsInvalid : ITestCase
    {
        public string Name => nameof(DrawingContextResolver_GetContext_NullDocReturnsInvalid);
        public void Run(Application app)
        {
            var resolver = new DrawingContextResolver(app);
            var ctx = resolver.GetContext((Document?)null);

            TestAssert.NotNull(ctx);
            TestAssert.False(ctx.IsValidDrawingContext);
            TestAssert.NotNull(ctx.ReasonIfInvalid);
            TestAssert.True(ctx.ReasonIfInvalid.Length > 0,
                "Expected a non-empty ReasonIfInvalid for a null doc.");
        }
    }

    internal sealed class DrawingContextResolver_GetContext_NullDocPopulatesEmptySelection : ITestCase
    {
        public string Name => nameof(DrawingContextResolver_GetContext_NullDocPopulatesEmptySelection);
        public void Run(Application app)
        {
            var resolver = new DrawingContextResolver(app);
            var ctx = resolver.GetContext((Document?)null);

            // Even on an invalid context, SelectionItems should be a non-null empty list,
            // not null — caller code expects it to be safe to iterate.
            TestAssert.NotNull(ctx.SelectionItems);
            TestAssert.Equal(0, ctx.SelectionItems.Count);
        }
    }

    internal sealed class DrawingContextResolver_GetContext_NullDocLeavesViewsNull : ITestCase
    {
        public string Name => nameof(DrawingContextResolver_GetContext_NullDocLeavesViewsNull);
        public void Run(Application app)
        {
            var resolver = new DrawingContextResolver(app);
            var ctx = resolver.GetContext((Document?)null);

            // Nothing should be populated when the doc is null
            TestAssert.Null(ctx.DrawingDocument);
            TestAssert.Null(ctx.ActiveSheet);
            TestAssert.Null(ctx.SelectedView);
            TestAssert.Null(ctx.ReferencedModelDocument);
        }
    }
}
#endif
