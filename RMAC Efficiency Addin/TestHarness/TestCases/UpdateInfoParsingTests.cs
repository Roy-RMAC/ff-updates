#if TEST_HARNESS
using System.Text.Json;
using Inventor;
using RMAC_Efficiency_Addin.Updates;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for UpdateInfo JSON deserialization.
    //
    // UpdateChecker.CheckForUpdateAsync ultimately does:
    //     var info = JsonSerializer.Deserialize<UpdateInfo>(json);
    // We exercise that exact code path with in-memory JSON strings — no HTTP.
    //
    // The HTTP and version-comparison parts of UpdateChecker are NOT covered here:
    // they need a real network round-trip and an injected current version, which is
    // a different testing approach (integration tests, not the harness).

    internal sealed class UpdateInfo_Parse_ValidJsonPopulatesAllFields : ITestCase
    {
        public string Name => nameof(UpdateInfo_Parse_ValidJsonPopulatesAllFields);
        public void Run(Application app)
        {
            const string json = """
            {
                "version": "1.2.3",
                "downloadUrl": "https://example.com/installer.exe",
                "releaseNotes": "Bug fixes and improvements"
            }
            """;

            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            TestAssert.NotNull(info);
            TestAssert.Equal("1.2.3", info!.Version);
            TestAssert.Equal("https://example.com/installer.exe", info.DownloadUrl);
            TestAssert.Equal("Bug fixes and improvements", info.ReleaseNotes);
        }
    }

    internal sealed class UpdateInfo_Parse_MissingFieldsDefaultToEmpty : ITestCase
    {
        public string Name => nameof(UpdateInfo_Parse_MissingFieldsDefaultToEmpty);
        public void Run(Application app)
        {
            // Only "version" is present — others should fall back to the property default ("")
            const string json = """{ "version": "2.0.0" }""";

            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            TestAssert.NotNull(info);
            TestAssert.Equal("2.0.0", info!.Version);
            TestAssert.Equal("", info.DownloadUrl);
            TestAssert.Equal("", info.ReleaseNotes);
        }
    }

    internal sealed class UpdateInfo_Parse_EmptyObjectAllDefaults : ITestCase
    {
        public string Name => nameof(UpdateInfo_Parse_EmptyObjectAllDefaults);
        public void Run(Application app)
        {
            const string json = "{}";

            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            TestAssert.NotNull(info);
            TestAssert.Equal("", info!.Version);
            TestAssert.Equal("", info.DownloadUrl);
            TestAssert.Equal("", info.ReleaseNotes);
        }
    }

    internal sealed class UpdateInfo_Parse_RoundTripPreservesValues : ITestCase
    {
        public string Name => nameof(UpdateInfo_Parse_RoundTripPreservesValues);
        public void Run(Application app)
        {
            var original = new UpdateInfo
            {
                Version = "3.14.159",
                DownloadUrl = "https://example.com/v3.exe",
                ReleaseNotes = "Pi release"
            };

            var json = JsonSerializer.Serialize(original);
            var roundTripped = JsonSerializer.Deserialize<UpdateInfo>(json);

            TestAssert.NotNull(roundTripped);
            TestAssert.Equal(original.Version, roundTripped!.Version);
            TestAssert.Equal(original.DownloadUrl, roundTripped.DownloadUrl);
            TestAssert.Equal(original.ReleaseNotes, roundTripped.ReleaseNotes);
        }
    }
}
#endif
