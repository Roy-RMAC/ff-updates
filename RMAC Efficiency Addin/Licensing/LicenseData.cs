using System;
using System.Text.Json.Serialization;

namespace RMAC_Efficiency_Addin.Licensing
{
    internal enum LicenseState
    {
        Activated,
        Expired,
        Deactivated
    }

    internal sealed class LicenseData
    {
        public int DataVersion { get; set; } = 1;

        public string? LicenseKey { get; set; }
        public string? InstanceId { get; set; }
        public string? MachineFingerprint { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public LicenseState State { get; set; }

        public DateTime LastValidationUtc { get; set; }
        public DateTime LastOnlineUtc { get; set; }
        public string? SubscriptionStatus { get; set; }
    }
}
