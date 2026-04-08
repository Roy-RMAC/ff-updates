#if TEST_HARNESS
namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Marker interface for tests that REQUIRE user input to run (clicking entities,
    /// dismissing modal dialogs from production code, etc.). When
    /// <see cref="TestMode.RunInteractiveTests"/> is false, the runner skips these
    /// instead of running them — useful for unattended runs while you're focused on
    /// debugging non-interactive tests.
    ///
    /// A test can implement BOTH <see cref="IFixtureTestCase"/> AND this interface
    /// (e.g. it needs the workspace AND user input). The runner handles the combination.
    /// </summary>
    internal interface IInteractiveTestCase : ITestCase
    {
    }
}
#endif
