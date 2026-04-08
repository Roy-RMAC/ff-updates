#if TEST_HARNESS
using Inventor;

namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Contract for a single self-test case. Implementations are instantiated and run by
    /// <see cref="SelfTestRunner"/>. A test passes by returning normally and fails by throwing
    /// any exception — the runner records the exception message as the failure reason.
    ///
    /// Use <see cref="TestAssert"/> helpers for readable assertions inside <c>Run</c>.
    /// </summary>
    internal interface ITestCase
    {
        /// <summary>Unique, human-readable name recorded in the results CSV.</summary>
        string Name { get; }

        /// <summary>
        /// Execute the test. Throw on failure. The passed <paramref name="app"/> is the live
        /// Inventor application; pure-logic tests may ignore it.
        /// </summary>
        void Run(Application app);
    }
}
#endif
