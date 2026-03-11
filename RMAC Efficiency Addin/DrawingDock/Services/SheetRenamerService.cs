// DrawingDock/Services/SheetRenamerService.cs
using Inventor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    internal sealed class SheetRenamerService
    {
        // Regex copied from your DrawingDockPane (kept here so the service is self-contained).
        private static readonly Regex RxPartStructured = new Regex(@"^P-(\d{2})-(\d{2})$", RegexOptions.Compiled);
        private static readonly Regex RxPartSequential = new Regex(@"^P-(\d{3})$", RegexOptions.Compiled);
        private static readonly Regex RxAsm = new Regex(@"^A-(\d{2})([A-Za-z]*)$", RegexOptions.Compiled);
        private static readonly Regex RxFrameMember = new Regex(@"^(A-\d{2}[A-Za-z]*)-(\d{2})$", RegexOptions.Compiled);

        internal sealed class Options
        {
            /// <summary>
            /// If set, parts sheets are detected when title block definition name matches this (case-insensitive).
            /// If null/empty, detection falls back to contains "Parts".
            /// </summary>
            public string? PartsTitleBlockName { get; set; }

            /// <summary>Enable writing a log file to %TEMP%\RMAC_Efficiency_Addin.</summary>
            public bool EnableLog { get; set; } = true;

            /// <summary>Also attempt to reorder sheets in the browser tree.</summary>
            public bool ReorderSheets { get; set; } = true;

            /// <summary>Sheet ordering mode: GroupByAssembly (interleaved) or PartsAtEnd (sequential).</summary>
            public SheetOrderingMode OrderingMode { get; set; } = SheetOrderingMode.GroupByAssembly;
        }

        internal sealed class Result
        {
            public bool Success { get; }
            public string Status { get; }
            public IReadOnlyList<string> Warnings { get; }
            public string? LogPath { get; }

            internal Result(bool success, string status, List<string> warnings, string? logPath)
            {
                Success = success;
                Status = status;
                Warnings = warnings;
                LogPath = logPath;
            }
        }

        public Result Run(DrawingDocument dd, Options? options = null)
        {
            if (dd == null) throw new ArgumentNullException(nameof(dd));
            options ??= new Options();

            SheetRenameLog.Enabled = options.EnableLog;
            SheetRenameLog.Start();

            var warnings = new List<string>();

            try
            {
                SheetRenameLog.Info($"Rename Sheets started. Drawing='{SafeName(dd)}' Sheets={SafeGetCount(dd.Sheets)}");

                var infos = new List<SheetInfo>();
                var pnCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                int sheetIndex = 0;
                foreach (Sheet sh in dd.Sheets)
                {
                    sheetIndex++;
                    string shName = SafeSheetName(sh);

                    int viewCount = 0;
                    try { viewCount = sh.DrawingViews.Count; } catch { viewCount = 0; }

                    bool isPartsTemplate = false;
                    try { isPartsTemplate = IsPartsTitleBlock(sh, options.PartsTitleBlockName); } catch { isPartsTemplate = false; }

                    SheetRenameLog.Info($"Sheet[{sheetIndex}] '{shName}': Views={viewCount}, IsPartsTemplate={isPartsTemplate}");

                    var info = new SheetInfo { Sheet = sh, IsPartsTemplate = isPartsTemplate };

                    if (viewCount == 0)
                    {
                        info.SortGroup = 3;
                        info.BaseName = "Empty";
                        infos.Add(info);
                        SheetRenameLog.Info("  -> Marked Empty");
                        continue;
                    }

                    if (isPartsTemplate)
                    {
                        try
                        {
                            BuildPartsSheetName(sh, info, pnCache, warnings);
                            SheetRenameLog.Info($"  -> Parts sheet BaseName='{info.BaseName}', ParentAsm='{info.ParentAsm}', Range=({info.PartMin},{info.PartMax}) SortGroup={info.SortGroup}");
                        }
                        catch (Exception ex)
                        {
                            info.SortGroup = 1;
                            info.BaseName = "NonCompliant";
                            warnings.Add($"Parts sheet '{shName}' naming failed: {ex.Message}");
                            SheetRenameLog.Warn("  -> ERROR in BuildPartsSheetName: " + ex.Message);
                        }

                        infos.Add(info);
                        continue;
                    }

                    try
                    {
                        BuildAssemblySheetName(sh, info, pnCache, warnings);
                        SheetRenameLog.Info($"  -> Assembly sheet BaseName='{info.BaseName}', SortGroup={info.SortGroup}");
                    }
                    catch (Exception ex)
                    {
                        info.SortGroup = 1;
                        info.BaseName = "NonCompliant";
                        warnings.Add($"Assembly sheet '{shName}' naming failed: {ex.Message}");
                        SheetRenameLog.Warn("  -> ERROR in BuildAssemblySheetName: " + ex.Message);
                    }

                    infos.Add(info);
                }

                SheetRenameLog.Info($"Collected {infos.Count} SheetInfo entries. Applying suffixing...");
                ApplySuffixing(infos);
                DetectDuplicateFinalNames(infos, warnings);

                SheetRenameLog.Info("Suffixing result (BaseName -> FinalName):");
                foreach (var i in infos)
                    SheetRenameLog.Info($"  '{SafeSheetName(i.Sheet)}' | SortGroup={i.SortGroup} | Base='{i.BaseName}' -> Final='{i.FinalName}'");

                try { AddPartsRangeOverlapWarnings(infos, warnings); }
                catch (Exception ex)
                {
                    SheetRenameLog.Warn("AddPartsRangeOverlapWarnings failed: " + ex.Message);
                    warnings.Add("Parts range overlap check failed: " + ex.Message);
                }

                SheetRenameLog.Info("TwoPassRename starting...");
                TwoPassRename(infos, warnings);
                SheetRenameLog.Info("TwoPassRename complete.");

                if (options.ReorderSheets)
                {
                    SheetRenameLog.Info($"ReorderSheetsByName starting... Mode={options.OrderingMode}");
                    try { ReorderSheetsByName(dd, infos, warnings, options.OrderingMode); }
                    catch (Exception ex)
                    {
                        SheetRenameLog.Warn("ReorderSheetsByName threw: " + ex.Message);
                        warnings.Add("Sheet reorder failed: " + ex.Message);
                    }
                    SheetRenameLog.Info("ReorderSheetsByName complete.");
                }

                string status = $"Rename Sheets complete. Processed {infos.Count} sheets.";
                SheetRenameLog.Info(status);

                return new Result(true, status, warnings, SheetRenameLog.Path);
            }
            catch (Exception ex)
            {
                SheetRenameLog.Err("Unhandled exception: " + ex);
                return new Result(false, ex.Message, warnings, SheetRenameLog.Path);
            }
            finally
            {
                SheetRenameLog.Info("Rename Sheets finished.");
                SheetRenameLog.Stop();
            }
        }

        // ============================================================
        // Data model (copied from pane)
        // ============================================================
        private sealed class SheetInfo
        {
            public Sheet Sheet = null!;
            public string BaseName = "";
            public string FinalName = "";

            // Ordering buckets:
            // 0 = Compliant (Assemblies + their Frame sheets), 1 = NonCompliant, 2 = Part sheets, 3 = Empty
            public int SortGroup;
            public bool IsFrameSheet;
            public string? ParentAsm;
            public int? PartMin;
            public int? PartMax;
            public int? FrameMin;
            public int? FrameMax;
            public bool IsPartsTemplate;
        }

        // ============================================================
        // Title block detection
        // ============================================================
        private static bool IsPartsTitleBlock(Sheet sh, string? configuredPartsTitleBlockName)
        {
            try
            {
                var tb = sh.TitleBlock;
                if (tb == null) return false;

                var defName = tb.Definition?.Name ?? "";
                var configured = (configuredPartsTitleBlockName ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(configured) &&
                    string.Equals(defName, configured, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (defName.IndexOf("Parts", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }

            return false;
        }

        // ============================================================
        // Naming logic
        // ============================================================
        private static void BuildAssemblySheetName(Sheet sh, SheetInfo info, Dictionary<string, string> pnCache, List<string> warnings)
        {
            AssemblyDocument? asm = null;

            try
            {
                foreach (DrawingView v in sh.DrawingViews)
                {
                    var d = v.ReferencedDocumentDescriptor?.ReferencedDocument;

                    if (d is AssemblyDocument a)
                    {
                        asm = a;
                        break;
                    }

                    if (d is PresentationDocument pdoc)
                    {
                        try
                        {
                            foreach (DocumentDescriptor dd in pdoc.ReferencedDocumentDescriptors)
                            {
                                if (dd?.ReferencedDocument is AssemblyDocument a2)
                                {
                                    asm = a2;
                                    break;
                                }
                            }

                            if (asm != null)
                                break;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (asm == null)
            {
                info.SortGroup = 1;
                info.BaseName = "NonCompliant";
                warnings.Add($"Sheet '{SafeSheetName(sh)}' could not find an assembly view. Marked NonCompliant.");
                return;
            }

            string asmPn = GetPartNumberCached(asm, pnCache) ?? asm.DisplayName;
            info.SortGroup = 0;
            info.BaseName = asmPn;
        }

        private static void BuildPartsSheetName(Sheet sh, SheetInfo info, Dictionary<string, string> pnCache, List<string> warnings)
        {
            var frameParents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var frameNos = new List<int>();
            var partNosSequential = new List<int>();
            var partNosStructured = new List<int>();
            var structuredParents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var otherPartNos = new List<string>();

            try
            {
                foreach (DrawingView v in sh.DrawingViews)
                {
                    var d = v.ReferencedDocumentDescriptor?.ReferencedDocument;
                    if (d is not PartDocument pd) continue;

                    string? pn = GetPartNumberCached(pd, pnCache);
                    if (string.IsNullOrWhiteSpace(pn)) continue;
                    pn = pn.Trim();

                    var mf = RxFrameMember.Match(pn);
                    if (mf.Success)
                    {
                        string parentAsm = mf.Groups[1].Value;
                        int n = int.Parse(mf.Groups[2].Value);
                        frameParents[parentAsm] = frameParents.TryGetValue(parentAsm, out var c) ? c + 1 : 1;
                        frameNos.Add(n);
                        continue;
                    }

                    var ms = RxPartSequential.Match(pn);
                    if (ms.Success)
                    {
                        partNosSequential.Add(int.Parse(ms.Groups[1].Value));
                        continue;
                    }

                    var m = RxPartStructured.Match(pn);
                    if (m.Success)
                    {
                        string parentXX = m.Groups[1].Value;
                        int partYY = int.Parse(m.Groups[2].Value);
                        structuredParents[parentXX] = structuredParents.TryGetValue(parentXX, out int c) ? c + 1 : 1;
                        partNosStructured.Add(partYY);
                        continue;
                    }

                    otherPartNos.Add(pn);
                }
            }
            catch { }

            bool hasFrames = frameNos.Count > 0;
            bool hasStandardParts = partNosSequential.Count > 0 || partNosStructured.Count > 0 || otherPartNos.Count > 0;

            if (hasFrames && hasStandardParts)
            {
                info.SortGroup = 1;
                info.BaseName = "NonCompliant";
                warnings.Add($"Sheet '{SafeSheetName(sh)}' contains both frame-member part numbers and standard part numbers. Marked NonCompliant.");
                return;
            }

            // Check for mixed part numbering schemes on the same sheet
            int schemeCount = (partNosSequential.Count > 0 ? 1 : 0)
                            + (partNosStructured.Count > 0 ? 1 : 0)
                            + (otherPartNos.Count > 0 ? 1 : 0);
            if (schemeCount > 1)
            {
                var schemes = new List<string>();
                if (partNosStructured.Count > 0) schemes.Add($"structured (e.g. P-{structuredParents.First().Key}-{partNosStructured.First():00})");
                if (partNosSequential.Count > 0) schemes.Add($"sequential (e.g. P-{partNosSequential.First():000})");
                if (otherPartNos.Count > 0) schemes.Add($"other ({otherPartNos.First()})");

                info.SortGroup = 1;
                info.BaseName = "NonCompliant";
                warnings.Add($"Sheet '{SafeSheetName(sh)}' mixes part numbering schemes: {string.Join(" + ", schemes)}. Marked NonCompliant.");
                return;
            }

            if (hasFrames)
            {
                if (frameParents.Count == 0)
                {
                    info.SortGroup = 1;
                    info.BaseName = "NonCompliant";
                    return;
                }

                string parentAsm = frameParents.OrderByDescending(k => k.Value).First().Key;

                if (frameParents.Count > 1)
                {
                    string all = string.Join(", ", frameParents.Keys.OrderBy(x => x));
                    warnings.Add($"Frame sheet '{SafeSheetName(sh)}' contains frame members from multiple assemblies ({all}). Using {parentAsm} for naming.");
                }

                int minF = frameNos.Min();
                int maxF = frameNos.Max();

                info.ParentAsm = parentAsm;
                info.FrameMin = minF;
                info.FrameMax = maxF;
                info.PartMin = null;
                info.PartMax = null;
                info.SortGroup = 0;
                info.IsFrameSheet = true;
                info.BaseName = minF == maxF
                    ? $"{parentAsm} Frame - {minF:00}"
                    : $"{parentAsm} Frame - {minF:00} - {maxF:00}";
                return;
            }

            if (partNosSequential.Count > 0)
            {
                int minS = partNosSequential.Min();
                int maxS = partNosSequential.Max();

                info.ParentAsm = null;
                info.PartMin = minS;
                info.PartMax = maxS;
                info.FrameMin = null;
                info.FrameMax = null;
                info.SortGroup = 2;
                info.IsFrameSheet = false;
                info.BaseName = minS == maxS
                    ? $"P-{minS:000}"
                    : $"P-{minS:000} - P-{maxS:000}";
                return;
            }

            if (partNosStructured.Count > 0 && structuredParents.Count > 0)
            {
                string chosenParentXX = structuredParents.OrderByDescending(k => k.Value).First().Key;

                if (structuredParents.Count > 1)
                {
                    string allParents = string.Join(", ", structuredParents.Keys.OrderBy(x => x).Select(x => $"A-{x}"));
                    warnings.Add($"Parts sheet '{SafeSheetName(sh)}' contains parts from multiple parents ({allParents}). Using A-{chosenParentXX} for naming.");
                }

                int min = partNosStructured.Min();
                int max = partNosStructured.Max();

                string parentAsm = $"A-{chosenParentXX}";
                info.ParentAsm = parentAsm;
                info.PartMin = min;
                info.PartMax = max;
                info.FrameMin = null;
                info.FrameMax = null;

                info.SortGroup = 2;
                info.IsFrameSheet = false;
                info.BaseName = $"{parentAsm} Parts ({min:00}-{max:00})";
                return;
            }

            if (otherPartNos.Count > 0)
            {
                var ordered = otherPartNos
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (ordered.Count == 0)
                {
                    info.SortGroup = 1;
                    info.BaseName = "NonCompliant";
                    return;
                }

                string first = ordered.First();
                string last = ordered.Last();

                info.ParentAsm = null;
                info.PartMin = null;
                info.PartMax = null;
                info.FrameMin = null;
                info.FrameMax = null;
                info.SortGroup = 2;
                info.IsFrameSheet = false;
                info.BaseName = string.Equals(first, last, StringComparison.OrdinalIgnoreCase)
                    ? first
                    : $"{first} - {last}";
                return;
            }

            info.SortGroup = 1;
            info.BaseName = "NonCompliant";
            warnings.Add($"Parts sheet '{SafeSheetName(sh)}' contains no resolvable part numbers. Marked NonCompliant.");
        }

        private static void ApplySuffixing(List<SheetInfo> infos)
        {
            var assemblyCounts = infos
                .Where(i => i.SortGroup == 0 && !i.IsFrameSheet && !i.IsPartsTemplate)
                .GroupBy(i => i.BaseName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            int emptyIdx = 0;
            int nonIdx = 0;
            var suffixIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var i in infos)
            {
                if (i.SortGroup == 3)
                {
                    i.FinalName = $"Empty {ToLetters(emptyIdx++)}";
                    continue;
                }

                if (i.SortGroup == 1)
                {
                    i.FinalName = $"NonCompliant {ToLetters(nonIdx++)}";
                    continue;
                }

                if (i.SortGroup == 0 && !i.IsFrameSheet && !i.IsPartsTemplate &&
                    assemblyCounts.TryGetValue(i.BaseName, out int c) && c > 1)
                {
                    int idx = suffixIndex.TryGetValue(i.BaseName, out var cur) ? cur : 0;
                    suffixIndex[i.BaseName] = idx + 1;
                    i.FinalName = $"{i.BaseName}{ToLetters(idx)}";
                    continue;
                }

                i.FinalName = i.BaseName;
            }
        }

        private static void AddPartsRangeOverlapWarnings(List<SheetInfo> infos, List<string> warnings)
        {
            var parts = infos
                .Where(i => i.SortGroup == 2 && i.PartMin.HasValue && i.PartMax.HasValue)
                .ToList();

            // Group structured parts by ParentAsm, sequential parts (ParentAsm == null) as their own group
            var structured = parts.Where(p => p.ParentAsm != null).ToList();
            foreach (var grp in structured.GroupBy(p => p.ParentAsm!, StringComparer.OrdinalIgnoreCase))
            {
                CheckRangeOverlaps(grp.Key, grp, warnings, "00");
            }

            var sequential = parts.Where(p => p.ParentAsm == null).ToList();
            if (sequential.Count > 1)
            {
                CheckRangeOverlaps("Sequential", sequential, warnings, "000");
            }
        }

        private static void CheckRangeOverlaps(string groupLabel, IEnumerable<SheetInfo> sheets, List<string> warnings, string numFormat)
        {
            var list = sheets
                .Select(p => (min: p.PartMin!.Value, max: p.PartMax!.Value, name: p.FinalName))
                .OrderBy(x => x.min)
                .ToList();

            for (int a = 0; a < list.Count; a++)
            {
                for (int b = a + 1; b < list.Count; b++)
                {
                    bool overlap = list[a].min <= list[b].max && list[b].min <= list[a].max;
                    if (overlap)
                    {
                        warnings.Add(
                            $"{groupLabel} parts ranges overlap/crossover: " +
                            $"'{list[a].name}' ({list[a].min.ToString(numFormat)}-{list[a].max.ToString(numFormat)}) vs " +
                            $"'{list[b].name}' ({list[b].min.ToString(numFormat)}-{list[b].max.ToString(numFormat)}).");
                    }
                }
            }
        }

        private static void DetectDuplicateFinalNames(List<SheetInfo> infos, List<string> warnings)
        {
            var duplicates = infos
                .GroupBy(i => i.FinalName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var grp in duplicates)
            {
                var sheetNames = grp.Select(i => SafeSheetName(i.Sheet)).ToList();
                warnings.Add(
                    $"Multiple sheets resolve to the same name '{grp.Key}': " +
                    $"{string.Join(", ", sheetNames.Select(n => $"'{n}'"))}. " +
                    "This typically means duplicate parts exist on different sheets.");

                SheetRenameLog.Warn($"Duplicate FinalName '{grp.Key}' across sheets: {string.Join(", ", sheetNames)}");

                // Disambiguate to prevent __tmp__ names persisting after rename
                int suffix = 1;
                foreach (var i in grp.Skip(1))
                {
                    suffix++;
                    i.FinalName = $"{grp.Key} ({suffix})";
                }
            }
        }

        private static void TwoPassRename(List<SheetInfo> infos, List<string> warnings)
        {
            string token = Guid.NewGuid().ToString("N");
            int idx = 1;

            foreach (var i in infos)
            {
                try { i.Sheet.Name = $"__tmp__{token}_{idx++:000}"; }
                catch { }
            }

            foreach (var i in infos)
            {
                try { i.Sheet.Name = i.FinalName; }
                catch (Exception ex)
                {
                    string current = SafeSheetName(i.Sheet);
                    warnings.Add($"Failed to rename sheet to '{i.FinalName}' (stuck as '{current}'): {ex.Message}");
                    SheetRenameLog.Warn($"TwoPassRename pass 2 failed for '{i.FinalName}': {ex.Message}");
                }
            }
        }

        private static void ReorderSheetsByName(DrawingDocument dd, List<SheetInfo> infos, List<string> warnings, SheetOrderingMode mode)
        {
            // Ordered list of sheets
            var orderedInfos = infos.ToList();
            Comparison<SheetInfo> comparator = mode == SheetOrderingMode.GroupByAssembly
                ? CompareSheetInfoGroupByAssembly
                : CompareSheetInfoPartsAtEnd;
            orderedInfos.Sort(comparator);

            SheetRenameLog.Info("Sorted sheet order:");
            for (int idx = 0; idx < orderedInfos.Count; idx++)
            {
                var si = orderedInfos[idx];
                SheetRenameLog.Info($"  [{idx}] SortGroup={si.SortGroup} Final='{si.FinalName}' ParentAsm='{si.ParentAsm}' IsFrame={si.IsFrameSheet}");
            }

            var ordered = orderedInfos.Select(i => i.Sheet).ToList();

            var browserPane = GetDrawingBrowserPane(dd);
            if (browserPane == null)
            {
                warnings.Add("Could not access a BrowserPane to reorder sheets. Sheets were renamed but not reordered.");
                return;
            }

            BrowserNode? targetNode = null;
            try
            {
                var lastSheet = ordered[ordered.Count - 1];
                targetNode = browserPane.GetBrowserNodeFromObject(lastSheet);
            }
            catch { targetNode = null; }

            if (targetNode == null)
            {
                warnings.Add("Could not resolve a valid target sheet node for reordering. Sheets were renamed but not reordered.");
                return;
            }

            foreach (var sh in ordered)
            {
                try
                {
                    var sheetNode = browserPane.GetBrowserNodeFromObject(sh);
                    browserPane.Reorder(targetNode, false, sheetNode);
                    targetNode = sheetNode;
                }
                catch (Exception exOne)
                {
                    warnings.Add($"Reorder failed for sheet '{SafeSheetName(sh)}': {exOne.Message}");
                }
            }
        }

        /// <summary>
        /// PartsAtEnd mode: All assemblies/frames first (SortGroup 0), then all parts (SortGroup 2),
        /// then NonCompliant, then Empty.
        /// </summary>
        private static int CompareSheetInfoPartsAtEnd(SheetInfo a, SheetInfo b)
        {
            // Map SortGroup to desired order: 0 (Asm/Frame) → 2 (Parts) → 1 (NonCompliant) → 3 (Empty)
            int aOrder = GetPartsAtEndOrder(a.SortGroup);
            int bOrder = GetPartsAtEndOrder(b.SortGroup);
            int g = aOrder.CompareTo(bOrder);
            if (g != 0) return g;

            switch (a.SortGroup)
            {
                case 0:
                    return CompareAssemblyFrameSheets(a, b);

                case 2:
                    {
                        // Group by ParentAsm first — sequential parts (null) before structured
                        bool aHasParent = a.ParentAsm != null;
                        bool bHasParent = b.ParentAsm != null;

                        if (aHasParent != bHasParent)
                            return aHasParent ? 1 : -1;

                        if (aHasParent) // both have ParentAsm — sort by assembly number
                        {
                            var (aNum, aSuf) = ParseAssemblyKey(a.ParentAsm!);
                            var (bNum, bSuf) = ParseAssemblyKey(b.ParentAsm!);

                            int pn = aNum.CompareTo(bNum);
                            if (pn != 0) return pn;

                            int ps = StringComparer.OrdinalIgnoreCase.Compare(aSuf, bSuf);
                            if (ps != 0) return ps;
                        }

                        return ComparePartRange(a, b);
                    }

                default:
                    return StringComparer.OrdinalIgnoreCase.Compare(a.FinalName, b.FinalName);
            }
        }

        /// <summary>
        /// GroupByAssembly mode: Each assembly is followed by its frames then its parts,
        /// before moving to the next assembly. Sequential parts (no parent) come after
        /// all assembly groups. NonCompliant and Empty remain last.
        /// </summary>
        private static int CompareSheetInfoGroupByAssembly(SheetInfo a, SheetInfo b)
        {
            // NonCompliant (1) and Empty (3) always sort last, in that order
            int aEffective = GetGroupByAssemblyBucket(a);
            int bEffective = GetGroupByAssemblyBucket(b);

            // Bucket: 0 = assembly-grouped (asm/frame/structured parts), 1 = sequential parts, 2 = NonCompliant, 3 = Empty
            int bucket = aEffective.CompareTo(bEffective);
            if (bucket != 0) return bucket;

            switch (aEffective)
            {
                case 0: // Assembly-grouped sheets (assemblies, frames, structured parts)
                    {
                        // Sort by parent assembly number first
                        string aAsm = GetAssemblyKey(a);
                        string bAsm = GetAssemblyKey(b);

                        var (aNum, aSuf) = ParseAssemblyKey(aAsm);
                        var (bNum, bSuf) = ParseAssemblyKey(bAsm);

                        int n = aNum.CompareTo(bNum);
                        if (n != 0) return n;

                        int s = StringComparer.OrdinalIgnoreCase.Compare(aSuf, bSuf);
                        if (s != 0) return s;

                        // Within same assembly: assembly sheets → frames → parts
                        int aType = GetSheetTypeOrder(a);
                        int bType = GetSheetTypeOrder(b);
                        int t = aType.CompareTo(bType);
                        if (t != 0) return t;

                        // Within same type, use type-specific ordering
                        if (aType == 0) // both assembly sheets
                        {
                            // Multiple sheets for same assembly (suffixed)
                            var (aFNum, aFSuf) = ParseAssemblyKey(a.FinalName);
                            var (bFNum, bFSuf) = ParseAssemblyKey(b.FinalName);
                            int fn = aFNum.CompareTo(bFNum);
                            if (fn != 0) return fn;
                            int fs = StringComparer.OrdinalIgnoreCase.Compare(aFSuf, bFSuf);
                            if (fs != 0) return fs;
                            return StringComparer.OrdinalIgnoreCase.Compare(a.FinalName, b.FinalName);
                        }

                        if (aType == 1) // both frame sheets
                        {
                            int amin = a.FrameMin ?? int.MaxValue;
                            int bmin = b.FrameMin ?? int.MaxValue;
                            int mn = amin.CompareTo(bmin);
                            if (mn != 0) return mn;
                            int amax = a.FrameMax ?? int.MaxValue;
                            int bmax = b.FrameMax ?? int.MaxValue;
                            return amax.CompareTo(bmax);
                        }

                        // aType == 2: both structured parts
                        return ComparePartRange(a, b);
                    }

                case 1: // Sequential parts (no parent)
                    return ComparePartRange(a, b);

                default: // NonCompliant or Empty
                    return StringComparer.OrdinalIgnoreCase.Compare(a.FinalName, b.FinalName);
            }
        }

        /// <summary>Returns the assembly key for grouping: the original assembly PN (BaseName)
        /// for assembly sheets, or the ParentAsm for frame/parts sheets.</summary>
        private static string GetAssemblyKey(SheetInfo info)
        {
            if (info.SortGroup == 0 && !info.IsFrameSheet)
                return info.BaseName; // assembly sheet — use BaseName so suffixed duplicates (A-01A, A-01B) group together
            return info.ParentAsm ?? "";
        }

        /// <summary>Order within an assembly group: 0 = assembly sheet, 1 = frame sheet, 2 = parts sheet.</summary>
        private static int GetSheetTypeOrder(SheetInfo info)
        {
            if (info.SortGroup == 0 && !info.IsFrameSheet) return 0; // assembly
            if (info.SortGroup == 0 && info.IsFrameSheet) return 1;  // frame
            return 2; // parts (SortGroup 2 with ParentAsm)
        }

        /// <summary>Bucket for GroupByAssembly mode:
        /// 0 = assembly-grouped (asm/frame/structured parts with parent),
        /// 1 = sequential parts (no parent), 2 = NonCompliant, 3 = Empty.</summary>
        private static int GetGroupByAssemblyBucket(SheetInfo info)
        {
            if (info.SortGroup == 0) return 0; // assembly or frame
            if (info.SortGroup == 2 && info.ParentAsm != null) return 0; // structured parts with parent
            if (info.SortGroup == 2) return 1; // sequential parts (no parent)
            if (info.SortGroup == 1) return 2; // NonCompliant
            return 3; // Empty
        }

        /// <summary>Order for PartsAtEnd mode: Asm/Frame (0) → Parts (1) → NonCompliant (2) → Empty (3).</summary>
        private static int GetPartsAtEndOrder(int sortGroup) => sortGroup switch
        {
            0 => 0, // Assemblies/Frames
            2 => 1, // Parts
            1 => 2, // NonCompliant
            3 => 3, // Empty
            _ => 4
        };

        /// <summary>Compare assembly and frame sheets within SortGroup 0.</summary>
        private static int CompareAssemblyFrameSheets(SheetInfo a, SheetInfo b)
        {
            // Use BaseName for assembly sheets so suffixed duplicates (A-01A, A-01B) group with their parent assembly
            string aKeyName = a.IsFrameSheet ? a.ParentAsm ?? "" : a.BaseName;
            string bKeyName = b.IsFrameSheet ? b.ParentAsm ?? "" : b.BaseName;

            var (an, asuf) = ParseAssemblyKey(aKeyName);
            var (bn, bsuf) = ParseAssemblyKey(bKeyName);

            int n = an.CompareTo(bn);
            if (n != 0) return n;

            int s = StringComparer.OrdinalIgnoreCase.Compare(asuf, bsuf);
            if (s != 0) return s;

            int at = a.IsFrameSheet ? 1 : 0;
            int bt = b.IsFrameSheet ? 1 : 0;
            int t = at.CompareTo(bt);
            if (t != 0) return t;

            if (!a.IsFrameSheet)
            {
                var (aNum, aSuf) = ParseAssemblyKey(a.FinalName);
                var (bNum, bSuf) = ParseAssemblyKey(b.FinalName);

                int nn = aNum.CompareTo(bNum);
                if (nn != 0) return nn;

                int ss = StringComparer.OrdinalIgnoreCase.Compare(aSuf, bSuf);
                if (ss != 0) return ss;

                return StringComparer.OrdinalIgnoreCase.Compare(a.FinalName, b.FinalName);
            }

            int amin = a.FrameMin ?? int.MaxValue;
            int bmin = b.FrameMin ?? int.MaxValue;
            int mn = amin.CompareTo(bmin);
            if (mn != 0) return mn;

            int amax = a.FrameMax ?? int.MaxValue;
            int bmax = b.FrameMax ?? int.MaxValue;
            int mx = amax.CompareTo(bmax);
            if (mx != 0) return mx;

            return StringComparer.OrdinalIgnoreCase.Compare(a.FinalName, b.FinalName);
        }

        /// <summary>Compare part sheets by PartMin then PartMax range.</summary>
        private static int ComparePartRange(SheetInfo a, SheetInfo b)
        {
            int amin = a.PartMin ?? int.MaxValue;
            int bmin = b.PartMin ?? int.MaxValue;
            int n = amin.CompareTo(bmin);
            if (n != 0) return n;

            int amax = a.PartMax ?? int.MaxValue;
            int bmax = b.PartMax ?? int.MaxValue;
            int m = amax.CompareTo(bmax);
            if (m != 0) return m;

            return StringComparer.OrdinalIgnoreCase.Compare(a.FinalName, b.FinalName);
        }

        private static (int num, string suffix) ParseAssemblyKey(string name)
        {
            try
            {
                var m = RxAsm.Match(name.Trim());
                if (m.Success)
                {
                    int num = int.Parse(m.Groups[1].Value);
                    string suf = m.Groups[2].Value ?? string.Empty;
                    return (num, suf);
                }
            }
            catch { }

            return (int.MaxValue, string.Empty);
        }

        // ============================================================
        // Helpers
        // ============================================================
        private static string? GetPartNumberCached(object doc, Dictionary<string, string> pnCache)
        {
            string key = GetDocCacheKey(doc);
            if (pnCache.TryGetValue(key, out var v)) return v;

            string? pn = GetPropertyValueSafe(doc, "Design Tracking Properties", "Part Number");
            if (pn != null) pnCache[key] = pn;
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

        private static int SafeGetCount(object sheetsCollection)
        {
            try { return ((dynamic)sheetsCollection).Count; }
            catch { return 0; }
        }

        private static BrowserPane? GetDrawingBrowserPane(DrawingDocument dd)
        {
            try
            {
                foreach (BrowserPane p in dd.BrowserPanes)
                {
                    if (string.Equals(p.Name, "Model", StringComparison.OrdinalIgnoreCase)) return p;
                    if (string.Equals(p.Name, "Drawing", StringComparison.OrdinalIgnoreCase)) return p;
                }

                dynamic panes = dd.BrowserPanes;
                try { return (BrowserPane)panes.Item(1); } catch { }
                try { return (BrowserPane)panes.get_Item(1); } catch { }
                try { return (BrowserPane)panes[1]; } catch { }
            }
            catch { }

            return null;
        }

        private static string SafeSheetName(Sheet sh)
        {
            try { return sh.Name; }
            catch { return "<unknown sheet>"; }
        }

        private static string SafeName(object doc)
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

            return "<unknown doc>";
        }

        private static string ToLetters(int index)
        {
            index = Math.Max(0, index);
            var s = "";
            while (true)
            {
                int rem = index % 26;
                s = (char)('A' + rem) + s;
                index = index / 26 - 1;
                if (index < 0) break;
            }
            return s;
        }

        // ============================================================
        // Logging (copied)
        // ============================================================
        private static class SheetRenameLog
        {
            public static bool Enabled = true;
            public static string? Path;
            private static StreamWriter? _w;

            public static void Start()
            {
                if (!Enabled) return;

                try
                {
                    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RMAC_Efficiency_Addin");
                    Directory.CreateDirectory(dir);

                    Path = System.IO.Path.Combine(dir, $"RMAC_RenameSheets_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    _w = new StreamWriter(Path, append: false, encoding: Encoding.UTF8) { AutoFlush = true };

                    Info("Log started");
                }
                catch
                {
                    Path = null;
                    _w = null;
                }
            }

            public static void Stop()
            {
                try { _w?.Flush(); } catch { }
                try { _w?.Dispose(); } catch { }
                _w = null;
            }

            public static void Info(string msg) => Write("INFO", msg);
            public static void Warn(string msg) => Write("WARN", msg);
            public static void Err(string msg) => Write("ERR ", msg);

            private static void Write(string lvl, string msg)
            {
                if (!Enabled) return;

                string line = $"{DateTime.Now:HH:mm:ss.fff} [{lvl}] {msg}";
                try { System.Diagnostics.Debug.WriteLine("[RMAC][RenameSheets] " + line); } catch { }
                try { _w?.WriteLine(line); } catch { }
            }
        }
    }
}
