#if TEST_HARNESS
namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Marker interface for tests that require <see cref="FixtureWorkspace"/> to be set up
    /// (i.e. the test project copied to a working location). The runner runs all
    /// non-fixture tests first, then sets up the workspace, runs fixture tests, then tears
    /// down — so non-fixture tests don't pay the copy cost and don't fail just because the
    /// source dataset is missing on a particular machine.
    /// </summary>
    internal interface IFixtureTestCase : ITestCase
    {
    }
}
#endif
