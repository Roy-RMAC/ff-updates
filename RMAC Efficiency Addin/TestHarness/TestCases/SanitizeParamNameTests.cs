#if TEST_HARNESS
using Inventor;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for SheetMetalThicknessCore.SanitizeUserParameterName.
    //
    // Pure string-in / string-out logic. No fixture, no live Inventor API calls.
    // Each case is its own ITestCase so failures are isolated and named individually in the CSV.
    //
    // Target behaviour (from SheetMetalThicknessCore.cs):
    //   - null/whitespace  -> DefaultThicknessParamName ("THICKNESS")
    //   - spaces, hyphens, dots, slashes, colons, semicolons -> replaced with '_'
    //   - non-alphanumeric (other) chars -> stripped
    //   - consecutive underscores -> collapsed
    //   - leading digit -> prefixed with "T_"
    //   - capped at 64 chars

    internal sealed class SanitizeParamName_NullReturnsDefault : ITestCase
    {
        public string Name => nameof(SanitizeParamName_NullReturnsDefault);
        public void Run(Application app)
        {
            var result = SheetMetalThicknessCore.SanitizeUserParameterName(null);
            TestAssert.Equal("THICKNESS", result);
        }
    }

    internal sealed class SanitizeParamName_EmptyReturnsDefault : ITestCase
    {
        public string Name => nameof(SanitizeParamName_EmptyReturnsDefault);
        public void Run(Application app)
        {
            var result = SheetMetalThicknessCore.SanitizeUserParameterName("");
            TestAssert.Equal("THICKNESS", result);
        }
    }

    internal sealed class SanitizeParamName_WhitespaceReturnsDefault : ITestCase
    {
        public string Name => nameof(SanitizeParamName_WhitespaceReturnsDefault);
        public void Run(Application app)
        {
            var result = SheetMetalThicknessCore.SanitizeUserParameterName("   ");
            TestAssert.Equal("THICKNESS", result);
        }
    }

    internal sealed class SanitizeParamName_ValidNameUnchanged : ITestCase
    {
        public string Name => nameof(SanitizeParamName_ValidNameUnchanged);
        public void Run(Application app)
        {
            var result = SheetMetalThicknessCore.SanitizeUserParameterName("MY_THK");
            TestAssert.Equal("MY_THK", result);
        }
    }

    internal sealed class SanitizeParamName_SpacesBecomeUnderscores : ITestCase
    {
        public string Name => nameof(SanitizeParamName_SpacesBecomeUnderscores);
        public void Run(Application app)
        {
            var result = SheetMetalThicknessCore.SanitizeUserParameterName("my thickness");
            TestAssert.Equal("my_thickness", result);
        }
    }

    internal sealed class SanitizeParamName_LeadingDigitGetsPrefix : ITestCase
    {
        public string Name => nameof(SanitizeParamName_LeadingDigitGetsPrefix);
        public void Run(Application app)
        {
            var result = SheetMetalThicknessCore.SanitizeUserParameterName("1MM_THK");
            TestAssert.Equal("T_1MM_THK", result);
        }
    }

    internal sealed class SanitizeParamName_OnlyIllegalCharsReturnsDefault : ITestCase
    {
        public string Name => nameof(SanitizeParamName_OnlyIllegalCharsReturnsDefault);
        public void Run(Application app)
        {
            // "@#$%" — all illegal, nothing salvageable, should fall back to default
            var result = SheetMetalThicknessCore.SanitizeUserParameterName("@#$%");
            TestAssert.Equal("THICKNESS", result);
        }
    }
}
#endif
