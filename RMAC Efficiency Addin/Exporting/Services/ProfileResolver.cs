// Exporting/Services/ProfileResolver.cs
using RMAC_Efficiency_Addin.Exporting.Model;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.Exporting.Services
{
    /// <summary>
    /// Resolves an ExportProfileSettings for a given category (Comments value).
    /// Centralizes "missing/disabled profile" behaviour.
    /// </summary>
    internal sealed class ProfileResolver
    {
        private readonly ExportContext _ctx;
        private readonly CommentClassifier _classifier;

        public ProfileResolver(ExportContext ctx, CommentClassifier classifier)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        }

        /// <summary>
        /// Try resolve a profile for a category.
        /// Returns true if a profile exists and is enabled.
        /// Returns false if missing or disabled; reason is supplied for logging/UI.
        /// </summary>
        public bool TryGetProfile(string category, out ExportProfileSettings profile, out string? reason)
        {
            profile = null!;
            reason = null;

            category = _classifier.Normalize(category);

            if (string.IsNullOrWhiteSpace(category))
            {
                reason = "Comments category is blank.";
                return false;
            }

            var map = _ctx.Settings?.ProfilesByComment;
            if (map == null || map.Count == 0)
            {
                reason = "No ProfilesByComment configured in settings.";
                return false;
            }

            if (!TryGetProfileFromMap(map, category, out profile))
            {
                reason = $"No export profile configured for category '{category}'.";
                return false;
            }

            if (profile == null)
            {
                reason = $"Profile entry for category '{category}' is null.";
                return false;
            }

            profile.SanitizeInPlace();

            if (!profile.HasAnyActionSelected())
            {
                reason = $"Export profile has no actions selected for category '{category}'.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Strict lookup by category. Dictionary should be case-insensitive,
        /// but we also handle accidental case-sensitive dictionaries defensively.
        /// </summary>
        private static bool TryGetProfileFromMap(
            Dictionary<string, ExportProfileSettings> map,
            string category,
            out ExportProfileSettings profile)
        {
            profile = null!;

            if (map.TryGetValue(category, out profile!))
                return true;

            // Defensive fallback if comparer wasn't case-insensitive for some reason
            foreach (var kv in map)
            {
                if (string.Equals(kv.Key?.Trim(), category, StringComparison.OrdinalIgnoreCase))
                {
                    profile = kv.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Optional helper: returns all configured categories (for UI lists).
        /// </summary>
        public string[] GetConfiguredCategories()
        {
            var map = _ctx.Settings?.ProfilesByComment;
            if (map == null || map.Count == 0)
                return Array.Empty<string>();

            var keys = new string[map.Count];
            int i = 0;
            foreach (var k in map.Keys)
                keys[i++] = k;

            Array.Sort(keys, StringComparer.OrdinalIgnoreCase);
            return keys;
        }
    }
}
