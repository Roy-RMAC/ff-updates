using System;
using System.Threading.Tasks;

namespace RMAC_Efficiency_Addin.Licensing
{
    /// <summary>
    /// Display-friendly snapshot of the current license state.
    /// </summary>
    internal sealed class LicenseInfo
    {
        public LicenseState State { get; init; }
        public string? MaskedKey { get; init; }
        public string? MachineId { get; init; }
        public DateTime? LastValidated { get; init; }
        public string? SubscriptionStatus { get; init; }
    }

    internal static class LicenseManager
    {
        private static readonly TimeSpan ValidationInterval = TimeSpan.FromDays(5);
        private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(5);

        // ---------------------------------------------------------------
        //  Check license on startup
        // ---------------------------------------------------------------

        public static LicenseCheckResult CheckLicense()
        {
            var fingerprint = MachineFingerprint.Get();
            var data = LicenseStore.Load();

            if (data == null)
                return new LicenseCheckResult { Status = LicenseStatus.NeedsActivation };

            // Fingerprint mismatch — license belongs to a different machine
            if (!string.Equals(data.MachineFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                return new LicenseCheckResult
                {
                    Status = LicenseStatus.NeedsActivation,
                    Message = "This license was activated on a different machine."
                };

            // Clock rollback detection
            var now = DateTime.UtcNow;
            if (now < data.LastValidationUtc || now < data.LastOnlineUtc)
                return new LicenseCheckResult
                {
                    Status = LicenseStatus.Expired,
                    Message = "System clock appears to have been set back. Please correct your system time."
                };

            switch (data.State)
            {
                case LicenseState.Activated:
                    return CheckActivated(data, now, fingerprint);

                case LicenseState.Expired:
                case LicenseState.Deactivated:
                default:
                    return new LicenseCheckResult
                    {
                        Status = LicenseStatus.Expired,
                        Message = "Your license has expired or been deactivated."
                    };
            }
        }

        private static LicenseCheckResult CheckActivated(LicenseData data, DateTime now, string fingerprint)
        {
            var sinceValidation = now - data.LastValidationUtc;

            // Still within cached validation window — no API call needed
            if (sinceValidation <= ValidationInterval)
                return new LicenseCheckResult { Status = LicenseStatus.Licensed };

            // Cache expired — try online validation
            var validateResult = RunAsync(() =>
                LemonSqueezyClient.ValidateAsync(data.LicenseKey!, data.InstanceId!));

            if (validateResult.Error != null && IsNetworkError(validateResult.Error))
            {
                // Offline — check grace period
                var sinceOnline = now - data.LastOnlineUtc;
                if (sinceOnline <= GracePeriod)
                    return new LicenseCheckResult
                    {
                        Status = LicenseStatus.Licensed,
                        IsGracePeriod = true,
                        Message = "Running in offline mode. Internet connection required soon."
                    };

                return new LicenseCheckResult
                {
                    Status = LicenseStatus.Expired,
                    Message = "Unable to verify license. Please connect to the internet."
                };
            }

            if (validateResult.Valid && validateResult.LicenseStatus == "active")
            {
                data.LastValidationUtc = now;
                data.LastOnlineUtc = now;
                data.SubscriptionStatus = validateResult.LicenseStatus;
                LicenseStore.Save(data);

                return new LicenseCheckResult { Status = LicenseStatus.Licensed };
            }

            // License no longer valid (expired, cancelled, disabled)
            data.State = LicenseState.Expired;
            data.SubscriptionStatus = validateResult.LicenseStatus;
            LicenseStore.Save(data);

            return new LicenseCheckResult
            {
                Status = LicenseStatus.Expired,
                Message = $"Your subscription is no longer active (status: {validateResult.LicenseStatus ?? "unknown"})."
            };
        }

        // ---------------------------------------------------------------
        //  Activate a license key
        // ---------------------------------------------------------------

        public static ActivateResult Activate(string licenseKey)
        {
            var fingerprint = MachineFingerprint.Get();
            var result = RunAsync(() => LemonSqueezyClient.ActivateAsync(licenseKey, fingerprint));

            if (!result.Success)
                return result;

            var data = new LicenseData
            {
                LicenseKey = licenseKey,
                InstanceId = result.InstanceId,
                MachineFingerprint = fingerprint,
                State = LicenseState.Activated,
                LastValidationUtc = DateTime.UtcNow,
                LastOnlineUtc = DateTime.UtcNow,
                SubscriptionStatus = result.LicenseStatus
            };

            LicenseStore.Save(data);
            return result;
        }

        // ---------------------------------------------------------------
        //  Deactivate (free the seat)
        // ---------------------------------------------------------------

        public static bool Deactivate()
        {
            var data = LicenseStore.Load();
            if (data?.LicenseKey == null || data.InstanceId == null)
            {
                LicenseStore.Delete();
                return true;
            }

            var ok = RunAsync(() => LemonSqueezyClient.DeactivateAsync(data.LicenseKey, data.InstanceId));
            LicenseStore.Delete();
            return ok;
        }

        // ---------------------------------------------------------------
        //  Info for display in settings UI
        // ---------------------------------------------------------------

        public static LicenseInfo GetCurrentInfo()
        {
            var data = LicenseStore.Load();
            var fingerprint = MachineFingerprint.Get();

            if (data == null)
                return new LicenseInfo
                {
                    State = LicenseState.Deactivated,
                    MachineId = fingerprint[..12] + "..."
                };

            string? masked = null;
            if (data.LicenseKey != null && data.LicenseKey.Length > 4)
                masked = new string('*', data.LicenseKey.Length - 4) + data.LicenseKey[^4..];

            return new LicenseInfo
            {
                State = data.State,
                MaskedKey = masked,
                MachineId = fingerprint[..12] + "...",
                LastValidated = data.LastValidationUtc == default ? null : data.LastValidationUtc,
                SubscriptionStatus = data.SubscriptionStatus
            };
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Run an async method synchronously without deadlocking the STA thread.
        /// </summary>
        private static T RunAsync<T>(Func<Task<T>> work)
        {
            return Task.Run(work).GetAwaiter().GetResult();
        }

        private static bool IsNetworkError(string error)
        {
            return error.Contains("Network error", StringComparison.OrdinalIgnoreCase)
                || error.Contains("timed out", StringComparison.OrdinalIgnoreCase);
        }
    }
}
