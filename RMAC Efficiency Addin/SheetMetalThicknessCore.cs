using System;
using System.Runtime.InteropServices;
using Inventor;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Pure logic for setting sheet metal thickness from a selected sketch line.
    /// Caller is responsible for document validation, pick interaction, transaction
    /// management (see <c>InventorTransaction.RunFast</c>), post-run document/view
    /// refresh, and error presentation.
    /// </summary>
    internal static class SheetMetalThicknessCore
    {
        public const string RefSketchPrefix = "RMAC_SM_THK_REF";
        public const string RefWorkPlanePrefix = "RMAC_SM_THK_WP";

        /// <summary>Default exported thickness parameter name (if no setting is configured).</summary>
        public const string DefaultThicknessParamName = "THICKNESS";

        /// <summary>Backward-compat cleanup (older parts may have this).</summary>
        public const string OldThicknessParamName = "RMAC_SM_THK";

        /// <summary>Deterministic name for the sketch dimension parameter we create.</summary>
        public const string RefDimParamName = "RMAC_SM_THK_DIM";

        /// <summary>
        /// Sanitizes a user-supplied parameter name into something Inventor will accept.
        /// Intended for the exported thickness UserParameter name.
        /// </summary>
        public static string SanitizeUserParameterName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return DefaultThicknessParamName;

            var s = raw.Trim();

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
                if (sb.Length >= 64) break;
            }

            var cleaned = sb.ToString().Trim('_');
            if (string.IsNullOrWhiteSpace(cleaned))
                return DefaultThicknessParamName;

            // Inventor parameter names shouldn't start with a digit.
            if (char.IsDigit(cleaned[0]))
                cleaned = "T_" + cleaned;

            return cleaned;
        }

        /// <summary>
        /// Runs the full sheet metal thickness pipeline: cleanup old state → create a
        /// driven dimension between the line endpoints → expose as user parameter →
        /// drive the sheet metal thickness expression from it.
        /// Throws on failure; caller handles presentation.
        /// </summary>
        public static void Apply(
            Application app,
            PartDocument partDoc,
            SheetMetalComponentDefinition smDef,
            SketchLine skLine,
            PlanarSketch parentSketch,
            string thicknessParamName)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (partDoc == null) throw new ArgumentNullException(nameof(partDoc));
            if (smDef == null) throw new ArgumentNullException(nameof(smDef));
            if (skLine == null) throw new ArgumentNullException(nameof(skLine));
            if (parentSketch == null) throw new ArgumentNullException(nameof(parentSketch));
            if (string.IsNullOrWhiteSpace(thicknessParamName))
                throw new ArgumentException("Thickness parameter name is required.", nameof(thicknessParamName));

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
                // and rename its parameter to RMAC_SM_THK_DIM.
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

                        // Rename the dimension parameter to a stable, known name.
                        try { dim.Parameter.Name = RefDimParamName; }
                        catch
                        {
                            // If rename fails, fall back to using its auto name. We still prefer the stable name.
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
    }
}
