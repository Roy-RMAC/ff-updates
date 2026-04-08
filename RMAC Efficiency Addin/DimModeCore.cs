using System;
using Inventor;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Pure logic for applying a sketch dimension visibility policy to every sketch in a part.
    /// Caller is responsible for settings persistence, combo-box UI updates, and running inside
    /// a transaction.
    /// </summary>
    internal static class DimModeCore
    {
        /// <summary>
        /// Applies <paramref name="mode"/> to every 2D and 3D sketch in the part, respecting the
        /// pinned flag on each sketch (pinned sketches are always treated as AllVisible).
        /// Individual sketch failures are swallowed — one bad sketch does not abort the sweep.
        /// </summary>
        public static void ApplyToPart(PartDocument partDoc, DimPolicyMode mode, SketchDimensionPolicy dims)
        {
            if (partDoc == null) throw new ArgumentNullException(nameof(partDoc));
            if (dims == null) throw new ArgumentNullException(nameof(dims));

            var def = partDoc.ComponentDefinition;

            foreach (object o in def.Sketches)
            {
                if (o is not Sketch sk) continue;
                try { SketchDimCore.ApplyMode(sk, mode, respectPinned: true, dims); }
                catch { /* ignore individual sketch failures */ }
            }

            foreach (object o in def.Sketches3D)
            {
                if (o is not Sketch3D sk3) continue;
                try { SketchDimCore.ApplyMode(sk3, mode, respectPinned: true, dims); }
                catch { /* ignore individual sketch failures */ }
            }
        }
    }
}
