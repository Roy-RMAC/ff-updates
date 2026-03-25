namespace RMAC_Efficiency_Addin.Licensing
{
    internal enum LicenseStatus
    {
        Licensed,
        Expired,
        NeedsActivation
    }

    internal sealed class LicenseCheckResult
    {
        public LicenseStatus Status { get; init; }
        public string? Message { get; init; }
        public bool IsGracePeriod { get; init; }
    }
}
