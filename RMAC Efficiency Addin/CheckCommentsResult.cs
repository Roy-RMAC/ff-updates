using System.Collections.Generic;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Result of a single comments-checker run. Returned by
    /// <see cref="CommentsChecker.RunCore"/> so callers (UI wrapper, harness tests) can present
    /// the outcome without the checker itself reaching for MessageBox.
    /// </summary>
    /// <remarks>
    /// Note: the checker's inner <c>PromptAndSet*</c> flow still shows modal pickers per-row when
    /// a comment needs fixing. Harness tests should use fixtures where every row already has a
    /// valid comment so no prompts fire.
    /// </remarks>
    internal sealed class CheckCommentsResult
    {
        /// <summary>True if the pipeline ran to completion without fatal errors.</summary>
        public bool Success { get; set; }

        /// <summary>Fatal errors that stopped the pipeline (e.g. BOM view inaccessible).</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Count of rows whose comment was updated.</summary>
        public int UpdatedCount { get; set; }

        /// <summary>Count of rows that were skipped (already-valid, no doc, or user cancelled picker).</summary>
        public int SkippedCount { get; set; }
    }
}
