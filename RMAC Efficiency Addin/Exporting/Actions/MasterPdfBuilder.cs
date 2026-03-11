// Exporting/Actions/MasterPdfBuilder.cs
// Merges multiple PDF files into a single "master" PDF using PdfSharp.
// No external software required (replaces previous Acrobat COM dependency).

using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.IO;

namespace RMAC_Efficiency_Addin.Exporting.Actions
{
    /// <summary>
    /// Merges many PDF files into a single "master" PDF using PdfSharp.
    /// This class does not *create* PDFs from drawings; it only merges existing PDFs.
    /// </summary>
    internal sealed class MasterPdfBuilder
    {
        /// <summary>
        /// Merge <paramref name="sourcePdfPaths"/> into <paramref name="outputPdfPath"/>.
        /// Returns true if merged/created, false if skipped or failed.
        /// </summary>
        public bool BuildMasterPdf(
            IEnumerable<string> sourcePdfPaths,
            string outputPdfPath,
            bool alwaysOverwrite,
            out string? error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(outputPdfPath))
                {
                    error = "Output PDF path is empty.";
                    return false;
                }

                var sources = new List<string>();
                foreach (var p in sourcePdfPaths ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var fp = p.Trim();
                    if (File.Exists(fp))
                        sources.Add(fp);
                }

                if (sources.Count == 0)
                {
                    error = "No source PDFs found to merge.";
                    return false;
                }

                var outDir = Path.GetDirectoryName(outputPdfPath);
                if (!string.IsNullOrWhiteSpace(outDir))
                    Directory.CreateDirectory(outDir);

                if (!alwaysOverwrite && File.Exists(outputPdfPath))
                {
                    AddinSettings.Log($"Master PDF skipped (exists): {outputPdfPath}");
                    return false;
                }

                MergeWithPdfSharp(sources, outputPdfPath);

                AddinSettings.Log($"Master PDF created: {outputPdfPath}");
                return true;
            }
            catch (Exception ex)
            {
                error = $"PDF merge failed: {ex.Message}";
                AddinSettings.Log($"Master PDF build error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Uses PdfSharp to import pages from each source PDF into a single output document.
        /// </summary>
        private static void MergeWithPdfSharp(List<string> sources, string outputPdfPath)
        {
            using var outputDoc = new PdfDocument();

            foreach (var srcPath in sources)
            {
                // Open in import mode — required for page copying
                using var inputDoc = PdfReader.Open(srcPath, PdfDocumentOpenMode.Import);

                for (int i = 0; i < inputDoc.PageCount; i++)
                {
                    outputDoc.AddPage(inputDoc.Pages[i]);
                }
            }

            if (outputDoc.PageCount == 0)
                throw new InvalidOperationException("No pages found in source PDFs.");

            outputDoc.Save(outputPdfPath);
        }
    }
}
