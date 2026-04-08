using System;
using System.Collections.Generic;
using Inventor;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Diagnostic tool: walks Inventor's ControlDefinitions collection and returns the ones
    /// whose internal name, display name, or description match "sketch" + (prop|color|style).
    /// Used by the "Find Sketch Property Commands" ribbon button.
    /// </summary>
    internal static class SketchCommandInspector
    {
        /// <summary>
        /// Returns up to <paramref name="maxResults"/> matching command definitions as human-readable strings.
        /// Each entry is of the form "<InternalName> | <DisplayName> | <DescriptionText>".
        /// </summary>
        public static List<string> FindMatches(Application app, int maxResults = 50)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            var hits = new List<string>();
            var defs = app.CommandManager.ControlDefinitions;

            foreach (ControlDefinition d in defs)
            {
                var name = d.InternalName ?? "";
                var desc = d.DescriptionText ?? "";
                var disp = d.DisplayName ?? "";

                var blob = (name + " " + disp + " " + desc).ToLowerInvariant();
                if (blob.Contains("sketch") && (blob.Contains("prop") || blob.Contains("color") || blob.Contains("style")))
                {
                    hits.Add($"{name} | {disp} | {desc}");
                    if (hits.Count >= maxResults) break;
                }
            }

            return hits;
        }
    }
}
