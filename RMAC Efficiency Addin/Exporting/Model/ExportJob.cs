// Exporting/Model/ExportJob.cs
using Inventor;
using System;

namespace RMAC_Efficiency_Addin.Exporting.Model
{
    internal enum ExportJobType
    {
        ExportDXF,
        ExportSTEP,
        ExportBOMExcel,
        ExportDrawingPackagePdf,
        ExportMasterPdf,
        RunDxfPostProcessor
    }

    internal sealed class ExportJob
    {
        private ExportJob(ExportJobType type) { Type = type; }

        public ExportJobType Type { get; }

        // IMPORTANT: Use _Document as the common base across Drawing/Assembly/Part PIAs.
        public _Document? Doc { get; private set; }

        public string? Category { get; private set; }

        public string OutputPathOrFolder { get; private set; } = "";

        public bool AlwaysOverwrite { get; private set; }

        public string? Label { get; private set; }

        public string[]? SourcePdfPaths { get; private set; }

        public _Document[]? DrawingTargets { get; private set; }

        /// <summary>File paths of target model documents (used when _Document refs may be closed).</summary>
        public string[]? DrawingTargetPaths { get; private set; }

        /// <summary>1-based sheet indices in the active drawing to export for this category.</summary>
        public int[]? SheetIndices { get; private set; }

        public string? PostProcessorExePath { get; private set; }
        public bool QueueFilePathAsArg { get; private set; }

        public static ExportJob CreateDxf(_Document doc, string category, string dxfPath, bool alwaysOverwrite, string? label = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(dxfPath)) throw new ArgumentNullException(nameof(dxfPath));

            return new ExportJob(ExportJobType.ExportDXF)
            {
                Doc = doc,
                Category = (category ?? "").Trim(),
                OutputPathOrFolder = dxfPath,
                AlwaysOverwrite = alwaysOverwrite,
                Label = label
            };
        }

        public static ExportJob CreateStep(_Document doc, string category, string stepPath, bool alwaysOverwrite, string? label = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(stepPath)) throw new ArgumentNullException(nameof(stepPath));

            return new ExportJob(ExportJobType.ExportSTEP)
            {
                Doc = doc,
                Category = (category ?? "").Trim(),
                OutputPathOrFolder = stepPath,
                AlwaysOverwrite = alwaysOverwrite,
                Label = label
            };
        }

        public static ExportJob CreateBomExcel(AssemblyDocument topAssembly, string bomPath, bool alwaysOverwrite, string? label = null)
        {
            if (topAssembly == null) throw new ArgumentNullException(nameof(topAssembly));
            if (string.IsNullOrWhiteSpace(bomPath)) throw new ArgumentNullException(nameof(bomPath));

            return new ExportJob(ExportJobType.ExportBOMExcel)
            {
                Doc = topAssembly as _Document,
                OutputPathOrFolder = bomPath,
                AlwaysOverwrite = alwaysOverwrite,
                Label = label ?? "BOM Excel"
            };
        }

        public static ExportJob CreateDrawingPackagePdf(string category, string[] targetPaths, string outputFolder, bool alwaysOverwrite, string? label = null)
        {
            if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentNullException(nameof(outputFolder));
            targetPaths ??= Array.Empty<string>();

            return new ExportJob(ExportJobType.ExportDrawingPackagePdf)
            {
                Category = (category ?? "").Trim(),
                DrawingTargetPaths = targetPaths,
                OutputPathOrFolder = outputFolder,
                AlwaysOverwrite = alwaysOverwrite,
                Label = label ?? $"Drawings ({category})"
            };
        }

        /// <summary>
        /// Creates a job to export specific sheets from the active drawing as a merged PDF.
        /// </summary>
        public static ExportJob CreateCategoryDrawingPdf(string category, int[] sheetIndices, string outputPath, bool alwaysOverwrite, string? label = null)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentNullException(nameof(outputPath));
            sheetIndices ??= Array.Empty<int>();

            return new ExportJob(ExportJobType.ExportDrawingPackagePdf)
            {
                Category = (category ?? "").Trim(),
                SheetIndices = sheetIndices,
                OutputPathOrFolder = outputPath,
                AlwaysOverwrite = alwaysOverwrite,
                Label = label ?? $"Drawing Package ({category})"
            };
        }

        public static ExportJob CreateMasterPdf(string outputPdfPath, string[] sourcePdfPaths, bool alwaysOverwrite, string? label = null)
        {
            if (string.IsNullOrWhiteSpace(outputPdfPath)) throw new ArgumentNullException(nameof(outputPdfPath));
            sourcePdfPaths ??= Array.Empty<string>();

            return new ExportJob(ExportJobType.ExportMasterPdf)
            {
                OutputPathOrFolder = outputPdfPath,
                SourcePdfPaths = sourcePdfPaths,
                AlwaysOverwrite = alwaysOverwrite,
                Label = label ?? "Master PDF"
            };
        }

        public static ExportJob CreateRunDxfPostProcessor(string exePath, string queueFilePath, bool queueFilePathAsArg, bool alwaysOverwriteQueueFile, string? label = null)
        {
            if (string.IsNullOrWhiteSpace(exePath)) throw new ArgumentNullException(nameof(exePath));
            if (string.IsNullOrWhiteSpace(queueFilePath)) throw new ArgumentNullException(nameof(queueFilePath));

            return new ExportJob(ExportJobType.RunDxfPostProcessor)
            {
                PostProcessorExePath = exePath,
                OutputPathOrFolder = queueFilePath,
                QueueFilePathAsArg = queueFilePathAsArg,
                AlwaysOverwrite = alwaysOverwriteQueueFile,
                Label = label ?? "DXF Post Processor"
            };
        }

        public override string ToString()
        {
            var t = Type.ToString();
            if (!string.IsNullOrWhiteSpace(Label)) t += $" :: {Label}";
            if (!string.IsNullOrWhiteSpace(OutputPathOrFolder)) t += $" -> {OutputPathOrFolder}";
            return t;
        }
    }
}
