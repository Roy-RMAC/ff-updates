using System;
using Inventor;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Pure logic for bulk sketch visibility operations on a part document.
    /// Caller is responsible for document-type validation, sketch-edit checks,
    /// and running inside a transaction (see <c>InventorTransaction.RunFast</c>).
    /// </summary>
    internal static class SketchVisibilityCore
    {
        /// <summary>
        /// Sets visibility on every 2D and 3D sketch in the part. Individual sketch
        /// failures are captured in <see cref="SketchVisibilityResult.Failures"/> rather
        /// than thrown, so one bad sketch doesn't abort the whole sweep.
        /// </summary>
        public static SketchVisibilityResult SetAll(PartDocument partDoc, bool makeVisible)
        {
            if (partDoc == null) throw new ArgumentNullException(nameof(partDoc));

            var result = new SketchVisibilityResult();
            var def = partDoc.ComponentDefinition;

            foreach (object o in def.Sketches)
            {
                if (o is not Sketch sk) continue;
                try
                {
                    sk.Visible = makeVisible;
                    result.Sketches2DChanged++;
                }
                catch
                {
                    result.Failures++;
                }
            }

            foreach (object o in def.Sketches3D)
            {
                if (o is not Sketch3D sk3) continue;
                try
                {
                    sk3.Visible = makeVisible;
                    result.Sketches3DChanged++;
                }
                catch
                {
                    result.Failures++;
                }
            }

            return result;
        }
    }

    internal sealed class SketchVisibilityResult
    {
        public int Sketches2DChanged { get; set; }
        public int Sketches3DChanged { get; set; }
        public int Failures { get; set; }
        public int TotalChanged => Sketches2DChanged + Sketches3DChanged;
    }
}
