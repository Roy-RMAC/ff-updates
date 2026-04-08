using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;
using RMAC_Efficiency_Addin.Settings;
using System;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Applies per-entity OverrideColor to sketches to reduce visual clutter (inactive palette),
    /// and clears OverrideColor when requested.
    ///
    /// NOTE: We deliberately keep this class focused: no "active sketch palette" and no diagnostics.
    /// The active sketch appearance is now handled by Inventor's own "Show Format" toggle.
    /// </summary>
    internal sealed class SketchStyler
    {
        private readonly Inventor.Application _app;

        public SketchStyler(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        // ============================================================
        // Public API
        // ============================================================
        public void ClearOverrides(object sketchObj)
        {
            if (sketchObj == null) return;

            // 2D Sketch
            if (sketchObj is Inventor.Sketch sk2)
            {
                foreach (object ent in sk2.SketchEntities)
                    TryClearOverrideColor(ent);

                foreach (object pt in sk2.SketchPoints)
                    TryClearOverrideColor(pt);

                return;
            }

            // 3D Sketch
            if (sketchObj is Inventor.Sketch3D sk3)
            {
                // 3D sketch-level override exists
                try { sk3.OverrideColor = null; } catch { }

                foreach (object ent3 in sk3.SketchEntities3D)
                    TryClearOverrideColor(ent3);

                return;
            }

            // Proxy fallback
            try
            {
                dynamic sk = sketchObj;

                // Try 2D-style collections
                try
                {
                    foreach (object ent in sk.SketchEntities)
                        TryClearOverrideColor(ent);

                    foreach (object pt in sk.SketchPoints)
                        TryClearOverrideColor(pt);

                    return;
                }
                catch { }

                // Try 3D-style collections
                try
                {
                    try { sk.OverrideColor = null; } catch { }

                    foreach (object ent3 in sk.SketchEntities3D)
                        TryClearOverrideColor(ent3);

                    return;
                }
                catch { }
            }
            catch { }
        }

        /// <summary>
        /// Clear all OverrideColor from every sketch in a part (restore vanilla appearance).
        /// </summary>
        public void ClearOverridesFromPart(PartDocument partDoc)
        {
            var cd = partDoc.ComponentDefinition;

            foreach (object o in cd.Sketches)
                if (o is Inventor.Sketch sk)
                    ClearOverrides(sk);

            foreach (object o in cd.Sketches3D)
                if (o is Inventor.Sketch3D sk3)
                    ClearOverrides(sk3);
        }

        /// <summary>
        /// Apply the muted/inactive palette to a sketch (native or edit object proxy).
        /// Respects the current InactiveSketchColorMode setting.
        /// </summary>
        public void ApplyInactive(object sketchObj)
        {
            AddinSettings.EnsureLoaded();

            if (AddinSettings.Current.InactiveSketchColorMode == InactiveSketchColorMode.RandomPerSketch)
            {
                // Find this sketch's index within its parent part
                int index = FindSketchIndex(sketchObj);
                ApplyRandomColorToSketch(sketchObj, index);
                return;
            }

            var pal = BuildInactivePaletteFromSettings();
            ApplyPaletteToEditObject(sketchObj, pal);
        }

        /// <summary>
        /// Apply the muted role-based palette to all visible sketches in a part,
        /// regardless of the current InactiveSketchColorMode setting. Used when
        /// you specifically need the muted palette for visibility (e.g. selection sessions).
        /// </summary>
        public void ApplyMutedPaletteToPart(PartDocument partDoc)
        {
            var pal = BuildInactivePaletteFromSettings();
            var cd = partDoc.ComponentDefinition;

            foreach (object o in cd.Sketches)
                if (o is Inventor.Sketch sk && IsVisible(sk))
                    ApplyPaletteToEditObject(sk, pal);

            foreach (object o in cd.Sketches3D)
                if (o is Inventor.Sketch3D sk3 && IsVisible(sk3))
                    ApplyPaletteToEditObject(sk3, pal);
        }

        /// <summary>
        /// Apply the muted/inactive palette to all visible sketches in a part.
        /// Respects the current InactiveSketchColorMode setting.
        /// </summary>
        public void ApplyInactiveToPart(PartDocument partDoc, Func<object, bool>? shouldApply = null)
        {
            AddinSettings.EnsureLoaded();
            bool randomMode = AddinSettings.Current.InactiveSketchColorMode == InactiveSketchColorMode.RandomPerSketch;

            var pal = randomMode ? null : BuildInactivePaletteFromSettings();
            var cd = partDoc.ComponentDefinition;
            int colorIndex = 0;

            foreach (object o in cd.Sketches)
            {
                if (o is Inventor.Sketch sk && IsVisible(sk) && (shouldApply == null || shouldApply(sk)))
                {
                    if (randomMode)
                        ApplyRandomColorToSketch(sk, colorIndex++);
                    else
                        ApplyPaletteToEditObject(sk, pal!);
                }
            }

            foreach (object o in cd.Sketches3D)
            {
                if (o is Inventor.Sketch3D sk3 && IsVisible(sk3) && (shouldApply == null || shouldApply(sk3)))
                {
                    if (randomMode)
                        ApplyRandomColorToSketch(sk3, colorIndex++);
                    else
                        ApplyPaletteToEditObject(sk3, pal!);
                }
            }
        }

        // ============================================================
        // Override clear helper
        // ============================================================
        private static void TryClearOverrideColor(object sketchEntity)
        {
            if (sketchEntity == null) return;

            // In practice this clears the per-entity override:
            // OverrideColor is a COM property and Inventor accepts null/VT_EMPTY to clear.
            try { ((dynamic)sketchEntity).OverrideColor = null; return; } catch { }

            // SketchPoint objects may expose Color instead of OverrideColor
            try { ((dynamic)sketchEntity).Color = null; } catch { }
        }

        // ============================================================
        // Palettes + styling
        // ============================================================
        private enum SketchRole
        {
            Projected,
            UnderCurve,
            FullCurve,
            OverCurve,
            UnderConstruction,
            FullConstruction,
            OverConstruction
        }

        private sealed class SketchPalette
        {
            public Inventor.Color Projected = null!;
            public Inventor.Color UnderCurve = null!;
            public Inventor.Color FullCurve = null!;
            public Inventor.Color OverCurve = null!;
            public Inventor.Color UnderConstruction = null!;
            public Inventor.Color FullConstruction = null!;
            public Inventor.Color OverConstruction = null!;

            public Inventor.Color Get(SketchRole role) => role switch
            {
                SketchRole.Projected => Projected,
                SketchRole.UnderCurve => UnderCurve,
                SketchRole.FullCurve => FullCurve,
                SketchRole.OverCurve => OverCurve,
                SketchRole.UnderConstruction => UnderConstruction,
                SketchRole.FullConstruction => FullConstruction,
                SketchRole.OverConstruction => OverConstruction,
                _ => UnderCurve
            };
        }

        private SketchPalette BuildInactivePaletteFromSettings()
        {
            AddinSettings.EnsureLoaded();

            var s = AddinSettings.Current.InactiveSketchPalette ?? InactiveSketchPaletteSettings.CreateDefaults();
            s.SanitizeInPlace();

            return new SketchPalette
            {
                Projected = ToInvColor(s.Projected),
                UnderCurve = ToInvColor(s.UnderCurve),
                FullCurve = ToInvColor(s.FullCurve),
                OverCurve = ToInvColor(s.OverCurve),
                UnderConstruction = ToInvColor(s.UnderConstruction),
                FullConstruction = ToInvColor(s.FullConstruction),
                OverConstruction = ToInvColor(s.OverConstruction),
            };
        }

        private Inventor.Color ToInvColor(Rgb24 rgb)
        {
            byte r = (byte)Math.Clamp(rgb.R, 0, 255);
            byte g = (byte)Math.Clamp(rgb.G, 0, 255);
            byte b = (byte)Math.Clamp(rgb.B, 0, 255);
            return _app.TransientObjects.CreateColor(r, g, b);
        }

        private static bool IsVisible(Inventor.Sketch sk) { try { return sk.Visible; } catch { return false; } }
        private static bool IsVisible(Inventor.Sketch3D sk3) { try { return sk3.Visible; } catch { return false; } }

        /// <summary>
        /// Find the ordinal index of a sketch within its parent part (0-based).
        /// Used to assign a consistent golden-angle hue to a single sketch.
        /// </summary>
        private int FindSketchIndex(object sketchObj)
        {
            try
            {
                if (_app.ActiveDocument is not PartDocument partDoc)
                    return 0;

                string? targetName = SketchHelpers.TryGetName(sketchObj);
                if (string.IsNullOrWhiteSpace(targetName))
                    return 0;

                var cd = partDoc.ComponentDefinition;
                int index = 0;

                foreach (object o in cd.Sketches)
                {
                    if (o is Inventor.Sketch sk)
                    {
                        if (string.Equals(sk.Name, targetName, StringComparison.OrdinalIgnoreCase))
                            return index;
                        index++;
                    }
                }

                foreach (object o in cd.Sketches3D)
                {
                    if (o is Inventor.Sketch3D sk3)
                    {
                        if (string.Equals(sk3.Name, targetName, StringComparison.OrdinalIgnoreCase))
                            return index;
                        index++;
                    }
                }
            }
            catch { }

            return 0;
        }

        private static SketchRole Classify2D(object entObj)
        {
            try
            {
                var se = entObj as Inventor.SketchEntity;
                if (se != null && se.ReferencedEntity != null)
                    return SketchRole.Projected;
            }
            catch { }

            bool isConstruction = false;
            try { dynamic d = entObj; isConstruction = (bool)d.Construction; } catch { }

            Inventor.ConstraintStatusEnum status = Inventor.ConstraintStatusEnum.kUnderConstrainedConstraintStatus;
            try { dynamic d = entObj; status = (Inventor.ConstraintStatusEnum)d.ConstraintStatus; } catch { }

            bool isFull = status == Inventor.ConstraintStatusEnum.kFullyConstrainedConstraintStatus;
            bool isOver = status == Inventor.ConstraintStatusEnum.kOverConstrainedConstraintStatus;

            if (!isConstruction)
            {
                if (isOver) return SketchRole.OverCurve;
                if (isFull) return SketchRole.FullCurve;
                return SketchRole.UnderCurve;
            }
            else
            {
                if (isOver) return SketchRole.OverConstruction;
                if (isFull) return SketchRole.FullConstruction;
                return SketchRole.UnderConstruction;
            }
        }

        private static SketchRole Classify3D(object entObj)
        {
            try
            {
                var se = entObj as Inventor.SketchEntity3D;
                if (se != null && se.ReferencedEntity != null)
                    return SketchRole.Projected;
            }
            catch { }

            bool isConstruction = false;
            try { dynamic d = entObj; isConstruction = (bool)d.Reference; } catch { }

            Inventor.ConstraintStatusEnum status = Inventor.ConstraintStatusEnum.kUnderConstrainedConstraintStatus;
            try { dynamic d = entObj; status = (Inventor.ConstraintStatusEnum)d.ConstraintStatus; } catch { }

            bool isFull = status == Inventor.ConstraintStatusEnum.kFullyConstrainedConstraintStatus;
            bool isOver = status == Inventor.ConstraintStatusEnum.kOverConstrainedConstraintStatus;

            if (!isConstruction)
            {
                if (isOver) return SketchRole.OverCurve;
                if (isFull) return SketchRole.FullCurve;
                return SketchRole.UnderCurve;
            }
            else
            {
                if (isOver) return SketchRole.OverConstruction;
                if (isFull) return SketchRole.FullConstruction;
                return SketchRole.UnderConstruction;
            }
        }

        private static void TrySetOverrideColor(object sketchEntity, Inventor.Color color)
        {
            try { ((dynamic)sketchEntity).OverrideColor = color; return; } catch { }

            // SketchPoint objects may expose Color instead of OverrideColor
            try { ((dynamic)sketchEntity).Color = color; } catch { }
        }

        // ============================================================
        // Random color per sketch
        // ============================================================

        private void ApplyRandomColorToSketch(object sketchObj, int sketchIndex)
        {
            var (curveColor, constructionColor) = GenerateSketchColors(sketchIndex);

            if (sketchObj is Inventor.Sketch sk2)
            {
                foreach (object ent in sk2.SketchEntities)
                {
                    bool isConstruction = false;
                    try { dynamic d = ent; isConstruction = (bool)d.Construction; } catch { }
                    TrySetOverrideColor(ent, isConstruction ? constructionColor : curveColor);
                }
                foreach (object pt in sk2.SketchPoints)
                    TrySetOverrideColor(pt, curveColor);
                return;
            }

            if (sketchObj is Inventor.Sketch3D sk3)
            {
                try { sk3.OverrideColor = curveColor; } catch { }
                foreach (object ent3 in sk3.SketchEntities3D)
                {
                    bool isConstruction = false;
                    try { dynamic d = ent3; isConstruction = (bool)d.Reference; } catch { }
                    TrySetOverrideColor(ent3, isConstruction ? constructionColor : curveColor);
                }
                return;
            }

            // Proxy fallback
            try
            {
                dynamic sk = sketchObj;
                try
                {
                    foreach (object ent in sk.SketchEntities)
                    {
                        bool isConstruction = false;
                        try { dynamic d = ent; isConstruction = (bool)d.Construction; } catch { }
                        TrySetOverrideColor(ent, isConstruction ? constructionColor : curveColor);
                    }
                    foreach (object pt in sk.SketchPoints)
                        TrySetOverrideColor(pt, curveColor);
                    return;
                }
                catch { }

                try
                {
                    try { sk.OverrideColor = curveColor; } catch { }
                    foreach (object ent3 in sk.SketchEntities3D)
                    {
                        bool isConstruction = false;
                        try { dynamic d = ent3; isConstruction = (bool)d.Reference; } catch { }
                        TrySetOverrideColor(ent3, isConstruction ? constructionColor : curveColor);
                    }
                    return;
                }
                catch { }
            }
            catch { }
        }

        /// <summary>
        /// Generate a deterministic (curve, construction) color pair from a sketch index.
        /// Uses the golden angle (137.508°) to maximally separate hues regardless of count.
        /// Construction color is a lighter variant of the curve color.
        /// </summary>
        private (Inventor.Color curve, Inventor.Color construction) GenerateSketchColors(int sketchIndex)
        {
            // Golden angle ensures maximum hue separation for any number of sketches
            const double GoldenAngle = 137.508;
            double hue = (sketchIndex * GoldenAngle) % 360.0;
            double saturation = 0.65;
            double lightness = 0.45;

            var (cr, cg, cb) = HslToRgb(hue, saturation, lightness);
            var curve = _app.TransientObjects.CreateColor((byte)cr, (byte)cg, (byte)cb);

            // Construction: same hue, higher lightness for a washed-out / lighter look
            var (lr, lg, lb) = HslToRgb(hue, 0.45, 0.70);
            var construction = _app.TransientObjects.CreateColor((byte)lr, (byte)lg, (byte)lb);

            return (curve, construction);
        }

        private static (int R, int G, int B) HslToRgb(double h, double s, double l)
        {
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = l - c / 2.0;

            double r1, g1, b1;
            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            return (
                Math.Clamp((int)((r1 + m) * 255 + 0.5), 0, 255),
                Math.Clamp((int)((g1 + m) * 255 + 0.5), 0, 255),
                Math.Clamp((int)((b1 + m) * 255 + 0.5), 0, 255)
            );
        }

        // ============================================================
        // Role-based palette application
        // ============================================================

        private void ApplyPaletteToEditObject(object editSketchObj, SketchPalette pal)
        {
            if (editSketchObj is Inventor.Sketch sk2)
            {
                foreach (object ent in sk2.SketchEntities)
                    TrySetOverrideColor(ent, pal.Get(Classify2D(ent)));

                foreach (object pt in sk2.SketchPoints)
                    TrySetOverrideColor(pt, pal.UnderCurve);

                return;
            }

            if (editSketchObj is Inventor.Sketch3D sk3)
            {
                try { sk3.OverrideColor = pal.UnderCurve; } catch { }

                foreach (object ent3 in sk3.SketchEntities3D)
                    TrySetOverrideColor(ent3, pal.Get(Classify3D(ent3)));

                return;
            }

            // Proxy fallback
            try
            {
                dynamic sk = editSketchObj;

                try
                {
                    foreach (object ent in sk.SketchEntities)
                        TrySetOverrideColor(ent, pal.Get(Classify2D(ent)));

                    foreach (object pt in sk.SketchPoints)
                        TrySetOverrideColor(pt, pal.UnderCurve);

                    return;
                }
                catch { }

                try
                {
                    try { sk.OverrideColor = pal.UnderCurve; } catch { }

                    foreach (object ent3 in sk.SketchEntities3D)
                        TrySetOverrideColor(ent3, pal.Get(Classify3D(ent3)));

                    return;
                }
                catch { }
            }
            catch { }
        }
    }
}
