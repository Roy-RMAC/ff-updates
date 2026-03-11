using Inventor;
using System;
using System.Globalization;

namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal sealed class SequentialNumberingScheme : IPartNumberingScheme
    {
        public string Name => "Sequential";

        // BOMStructure constants (same pattern as StructuredNumberingScheme)
        private const int BOMSTRUCT_NORMAL = 51970;
        private const int BOMSTRUCT_REFERENCE = 51972;

        public void Run(RenumberingContext ctx, dynamic bomRows)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            ctx.Info("Start RMAC Part Numbering Protocol (Sequential)");
            ctx.Info($"Target assembly: {ctx.SafeName(ctx.TopAssembly)}");

            ctx.UpdateStatusText("Resetting flags\u2026");
            ctx.Info("Resetting PartSet flags...");
            ResetItemCheck(ctx, bomRows);

            // Match Structured semantics:
            // - Assemblies get A-## using ctx.AssemblyCounter (starts at 2)
            // - Parts get P-### using a dedicated part counter
            // - Do NOT dive into child rows until the current BOM block is fully processed

            ctx.AssemblyCounter = 2;

            // Optionally renumber top assembly (if not fixed)
            try
            {
                if (!ctx.KeepTopLevelAssemblyPartNumberFixed && ctx.IsModifiable(ctx.TopAssembly))
                {
                    string oldTopPn = ctx.GetIPropString(ctx.TopAssembly, "Design Tracking Properties", "Part Number") ?? "<no PN>";
                    ctx.SetPartNumber(ctx.TopAssembly, "A-01");
                    ctx.SaveIfDirty(ctx.TopAssembly);
                    ctx.Info($"TOP PN: {oldTopPn} -> A-01");
                }
            }
            catch { /* best-effort only */ }

            ctx.Info("Begin sequential processing (Parts: P-001..., Assemblies: A-02...)");
            int partCounter = 1;
            SetPartNumbersSequential(ctx, bomRows, ref partCounter);

            ctx.Info($"Sequential numbering complete. Last part counter = {partCounter - 1:000}, Last assembly counter = {ctx.AssemblyCounter - 1:00}");
        }

        private void SetPartNumbersSequential(RenumberingContext ctx, dynamic bomRows, ref int partCounter)
        {
            int rowCount = ctx.GetCount(bomRows);
            ctx.Dbg($"Sequential: BOMRows count = {rowCount}");

            int seen = 0, renumbered = 0, skippedNotMod = 0, skippedRef = 0, skippedVol0 = 0, skippedAlready = 0;

            // Breadth-first within this block: process all rows first, then recurse.
            // This avoids interleaving parent numbering with nested children.
            var deferredChildBlocks = new System.Collections.Generic.List<dynamic>();

            for (int rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                ctx.StepProgress($"Numbering components ({rowIndex}/{rowCount})\u2026");

                dynamic row = ctx.ComItem(bomRows, rowIndex);
                if (row == null) continue;

                object? childRowsObj = ctx.TryGet(row, "ChildRows");

                bool recurse = false;

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

                        // If this doc is an assembly, skipping it implicitly skips its subtree.
                        // To avoid recursing, ensure recurse stays false and just continue to next compDef.
                        continue;
                    }

                    int bomStruct = ctx.GetInt(compDef, "BOMStructure", -1);
                    if (bomStruct == BOMSTRUCT_REFERENCE)
                    {
                        skippedRef++;
                        continue;
                    }

                    bool isMod = ctx.IsModifiable(doc);
                    int partSet = ctx.GetUserPropInt(doc, "PartSet", 0);
                    int docType = ctx.GetInt(doc, "DocumentType", -1);
                    double vol = ctx.GetVolume(compDef);

                    string curPn = ctx.GetIPropString(doc, "Design Tracking Properties", "Part Number") ?? "<no PN>";
                    string name = ctx.SafeName(doc);

                    seen++;

                    ctx.Dbg(
                        $"SEQ r={rowIndex}/{rowCount} | {DocTypeName(docType)} | Mod={(isMod ? 1 : 0)} | PartSet={partSet} | BOM={BomStructName(bomStruct)} | Vol={vol:0.####} | PN={curPn} | {name}"
                    );

                    // Decide recursion (same philosophy as Structured)
                    if (childRowsObj != null && (bomStruct == BOMSTRUCT_NORMAL || ctx.HasInterest(doc, "FrameDoc")))
                        recurse = true;

                    // Skip skeleton docs (keeps behavior aligned with your Structured logic intent)
                    if (ctx.HasInterest(doc, "SkeletonDoc"))
                        continue;

                    // Only renumber each modifiable doc once
                    if (!isMod)
                    {
                        skippedNotMod++;

                        // Match Structured's "not modifiable" noise reduction
                        if (!ctx.PropertySetExists(doc, "ContentCenter"))
                            ctx.Info($"{curPn} is not modifiable | {name}");

                        continue;
                    }

                    if (partSet != 0)
                    {
                        skippedAlready++;
                        continue;
                    }

                    // Only assign to parts/assemblies.
                    if (docType != (int)DocumentTypeEnum.kPartDocumentObject &&
                        docType != (int)DocumentTypeEnum.kAssemblyDocumentObject)
                        continue;

                    // Assemblies get A-## (own counter), Parts get P-### (own counter)
                    if (docType == (int)DocumentTypeEnum.kAssemblyDocumentObject)
                    {
                        string newPn = $"A-{ctx.AssemblyCounter:00}";
                        ctx.AssemblyCounter++;

                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        renumbered++;
                        ctx.Info($"SET (SeqAsm) PN: {curPn} -> {newPn} | {name}");
                    }
                    else
                    {
                        // For PART documents: skip true zero-volume items (virtual-ish / empty)
                        if (docType == (int)DocumentTypeEnum.kPartDocumentObject && vol <= 0)
                        {
                            skippedVol0++;
                            continue;
                        }

                        string newPn = $"P-{partCounter:000}";
                        partCounter++;

                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        renumbered++;
                        ctx.Info($"SET (SeqPrt) PN: {curPn} -> {newPn} | {name}");
                    }
                }

                // If any component definition in this row caused recurse=true, defer child processing.
                // Note: If the row's assembly was skipped above, recurse will remain false.
                if (childRowsObj != null && recurse)
                    deferredChildBlocks.Add(childRowsObj);
            }

            // Recurse only after finishing this BOM block
            foreach (var child in deferredChildBlocks)
                SetPartNumbersSequential(ctx, child, ref partCounter);

            ctx.Info($"SEQ: End block. Seen={seen}, Renumbered={renumbered}, SkippedAlready={skippedAlready}, SkippedRef={skippedRef}, NotMod={skippedNotMod}, Vol0={skippedVol0}");
        }

        /// <summary>
        /// Mirrors StructuredNumberingScheme.ResetItemCheck:
        /// - Sets PartSet=0 on modifiable docs
        /// - Recurses into ChildRows for normal assemblies (BOMStructure==51970)
        /// - ALSO recurses into FrameDoc ChildRows (phantom) so frame members get reset too.
        /// </summary>
        private void ResetItemCheck(RenumberingContext ctx, dynamic bomRows)
        {
            int rowCount = ctx.GetCount(bomRows);
            ctx.Dbg($"ResetItemCheck (Seq): BOMRows count = {rowCount}");

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

                    // Skip gate (must be checked BEFORE touching PartSet or recursing)
                    string skipReason;
                    if (ctx.ShouldSkip(doc, out skipReason))
                    {
                        ctx.Info($"SKIP: {ctx.SafeName(doc)} | {skipReason}");
                        // Do not reset PartSet for skipped docs; if it's an assembly, do not recurse.
                        continue;
                    }

                    // Only reset if modifiable (avoid exceptions)
                    if (ctx.IsModifiable(doc))
                        ctx.SetUserPropInt(doc, "PartSet", 0);

                    int bomStruct = ctx.GetInt(compDef, "BOMStructure", -1);

                    if (childRowsObj != null &&
                        (bomStruct == BOMSTRUCT_NORMAL || ctx.HasInterest(doc, "FrameDoc")))
                    {
                        recurse = true;
                    }
                }

                // If assembly in this row is skipped, recurse will remain false.
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
