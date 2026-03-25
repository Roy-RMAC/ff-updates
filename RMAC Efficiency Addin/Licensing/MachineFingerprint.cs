using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace RMAC_Efficiency_Addin.Licensing
{
    internal static class MachineFingerprint
    {
        private static string? _cached;

        public static string Get()
        {
            if (_cached != null) return _cached;

            string raw;
            try
            {
                var mb = QueryWmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
                var cpu = QueryWmi("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
                raw = (mb ?? "") + (cpu ?? "");
            }
            catch
            {
                // WMI unavailable (locked-down environment) — degrade gracefully
                raw = Environment.MachineName + "|" + Environment.UserName;
            }

            if (string.IsNullOrWhiteSpace(raw))
                raw = Environment.MachineName + "|" + Environment.UserName;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            _cached = Convert.ToHexString(hash).ToLowerInvariant();
            return _cached;
        }

        private static string? QueryWmi(string query, string property)
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var obj in searcher.Get())
            {
                var val = obj[property]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val))
                    return val;
            }
            return null;
        }
    }
}
