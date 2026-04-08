#if TEST_HARNESS
using Inventor;
using RMAC_Efficiency_Addin.Licensing;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for MachineFingerprint.Get().
    //
    // The fingerprint is a SHA-256 hex digest of WMI-derived hardware identifiers, falling
    // back to MachineName + UserName if WMI is unavailable. Either path produces a stable
    // 64-character lowercase hex string.
    //
    // We can't assert a specific value (hardware varies), but we CAN assert shape + stability.

    internal sealed class MachineFingerprint_Get_ReturnsNonEmpty : ITestCase
    {
        public string Name => nameof(MachineFingerprint_Get_ReturnsNonEmpty);
        public void Run(Application app)
        {
            var fp = MachineFingerprint.Get();
            TestAssert.NotNull(fp);
            TestAssert.True(fp.Length > 0, "Fingerprint should be non-empty.");
        }
    }

    internal sealed class MachineFingerprint_Get_Is64HexChars : ITestCase
    {
        public string Name => nameof(MachineFingerprint_Get_Is64HexChars);
        public void Run(Application app)
        {
            var fp = MachineFingerprint.Get();
            TestAssert.Equal(64, fp.Length); // SHA-256 = 32 bytes = 64 hex chars

            foreach (var c in fp)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                TestAssert.True(isHex, $"Character '{c}' is not lowercase hex.");
            }
        }
    }

    internal sealed class MachineFingerprint_Get_StableAcrossCalls : ITestCase
    {
        public string Name => nameof(MachineFingerprint_Get_StableAcrossCalls);
        public void Run(Application app)
        {
            var a = MachineFingerprint.Get();
            var b = MachineFingerprint.Get();
            TestAssert.Equal(a, b);
        }
    }
}
#endif
