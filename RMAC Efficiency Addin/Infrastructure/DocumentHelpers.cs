using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.Infrastructure
{
    internal static class DocumentHelpers
    {
        /// <summary>Safely read FullFileName from any Inventor document COM object.</summary>
        internal static string SafeFullFileName(object? doc)
        {
            try { return (string?)((dynamic)doc)?.FullFileName ?? ""; }
            catch { return ""; }
        }

        /// <summary>Safely read InternalName from any Inventor document COM object.</summary>
        internal static string SafeInternalName(object? doc)
        {
            try { return (string?)((dynamic)doc)?.InternalName ?? ""; }
            catch { return ""; }
        }

        /// <summary>Safely read DisplayName from any Inventor document COM object.</summary>
        internal static string SafeDisplayName(object? doc)
        {
            try { return (string?)((dynamic)doc)?.DisplayName ?? "(unknown)"; }
            catch { return "(unknown)"; }
        }
    }

    /// <summary>
    /// Compares Inventor document COM objects by FullFileName (case-insensitive),
    /// falling back to InternalName when FullFileName is unavailable.
    /// </summary>
    internal sealed class DocComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T? x, T? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            string fx = DocumentHelpers.SafeFullFileName(x);
            string fy = DocumentHelpers.SafeFullFileName(y);

            if (!string.IsNullOrEmpty(fx) || !string.IsNullOrEmpty(fy))
                return string.Equals(fx, fy, StringComparison.OrdinalIgnoreCase);

            return string.Equals(
                DocumentHelpers.SafeInternalName(x),
                DocumentHelpers.SafeInternalName(y),
                StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(T obj)
        {
            string f = DocumentHelpers.SafeFullFileName(obj);
            if (!string.IsNullOrEmpty(f))
                return StringComparer.OrdinalIgnoreCase.GetHashCode(f);

            string n = DocumentHelpers.SafeInternalName(obj);
            return !string.IsNullOrEmpty(n)
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(n)
                : 0;
        }
    }
}
