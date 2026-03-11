// Exporting/Services/FolderRouter.cs
using RMAC_Efficiency_Addin.Exporting.Model;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.IO;

namespace RMAC_Efficiency_Addin.Exporting.Services
{
    /// <summary>
    /// Resolves output folders for categories using ExportProfileSettings.FolderName.
    /// Keeps the "PROFILE & PRESS share a folder" behaviour clean and centralized.
    ///
    /// This class does NOT decide what to export; it only routes to folders and ensures they exist.
    /// </summary>
    internal sealed class FolderRouter
    {
        private readonly ExportContext _ctx;

        public FolderRouter(ExportContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>
        /// Ensures BaseOutputDir exists (call at start of run).
        /// </summary>
        public bool EnsureBaseFolder(out string? error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(_ctx.BaseOutputDir))
                {
                    error = "BaseOutputDir is empty.";
                    return false;
                }

                Directory.CreateDirectory(_ctx.BaseOutputDir);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _ctx.AddError($"Failed to create base export folder '{_ctx.BaseOutputDir}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Returns the output folder for a category. Creates it if missing.
        /// Folder is determined by profile.FolderName (can be shared by multiple categories).
        /// </summary>
        public string GetCategoryFolder(string category, ExportProfileSettings profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            category = (category ?? "").Trim();

            // ExportContext already handles caching and folder creation.
            var dir = _ctx.ResolveCategoryOutputDir(category, profile);

            // Defensive ensure directory exists
            TryEnsureDir(dir);

            return dir;
        }

        /// <summary>
        /// Returns a subfolder inside the category folder (e.g. DXF/STEP).
        /// If subfolderName is blank, returns the category folder.
        /// </summary>
        public string GetCategorySubFolder(string category, ExportProfileSettings profile, string subfolderName)
        {
            var catDir = GetCategoryFolder(category, profile);

            subfolderName = (subfolderName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(subfolderName))
                return catDir;

            var subDir = Path.Combine(catDir, subfolderName);
            TryEnsureDir(subDir);
            return subDir;
        }

        /// <summary>
        /// Returns a folder for a "folder label" (profile.FolderName) directly.
        /// Useful when you want to create folders up-front based on presence of categories.
        /// </summary>
        public string GetFolderLabelDir(string folderLabel)
        {
            folderLabel = (folderLabel ?? "").Trim();
            var dir = _ctx.GetOrCreateFolderLabelDir(folderLabel);
            TryEnsureDir(dir);
            return dir;
        }

        /// <summary>
        /// Returns a common global folder inside BaseOutputDir (e.g. "BOM", "MASTER PDF").
        /// Keeps consistent structure if you want these separated later.
        /// </summary>
        public string GetGlobalSubFolder(string subfolderName)
        {
            subfolderName = (subfolderName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(subfolderName))
                return _ctx.BaseOutputDir;

            var dir = Path.Combine(_ctx.BaseOutputDir, subfolderName);
            TryEnsureDir(dir);
            return dir;
        }

        private void TryEnsureDir(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) return;
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                _ctx.AddError($"Failed to create folder '{dir}': {ex.Message}");
            }
        }
    }
}
