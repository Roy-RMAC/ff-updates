using Inventor;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal sealed class FramesOnlyNumberingScheme : IPartNumberingScheme
    {
        public string Name => "FramesOnly";

        // BOMStructure constants (matching StructuredNumberingScheme)
        private const int BOMSTRUCT_NORMAL = 51970;
        private const int BOMSTRUCT_REFERENCE = 51972;

        public void Run(RenumberingContext ctx, dynamic bomRows)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            ctx.Info("Start RMAC Part Numbering Protocol (FramesOnly)");
            ctx.Info($"Target assembly: {ctx.SafeName(ctx.TopAssembly)}");

            // Determine initial prefix from the top assembly PN (or fallback)
            string? topPn = ctx.GetIPropString(ctx.TopAssembly, "Design Tracking Properties", "Part Number");
            if (string.IsNullOrWhiteSpace(topPn))
                topPn = "A-01"; // orchestrator sets this, but keep a safe fallback

            ctx.Info($"FramesOnly: Top prefix = {topPn}");

            ctx.UpdateStatusText("Resetting flags\u2026");
            ctx.Info("Resetting PartSet flags...");
            ResetItemCheck(ctx, bomRows);

            // Per-parent prefix counters: XX-01, XX-02...
            var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            ctx.Info("Begin frames-only processing (XX-01...)");
            ProcessBom(ctx, bomRows, topPn, counters);

            ctx.Info("Frames-only numbering complete.");
        }

        private void ProcessBom(RenumberingContext ctx,
                                dynamic bomRows,
                                string parentPrefix,
                                Dictionary<string, int> counters)
        {
            int rowCount = ctx.GetCount(bomRows);
            ctx.Dbg($"FramesOnly: BOMRows count = {rowCount} | Prefix={parentPrefix}");

            int seen = 0, renFrame = 0, renSkel = 0, skippedNotMod = 0, skippedRef = 0, skippedAlready = 0;
            int recurseCalls = 0;

            for (int rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                ctx.StepProgress($"Processing frames ({rowIndex}/{rowCount})\u2026");

                dynamic row = ctx.ComItem(bomRows, rowIndex);
                if (row == null) continue;

                object? childRowsObj = ctx.TryGet(row, "ChildRows");

                // We may decide to recurse after evaluating this row's component definition(s)
                bool recurse = false;
                string recursePrefix = parentPrefix;

                foreach (dynamic compDef in ctx.GetRowComponentDefinitions(row))
                {
                    object? docObj = ctx.TryGet(compDef, "Document");
                    if (docObj == null) continue;
                    dynamic doc = docObj;

                    // Skip gate (must be checked BEFORE touching PartSet or recursing)
                    string skipReason;
                    if (ctx.ShouldSkip(doc, out skipReason))
                    {
                        ctx.Info($"SKIP: {ctx.SafeName(doc)} | {skipReason}");
                        // If this was an assembly, we intentionally do NOT recurse into its child rows.
                        continue;
                    }

                    int bomStruct = ctx.GetInt(compDef, "BOMStructure", -1);
                    if (bomStruct == BOMSTRUCT_REFERENCE)
                    {
                        skippedRef++;
                        continue;
                    }

                    bool isMod = true; // ShouldSkip already verified IsModifiable
                    int partSet = ctx.GetUserPropInt(doc, "PartSet", 0);
                    int docType = ctx.GetInt(doc, "DocumentType", -1);

                    string curPn = ctx.GetIPropString(doc, "Design Tracking Properties", "Part Number") ?? "<no PN>";
                    string name = ctx.SafeName(doc);

                    seen++;
                    ctx.Dbg($"FO r={rowIndex}/{rowCount} | {DocTypeName(docType)} | Mod={(isMod ? 1 : 0)} | PartSet={partSet} | BOM={BomStructName(bomStruct)} | PN={curPn} | {name}");

                    // Establish recursion rules:
                    // 1) NORMAL subassembly: recurse into child rows and (if it has a PN) use it as the next prefix.
                    if (childRowsObj != null &&
                        docType == (int)DocumentTypeEnum.kAssemblyDocumentObject &&
                        bomStruct == BOMSTRUCT_NORMAL)
                    {
                        recurse = true;

                        // Use the assembly's PN if set, otherwise inherit current prefix.
                        string asmPn = ctx.GetIPropString(doc, "Design Tracking Properties", "Part Number");
                        if (!string.IsNullOrWhiteSpace(asmPn))
                            recursePrefix = asmPn;
                        else
                            recursePrefix = parentPrefix;
                    }

                    // 2) FrameDoc container: recurse into child rows but KEEP same prefix (don't use A-xx-FRAME)
                    if (childRowsObj != null && ctx.HasInterest(doc, "FrameDoc"))
                    {
                        recurse = true;
                        recursePrefix = parentPrefix;
                    }

                    // If not modifiable: log similarly to Structured (avoid CC noise)

                    // Only number each doc once
                    if (partSet != 0)
                    {
                        skippedAlready++;
                        continue;
                    }

                    // Skeleton (optional but practical: still a FG artifact)
                    if (ctx.HasInterest(doc, "SkeletonDoc"))
                    {
                        string newPn = $"{parentPrefix}-SKELETON";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        renSkel++;
                        ctx.Info($"SET (Skeleton)  PN: {curPn} -> {newPn} | {name}");
                        continue;
                    }

                    // Frame members only
                    if (ctx.HasInterest(doc, "FrameMemberDoc"))
                    {
                        int n = GetAndIncCounter(counters, parentPrefix);

                        string newPn = $"{parentPrefix}-{n:00}";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        renFrame++;
                        ctx.Info($"SET (FrameMem)  PN: {curPn} -> {newPn} | {name}");
                        continue;
                    }

                    // Everything else: leave untouched (FramesOnly)
                }

                if (childRowsObj != null && recurse)
                {
                    recurseCalls++;
                    ctx.Dbg($"FO recurse: Prefix={recursePrefix} ChildRows={ctx.GetCount(childRowsObj)}");
                    ProcessBom(ctx, childRowsObj, recursePrefix, counters);
                }
            }

            ctx.Info($"FO SUMMARY: Rows={rowCount}, Seen={seen}, FrameRen={renFrame}, SkeletonRen={renSkel}, SkippedAlready={skippedAlready}, SkippedRef={skippedRef}, NotMod={skippedNotMod}, RecurseCalls={recurseCalls}");
        }

        private static int GetAndIncCounter(Dictionary<string, int> counters, string prefix)
        {
            if (!counters.TryGetValue(prefix, out int n))
                n = 1;

            counters[prefix] = n + 1;
            return n;
        }

        /// <summary>
        /// Same reset pattern as Structured:
        /// - Sets PartSet=0 on modifiable docs
        /// - Recurses into ChildRows for normal assemblies + FrameDoc rows
        /// </summary>
        private void ResetItemCheck(RenumberingContext ctx, dynamic bomRows)
        {
            int rowCount = ctx.GetCount(bomRows);
            ctx.Dbg($"ResetItemCheck (FramesOnly): BOMRows count = {rowCount}");

            for (int rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                dynamic row = ctx.ComItem(bomRows, rowIndex);
                if (row == null) continue;

                object? childRowsObj = ctx.TryGet(row, "ChildRows");
                bool recurse = false;

                foreach (dynamic compDef in ctx.GetRowComponentDefinitions(row))
                {
                    object? docObj = ctx.TryGet(compDef, "Document");
                    if (docObj == null) continue;
                    dynamic doc = docObj;

                    bool isMod = true; // ShouldSkip already verified IsModifiable
                    if (isMod)
                        ctx.SetUserPropInt(doc, "PartSet", 0);

                    int bomStruct = ctx.GetInt(compDef, "BOMStructure", -1);

                    if (childRowsObj != null &&
                        (bomStruct == BOMSTRUCT_NORMAL || ctx.HasInterest(doc, "FrameDoc")))
                    {
                        recurse = true;
                    }
                }

                if (childRowsObj != null && recurse)
                    ResetItemCheck(ctx, childRowsObj);
            }
        }

        private static string DocTypeName(int docType)
        {
            if (docType == (int)DocumentTypeEnum.kAssemblyDocumentObject) return "ASM";
            if (docType == (int)DocumentTypeEnum.kPartDocumentObject) return "PRT";
            if (docType == (int)DocumentTypeEnum.kDrawingDocumentObject) return "IDW";
            return docType.ToString(CultureInfo.InvariantCulture);
        }

        private static string BomStructName(int bomStruct)
        {
            return bomStruct switch
            {
                BOMSTRUCT_NORMAL => "NORMAL",
                BOMSTRUCT_REFERENCE => "REF",
                _ => bomStruct.ToString(CultureInfo.InvariantCulture)
            };
        }
    }
}
