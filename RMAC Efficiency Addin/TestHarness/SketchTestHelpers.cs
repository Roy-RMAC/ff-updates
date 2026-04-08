#if TEST_HARNESS
using Inventor;

namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Shared helpers for fixture tests that operate on parts containing sketches.
    /// All operations are best-effort: individual sketch failures are swallowed so a
    /// single problematic sketch doesn't abort a setup helper.
    /// </summary>
    internal static class SketchTestHelpers
    {
        /// <summary>
        /// Sets every 2D and 3D sketch in the part to <c>Visible = true</c>.
        /// Used by tests that need a known starting state regardless of what previous
        /// tests left behind.
        /// </summary>
        public static void ResetAllSketchesVisible(PartDocument doc)
        {
            var def = doc.ComponentDefinition;
            foreach (object o in def.Sketches)
                if (o is Sketch sk) try { sk.Visible = true; } catch { }
            foreach (object o in def.Sketches3D)
                if (o is Sketch3D sk3) try { sk3.Visible = true; } catch { }
        }

        /// <summary>
        /// Sets every 2D and 3D sketch in the part to <c>Visible = false</c>.
        /// </summary>
        public static void ResetAllSketchesInvisible(PartDocument doc)
        {
            var def = doc.ComponentDefinition;
            foreach (object o in def.Sketches)
                if (o is Sketch sk) try { sk.Visible = false; } catch { }
            foreach (object o in def.Sketches3D)
                if (o is Sketch3D sk3) try { sk3.Visible = false; } catch { }
        }

        /// <summary>
        /// Returns total count of 2D + 3D sketches in the part.
        /// </summary>
        public static int CountAllSketches(PartDocument doc)
        {
            int n = 0;
            var def = doc.ComponentDefinition;
            foreach (object o in def.Sketches) if (o is Sketch) n++;
            foreach (object o in def.Sketches3D) if (o is Sketch3D) n++;
            return n;
        }

        /// <summary>
        /// Returns count of sketches whose <c>Visible</c> property currently reads as the
        /// requested value. Read failures are treated as "not matching".
        /// </summary>
        public static int CountSketchesWithVisible(PartDocument doc, bool wantVisible)
        {
            int n = 0;
            var def = doc.ComponentDefinition;

            foreach (object o in def.Sketches)
            {
                if (o is not Sketch sk) continue;
                bool v = false;
                try { v = sk.Visible; } catch { continue; }
                if (v == wantVisible) n++;
            }
            foreach (object o in def.Sketches3D)
            {
                if (o is not Sketch3D sk3) continue;
                bool v = false;
                try { v = sk3.Visible; } catch { continue; }
                if (v == wantVisible) n++;
            }

            return n;
        }

        /// <summary>
        /// Counts sketch entities (across all 2D sketches) that currently have a non-null
        /// OverrideColor. Used to verify color sweeps actually applied.
        /// </summary>
        public static int CountEntitiesWithOverrideColor(PartDocument doc)
        {
            int n = 0;
            var def = doc.ComponentDefinition;

            foreach (object o in def.Sketches)
            {
                if (o is not Sketch sk) continue;
                try
                {
                    foreach (object ent in sk.SketchEntities)
                    {
                        try
                        {
                            object? oc = ((dynamic)ent).OverrideColor;
                            if (oc != null) n++;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return n;
        }

        /// <summary>
        /// Counts dimension constraints (across all 2D sketches) whose <c>Visible</c> currently
        /// reads as the requested value.
        /// </summary>
        public static int CountDimensionsWithVisible(PartDocument doc, bool wantVisible)
        {
            int n = 0;
            var def = doc.ComponentDefinition;

            foreach (object o in def.Sketches)
            {
                if (o is not Sketch sk) continue;
                try
                {
                    var dims = sk.DimensionConstraints;
                    int count = 0;
                    try { count = dims.Count; } catch { continue; }
                    for (int i = 1; i <= count; i++)
                    {
                        try
                        {
                            object dc = dims[i];
                            bool v = false;
                            try { v = ((dynamic)dc).Visible; } catch { continue; }
                            if (v == wantVisible) n++;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return n;
        }

        /// <summary>
        /// Total dimension constraint count across all 2D sketches.
        /// </summary>
        public static int CountAllDimensions(PartDocument doc)
        {
            int n = 0;
            var def = doc.ComponentDefinition;
            foreach (object o in def.Sketches)
            {
                if (o is not Sketch sk) continue;
                try { n += sk.DimensionConstraints.Count; } catch { }
            }
            return n;
        }

        /// <summary>
        /// Interactive: prompt the user to click a sketch curve in the viewport, resolve
        /// the click to its parent <see cref="Sketch"/> or <see cref="Sketch3D"/> object,
        /// and return it. Returns null if the user cancels (presses Escape).
        ///
        /// Mirrors the production <c>ResolveSketch</c> walk: if the picked thing is already
        /// a sketch, return it; otherwise try Parent and NativeObject in turn.
        /// </summary>
        public static object? PickSketchInteractive(Application app, string prompt)
        {
            try
            {
                object picked = app.CommandManager.Pick(SelectionFilterEnum.kSketchCurveFilter, prompt);
                return ResolvePickedSketch(picked);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null; // user cancelled
            }
        }

        private static object? ResolvePickedSketch(object picked)
        {
            if (picked == null) return null;

            // Step 1: unwrap any proxy on the picked thing itself.
            // Proxies look like the underlying type but don't accept attribute writes.
            object current = UnwrapNative(picked);

            // Step 2: if it's already a sketch, return it (after unwrapping).
            if (current is Sketch || current is Sketch3D) return current;

            // Step 3: walk Parent to get from a SketchLine/curve to its owning sketch.
            try
            {
                dynamic d = current;
                object? parent = d.Parent;
                if (parent != null)
                {
                    // Unwrap the parent too — the chain might be Proxy(line) → Proxy(sketch)
                    var nativeParent = UnwrapNative(parent);
                    if (nativeParent is Sketch || nativeParent is Sketch3D)
                        return nativeParent;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// If <paramref name="obj"/> exposes a NativeObject property and that returns a non-null
        /// reference, return it; otherwise return the original. Used to escape Inventor's
        /// browser/viewport proxy wrappers and reach the underlying native object.
        /// </summary>
        private static object UnwrapNative(object obj)
        {
            if (obj == null) return obj!;
            try
            {
                dynamic d = obj;
                object? native = d.NativeObject;
                if (native != null) return native;
            }
            catch { }
            return obj;
        }
    }
}
#endif
