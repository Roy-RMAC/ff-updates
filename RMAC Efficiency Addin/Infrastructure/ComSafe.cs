using RMAC_Efficiency_Addin.Settings;
using System;
using System.Runtime.InteropServices;

namespace RMAC_Efficiency_Addin.Infrastructure
{
    internal static class ComSafe
    {
        public static void Try(string context, Action action)
        {
            try { action(); }
            catch (COMException) { /* common Inventor cancel/COM edge */ }
            catch (Exception ex) { AddinSettings.Log($"{context}: {ex.Message}"); }
        }

        public static T Try<T>(string context, Func<T> func, T fallback = default!)
        {
            try { return func(); }
            catch (COMException) { return fallback; }
            catch (Exception ex)
            {
                AddinSettings.Log($"{context}: {ex.Message}");
                return fallback;
            }
        }

        public static void Ignore(Action action)
        {
            try { action(); } catch { /* intentionally swallow */ }
        }
    }
}
