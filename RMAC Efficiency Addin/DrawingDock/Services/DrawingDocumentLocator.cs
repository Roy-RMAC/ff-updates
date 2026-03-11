// DrawingDock/Services/DrawingDocumentLocator.cs
using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using IODir = System.IO.Directory;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    internal sealed class DrawingDocumentLocator
    {
        internal sealed class Options
        {
            public string[] DrawingExtensions { get; set; } = new[] { ".idw", ".dwg" };
            public bool SearchCommonNearbyFolders { get; set; } = true;
            public bool SearchProjectWorkspace { get; set; } = true;
            public int MaxFilesToScan { get; set; } = 5000;
            public int WorkspaceMaxDepth { get; set; } = 2;
            public bool OpenIfFound { get; set; } = false;
        }

        internal sealed class Result
        {
            public bool Found { get; }
            public string Reason { get; }
            public string? DrawingFullPath { get; }
            public DrawingDocument? OpenedDrawing { get; }

            internal Result(bool found, string reason, string? path = null, DrawingDocument? opened = null)
            {
                Found = found;
                Reason = reason;
                DrawingFullPath = path;
                OpenedDrawing = opened;
            }
        }

        private readonly Inventor.Application _app;

        public DrawingDocumentLocator(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public Result FindDrawingForModel(Document modelDoc, Options? options = null)
        {
            if (modelDoc == null) throw new ArgumentNullException(nameof(modelDoc));
            options ??= new Options();

            // If they pass a drawing already, just return it.
            if (modelDoc.DocumentType == DocumentTypeEnum.kDrawingDocumentObject && modelDoc is DrawingDocument dd)
            {
                string path = DocumentHelpers.SafeFullFileName(modelDoc);
                return new Result(true, "Input document is already a drawing.", path, dd);
            }

            string? modelPath = DocumentHelpers.SafeFullFileName(modelDoc);
            if (string.IsNullOrWhiteSpace(modelPath) || !IOFile.Exists(modelPath))
                return new Result(false, "Model document has no valid FullFileName on disk.");

            string modelDir = IOPath.GetDirectoryName(modelPath)!;
            string modelBase = IOPath.GetFileNameWithoutExtension(modelPath);

            // Build candidate file paths (fast path: direct existence checks).
            foreach (var p in EnumerateNearbyCandidates(modelDir, modelBase, options))
            {
                if (IOFile.Exists(p))
                    return ReturnFound(p, options.OpenIfFound);
            }

            // Optionally scan project workspace (shallow).
            if (options.SearchProjectWorkspace)
            {
                var ws = TryGetWorkspacePath();
                if (!string.IsNullOrWhiteSpace(ws) && IODir.Exists(ws))
                {
                    // Try direct same-base in workspace root first
                    foreach (var ext in options.DrawingExtensions)
                    {
                        string direct = IOPath.Combine(ws!, modelBase + ext);
                        if (IOFile.Exists(direct))
                            return ReturnFound(direct, options.OpenIfFound);
                    }

                    // Shallow recursive search
                    var found = ShallowWorkspaceSearch(ws!, modelBase, options);
                    if (!string.IsNullOrWhiteSpace(found))
                        return ReturnFound(found!, options.OpenIfFound);
                }
            }

            return new Result(false, "No matching drawing found near model or in project workspace.");
        }

        private static IEnumerable<string> EnumerateNearbyCandidates(string modelDir, string modelBase, Options options)
        {
            // 1) Same folder, same base name.
            foreach (var ext in options.DrawingExtensions)
                yield return IOPath.Combine(modelDir, modelBase + ext);

            if (!options.SearchCommonNearbyFolders)
                yield break;

            // 2) Common nearby folders: subfolder and sibling variants.
            var common = new[] { "Drawings", "Drawing", "IDW", "DWG", "Documentation", "Docs" };

            // Subfolders under modelDir
            foreach (var folder in common)
            {
                string sub = IOPath.Combine(modelDir, folder);
                foreach (var ext in options.DrawingExtensions)
                    yield return IOPath.Combine(sub, modelBase + ext);
            }

            // Sibling folders next to modelDir (parent\Drawings etc.)
            string? parent = SafeGetParent(modelDir);
            if (parent != null)
            {
                foreach (var folder in common)
                {
                    string sib = IOPath.Combine(parent, folder);
                    foreach (var ext in options.DrawingExtensions)
                        yield return IOPath.Combine(sib, modelBase + ext);
                }
            }
        }

        private static string? SafeGetParent(string dir)
        {
            try { return IODir.GetParent(dir)?.FullName; }
            catch { return null; }
        }

        private string? TryGetWorkspacePath()
        {
            try
            {
                var dpm = _app.DesignProjectManager;
                var prj = dpm?.ActiveDesignProject;
                var ws = prj?.WorkspacePath;
                return string.IsNullOrWhiteSpace(ws) ? null : ws;
            }
            catch
            {
                return null;
            }
        }

        private static string? ShallowWorkspaceSearch(string workspacePath, string modelBase, Options options)
        {
            int scanned = 0;
            var stack = new Stack<(string dir, int depth)>();
            stack.Push((workspacePath, 0));

            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();
                if (depth > options.WorkspaceMaxDepth) continue;

                foreach (var ext in options.DrawingExtensions)
                {
                    string candidate = IOPath.Combine(dir, modelBase + ext);
                    if (IOFile.Exists(candidate))
                        return candidate;
                }

                if (scanned >= options.MaxFilesToScan) break;

                IEnumerable<string> subs;
                try { subs = IODir.EnumerateDirectories(dir); }
                catch { continue; }

                foreach (var sub in subs)
                {
                    if (scanned >= options.MaxFilesToScan) break;
                    scanned++;
                    stack.Push((sub, depth + 1));
                }
            }

            return null;
        }

        private Result ReturnFound(string path, bool openIfFound)
        {
            if (!openIfFound)
                return new Result(true, "Drawing found.", path, null);

            DrawingDocument? opened = null;
            try
            {
                var doc = _app.Documents.Open(path, true);
                opened = doc as DrawingDocument;
                return new Result(true, opened != null ? "Drawing found and opened." : "Drawing found (opened as non-drawing?).", path, opened);
            }
            catch (Exception ex)
            {
                return new Result(true, $"Drawing found but failed to open: {ex.Message}", path, null);
            }
        }

    }
}
