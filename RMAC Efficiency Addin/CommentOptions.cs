using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RMAC_Efficiency_Addin
{
    internal sealed class CommentOptions
    {
        public List<string> Part { get; set; } = new();
        public List<string> Assembly { get; set; } = new();
        public List<string> TreatAsInseparableWhenAssemblyIs { get; set; } = new();

        // NEW: used by "Check Views Coverage" to ignore BOM items whose Comments match any of these
        public List<string> SkipCommentsForViewCoverage { get; set; } = new();

        public static string GetConfigPathNextToAddin()
        {
            // Prefer folder of the active "global" settings.json (Custom/Company/User)
            try
            {
                AddinSettings.EnsureLoaded();

                var settingsPath = AddinSettings.LoadedPath;
                var settingsDir = Path.GetDirectoryName(settingsPath);

                if (!string.IsNullOrWhiteSpace(settingsDir))
                    return Path.Combine(settingsDir, "comment-options.json");
            }
            catch
            {
                // ignore and fall back
            }

            // Fallback: next to add-in DLL
            var dllDir = Path.GetDirectoryName(typeof(CommentOptions).Assembly.Location)
                        ?? throw new InvalidOperationException("Cannot resolve add-in assembly folder.");
            return Path.Combine(dllDir, "comment-options.json");
        }


        public static CommentOptions LoadOrCreateDefault(string path)
        {
            if (!File.Exists(path))
            {
                var defaults = new CommentOptions
                {
                    Part = new List<string> { "FABRICATED", "PURCHASE", "HARDWARE" },
                    Assembly = new List<string> { "FABRICATED", "PURCHASE", "INSEPERABLE", "WELDMENT" },
                    TreatAsInseparableWhenAssemblyIs = new List<string> { "WELDMENT", "PURCHASE" },

                    // Default skip list for view coverage checks
                    SkipCommentsForViewCoverage = new List<string> { "HARDWARE" }
                };
                defaults.Save(path);
                return defaults;
            }

            var json = File.ReadAllText(path);
            var opts = JsonSerializer.Deserialize<CommentOptions>(json)
                       ?? throw new InvalidOperationException("Failed to parse comment-options.json");

            // Backwards compatibility: older files won't have SkipCommentsForViewCoverage
            opts.Part = CleanList(opts.Part);
            opts.Assembly = CleanList(opts.Assembly);
            opts.TreatAsInseparableWhenAssemblyIs = CleanList(opts.TreatAsInseparableWhenAssemblyIs);
            opts.SkipCommentsForViewCoverage = CleanList(opts.SkipCommentsForViewCoverage);

            // Ensure there's at least a sensible default skip list if user hasn't set one yet
            if (opts.SkipCommentsForViewCoverage.Count == 0)
                opts.SkipCommentsForViewCoverage = new List<string> { "HARDWARE" };

            if (opts.Part.Count == 0) throw new InvalidOperationException("Part list is empty in comment-options.json");
            if (opts.Assembly.Count == 0) throw new InvalidOperationException("Assembly list is empty in comment-options.json");

            return opts;
        }

        public void Save(string path)
        {
            Part = CleanList(Part);
            Assembly = CleanList(Assembly);
            TreatAsInseparableWhenAssemblyIs = CleanList(TreatAsInseparableWhenAssemblyIs);
            SkipCommentsForViewCoverage = CleanList(SkipCommentsForViewCoverage);

            // Never write an empty skip list by accident; keep a sane default
            if (SkipCommentsForViewCoverage.Count == 0)
                SkipCommentsForViewCoverage = new List<string> { "HARDWARE" };

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var sw = new StreamWriter(fs);
            sw.Write(json);
        }

        private static List<string> CleanList(IEnumerable<string>? items)
        {
            return (items ?? Enumerable.Empty<string>())
                .Select(s => (s ?? "").Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
