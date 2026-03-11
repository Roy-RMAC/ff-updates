// Exporting/Model/ExportPlan.cs
using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;
using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.Exporting.Model
{
    /// <summary>
    /// A planned set of export operations for one run.
    /// Contains jobs + useful groupings to support "one per category" operations
    /// (drawing packages, folder creation, etc).
    /// </summary>
    internal sealed class ExportPlan
    {
        public ExportPlan()
        {
            Jobs = new List<ExportJob>();

            // Category -> documents that belong to that category (used for drawing package)
            CategoryTargets = new Dictionary<string, HashSet<Document>>(StringComparer.OrdinalIgnoreCase);

            // Optional: category -> planned output folder (resolved during planning/routing)
            CategoryOutputFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public List<ExportJob> Jobs { get; }

        public Dictionary<string, HashSet<Document>> CategoryTargets { get; }

        public Dictionary<string, string> CategoryOutputFolders { get; }

        public void AddJob(ExportJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            Jobs.Add(job);
        }

        /// <summary>
        /// Adds a target document to a category grouping (for category-level operations).
        /// </summary>
        public void AddCategoryTarget(string category, Document doc)
        {
            if (doc == null) return;

            category = (category ?? "").Trim();

            if (!CategoryTargets.TryGetValue(category, out var set))
            {
                set = new HashSet<Document>(new DocComparer<Document>());
                CategoryTargets[category] = set;
            }

            set.Add(doc);
        }

        public Document[] GetCategoryTargetsArray(string category)
        {
            category = (category ?? "").Trim();
            if (!CategoryTargets.TryGetValue(category, out var set) || set.Count == 0)
                return Array.Empty<Document>();

            var arr = new Document[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        public void SetCategoryOutputFolder(string category, string folder)
        {
            category = (category ?? "").Trim();
            folder ??= "";
            CategoryOutputFolders[category] = folder;
        }

        public bool TryGetCategoryOutputFolder(string category, out string folder)
        {
            category = (category ?? "").Trim();
            return CategoryOutputFolders.TryGetValue(category, out folder!);
        }

        /// <summary>
        /// Convenience: add jobs of a given type count.
        /// </summary>
        public int CountJobs(ExportJobType type)
        {
            int c = 0;
            foreach (var j in Jobs)
                if (j.Type == type) c++;
            return c;
        }

    }
}
