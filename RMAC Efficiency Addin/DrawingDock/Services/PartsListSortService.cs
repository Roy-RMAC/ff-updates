// DrawingDock/Services/PartsListSortService.cs
using Inventor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    internal sealed class PartsListSortService
    {
        internal sealed class Options
        {
            /// <summary>
            /// Substring match used to find the Parts List column to sort by.
            /// Default matches your existing behaviour: "Part Number".
            /// </summary>
            public string SortColumnContainsText { get; set; } = "Part Number";

            /// <summary>
            /// Sort ascending (true) or descending (false).
            /// </summary>
            public bool SortAscending { get; set; } = true;

            /// <summary>
            /// Only process Parts Lists that reference an Assembly document.
            /// </summary>
            public bool OnlyAssemblyPartsLists { get; set; } = true;

            /// <summary>
            /// Call PartsList.Renumber after sorting.
            /// </summary>
            public bool RenumberAfterSort { get; set; } = true;
        }

        internal sealed class Result
        {
            public bool Success { get; }
            public int Processed { get; }
            public int Skipped { get; }
            public IReadOnlyList<string> Warnings { get; }

            internal Result(bool success, int processed, int skipped, List<string> warnings)
            {
                Success = success;
                Processed = processed;
                Skipped = skipped;
                Warnings = warnings;
            }
        }

        public Result Run(DrawingDocument drawing, Options? options = null)
        {
            if (drawing == null) throw new ArgumentNullException(nameof(drawing));
            options ??= new Options();

            int processed = 0;
            int skipped = 0;
            var warnings = new List<string>();

            try
            {
                foreach (Sheet sh in drawing.Sheets)
                {
                    PartsLists pls;
                    try { pls = sh.PartsLists; }
                    catch { continue; }

                    foreach (PartsList pl in pls)
                    {
                        try
                        {
                            if (options.OnlyAssemblyPartsLists && !PartsListReferencesAssembly(pl))
                            {
                                skipped++;
                                continue;
                            }

                            string? colTitle = FindPartsListColumnTitle(pl, options.SortColumnContainsText);
                            if (string.IsNullOrWhiteSpace(colTitle))
                            {
                                warnings.Add($"Sheet '{SafeSheetName(sh)}': Could not find a '{options.SortColumnContainsText}' column to sort by.");
                                skipped++;
                                continue;
                            }

                            // Inventor exposes Sort/Renumber on PartsList, but some PIAs differ.
                            // Use dynamic to avoid signature issues.
                            dynamic dpl = pl;

                            dpl.Sort(colTitle, options.SortAscending);

                            if (options.RenumberAfterSort)
                                dpl.Renumber();

                            processed++;
                        }
                        catch (Exception exOne)
                        {
                            warnings.Add($"Sheet '{SafeSheetName(sh)}': Parts List sort/renumber failed: {exOne.Message}");
                        }
                    }
                }

                return new Result(true, processed, skipped, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add("Sort BOMs failed: " + ex.Message);
                return new Result(false, processed, skipped, warnings);
            }
        }

        private static bool PartsListReferencesAssembly(PartsList pl)
        {
            try
            {
                var desc = pl.ReferencedDocumentDescriptor;
                if (desc == null) return false;

                object doc = desc.ReferencedDocument;
                return doc is AssemblyDocument;
            }
            catch
            {
                return false;
            }
        }

        private static string? FindPartsListColumnTitle(PartsList pl, string containsText)
        {
            if (string.IsNullOrWhiteSpace(containsText))
                containsText = "Part Number";

            try
            {
                foreach (PartsListColumn c in pl.PartsListColumns)
                {
                    try
                    {
                        string t = c.Title;
                        if (!string.IsNullOrWhiteSpace(t) &&
                            t.IndexOf(containsText, StringComparison.OrdinalIgnoreCase) >= 0)
                            return t;
                    }
                    catch { }
                }
            }
            catch { }

            // Fallback: sometimes the title is exactly what Inventor expects
            return containsText;
        }

        private static string SafeSheetName(Sheet sh)
        {
            try { return sh.Name; } catch { return "<unknown sheet>"; }
        }
    }
}
