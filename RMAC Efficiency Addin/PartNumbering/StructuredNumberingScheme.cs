using Inventor;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal sealed class StructuredNumberingScheme : IPartNumberingScheme
    {
        public string Name => "Structured";

        // BOMStructure constants (matching your current file)
        private const int BOMSTRUCT_NORMAL = 51970;
        private const int BOMSTRUCT_REFERENCE = 51972;
        private const int BOMSTRUCT_INSEPARABLE = 51974;

        private readonly FrameMemberMatcher _matcher = new();

        private sealed class FrameEntry
        {
            public FrameMemberMatcher.FrameData Data = new();
            public string PartNo = "";
            public string DocName = "";
        }

        public void Run(RenumberingContext ctx, dynamic bomRows)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            ctx.Info("Start RMAC Part Numbering Protocol (Structured)");
            ctx.Info($"Target assembly: {ctx.SafeName(ctx.TopAssembly)}");

            // Init counters (same as your current Run())
            ctx.AssemblyCounter = 2;
            ctx.GenPartCounter = 1;

            // Top-level part number
            dynamic dAsm = ctx.TopAssembly;
            string oldTopPn = ctx.GetIPropString(dAsm, "Design Tracking Properties", "Part Number") ?? "<no PN>";
            if (!ctx.KeepTopLevelAssemblyPartNumberFixed)
            {
                ctx.SetIProp(dAsm, "Design Tracking Properties", "Part Number", "A-01");
                ctx.Info($"TOP PN: {oldTopPn} -> A-01");
            }
            else
            {
                ctx.Info($"TOP PN: {oldTopPn} (kept fixed)");
            }

            ctx.UpdateStatusText("Resetting flags\u2026");
            ctx.Info("Resetting PartSet flags...");
            ResetItemCheck(ctx, bomRows);

            ctx.Info("Begin Processing A-01 BOM");
            SetPartNumbers(ctx, bomRows, 1);

            ctx.Info("Structured numbering complete.");
        }

        /// <summary>
        /// Mirrors your existing SetPartNumbers:
        /// Pass 1: traverse and renumber parts/assemblies. FrameDoc is only labelled.
        /// Pass 2: scan rows; if FrameDoc row has ChildRows then FrameProcessor(row.ChildRows, aNo, partCounterLocal).
        /// </summary>
        private void SetPartNumbers(RenumberingContext ctx, dynamic bomRows, int aNo)
        {
            ctx.Info($"Begin Processing A-{aNo:00}");

            int partCounterLocal = 1;
            int rowCount = ctx.GetCount(bomRows);

            int seen = 0, skippedNotMod = 0, skippedVol0 = 0;
            int renAsm = 0, renPrt = 0, renGen = 0, renFrameAsm = 0;

            ctx.Info($"A-{aNo:00}: BOMRows count = {rowCount}");

            // -------------------------
            // PASS 1
            // -------------------------
            for (int rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                ctx.StepProgress($"Numbering components ({rowIndex}/{rowCount})\u2026");

                dynamic row = ctx.ComItem(bomRows, rowIndex);
                if (row == null) continue;

                object? childRowsObj = ctx.TryGet(row, "ChildRows");

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
                        // If this doc is an assembly, skipping it implicitly skips its subtree:
                        // by continuing here we also avoid SetPartNumbers recursion below for this row.
                        continue;
                    }

                    double vol = ctx.GetVolume(compDef);

                    bool isMod = ctx.IsModifiable(doc);
                    int partSet = ctx.GetUserPropInt(doc, "PartSet", 0);
                    int bomStruct = ctx.GetInt(compDef, "BOMStructure", -1);
                    int docType = ctx.GetInt(doc, "DocumentType", -1);

                    // Skip reference rows early (keeps noise down)
                    if (bomStruct == BOMSTRUCT_REFERENCE)
                        continue;

                    string curPn = ctx.GetIPropString(doc, "Design Tracking Properties", "Part Number") ?? "<no PN>";
                    string name = ctx.SafeName(doc);

                    seen++;
                    ctx.Dbg($"ROW A-{aNo:00} r={rowIndex}/{rowCount} | {DocTypeName(docType)} | Mod={(isMod ? 1 : 0)} | PartSet={partSet} | BOM={BomStructName(bomStruct)} | Vol={vol:0.####} | PN={curPn} | {name}");

                    // Frame assemblies: label only (no recursion here)
                    if (ctx.HasInterest(doc, "FrameDoc") && isMod && partSet == 0)
                    {
                        ctx.SetUserPropInt(doc, "PartSet", 1);

                        string newPn = $"A-{aNo:00}-FRAME";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.SaveIfDirty(doc);

                        renFrameAsm++;
                        renAsm++;
                        ctx.Info($"SET (FrameAsm)  PN: {curPn} -> {newPn} | {name}");
                        continue;
                    }

                    // Assemblies: only recurse if BOMStructure == NORMAL (51970)
                    if (docType == (int)DocumentTypeEnum.kAssemblyDocumentObject &&
                        isMod &&
                        partSet == 0 &&
                        bomStruct == BOMSTRUCT_NORMAL)
                    {
                        string newPn = $"A-{ctx.AssemblyCounter:00}";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        renAsm++;
                        ctx.Info($"SET (Asm)       PN: {curPn} -> {newPn} | {name}");

                        int aInput = ctx.AssemblyCounter; // the number just assigned
                        ctx.AssemblyCounter++;

                        ctx.Info($"Found Subassembly - Beginning processing A-{aInput:00}");

                        if (childRowsObj != null)
                        {
                            ctx.Dbg($"A-{aInput:00}: ChildRows count = {ctx.GetCount(childRowsObj)} | Parent={name}");
                            SetPartNumbers(ctx, childRowsObj, aInput);
                        }
                        else
                        {
                            ctx.Warn($"A-{aInput:00}: ChildRows is NULL for assembly row | {name}");
                        }

                        continue;
                    }

                    // General parts: PartSet==1 and Vol>0
                    if (docType == (int)DocumentTypeEnum.kPartDocumentObject &&
                        isMod &&
                        partSet == 1 &&
                        vol > 0)
                    {
                        string newPn = $"GEN-P-{ctx.GenPartCounter:000}";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.GenPartCounter++;
                        ctx.SetUserPropInt(doc, "PartSet", 2);
                        ctx.SaveIfDirty(doc);

                        renGen++;
                        ctx.Info($"SET (GenPart)   PN: {curPn} -> {newPn} | {name}");
                        continue;
                    }

                    // Inseparable assemblies as general parts: BOMStructure==INSEPARABLE (51974)
                    if (docType == (int)DocumentTypeEnum.kAssemblyDocumentObject &&
                        isMod &&
                        partSet == 1 &&
                        bomStruct == BOMSTRUCT_INSEPARABLE &&
                        vol > 0)
                    {
                        string newPn = $"GEN-P-{ctx.GenPartCounter:000}";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.GenPartCounter++;
                        ctx.SetUserPropInt(doc, "PartSet", 2);
                        ctx.SaveIfDirty(doc);

                        renGen++;
                        ctx.Info($"SET (Insep->Gen) PN: {curPn} -> {newPn} | {name}");
                        continue;
                    }

                    // Standard parts: PartSet==0 and Vol>0
                    // Do not renumber frame members here; handled by FrameProcessor pass.
                    if (docType == (int)DocumentTypeEnum.kPartDocumentObject &&
                        isMod &&
                        partSet == 0 &&
                        vol > 0 &&
                        !ctx.HasInterest(doc, "FrameMemberDoc") &&
                        !ctx.HasInterest(doc, "SkeletonDoc"))
                    {
                        string newPn = $"P-{aNo:00}-{partCounterLocal:00}";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        renPrt++;
                        ctx.Info($"SET (Part)      PN: {curPn} -> {newPn} | {name}");

                        partCounterLocal++;
                        continue;
                    }

                    // Inseparable assemblies as parts: BOMStructure==INSEPARABLE (51974) and PartSet==0
                    if (docType == (int)DocumentTypeEnum.kAssemblyDocumentObject &&
                        isMod &&
                        partSet == 0 &&
                        bomStruct == BOMSTRUCT_INSEPARABLE &&
                        vol > 0)
                    {
                        string newPn = $"P-{aNo:00}-{partCounterLocal:00}";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        renPrt++;
                        ctx.Info($"SET (Insep->Part) PN: {curPn} -> {newPn} | {name}");

                        partCounterLocal++;
                        continue;
                    }

                    if (!isMod) skippedNotMod++;
                    if (vol <= 0.00001) skippedVol0++;

                    if (vol < 0.00001)
                        ctx.Dbg($"{curPn} has no volume | {name}");
                }
            }

            ctx.Info($"A-{aNo:00} SUMMARY PASS1: Rows={rowCount}, Seen={seen}, NotMod={skippedNotMod}, Vol0={skippedVol0}, Renumbered: ASM={renAsm} (FrameAsm={renFrameAsm}), PRT={renPrt}, GEN={renGen}");

            // -------------------------
            // PASS 2: FrameProcessor calls
            // -------------------------
            int frameCalls = 0;

            for (int rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                dynamic row = ctx.ComItem(bomRows, rowIndex);
                if (row == null) continue;

                object? childRowsObj = ctx.TryGet(row, "ChildRows");
                if (childRowsObj == null) continue;

                bool isFrameRow = false;
                bool skipThisFrameSubtree = false;

                foreach (dynamic compDef in ctx.GetRowComponentDefinitions(row))
                {
                    object? docObj = ctx.TryGet(compDef, "Document");
                    if (docObj == null) continue;
                    dynamic doc = docObj;

                    if (!ctx.HasInterest(doc, "FrameDoc"))
                        continue;

                    string skipReason;
                    if (ctx.ShouldSkip(doc, out skipReason))
                    {
                        ctx.Info($"SKIP SUBTREE: {ctx.SafeName(doc)} | {skipReason}");
                        skipThisFrameSubtree = true;
                        break;
                    }

                    isFrameRow = true;
                    break;
                }

                if (!isFrameRow || skipThisFrameSubtree)
                    continue;

                frameCalls++;
                ctx.Info($"FRAME: Found FrameDoc row (r={rowIndex}/{rowCount}), ChildRows={ctx.GetCount(childRowsObj)}. Running FrameProcessor...");
                FrameProcessor(ctx, childRowsObj, aNo, partCounterLocal);
            }

            ctx.Info($"FRAME SUMMARY: FrameProcessor calls = {frameCalls}");
            ctx.Info($"Completed Processing of A-{aNo:00}");
        }

        /// <summary>
        /// Mirrors your FrameProcessor: skeleton + dedupe frame members + standard parts inside frame.
        /// </summary>
        private void FrameProcessor(RenumberingContext ctx, dynamic bomRows, int aNo, int startPartCount)
        {
            var entries = new List<FrameEntry>();
            int framePartCounter = startPartCount;

            int rowCount = ctx.GetCount(bomRows);

            int membersSeen = 0, membersMatched = 0, membersNew = 0, membersNotMod = 0, skeletonSet = 0;

            ctx.Info($"FRAME: Begin FrameProcessor (A-{aNo:00}) ChildRows={rowCount} startPartCount={startPartCount}");

            for (int rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                ctx.StepProgress($"Processing frame members ({rowIndex}/{rowCount})\u2026");

                dynamic row = ctx.ComItem(bomRows, rowIndex);
                if (row == null) continue;

                foreach (dynamic compDef in ctx.GetRowComponentDefinitions(row))
                {
                    object? docObj = ctx.TryGet(compDef, "Document");
                    if (docObj == null) continue;
                    dynamic doc = docObj;

                    string name = ctx.SafeName(doc);
                    string curPn = ctx.GetIPropString(doc, "Design Tracking Properties", "Part Number") ?? "<no PN>";

                    string skipReason;
                    if (ctx.ShouldSkip(doc, out skipReason))
                    {
                        if (skipReason == "Document not modifiable") membersNotMod++;
                        ctx.Info($"SKIP: {name} | {skipReason}");
                        continue;
                    }

                    // Skeleton
                    if (ctx.HasInterest(doc, "SkeletonDoc"))
                    {
                        string newPn = $"A-{aNo:00}-SKELETON FRAME";
                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        skeletonSet++;
                        ctx.Info($"FRAME SET (Skeleton) PN: {curPn} -> {newPn} | {name}");
                        continue;
                    }

                    // Frame members
                    if (ctx.HasInterest(doc, "FrameMemberDoc"))
                    {
                        membersSeen++;

                        var frame = _matcher.Extract(ctx, compDef, doc);

                        ctx.Dbg($"FRAME MEMBER: {name}");
                        ctx.Dbg($"  Mat='{frame.Material}' Stock='{frame.Stock}' G_L='{frame.Length}'");
                        ctx.Dbg($"  Mass={frame.Mass:0.####} Vol={frame.Volume:0.####} Area={frame.Area:0.####}");
                        ctx.Dbg($"  Ixx={frame.Ixx:0.####} Iyy={frame.Iyy:0.####} Izz={frame.Izz:0.####}");
                        ctx.Dbg($"  COG=({frame.COG_X:0.####},{frame.COG_Y:0.####},{frame.COG_Z:0.####}) BBZ={frame.BBZ:0.####}");

                        bool matchFound = false;

                        foreach (var entry in entries)
                        {
                            bool match = _matcher.IsMatch(frame, entry.Data);

                            ctx.Dbg($"  Compare -> {entry.PartNo} | {entry.DocName} | Match={(match ? 1 : 0)}");

                            if (match)
                            {
                                ctx.SetPartNumber(doc, entry.PartNo);
                                ctx.SetUserPropInt(doc, "PartSet", 1);
                                ctx.SaveIfDirty(doc);

                                membersMatched++;
                                ctx.Info($"FRAME MATCH: {curPn} -> {entry.PartNo} | {name} matched {entry.DocName}");
                                matchFound = true;
                                break;
                            }
                        }

                        if (!matchFound)
                        {
                            string newPn = $"P-{aNo:00}-{framePartCounter:00}";
                            framePartCounter++;

                            ctx.SetPartNumber(doc, newPn);
                            ctx.SetUserPropInt(doc, "PartSet", 1);
                            ctx.SaveIfDirty(doc);

                            entries.Add(new FrameEntry { Data = frame, PartNo = newPn, DocName = name });

                            membersNew++;
                            ctx.Info($"FRAME NEW: {curPn} -> {newPn} | {name}");
                        }

                        continue;
                    }

                    // Standard parts inside frame
                    int docType = ctx.GetInt(doc, "DocumentType", -1);
                    int partSet = ctx.GetUserPropInt(doc, "PartSet", 0);

                    if (docType == (int)DocumentTypeEnum.kPartDocumentObject &&
                        partSet == 0)
                    {
                        string newPn = $"P-{aNo:00}-{framePartCounter:00}";
                        framePartCounter++;

                        ctx.SetPartNumber(doc, newPn);
                        ctx.SetUserPropInt(doc, "PartSet", 1);
                        ctx.SaveIfDirty(doc);

                        ctx.Info($"FRAME SET (StdPart) PN: {curPn} -> {newPn} | {name}");
                    }
                }
            }

            ctx.Info($"FRAME: End FrameProcessor. Seen={membersSeen}, Matched={membersMatched}, New={membersNew}, SkeletonSet={skeletonSet}, NotMod={membersNotMod}, UniqueEntries={entries.Count}");
        }

        /// <summary>
        /// Mirrors your ResetItemCheck:
        /// - Sets PartSet=0 on modifiable docs
        /// - Recurses into ChildRows for normal assemblies (BOMStructure==51970)
        /// - ALSO recurses into FrameDoc ChildRows (phantom) so frame members get reset too.
        /// </summary>
        private void ResetItemCheck(RenumberingContext ctx, dynamic bomRows)
        {
            int rowCount = ctx.GetCount(bomRows);
            ctx.Dbg($"ResetItemCheck: BOMRows count = {rowCount}");

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
                        ctx.Dbg($"SKIP RESET: {ctx.SafeName(doc)} | {skipReason}");
                        continue;
                    }

                    if (ctx.IsModifiable(doc))
                        ctx.SetUserPropInt(doc, "PartSet", 0);

                    int bomStruct = ctx.GetInt(compDef, "BOMStructure", -1);

                    // Recurse rules:
                    // - Normal assemblies (51970)
                    // - FrameDoc rows (phantom) to ensure frame children get reset
                    if (childRowsObj != null &&
                        (bomStruct == BOMSTRUCT_NORMAL || ctx.HasInterest(doc, "FrameDoc")))
                    {
                        recurse = true;
                    }

                    if (!ctx.IsModifiable(doc) &&
                        bomStruct != BOMSTRUCT_REFERENCE &&
                        !ctx.PropertySetExists(doc, "ContentCenter"))
                    {
                        string pn = ctx.GetIPropString(doc, "Design Tracking Properties", "Part Number") ?? "<no part number>";
                        ctx.Info($"{pn} is not modifiable | {ctx.SafeName(doc)}");
                    }
                }

                if (childRowsObj != null && recurse)
                {
                    ResetItemCheck(ctx, childRowsObj);
                }
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
                BOMSTRUCT_INSEPARABLE => "INSEP",
                _ => bomStruct.ToString(CultureInfo.InvariantCulture)
            };
        }
    }
}
