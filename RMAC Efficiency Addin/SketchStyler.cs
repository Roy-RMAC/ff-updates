using Inventor;
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
        /// Apply the muted/inactive palette to a sketch (native or edit object proxy).
        /// </summary>
        public void ApplyInactive(object sketchObj)
        {
            var pal = BuildInactivePaletteFromSettings();
            ApplyPaletteToEditObject(sketchObj, pal);
        }

        /// <summary>
        /// Apply the muted/inactive palette to all visible sketches in a part.
        /// </summary>
        public void ApplyInactiveToPart(PartDocument partDoc, Func<object, bool>? shouldApply = null)
        {
            var pal = BuildInactivePaletteFromSettings();
            var cd = partDoc.ComponentDefinition;

            foreach (object o in cd.Sketches)
                if (o is Inventor.Sketch sk && IsVisible(sk) && (shouldApply == null || shouldApply(sk)))
                    ApplyPaletteToEditObject(sk, pal);

            foreach (object o in cd.Sketches3D)
                if (o is Inventor.Sketch3D sk3 && IsVisible(sk3) && (shouldApply == null || shouldApply(sk3)))
                    ApplyPaletteToEditObject(sk3, pal);
        }

        // ============================================================
        // Override clear helper
        // ============================================================
        private static void TryClearOverrideColor(object sketchEntity)
        {
            if (sketchEntity == null) return;

            // In practice this clears the per-entity override:
            // OverrideColor is a COM property and Inventor accepts null/VT_EMPTY to clear.
            try { ((dynamic)sketchEntity).OverrideColor = null; } catch { }
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
            try { ((dynamic)sketchEntity).OverrideColor = color; } catch { }
        }

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
