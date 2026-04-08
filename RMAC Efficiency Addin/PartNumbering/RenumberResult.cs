using System.Collections.Generic;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Result of a single part-numbering renumber run. Returned by
    /// <see cref="RmacPartNumberingRenumberer.RunCore"/> so callers (UI wrapper, harness tests)
    /// can decide how to present the outcome without the renumberer itself reaching for MessageBox.
    /// </summary>
    internal sealed class RenumberResult
    {
        /// <summary>True if the pipeline ran to completion without fatal errors and was not skipped.</summary>
        public bool Success { get; set; }

        /// <summary>True if the top-level assembly matched the skip rule and the run was aborted.</summary>
        public bool Skipped { get; set; }

        /// <summary>Human-readable reason the run was skipped, if any.</summary>
        public string? SkipReason { get; set; }

        /// <summary>The multi-line "missing properties" report, if any template tokens resolved to empty.</summary>
        public string? MissingPropertiesReport { get; set; }

        /// <summary>Path to the debug log file (only populated when Debug mode is enabled).</summary>
        public string? LogPath { get; set; }

        /// <summary>True if the run was executed with Debug mode enabled in settings.</summary>
        public bool DebugMode { get; set; }

        /// <summary>Fatal errors that stopped the pipeline. Non-empty implies <see cref="Success"/> is false.</summary>
        public List<string> Errors { get; } = new();
    }
}
