using Inventor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    internal sealed class ViewCoverageService
    {
        internal sealed class Options
        {
            public bool IncludeStructuredTopLevel { get; set; } = true;
            public bool IncludePartsOnly { get; set; } = true;

            public bool StructuredFirstLevelOnly { get; set; } = true;
            public bool EnablePartsOnlyView { get; set; } = true;

            /// <summary>
            /// If provided, BOM rows whose referenced document Comments match any entry are skipped.
            /// (Matches on trimmed comment text, case-insensitive if your set uses OrdinalIgnoreCase.)
            /// </summary>
            public ISet<string>? SkipComments { get; set; }

            /// <summary>Include BOM rows with blank PN as "<NO PART NUMBER>".</summary>
            public bool IncludeBlankPartNumbers { get; set; } = true;
        }

        internal sealed class Result
        {
            public bool Success { get; }
            public int FoundCount { get; }
            public int RequiredCount { get; }
            public IReadOnlyList<string> MissingPartNumbers { get; }
            public IReadOnlyList<string> Warnings { get; }

            internal Result(bool success, int foundCount, int requiredCount, List<string> missing, List<string> warnings)
            {
                Success = success;
                FoundCount = foundCount;
                RequiredCount = requiredCount;
                MissingPartNumbers = missing;
                Warnings = warnings;
            }
        }

        private readonly BomTraversalService _bom;

        public ViewCoverageService() : this(new BomTraversalService()) { }

        public ViewCoverageService(BomTraversalService bomTraversalService)
        {
            _bom = bomTraversalService ?? throw new ArgumentNullException(nameof(bomTraversalService));
        }

        public Result Run(DrawingDocument drawing, AssemblyDocument assembly, Options? options = null)
        {
            if (drawing == null) throw new ArgumentNullException(nameof(drawing));
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            options ??= new Options();

            var warnings = new List<string>();

            // 1) FOUND = all PN referenced by any DrawingView in the drawing
            var found = BuildFoundSetFromDrawing(drawing);

            // 2) REQUIRED = BOM PN set
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (options.IncludeStructuredTopLevel)
                {
                    foreach (var pn in GetRequiredFromBom(assembly, BomTraversalService.BomViewKind.Structured, options))
                        required.Add(pn);
                }

                if (options.IncludePartsOnly)
                {
                    foreach (var pn in GetRequiredFromBom(assembly, BomTraversalService.BomViewKind.PartsOnly, options))
                        required.Add(pn);
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Failed to read BOM: " + ex.Message);
                return new Result(false, found.Count, required.Count, new List<string>(), warnings);
            }

            // 3) MISSING
            var missing = required
                .Where(pn => !found.Contains(pn))
                .OrderBy(pn => pn, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new Result(true, found.Count, required.Count, missing, warnings);
        }

        private HashSet<string> BuildFoundSetFromDrawing(DrawingDocument drawing)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pnCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Sheet sh in drawing.Sheets)
            {
                foreach (DrawingView v in sh.DrawingViews)
                {
                    try
                    {
                        var refDoc = v.ReferencedDocumentDescriptor?.ReferencedDocument;
                        if (refDoc == null) continue;

                        string pn = GetPartNumberCached(refDoc, pnCache);
                        pn = (pn ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(pn))
                            continue;

                        found.Add(pn);
                    }
                    catch
                    {
                        // ignore per-view failures
                    }
                }
            }

            return found;
        }

        private IEnumerable<string> GetRequiredFromBom(AssemblyDocument asm, BomTraversalService.BomViewKind kind, Options options)
        {
            var skip = options.SkipComments;

            var bomOpts = new BomTraversalService.Options
            {
                StructuredFirstLevelOnly = options.StructuredFirstLevelOnly,
                EnablePartsOnlyView = options.EnablePartsOnlyView,
                DeduplicateByPartNumber = false,
                SkipComments = skip
            };

            var entries = _bom.GetBomEntries(asm, kind, bomOpts);

            foreach (var e in entries)
            {
                var pn = (e.PartNumber ?? "").Trim();

                if (string.IsNullOrWhiteSpace(pn))
                {
                    if (!options.IncludeBlankPartNumbers) continue;
                    pn = "<NO PART NUMBER>";
                }

                yield return pn;
            }
        }

        // ------------------------------
        // Property helpers (local, safe)
        // ------------------------------
        private static string GetPartNumberCached(object doc, Dictionary<string, string> pnCache)
        {
            string key = GetDocCacheKey(doc);
            if (pnCache.TryGetValue(key, out var v)) return v;

            string pn = GetPropertyValueSafe(doc, "Design Tracking Properties", "Part Number") ?? "";
            pnCache[key] = pn;
            return pn;
        }

        private static string GetDocCacheKey(object doc)
        {
            try
            {
                dynamic d = doc;
                try
                {
                    string f = d.FullFileName;
                    if (!string.IsNullOrWhiteSpace(f)) return f;
                }
                catch { }

                try
                {
                    string n = d.DisplayName;
                    if (!string.IsNullOrWhiteSpace(n)) return n;
                }
                catch { }
            }
            catch { }

            return doc.GetHashCode().ToString();
        }

        private static string? GetPropertyValueSafe(object docOrDefWithProps, string propSetName, string propName)
        {
            try
            {
                dynamic d = docOrDefWithProps;
                var ps = d.PropertySets[propSetName];
                var p = ps[propName];
                object v = p.Value;
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
