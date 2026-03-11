// DrawingDock/Services/BomTraversalService.cs
using Inventor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    internal sealed class BomTraversalService
    {
        public enum BomViewKind
        {
            Structured,
            PartsOnly
        }

        internal sealed class BomEntry
        {
            public BomViewKind ViewKind { get; }
            public BOMRow Row { get; }
            public ComponentDefinition ComponentDefinition { get; }
            public _Document Document { get; }
            public string PartNumber { get; }
            public string Comments { get; }

            public BomEntry(
                BomViewKind kind,
                BOMRow row,
                ComponentDefinition cd,
                _Document doc,
                string partNumber,
                string comments)
            {
                ViewKind = kind;
                Row = row;
                ComponentDefinition = cd;
                Document = doc;
                PartNumber = partNumber;
                Comments = comments;
            }
        }

        internal sealed class Options
        {
            public bool StructuredFirstLevelOnly { get; set; } = true;
            public bool EnablePartsOnlyView { get; set; } = true;
            public bool DeduplicateByPartNumber { get; set; } = true;
            public ISet<string>? SkipComments { get; set; }
            public Func<string, bool>? PartNumberFilter { get; set; }
        }

        public IReadOnlyList<BomEntry> GetBomEntries(AssemblyDocument asm, BomViewKind kind, Options? options = null)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));
            options ??= new Options();

            var bom = asm.ComponentDefinition.BOM;

            if (kind == BomViewKind.Structured)
            {
                try { bom.StructuredViewFirstLevelOnly = options.StructuredFirstLevelOnly; } catch { }
            }
            else
            {
                if (options.EnablePartsOnlyView)
                {
                    try { bom.PartsOnlyViewEnabled = true; } catch { }
                }
            }

            BOMView view = kind switch
            {
                BomViewKind.Structured => GetStructuredViewOrThrow(bom),
                BomViewKind.PartsOnly => GetPartsOnlyViewOrThrow(bom),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            if (options.DeduplicateByPartNumber)
            {
                var dict = new Dictionary<string, BomEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in EnumerateEntries(kind, view, options))
                    dict[entry.PartNumber] = entry;

                return dict.Values.OrderBy(e => e.PartNumber, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return EnumerateEntries(kind, view, options)
                .OrderBy(e => e.PartNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<BomEntry> EnumerateEntries(BomViewKind kind, BOMView view, Options options)
        {
            foreach (BOMRow row in view.BOMRows)
            {
                ComponentDefinition cd;
                try { cd = row.ComponentDefinitions[1]; }
                catch { continue; }

                _Document doc;
                try { doc = (_Document)cd.Document; }  // <-- FIX: cast from object/_Document
                catch { continue; }

                string pn = PropertyUtil.GetPropertyValueSafe(doc, "Design Tracking Properties", "Part Number") ?? "";
                pn = pn.Trim();
                if (string.IsNullOrWhiteSpace(pn))
                    pn = "<NO PART NUMBER>";

                if (options.PartNumberFilter != null && !options.PartNumberFilter(pn))
                    continue;

                string comments = PropertyUtil.GetPropertyValueSafe(doc, "Inventor Summary Information", "Comments") ?? "";
                comments = comments.Trim();

                if (options.SkipComments != null && !string.IsNullOrWhiteSpace(comments))
                {
                    if (options.SkipComments.Contains(comments))
                        continue;
                }

                yield return new BomEntry(kind, row, cd, doc, pn, comments);
            }
        }

        private static BOMView GetStructuredViewOrThrow(BOM bom)
        {
            try { return bom.BOMViews["Structured"]; }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Could not access the 'Structured' BOM view in the assembly.", ex);
            }
        }

        private static BOMView GetPartsOnlyViewOrThrow(BOM bom)
        {
            var v = TryGetPartsOnlyView(bom);
            if (v != null) return v;

            throw new InvalidOperationException("Could not access the 'Parts Only' BOM view in the assembly.");
        }

        private static BOMView? TryGetPartsOnlyView(BOM bom)
        {
            try
            {
                return bom.BOMViews["Parts Only"];
            }
            catch
            {
                try
                {
                    foreach (BOMView v in bom.BOMViews)
                    {
                        if (v.Name.IndexOf("Parts", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            v.Name.IndexOf("Only", StringComparison.OrdinalIgnoreCase) >= 0)
                            return v;
                    }
                }
                catch { }
            }

            return null;
        }

        private static class PropertyUtil
        {
            public static string? GetPropertyValueSafe(object docOrDefWithProps, string propSetName, string propName)
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
}
