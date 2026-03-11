// Exporting/Model/ExportContext.cs
using Inventor;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using SysEnv = System.Environment;

namespace RMAC_Efficiency_Addin.Exporting.Model
{
    internal sealed class ExportContext
    {
        public ExportContext(Inventor.Application app, AssemblyDocument topAssembly, ExportingSettings exportingSettings)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            TopAssembly = topAssembly ?? throw new ArgumentNullException(nameof(topAssembly));
            Settings = exportingSettings ?? throw new ArgumentNullException(nameof(exportingSettings));

            BaseOutputDir = SafeGetTopAssemblyDirectory(topAssembly);

            CategoryToOutputDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FolderLabelToOutputDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            CreatedFiles = new List<string>();
            CreatedPdfFiles = new List<string>();
            CreatedDxfFiles = new List<string>();
            CreatedStepFiles = new List<string>();

            Warnings = new List<string>();
            Errors = new List<string>();
        }

        public Inventor.Application App { get; }
        public AssemblyDocument TopAssembly { get; }
        public ExportingSettings Settings { get; }

        public string BaseOutputDir { get; set; }

        public bool AlwaysOverwrite => Settings.AlwaysOverwrite;

        public Dictionary<string, string> CategoryToOutputDir { get; }
        public Dictionary<string, string> FolderLabelToOutputDir { get; }

        public List<string> CreatedFiles { get; }
        public List<string> CreatedPdfFiles { get; }
        public List<string> CreatedDxfFiles { get; }
        public List<string> CreatedStepFiles { get; }

        public List<string> Warnings { get; }
        public List<string> Errors { get; }

        public void AddWarning(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Warnings.Add(message);
            AddinSettings.Log("[Export Warning] " + message);
        }

        public void AddError(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Errors.Add(message);
            AddinSettings.Log("[Export Error] " + message);
        }

        public void TrackCreatedFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            CreatedFiles.Add(path);

            var ext = IOPath.GetExtension(path)?.ToLowerInvariant();
            if (ext == ".pdf") CreatedPdfFiles.Add(path);
            else if (ext == ".dxf") CreatedDxfFiles.Add(path);
            else if (ext == ".stp" || ext == ".step") CreatedStepFiles.Add(path);
        }

        public string GetOrCreateFolderLabelDir(string folderLabel)
        {
            folderLabel ??= "";

            if (FolderLabelToOutputDir.TryGetValue(folderLabel, out var existing))
                return existing;

            var dir = string.IsNullOrWhiteSpace(folderLabel)
                ? BaseOutputDir
                : IOPath.Combine(BaseOutputDir, folderLabel);

            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { AddError($"Failed to create folder '{dir}': {ex.Message}"); }

            FolderLabelToOutputDir[folderLabel] = dir;
            return dir;
        }

        public string ResolveCategoryOutputDir(string category, ExportProfileSettings profile)
        {
            category ??= "";
            if (CategoryToOutputDir.TryGetValue(category, out var existing))
                return existing;

            var label = (profile?.FolderName ?? "").Trim();
            var dir = GetOrCreateFolderLabelDir(label);

            CategoryToOutputDir[category] = dir;
            return dir;
        }

        private static string SafeGetTopAssemblyDirectory(AssemblyDocument asm)
        {
            try
            {
                var p = asm.FullFileName;
                if (!string.IsNullOrWhiteSpace(p))
                {
                    var dir = IOPath.GetDirectoryName(p);
                    if (!string.IsNullOrWhiteSpace(dir))
                        return dir;
                }
            }
            catch { }

            return SysEnv.GetFolderPath(SysEnv.SpecialFolder.MyDocuments);
        }
    }
}
