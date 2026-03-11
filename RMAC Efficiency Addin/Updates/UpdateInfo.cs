using System.Text.Json.Serialization;

namespace RMAC_Efficiency_Addin.Updates
{
    internal sealed class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; } = "";
    }
}
