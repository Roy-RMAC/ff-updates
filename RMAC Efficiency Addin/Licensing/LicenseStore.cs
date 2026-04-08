using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RMAC_Efficiency_Addin.Licensing
{
    internal static class LicenseStore
    {
        private static readonly object _lock = new();

        private static readonly string _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FabFlow");

        private static readonly string _path = Path.Combine(_dir, "license.dat");

        public static LicenseData? Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path)) return null;

                    var encrypted = File.ReadAllBytes(_path);
                    var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
                    return JsonSerializer.Deserialize<LicenseData>(Encoding.UTF8.GetString(json));
                }
                catch
                {
                    // Decryption failure (file from another machine) or corrupt data
                    return null;
                }
            }
        }

        public static void Save(LicenseData data)
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_dir);
                var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
                var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.LocalMachine);
                File.WriteAllBytes(_path, encrypted);
            }
        }

        public static void Delete()
        {
            lock (_lock)
            {
                try { File.Delete(_path); } catch { }
            }
        }
    }
}
