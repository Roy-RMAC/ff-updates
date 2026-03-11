// Exporting/Services/CommentClassifier.cs
using Inventor;
using System;

namespace RMAC_Efficiency_Addin.Exporting.Services
{
    /// <summary>
    /// Central place to read + normalize the Inventor "Comments" iProperty used as your category key.
    /// This keeps behaviour consistent across BOM walking, planning, and execution.
    /// </summary>
    internal sealed class CommentClassifier
    {
        /// <summary>
        /// If true, category keys are forced to upper-invariant (PROFILE, PRESS...).
        /// Default false to preserve whatever users type (while still trimming).
        /// You can switch this on later if you decide your system should be case-normalized.
        /// </summary>
        public bool ForceUpperInvariant { get; set; } = false;

        /// <summary>
        /// Returns the raw "Comments" value from Summary Information, trimmed.
        /// Never throws.
        /// </summary>
        public string GetCategory(Document doc)
        {
            if (doc == null) return string.Empty;

            try
            {
                var ps = doc.PropertySets;
                if (ps == null) return string.Empty;

                PropertySet? summary = null;
                try { summary = ps["Summary Information"]; } catch { summary = null; }
                if (summary == null) return string.Empty;

                Inventor.Property? commentsProp = null;
                try { commentsProp = summary["Comments"]; } catch { commentsProp = null; }
                if (commentsProp == null) return string.Empty;

                var raw = commentsProp.Value?.ToString() ?? string.Empty;
                var trimmed = raw.Trim();

                if (ForceUpperInvariant)
                    trimmed = trimmed.ToUpperInvariant();

                return trimmed;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Normalizes a category string for dictionary lookup.
        /// Trim + optionally upper-invariant.
        /// </summary>
        public string Normalize(string? category)
        {
            var s = (category ?? string.Empty).Trim();
            if (ForceUpperInvariant) s = s.ToUpperInvariant();
            return s;
        }

        /// <summary>
        /// Convenience: compares category strings with your chosen normalization.
        /// </summary>
        public bool EqualsCategory(string? a, string? b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
