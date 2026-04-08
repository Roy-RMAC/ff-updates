#if TEST_HARNESS
using Inventor;
using RMAC_Efficiency_Addin.PartNumbering;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for FrameMemberMatcher.IsMatch(FrameData, FrameData).
    //
    // IsMatch is pure — takes two data objects, returns bool. The Extract method takes live
    // Inventor docs and is NOT tested here.
    //
    // The matcher runs three steps and ALL must pass:
    //   Step1: Material / Stock / Length equal + Mass/Volume/Area within tolerance
    //   Step2: Inertia tensor within 0.0001 tolerance
    //   Step3: Centre-of-mass relationship (identity, mirror, or BBZ-flip)
    //
    // We build a known-good reference FrameData, then flip specific fields for negative cases.

    internal static class FrameMemberMatcherTestHelpers
    {
        // Reference frame: a plausible "steel RHS 40x40x3, 1000mm long" member.
        // All values are arbitrary but stable — mutate individual fields in each test.
        public static FrameMemberMatcher.FrameData MakeReferenceFrame()
        {
            return new FrameMemberMatcher.FrameData
            {
                Material = "Steel",
                Stock = "RHS 40x40x3",
                Length = "1000",

                Mass = 3.45,
                Volume = 440.0,
                Area = 160000.0,

                Ixx = 0.0123,
                Iyy = 0.0123,
                Izz = 0.0005,

                COG_X = 0.0,
                COG_Y = 0.0,
                COG_Z = 50.0,

                BBZ = 100.0,
            };
        }
    }

    internal sealed class FrameMemberMatcher_IsMatch_IdenticalDataMatches : ITestCase
    {
        public string Name => nameof(FrameMemberMatcher_IsMatch_IdenticalDataMatches);
        public void Run(Application app)
        {
            var m = new FrameMemberMatcher();
            var a = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            var b = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            TestAssert.True(m.IsMatch(a, b));
        }
    }

    internal sealed class FrameMemberMatcher_IsMatch_DifferentMaterialFails : ITestCase
    {
        public string Name => nameof(FrameMemberMatcher_IsMatch_DifferentMaterialFails);
        public void Run(Application app)
        {
            var m = new FrameMemberMatcher();
            var a = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            var b = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            b.Material = "Aluminium";
            TestAssert.False(m.IsMatch(a, b));
        }
    }

    internal sealed class FrameMemberMatcher_IsMatch_DifferentStockFails : ITestCase
    {
        public string Name => nameof(FrameMemberMatcher_IsMatch_DifferentStockFails);
        public void Run(Application app)
        {
            var m = new FrameMemberMatcher();
            var a = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            var b = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            b.Stock = "RHS 50x50x3";
            TestAssert.False(m.IsMatch(a, b));
        }
    }

    internal sealed class FrameMemberMatcher_IsMatch_DifferentLengthFails : ITestCase
    {
        public string Name => nameof(FrameMemberMatcher_IsMatch_DifferentLengthFails);
        public void Run(Application app)
        {
            var m = new FrameMemberMatcher();
            var a = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            var b = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            b.Length = "1500";
            TestAssert.False(m.IsMatch(a, b));
        }
    }

    internal sealed class FrameMemberMatcher_IsMatch_MassWithinToleranceStillMatches : ITestCase
    {
        public string Name => nameof(FrameMemberMatcher_IsMatch_MassWithinToleranceStillMatches);
        public void Run(Application app)
        {
            var m = new FrameMemberMatcher();
            var a = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            var b = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            b.Mass += 0.005; // within < 0.01 tolerance
            TestAssert.True(m.IsMatch(a, b));
        }
    }

    internal sealed class FrameMemberMatcher_IsMatch_MassBeyondToleranceFails : ITestCase
    {
        public string Name => nameof(FrameMemberMatcher_IsMatch_MassBeyondToleranceFails);
        public void Run(Application app)
        {
            var m = new FrameMemberMatcher();
            var a = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            var b = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            b.Mass += 0.1; // well beyond 0.01 tolerance
            TestAssert.False(m.IsMatch(a, b));
        }
    }

    internal sealed class FrameMemberMatcher_IsMatch_InertiaBeyondToleranceFails : ITestCase
    {
        public string Name => nameof(FrameMemberMatcher_IsMatch_InertiaBeyondToleranceFails);
        public void Run(Application app)
        {
            var m = new FrameMemberMatcher();
            var a = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            var b = FrameMemberMatcherTestHelpers.MakeReferenceFrame();
            b.Ixx += 0.001; // beyond 0.0001 tolerance
            TestAssert.False(m.IsMatch(a, b));
        }
    }

}
#endif
