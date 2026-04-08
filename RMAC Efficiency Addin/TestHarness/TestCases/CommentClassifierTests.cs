#if TEST_HARNESS
using Inventor;
using RMAC_Efficiency_Addin.Exporting.Services;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for CommentClassifier.Normalize and .EqualsCategory.
    //
    // Both are pure string helpers — no doc access, no Inventor API.
    // GetCategory(Document) is NOT tested here: it needs a live document and belongs to a
    // later fixture-based wave.
    //
    // Default ForceUpperInvariant is false — keep it that way for these tests unless we
    // explicitly flip it inside a case.

    internal sealed class CommentClassifier_Normalize_NullReturnsEmpty : ITestCase
    {
        public string Name => nameof(CommentClassifier_Normalize_NullReturnsEmpty);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.Equal("", cc.Normalize(null));
        }
    }

    internal sealed class CommentClassifier_Normalize_TrimsWhitespace : ITestCase
    {
        public string Name => nameof(CommentClassifier_Normalize_TrimsWhitespace);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.Equal("PROFILE", cc.Normalize("  PROFILE  "));
        }
    }

    internal sealed class CommentClassifier_Normalize_PreservesCaseByDefault : ITestCase
    {
        public string Name => nameof(CommentClassifier_Normalize_PreservesCaseByDefault);
        public void Run(Application app)
        {
            var cc = new CommentClassifier(); // ForceUpperInvariant default = false
            TestAssert.Equal("Profile", cc.Normalize("Profile"));
        }
    }

    internal sealed class CommentClassifier_Normalize_ForceUpperInvariantUpperCases : ITestCase
    {
        public string Name => nameof(CommentClassifier_Normalize_ForceUpperInvariantUpperCases);
        public void Run(Application app)
        {
            var cc = new CommentClassifier { ForceUpperInvariant = true };
            TestAssert.Equal("PROFILE", cc.Normalize("Profile"));
        }
    }

    internal sealed class CommentClassifier_EqualsCategory_IdenticalMatches : ITestCase
    {
        public string Name => nameof(CommentClassifier_EqualsCategory_IdenticalMatches);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.True(cc.EqualsCategory("PROFILE", "PROFILE"));
        }
    }

    internal sealed class CommentClassifier_EqualsCategory_CaseInsensitive : ITestCase
    {
        public string Name => nameof(CommentClassifier_EqualsCategory_CaseInsensitive);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.True(cc.EqualsCategory("Profile", "PROFILE"));
        }
    }

    internal sealed class CommentClassifier_EqualsCategory_IgnoresSurroundingWhitespace : ITestCase
    {
        public string Name => nameof(CommentClassifier_EqualsCategory_IgnoresSurroundingWhitespace);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.True(cc.EqualsCategory("  PROFILE", "PROFILE  "));
        }
    }

    internal sealed class CommentClassifier_EqualsCategory_DifferentValuesDoNotMatch : ITestCase
    {
        public string Name => nameof(CommentClassifier_EqualsCategory_DifferentValuesDoNotMatch);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.False(cc.EqualsCategory("PROFILE", "PRESS"));
        }
    }

    internal sealed class CommentClassifier_EqualsCategory_BothNullMatch : ITestCase
    {
        public string Name => nameof(CommentClassifier_EqualsCategory_BothNullMatch);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.True(cc.EqualsCategory(null, null));
        }
    }

    internal sealed class CommentClassifier_EqualsCategory_NullEqualsEmpty : ITestCase
    {
        public string Name => nameof(CommentClassifier_EqualsCategory_NullEqualsEmpty);
        public void Run(Application app)
        {
            var cc = new CommentClassifier();
            TestAssert.True(cc.EqualsCategory(null, ""));
        }
    }
}
#endif
