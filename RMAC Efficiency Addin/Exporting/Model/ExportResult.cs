using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.Exporting
{
    /// <summary>
    /// Result of a single export run. Returned by <see cref="GlobalExporter.RunCore"/> so callers
    /// (UI wrapper, harness tests) can decide how to present the outcome without the exporter
    /// itself reaching for MessageBox.
    /// </summary>
    internal sealed class ExportResult
    {
        /// <summary>True if the pipeline ran to completion without fatal errors.</summary>
        public bool Success { get; set; }

        /// <summary>The output folder the export wrote to (may be null if never resolved).</summary>
        public string? OutputFolder { get; set; }

        /// <summary>Non-fatal warnings — export continued.</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>Fatal errors that stopped the pipeline. Non-empty implies <see cref="Success"/> is false.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Total jobs planned. Zero if the plan was never built.</summary>
        public int JobsTotal { get; set; }

        /// <summary>Jobs actually executed. May be less than <see cref="JobsTotal"/> if cancelled.</summary>
        public int JobsExecuted { get; set; }
    }
}
