// Exporting/Actions/DxfPostProcessor.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.Exporting.Actions
{
    /// <summary>
    /// Writes the DXF list file (one path per line) and launches the external post processor EXE.
    /// </summary>
    internal sealed class DxfPostProcessor
    {
        /// <summary>
        /// Writes the DXF queue file. Creates the directory if required.
        /// </summary>
        public bool WriteQueueFile(string queueFilePath, IEnumerable<string> dxfPaths, out string? error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(queueFilePath))
                {
                    error = "Queue file path is empty.";
                    return false;
                }

                var dir = Path.GetDirectoryName(queueFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var lines = new List<string>();
                foreach (var p in dxfPaths ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    lines.Add(p.Trim());
                }

                File.WriteAllLines(queueFilePath, lines);
                AddinSettings.Log($"DXF queue written: {queueFilePath} ({lines.Count} lines)");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AddinSettings.Log($"DXF queue write error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Launches the external post processor executable.
        /// If queueFilePathAsArg is true, queueFilePath is passed as the first argument.
        /// </summary>
        public bool Launch(string exePath, string queueFilePath, bool queueFilePathAsArg, out string? error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    error = "Post processor EXE path is empty.";
                    return false;
                }

                if (!File.Exists(exePath))
                {
                    error = $"Post processor EXE not found: {exePath}";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
                };

                if (queueFilePathAsArg)
                {
                    if (string.IsNullOrWhiteSpace(queueFilePath))
                    {
                        error = "queueFilePathAsArg is true but queueFilePath is empty.";
                        return false;
                    }

                    psi.Arguments = $"\"{queueFilePath}\"";
                }

                Process.Start(psi);
                AddinSettings.Log($"DXF post processor launched: {exePath}");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AddinSettings.Log($"DXF post processor launch error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Convenience: writes queue file then launches post processor.
        /// </summary>
        public bool Run(
            string exePath,
            string queueFilePath,
            IEnumerable<string> dxfPaths,
            bool alwaysOverwriteQueueFile,
            bool queueFilePathAsArg,
            out string? error)
        {
            error = null;

            try
            {
                if (!alwaysOverwriteQueueFile && File.Exists(queueFilePath))
                {
                    AddinSettings.Log($"DXF queue not overwritten (exists): {queueFilePath}");
                }
                else
                {
                    if (!WriteQueueFile(queueFilePath, dxfPaths, out error))
                        return false;
                }

                return Launch(exePath, queueFilePath, queueFilePathAsArg, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AddinSettings.Log($"DXF post processor run error: {ex}");
                return false;
            }
        }
    }
}
