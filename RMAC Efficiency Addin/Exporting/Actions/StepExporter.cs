// Exporting/Actions/StepExporter.cs
using Inventor;
using System;
using System.IO;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.Exporting.Actions
{
    internal sealed class StepExporter
    {
        private readonly Inventor.Application _app;

        public StepExporter(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public bool ExportStep(_Document doc, string stepPath, bool alwaysOverwrite, out string? error)
        {
            error = null;

            try
            {
                if (doc == null) { error = "Document is null."; return false; }
                if (string.IsNullOrWhiteSpace(stepPath)) { error = "STEP path is empty."; return false; }

                if (doc.DocumentType != DocumentTypeEnum.kPartDocumentObject &&
                    doc.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject)
                {
                    error = $"Unsupported document type for STEP: {doc.DocumentType}";
                    return false;
                }

                var dir = IOPath.GetDirectoryName(stepPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (!alwaysOverwrite && IOFile.Exists(stepPath))
                    return false;

                var stepAddIn = GetStepTranslator();
                if (stepAddIn == null)
                {
                    error = "STEP translator add-in not found.";
                    return false;
                }

                var ctx = _app.TransientObjects.CreateTranslationContext();
                ctx.Type = IOMechanismEnum.kFileBrowseIOMechanism;

                var opts = _app.TransientObjects.CreateNameValueMap();
                try { _ = stepAddIn.HasSaveCopyAsOptions[doc, ctx, opts]; } catch { }

                var data = _app.TransientObjects.CreateDataMedium();
                data.FileName = stepPath;

                stepAddIn.SaveCopyAs(doc, ctx, opts, data);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AddinSettings.Log($"STEP export error: {ex}");
                return false;
            }
        }

        private TranslatorAddIn? GetStepTranslator()
        {
            try
            {
                // Common Inventor STEP translator ClassId
                const string stepGuid = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";

                foreach (ApplicationAddIn a in _app.ApplicationAddIns)
                {
                    try
                    {
                        if (string.Equals(a.ClassIdString, stepGuid, StringComparison.OrdinalIgnoreCase))
                            return a as TranslatorAddIn;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }
    }
}
