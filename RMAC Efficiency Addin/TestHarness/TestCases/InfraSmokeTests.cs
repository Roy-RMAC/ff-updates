#if TEST_HARNESS
using System;
using Inventor;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Infrastructure smoke tests — verify the harness scaffolding itself works.
    //
    // These don't test add-in code, they test the test framework. If any of these fail,
    // the harness is broken and every other test result is suspect.

    internal sealed class Infra_App_IsNotNull : ITestCase
    {
        public string Name => nameof(Infra_App_IsNotNull);
        public void Run(Application app)
        {
            TestAssert.NotNull(app);
        }
    }

    internal sealed class Infra_App_HasSoftwareVersion : ITestCase
    {
        public string Name => nameof(Infra_App_HasSoftwareVersion);
        public void Run(Application app)
        {
            var version = app.SoftwareVersion.DisplayVersion;
            TestAssert.NotNull(version);
            TestAssert.True(version.Length > 0, "DisplayVersion should be non-empty.");
        }
    }

    internal sealed class Infra_TestAssert_EqualThrowsOnMismatch : ITestCase
    {
        public string Name => nameof(Infra_TestAssert_EqualThrowsOnMismatch);
        public void Run(Application app)
        {
            // Verify TestAssert.Equal throws as expected. If it silently passed when given
            // mismatched values, every other PASS in this run would be suspect.
            bool threw = false;
            try
            {
                TestAssert.Equal("expected", "actual");
            }
            catch (TestAssertException)
            {
                threw = true;
            }
            TestAssert.True(threw, "TestAssert.Equal should throw on mismatch.");
        }
    }

    internal sealed class Infra_TestAssert_TrueThrowsOnFalse : ITestCase
    {
        public string Name => nameof(Infra_TestAssert_TrueThrowsOnFalse);
        public void Run(Application app)
        {
            bool threw = false;
            try
            {
                TestAssert.True(false);
            }
            catch (TestAssertException)
            {
                threw = true;
            }
            TestAssert.True(threw, "TestAssert.True should throw on false.");
        }
    }

    internal sealed class Infra_ResultObjects_Constructible : ITestCase
    {
        public string Name => nameof(Infra_ResultObjects_Constructible);
        public void Run(Application app)
        {
            // Sanity check: every result type the refactor introduced is reachable from the
            // test assembly and constructible without arguments. Catches accessibility regressions.
            var sketchVis = new SketchVisibilityResult();
            TestAssert.Equal(0, sketchVis.TotalChanged);

            var export = new RMAC_Efficiency_Addin.Exporting.ExportResult();
            TestAssert.False(export.Success);
            TestAssert.NotNull(export.Errors);

            var renumber = new RenumberResult();
            TestAssert.False(renumber.Success);
            TestAssert.NotNull(renumber.Errors);

            var comments = new CheckCommentsResult();
            TestAssert.False(comments.Success);
            TestAssert.NotNull(comments.Errors);
        }
    }
}
#endif
