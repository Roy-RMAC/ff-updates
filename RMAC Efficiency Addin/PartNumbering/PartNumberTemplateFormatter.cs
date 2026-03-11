// PartNumberTemplateFormatter.cs
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal static class PartNumberTemplateFormatter
    {
        private static readonly Regex TokenRegex = new Regex(@"<([^>]+)>", RegexOptions.Compiled);

        // Property set name fallbacks (Inventor can vary by locale / API naming)
        private static readonly string[] SummaryInfoSets =
        {
            "Summary Information",
            "Inventor Summary Information"
        };

        private static readonly string[] DesignTrackingSets =
        {
            "Design Tracking Properties"
        };

        private static readonly string[] UserDefinedSets =
        {
            "User Defined Properties",
            "Inventor User Defined Properties"
        };

        public static string Apply(RenumberingContext ctx, dynamic doc, string basePartNumber)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            // Top-level fixed means: do not apply template to the top assembly
            if (ctx.KeepTopLevelAssemblyPartNumberFixed && IsTopAssembly(ctx, doc))
                return basePartNumber ?? string.Empty;

            string template = ctx.PartNumberTemplate;

            // Treat blank or "<PartNumber>" as "no template"
            if (string.IsNullOrWhiteSpace(template) ||
                string.Equals(template.Trim(), "<PartNumber>", StringComparison.OrdinalIgnoreCase))
                return basePartNumber ?? string.Empty;

            string result = TokenRegex.Replace(template, m =>
            {
                var raw = (m.Groups[1].Value ?? string.Empty).Trim();
                if (raw.Length == 0) return string.Empty;

                // Base PN
                if (raw.Equals("PartNumber", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("Part Number", StringComparison.OrdinalIgnoreCase))
                    return basePartNumber ?? string.Empty;

                // NEW: dotted friendly paths inside <>
                // Examples:
                //   <Project.Project>
                //   <Summary Information.Title>
                //   <Design Tracking Properties.Stock Number>
                // Also supports:
                //   <Summary.Title>
                //   <DT.Stock Number>
                if (TryParseDottedFriendlyToken(raw, out var setNameDot, out var propNameDot))
                {
                    return ResolveIPropAnySet(ctx, doc, ExpandSetAliases(setNameDot), propNameDot, "<" + raw + ">");
                }

                // Friendly aliases
                if (raw.Equals("Project", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveIPropAnySet(ctx, doc, DesignTrackingSets, "Project", "<Project>");
                }

                // IMPORTANT: Revision Number is typically in Summary Information (not Design Tracking)
                if (raw.Equals("Revision", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("Revision Number", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveIPropAnySet(ctx, doc, SummaryInfoSets, "Revision Number", "<" + raw + ">");
                }

                if (raw.Equals("Description", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveIPropAnySet(ctx, doc, DesignTrackingSets, "Description", "<Description>");
                }

                if (raw.Equals("Stock Number", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveIPropAnySet(ctx, doc, DesignTrackingSets, "Stock Number", "<Stock Number>");
                }

                // Explicit: iProp / uProp lookups
                if (raw.StartsWith("iProp:", StringComparison.OrdinalIgnoreCase))
                {
                    // <iProp:SetName:PropertyName>
                    var spec = raw.Substring("iProp:".Length);
                    if (TrySplit2(spec, out var setName, out var propName))
                        return ResolveIPropAnySet(ctx, doc, ExpandSetAliases(setName), propName, "<" + raw + ">");

                    ctx.RegisterMissingProperty(doc, "<" + raw + ">", "Token format should be <iProp:SetName:PropertyName>");
                    return string.Empty;
                }

                if (raw.StartsWith("uProp:", StringComparison.OrdinalIgnoreCase))
                {
                    // <uProp:PropertyName>
                    var propName = raw.Substring("uProp:".Length).Trim();
                    return ResolveUPropAnySet(ctx, doc, propName, "<" + raw + ">");
                }

                if (raw.StartsWith("asm.iProp:", StringComparison.OrdinalIgnoreCase))
                {
                    // <asm.iProp:SetName:PropertyName> (top assembly only)
                    var spec = raw.Substring("asm.iProp:".Length);
                    if (TrySplit2(spec, out var setName, out var propName))
                        return ResolveIPropTopOnlyAnySet(ctx, ExpandSetAliases(setName), propName, "<" + raw + ">");

                    ctx.RegisterMissingProperty(doc, "<" + raw + ">", "Token format should be <asm.iProp:SetName:PropertyName>");
                    return string.Empty;
                }

                if (raw.StartsWith("asm.uProp:", StringComparison.OrdinalIgnoreCase))
                {
                    // <asm.uProp:PropertyName> (top assembly only)
                    var propName = raw.Substring("asm.uProp:".Length).Trim();
                    return ResolveUPropTopOnlyAnySet(ctx, propName, "<" + raw + ">");
                }

                // Unknown token -> treat as missing (don’t keep token)
                ctx.RegisterMissingProperty(doc, "<" + raw + ">", "Unknown token");
                return string.Empty;
            });

            return result ?? string.Empty;
        }

        private static bool IsTopAssembly(RenumberingContext ctx, dynamic doc)
        {
            try
            {
                if (ReferenceEquals(doc, ctx.TopAssembly)) return true;

                string a = "";
                string b = "";
                try { a = (string)doc.FullFileName; } catch { a = ""; }
                try { b = (string)ctx.TopAssembly.FullFileName; } catch { b = ""; }

                if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                    return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Resolution policy:
        /// - If a source returns "" => treat as resolved (property exists but empty) and stop.
        /// - If a source returns "ERROR" => keep searching other sources; only emit ERROR if none resolve.
        /// - Otherwise return value.
        /// </summary>
        private static string ResolveIPropAnySet(
            RenumberingContext ctx,
            dynamic doc,
            string[] setNameCandidates,
            string propName,
            string token)
        {
            bool sawError = false;

            foreach (var src in EnumerateSources(ctx, doc))
            {
                var v = TryGetIPropWithSetFallback(ctx, src, setNameCandidates, propName, out bool anyTried, out bool anyNonError);

                // If anyNonError is true, v is either "" or a real value (both count as resolved)
                if (anyNonError)
                    return v ?? string.Empty;

                // Otherwise we only saw ERROR (or we couldn't try at all)
                if (anyTried)
                    sawError = true;
            }

            if (sawError)
            {
                ctx.RegisterMissingProperty(doc, token, $"{JoinSets(setNameCandidates)}/{propName}");
                return "ERROR";
            }

            ctx.RegisterMissingProperty(doc, token, $"{JoinSets(setNameCandidates)}/{propName}");
            return string.Empty;
        }

        private static string ResolveIPropTopOnlyAnySet(
            RenumberingContext ctx,
            string[] setNameCandidates,
            string propName,
            string token)
        {
            var src = ctx.TopAssembly;

            var v = TryGetIPropWithSetFallback(ctx, src, setNameCandidates, propName, out bool anyTried, out bool anyNonError);

            if (anyNonError)
                return v ?? string.Empty;

            if (anyTried)
            {
                ctx.RegisterMissingProperty(src, token, $"{JoinSets(setNameCandidates)}/{propName}");
                return "ERROR";
            }

            ctx.RegisterMissingProperty(src, token, $"{JoinSets(setNameCandidates)}/{propName}");
            return string.Empty;
        }

        private static string ResolveUPropAnySet(RenumberingContext ctx, dynamic doc, string propName, string token)
        {
            if (string.IsNullOrWhiteSpace(propName))
            {
                ctx.RegisterMissingProperty(doc, token, "User Defined Properties/<blank>");
                return "ERROR";
            }

            bool sawError = false;

            foreach (var src in EnumerateSources(ctx, doc))
            {
                // Your ctx.GetUserPropString(...) returns null if missing, string if exists (may be empty)
                var v = ctx.GetUserPropString(src, propName);

                if (v == null)
                {
                    sawError = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(v))
                    return string.Empty;

                return v;
            }

            if (sawError)
            {
                ctx.RegisterMissingProperty(doc, token, $"User Defined Properties/{propName}");
                return "ERROR";
            }

            ctx.RegisterMissingProperty(doc, token, $"User Defined Properties/{propName}");
            return string.Empty;
        }

        private static string ResolveUPropTopOnlyAnySet(RenumberingContext ctx, string propName, string token)
        {
            if (string.IsNullOrWhiteSpace(propName))
            {
                ctx.RegisterMissingProperty(ctx.TopAssembly, token, "User Defined Properties/<blank>");
                return "ERROR";
            }

            var v = ctx.GetUserPropString(ctx.TopAssembly, propName);
            if (v == null)
            {
                ctx.RegisterMissingProperty(ctx.TopAssembly, token, $"User Defined Properties/{propName}");
                return "ERROR";
            }

            if (string.IsNullOrWhiteSpace(v))
                return string.Empty;

            return v;
        }

        private static IEnumerable<dynamic> EnumerateSources(RenumberingContext ctx, dynamic doc)
        {
            // If enabled, prefer top assembly values (then fall back to the part/assembly being numbered).
            if (ctx.PartNumberTemplateUseTopLevelAssemblyProperties)
            {
                yield return ctx.TopAssembly;
                yield return doc;
                yield break;
            }

            yield return doc;
            yield return ctx.TopAssembly;
        }

        private static bool TrySplit2(string spec, out string a, out string b)
        {
            a = "";
            b = "";
            if (string.IsNullOrWhiteSpace(spec)) return false;

            var parts = spec.Split(new[] { ':' }, 2);
            if (parts.Length != 2) return false;

            a = parts[0].Trim();
            b = parts[1].Trim();
            return !(string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b));
        }

        /// <summary>
        /// Accepts:
        ///  - "Project.X" -> Design Tracking Properties / X
        ///  - "Summary Information.X" -> Summary Information / X
        ///  - "Design Tracking Properties.X" -> Design Tracking Properties / X
        ///  - "Summary.X" -> Summary Information / X
        ///  - "DT.X" -> Design Tracking Properties / X
        /// </summary>
        private static bool TryParseDottedFriendlyToken(string raw, out string setName, out string propName)
        {
            setName = "";
            propName = "";

            if (string.IsNullOrWhiteSpace(raw)) return false;

            // "Project.X" => Design Tracking Properties / X
            if (raw.StartsWith("Project.", StringComparison.OrdinalIgnoreCase))
            {
                setName = "Design Tracking Properties";
                propName = raw.Substring("Project.".Length).Trim();
                return !string.IsNullOrWhiteSpace(propName);
            }

            // "Summary Information.X" => Summary Information / X
            if (raw.StartsWith("Summary Information.", StringComparison.OrdinalIgnoreCase))
            {
                setName = "Summary Information";
                propName = raw.Substring("Summary Information.".Length).Trim();
                return !string.IsNullOrWhiteSpace(propName);
            }

            // "Design Tracking Properties.X" => Design Tracking Properties / X
            if (raw.StartsWith("Design Tracking Properties.", StringComparison.OrdinalIgnoreCase))
            {
                setName = "Design Tracking Properties";
                propName = raw.Substring("Design Tracking Properties.".Length).Trim();
                return !string.IsNullOrWhiteSpace(propName);
            }

            // Short aliases
            if (raw.StartsWith("Summary.", StringComparison.OrdinalIgnoreCase))
            {
                setName = "Summary Information";
                propName = raw.Substring("Summary.".Length).Trim();
                return !string.IsNullOrWhiteSpace(propName);
            }

            if (raw.StartsWith("DT.", StringComparison.OrdinalIgnoreCase))
            {
                setName = "Design Tracking Properties";
                propName = raw.Substring("DT.".Length).Trim();
                return !string.IsNullOrWhiteSpace(propName);
            }

            return false;
        }

        /// <summary>
        /// Expands friendly set names to candidate set names.
        /// Examples:
        ///  - "Summary Information" -> ["Summary Information", "Inventor Summary Information"]
        ///  - "Inventor Summary Information" -> same list
        ///  - "Design Tracking Properties" -> ["Design Tracking Properties"]
        ///  - "User Defined Properties" -> ["User Defined Properties", "Inventor User Defined Properties"]
        ///  - Unknown -> [original]
        /// </summary>
        private static string[] ExpandSetAliases(string setName)
        {
            if (string.IsNullOrWhiteSpace(setName))
                return new[] { setName ?? string.Empty };

            if (EqualsAny(setName, SummaryInfoSets))
                return SummaryInfoSets;

            if (EqualsAny(setName, DesignTrackingSets))
                return DesignTrackingSets;

            if (EqualsAny(setName, UserDefinedSets))
                return UserDefinedSets;

            return new[] { setName };
        }

        private static bool EqualsAny(string a, string[] candidates)
        {
            foreach (var c in candidates)
            {
                if (string.Equals(a, c, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Try multiple candidate property sets for the same property name.
        /// Returns last attempted value (typically "ERROR") but exposes flags to indicate whether any set resolved.
        /// </summary>
        private static string TryGetIPropWithSetFallback(
            RenumberingContext ctx,
            dynamic src,
            string[] setNameCandidates,
            string propName,
            out bool anyTried,
            out bool anyNonError)
        {
            anyTried = false;
            anyNonError = false;

            string last = "ERROR";

            if (setNameCandidates == null || setNameCandidates.Length == 0)
                setNameCandidates = new[] { string.Empty };

            foreach (var set in setNameCandidates)
            {
                if (string.IsNullOrWhiteSpace(set)) continue;

                anyTried = true;

                var v = ctx.GetIPropString(src, set, propName) ?? "ERROR";
                last = v;

                if (!string.Equals(v, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    // resolved: either "" or value
                    anyNonError = true;
                    return v;
                }
            }

            return last;
        }

        private static string JoinSets(string[] sets)
        {
            if (sets == null || sets.Length == 0) return "<none>";
            return string.Join("|", sets);
        }
    }
}
