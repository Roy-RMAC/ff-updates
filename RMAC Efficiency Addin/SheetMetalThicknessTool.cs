using Inventor;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RMAC_Efficiency_Addin
{
    public static class SheetMetalThicknessTools
    {
        private const string RefSketchPrefix = "RMAC_SM_THK_REF";
        private const string RefWorkPlanePrefix = "RMAC_SM_THK_WP";

        // Default exported thickness parameter name (if no setting is configured)
        private const string DefaultThicknessParamName = "THICKNESS";

        // Backward-compat cleanup (older parts may have this)
        private const string OldThicknessParamName = "RMAC_SM_THK";

        // Deterministic name for the sketch dimension parameter we create
        // (We delete/recreate it each run so it stays stable)
        private const string RefDimParamName = "RMAC_SM_THK_DIM";

        /// <summary>
        /// Sanitizes a user-supplied parameter name into something Inventor will accept.
        /// Intended for the exported thickness UserParameter name.
        /// </summary>
        public static string SanitizeUserParameterName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return DefaultThicknessParamName;

            // Trim and replace whitespace with underscores
            var s = raw.Trim();

            // Build: [A-Za-z0-9_] only, and normalize whitespace/punctuation to '_'...
            System.Text.StringBuilder sb = new(s.Length);
            bool lastWasUnderscore = false;

            foreach (char ch in s)
            {
                char c = ch;

                if (char.IsWhiteSpace(c) || c == '-' || c == '.' || c == ':' || c == ';' || c == '/' || c == '\\')
                    c = '_';

                bool ok = (c == '_') || char.IsLetterOrDigit(c);
                if (!ok)
                    continue;

                if (c == '_')
                {
                    if (lastWasUnderscore) continue;
                    lastWasUnderscore = true;
                }
                else
                {
                    lastWasUnderscore = false;
                }

                sb.Append(c);
                if (sb.Length >= 64) break; // keep it sane
            }

            var cleaned = sb.ToString().Trim('_');
            if (string.IsNullOrWhiteSpace(cleaned))
                return DefaultThicknessParamName;

            // Inventor parameter names shouldn't start with a digit.
            if (char.IsDigit(cleaned[0]))
                cleaned = "T_" + cleaned;

            return cleaned;
        }

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

            // Parameter name is a user-configured convention (global settings). Sanitize now so
            // downstream parameter creation always receives a valid Inventor identifier.
            string thicknessParamName = SanitizeUserParameterName(AddinSettings.Current.SheetMetalThicknessParamName);

            // Remember the active browser pane so we can restore it afterwards
            // (sketch edit/exit and partDoc.Update() can switch to the iLogic pane)
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

            Transaction? tx = null;
            bool oldSU = app.ScreenUpdating;

            try
            {
                app.ScreenUpdating = false;
                tx = app.TransactionManager.StartTransaction((Inventor._Document)partDoc, "RMAC Set Sheet Metal Thickness");

                Step("Cleanup old thickness setup", () => CleanupOldThicknessSetup(partDoc, smDef, thicknessParamName));

                // Ensure sketch is editable
                Step("Edit parent sketch", () =>
                {
                    try { parentSketch.Edit(); } catch { }
                });

                TwoPointDistanceDimConstraint dim = null!;
                Step("Create driven dimension (in parent sketch)", () =>
                {
                    // Create a driven aligned dimension between the line endpoints
                    // and rename its parameter to RMAC_SM_THK_DIM
                    TransientGeometry tg = app.TransientGeometry;

                    Point2d a = skLine.StartSketchPoint.Geometry;
                    Point2d b = skLine.EndSketchPoint.Geometry;

                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);

                    if (len < 1e-8)
                        throw new InvalidOperationException("Selected line is effectively zero length.");

                    Point2d mid = tg.CreatePoint2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

                    // Perpendicular unit vector for text placement
                    double nx = -dy / len;
                    double ny = dx / len;

                    // Try a handful of candidate placements (Inventor can E_FAIL on some placements)
                    double off = Math.Max(len * 0.25, 1.0);

                    Point2d[] candidates = new[]
                    {
                        tg.CreatePoint2d(mid.X + nx * off,       mid.Y + ny * off),
                        tg.CreatePoint2d(mid.X - nx * off,       mid.Y - ny * off),
                        tg.CreatePoint2d(mid.X + nx * off * 0.5, mid.Y + ny * off * 0.5),
                        tg.CreatePoint2d(mid.X - nx * off * 0.5, mid.Y - ny * off * 0.5),
                        tg.CreatePoint2d(mid.X + 1.0,            mid.Y + 1.0),
                        tg.CreatePoint2d(mid.X - 1.0,            mid.Y - 1.0),
                    };

                    Exception? last = null;

                    foreach (var textPt in candidates)
                    {
                        try
                        {
                            dim = parentSketch.DimensionConstraints.AddTwoPointDistance(
                                skLine.StartSketchPoint,
                                skLine.EndSketchPoint,
                                DimensionOrientationEnum.kAlignedDim,
                                textPt);

                            try { dim.Driven = true; } catch { }

                            // Rename the dimension parameter to a stable, known name
                            // (This is what we’ll reference from the UserParameter)
                            try { dim.Parameter.Name = RefDimParamName; }
                            catch
                            {
                                // If rename fails for any reason, fall back to using its auto name.
                                // But we still strongly prefer the stable name.
                            }

                            return;
                        }
                        catch (Exception ex)
                        {
                            last = ex;
                        }
                    }

                    throw new InvalidOperationException("Inventor rejected dimension creation (all placement candidates failed).", last);
                });

                Step("Exit parent sketch", () =>
                {
                    try { parentSketch.ExitEdit(); } catch { }
                });

                Step("Create exported user parameter linking to dimension", () =>
                {
                    // Use stable name if it exists; otherwise fall back to whatever Inventor assigned.
                    string dimName;
                    try
                    {
                        dimName = string.Equals(dim.Parameter.Name, RefDimParamName, StringComparison.OrdinalIgnoreCase)
                            ? RefDimParamName
                            : dim.Parameter.Name;
                    }
                    catch
                    {
                        dimName = dim.Parameter.Name;
                    }

                    // Clean reruns + backward compatibility
                    TryDeleteUserParameter(partDoc, thicknessParamName);
                    TryDeleteUserParameter(partDoc, OldThicknessParamName);

                    UserParameter up = partDoc.ComponentDefinition.Parameters.UserParameters.AddByExpression(
                        thicknessParamName,
                        dimName,
                        "mm");

                    try { up.ExposedAsProperty = true; } catch { }
                });

                Step("Set sheet metal thickness expression", () =>
                {
                    smDef.Thickness.Expression = thicknessParamName;
                });

                tx.End();
                tx = null;

                try { partDoc.Update(); } catch { }
            }
            catch (Exception ex)
            {
                try { tx?.Abort(); } catch { }
                ReportErrorToClipboard("RMAC Sheet Metal Thickness", ex);
            }
            finally
            {
                app.ScreenUpdating = oldSU;
                try { app.ActiveView.Update(); } catch { }

                // Restore the model browser pane (sketch operations / iLogic rules may have switched it)
                try { modelPane?.Activate(); } catch { }
            }
        }

        private static void CleanupOldThicknessSetup(PartDocument partDoc, SheetMetalComponentDefinition smDef, string thicknessParamName)
        {
            // If thickness references our parameter, detach to a numeric mm expression first
            Parameter thicknessParam = smDef.Thickness;

            bool referencesOurParam = false;
            try
            {
                string expr = thicknessParam.Expression ?? "";
                referencesOurParam =
                    expr.IndexOf(thicknessParamName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    expr.IndexOf(OldThicknessParamName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { }

            if (referencesOurParam)
            {
                UnitsOfMeasure uom = partDoc.UnitsOfMeasure;
                double curVal = (double)thicknessParam.Value;

                string mmExpr;
                try
                {
                    mmExpr = uom.GetStringFromValue(curVal, UnitsTypeEnum.kMillimeterLengthUnits);
                }
                catch
                {
                    // Fallback: Inventor internal length is cm; convert to mm
                    mmExpr = (curVal * 10.0).ToString("0.###") + " mm";
                }

                try { thicknessParam.Expression = mmExpr; } catch { }
            }

            // Remove old RMAC-created sketch dimensions (by parameter name)
            try
            {
                var sketches = partDoc.ComponentDefinition.Sketches;
                for (int i = 1; i <= sketches.Count; i++)
                {
                    PlanarSketch sk = sketches[i];
                    try
                    {
                        // DimensionConstraints is 1-based collection
                        var dims = sk.DimensionConstraints;
                        for (int d = dims.Count; d >= 1; d--)
                        {
                            object dc = dims[d];
                            try
                            {
                                dynamic dyn = dc;
                                Parameter p = dyn.Parameter;
                                string pn = p?.Name ?? "";

                                if (string.Equals(pn, RefDimParamName, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { dyn.Delete(); } catch { }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Delete any prior RMAC thickness sketches (or rename out of the way)
            try
            {
                var sketches = partDoc.ComponentDefinition.Sketches;
                for (int i = sketches.Count; i >= 1; i--)
                {
                    PlanarSketch sk = sketches[i];
                    string name = "";
                    try { name = sk.Name ?? ""; } catch { }

                    if (!name.StartsWith(RefSketchPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try { sk.Delete(); }
                    catch
                    {
                        try { sk.Name = name + "_OLD_" + DateTime.Now.ToString("HHmmss"); } catch { }
                    }
                }
            }
            catch { }

            // Delete any prior RMAC planes (or rename out of the way)
            try
            {
                var wps = partDoc.ComponentDefinition.WorkPlanes;
                for (int i = wps.Count; i >= 1; i--)
                {
                    WorkPlane wp = wps[i];
                    string n = "";
                    try { n = wp.Name ?? ""; } catch { }

                    if (!n.StartsWith(RefWorkPlanePrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try { wp.Delete(); }
                    catch
                    {
                        try { wp.Name = n + "_OLD_" + DateTime.Now.ToString("HHmmss"); } catch { }
                    }
                }
            }
            catch { }

            // Remove both names (new and old) so reruns are deterministic
            TryDeleteUserParameter(partDoc, thicknessParamName);
            TryDeleteUserParameter(partDoc, OldThicknessParamName);
        }

        private static void TryDeleteUserParameter(PartDocument partDoc, string name)
        {
            try
            {
                var ups = partDoc.ComponentDefinition.Parameters.UserParameters;
                foreach (UserParameter p in ups)
                {
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Delete();
                        return;
                    }
                }
            }
            catch { }
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

        private static void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                if (ex is COMException cex)
                    throw new COMException($"Failed at: {name}\n{cex.Message}", cex.ErrorCode);

                throw new InvalidOperationException($"Failed at: {name}\n{ex.Message}", ex);
            }
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
                    "An error occurred.\n\nDiagnostic details have been copied to the clipboard.\nPaste them here and we’ll fix it.",
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
