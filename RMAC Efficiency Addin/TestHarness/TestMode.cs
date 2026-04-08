#if TEST_HARNESS
namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Compile-time toggles for the self-test harness.
    /// </summary>
    internal static class TestMode
    {
        /// <summary>
        /// When true (default), <see cref="IInteractiveTestCase"/> tests are executed.
        /// They require user input (clicking sketches, dismissing dialogs) and so cannot
        /// run unattended.
        ///
        /// Flip to false when you're actively troubleshooting OTHER tests and don't want
        /// the suite to pause on every interactive test in the meantime. Interactive tests
        /// then get recorded as SKIP with a clear reason.
        ///
        /// IMPORTANT: leave this true for any release-candidate run, otherwise the
        /// interactive features (custom derive, etc.) get zero coverage.
        /// </summary>
        public const bool RunInteractiveTests = true;
    }
}
#endif
