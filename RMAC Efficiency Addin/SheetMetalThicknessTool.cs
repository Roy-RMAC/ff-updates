using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// UI wrapper for the sheet metal thickness tool. Handles document validation,
    /// the interactive sketch-line pick, transaction orchestration, and error reporting.
    /// The actual Inventor API work lives in <see cref="SheetMetalThicknessCore"/>.
    /// </summary>
    public static class SheetMetalThicknessTools
    {
        public static void SetSheetMetalThicknessFromSelectedLine(Inventor.Application app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            AddinSettings.EnsureLoaded();

            if (app.ActiveDocument is not PartDocument partDoc)
            {
                MessageBox.Show("Open a sheet metal part first.");
                return;
            }

            if (partDoc.ComponentDefinition is not SheetMetalComponentDefinition smDef)
            {
                MessageBox.Show("Active part is not a sheet metal part.");
                return;
            }

            object? picked = PickSketchLineWithPreSelect(app, "Select a sketch line to drive sheet metal thickness");
            if (picked == null) return; // cancelled

            object pickedNative = GetNativeObjectIfAny(picked);
            if (pickedNative is not SketchLine skLine)
            {
                MessageBox.Show($"Selection must be a SketchLine. Got: {pickedNative?.GetType().Name ?? "null"}");
                return;
            }

            object parentSketchObj = GetNativeObjectIfAny(skLine.Parent);
            if (parentSketchObj is not PlanarSketch parentSketch)
            {
                MessageBox.Show("Selected line must be in a 2D planar sketch.");
                return;
            }

            // Parameter name is a user-configured convention; sanitise so downstream parameter
            // creation always receives a valid Inventor identifier.
            string thicknessParamName = SheetMetalThicknessCore.SanitizeUserParameterName(
                AddinSettings.Current.SheetMetalThicknessParamName);

            // Remember the active browser pane so we can restore it afterwards
            // (sketch edit/exit and partDoc.Update() can switch to the iLogic pane).
            BrowserPane? modelPane = null;
            try
            {
                foreach (BrowserPane bp in partDoc.BrowserPanes)
                {
                    if (string.Equals(bp.Name, "Model", StringComparison.OrdinalIgnoreCase))
                    {
                        modelPane = bp;
                        break;
                    }
                }
            }
            catch { }

            try
            {
                InventorTransaction.RunFast(
                    app,
                    (_Document)partDoc,
                    "RMAC Set Sheet Metal Thickness",
                    () => SheetMetalThicknessCore.Apply(app, partDoc, smDef, skLine, parentSketch, thicknessParamName));

                try { partDoc.Update(); } catch { }
            }
            catch (Exception ex)
            {
                ReportErrorToClipboard("RMAC Sheet Metal Thickness", ex);
            }
            finally
            {
                // Restore the model browser pane (sketch operations / iLogic rules may have switched it).
                try { modelPane?.Activate(); } catch { }
            }
        }

        private static object? PickSketchLineWithPreSelect(Inventor.Application app, string prompt)
        {
            InteractionEvents? ie = null;
            SelectEvents? se = null;

            object? picked = null;
            bool done = false;

            Inventor.SelectEventsSink_OnPreSelectEventHandler? onPre = null;
            Inventor.SelectEventsSink_OnSelectEventHandler? onSel = null;

            try
            {
                ie = app.CommandManager.CreateInteractionEvents();
                ie.StatusBarText = prompt;

                se = ie.SelectEvents;
                try { se.SingleSelectEnabled = true; } catch { }

                se.AddSelectionFilter(SelectionFilterEnum.kAllEntitiesFilter);

                onPre = (ref object PreSelectEntity,
                         out bool DoHighlight,
                         ref ObjectCollection MorePreSelectEntities,
                         SelectionDeviceEnum SelectionDevice,
                         Point ModelPosition,
                         Point2d ViewPosition,
                         Inventor.View View) =>
                {
                    try
                    {
                        object native = GetNativeObjectIfAny(PreSelectEntity);
                        DoHighlight = native is SketchLine;
                    }
                    catch
                    {
                        DoHighlight = false;
                    }
                };

                onSel = (ObjectsEnumerator JustSelectedEntities,
                         SelectionDeviceEnum SelectionDevice,
                         Point ModelPosition,
                         Point2d ViewPosition,
                         Inventor.View View) =>
                {
                    try
                    {
                        if (JustSelectedEntities == null || JustSelectedEntities.Count < 1) return;

                        object o = GetNativeObjectIfAny(JustSelectedEntities[1]);

                        if (o is SketchLine)
                        {
                            picked = o;
                            done = true;

                            try { app.ActiveDocument.SelectSet.Clear(); } catch { }
                            try { ie?.Stop(); } catch { }
                            try { dynamic cm = app.CommandManager; cm.StopActiveCommand(); } catch { }
                        }
                        else
                        {
                            try { app.ActiveDocument.SelectSet.Clear(); } catch { }
                            app.StatusBarText = "Select a sketch LINE (2D sketch).";
                        }
                    }
                    catch { }
                };

                se.OnPreSelect += onPre;
                se.OnSelect += onSel;

                ie.OnTerminate += () => { done = true; };

                ie.Start();

                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (!done && DateTime.UtcNow < deadline)
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(10);
                }
                if (!done) picked = null;
            }
            catch (COMException)
            {
                picked = null;
            }
            finally
            {
                try
                {
                    if (se != null)
                    {
                        if (onPre != null) se.OnPreSelect -= onPre;
                        if (onSel != null) se.OnSelect -= onSel;
                    }
                }
                catch { }

                try { ie?.Stop(); } catch { }
            }

            return picked;
        }

        private static object GetNativeObjectIfAny(object obj)
        {
            if (obj == null) return obj!;

            try
            {
                dynamic d = obj;
                object native = d.NativeObject;
                if (native != null) return native;
            }
            catch { }

            return obj;
        }

        private static void ReportErrorToClipboard(string title, Exception ex)
        {
            string hresultText = "";

            if (ex is COMException cex)
                hresultText = $"HRESULT: 0x{cex.ErrorCode:X8}\r\n";

            string diag =
                $"{title}\r\n" +
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n\r\n" +
                hresultText +
                ex.ToString();

            try
            {
                Clipboard.SetText(diag);
                MessageBox.Show(
                    "An error occurred.\n\nDiagnostic details have been copied to the clipboard.\nPaste them here and we'll fix it.",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                MessageBox.Show(diag, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
