// Exporting/Services/BomWalker.cs
using Inventor;
using RMAC_Efficiency_Addin.Exporting.Model;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RMAC_Efficiency_Addin.Exporting.Services
{
    internal sealed class BomWalker
    {
        private readonly Inventor.Application _app;

        public BomWalker(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public sealed class BomWalkResult
        {
            public List<ExportItem> Items { get; } = new();
            public List<string> Warnings { get; } = new();
            public List<string> Errors { get; } = new();
        }

        public BomWalkResult WalkPartsOnly(AssemblyDocument topAssembly)
        {
            var result = new BomWalkResult();

            try
            {
                var bom = topAssembly.ComponentDefinition?.BOM;
                if (bom == null)
                {
                    result.Errors.Add("Assembly BOM is not available.");
                    return result;
                }

                try { bom.PartsOnlyViewEnabled = true; } catch { }

                // Locate "Parts Only" view by name
                BOMView? view = null;
                try
                {
                    foreach (BOMView v in bom.BOMViews)
                    {
                        if (string.Equals(v.Name, "Parts Only", StringComparison.OrdinalIgnoreCase))
                        {
                            view = v;
                            break;
                        }
                    }
                }
                catch { }

                if (view == null)
                {
                    result.Errors.Add("Could not resolve Parts Only BOM view.");
                    return result;
                }

                dynamic rows = view.BOMRows;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rowObj in rows)
                    ProcessRow(rowObj, seen, result);

                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"BOM walk failed: {ex.Message}");
                return result;
            }
        }

        private static void ProcessRow(object rowObj, HashSet<string> seen, BomWalkResult result)
        {
            if (rowObj == null) return;

            dynamic row = rowObj;

            _Document? doc = null;

            // ComponentDefinitions[1].Document
            try
            {
                dynamic compDefs = row.ComponentDefinitions;
                if (compDefs != null)
                {
                    dynamic cd = compDefs[1];
                    doc = cd?.Document as _Document;
                }
            }
            catch { }

            // Fallback
            if (doc == null)
            {
                try { doc = row.ComponentDefinition?.Document as _Document; } catch { }
            }

            if (doc == null)
            {
                result.Warnings.Add("BOM row skipped: could not resolve referenced document.");
                return;
            }

            var category = CommentClassifierRead(doc);

            var item = new ExportItem(doc, category);

            try
            {
                item.Quantity = TryGetDouble(row, "TotalQuantity")
                             ?? TryGetDouble(row, "ItemQuantity")
                             ?? TryGetDouble(row, "Quantity");
            }
            catch { }

            try
            {
                item.ItemNumber = TryGetString(row, "ItemNumber") ?? TryGetString(row, "Item");
            }
            catch { }

            var key = item.IdentityKey;
            if (seen.Add(key))
                result.Items.Add(item);
        }

        private static double? TryGetDouble(dynamic obj, string propName)
        {
            try
            {
                var v = obj.GetType().InvokeMember(propName, BindingFlags.GetProperty, null, obj, null);
                if (v == null) return null;

                if (v is double d) return d;
                if (v is float f) return f;
                if (v is int i) return i;
                if (v is decimal m) return (double)m;

                var s = v.ToString();
                if (string.IsNullOrWhiteSpace(s)) return null;

                double parsed;
                if (double.TryParse(s, out parsed))
                    return parsed;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetString(dynamic obj, string propName)
        {
            try
            {
                var v = obj.GetType().InvokeMember(propName, BindingFlags.GetProperty, null, obj, null);
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string CommentClassifierRead(_Document doc)
        {
            try
            {
                var ps = doc.PropertySets;
                if (ps == null) return "";

                PropertySet? summary = null;
                try { summary = ps["Summary Information"]; } catch { summary = null; }
                if (summary == null) return "";

                Inventor.Property? comments = null;
                try { comments = summary["Comments"]; } catch { comments = null; }

                return (comments?.Value?.ToString() ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }
    }
}
