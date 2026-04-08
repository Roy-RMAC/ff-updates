using System;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Pure logic for applying sketch-dimension visibility policies to a single sketch.
    /// Caller is responsible for document-type validation, sketch-edit checks, and
    /// running inside a transaction (see <c>InventorTransaction.RunFast</c>).
    /// </summary>
    internal static class SketchDimCore
    {
        /// <summary>
        /// Shows all dimensions on the sketch and pins it (so future mode sweeps leave it alone).
        /// </summary>
        public static void ShowAndPin(object sketch, SketchDimensionPolicy dims)
        {
            if (sketch == null) throw new ArgumentNullException(nameof(sketch));
            if (dims == null) throw new ArgumentNullException(nameof(dims));

            dims.ApplyShowAll(sketch);
            SketchDimPin.SetPinned(sketch, true);
        }

        /// <summary>
        /// Unpins the sketch and immediately re-applies the current dim mode to it,
        /// ignoring its (now removed) pinned state.
        /// </summary>
        public static void UnpinAndApply(object sketch, DimPolicyMode mode, SketchDimensionPolicy dims)
        {
            if (sketch == null) throw new ArgumentNullException(nameof(sketch));
            if (dims == null) throw new ArgumentNullException(nameof(dims));

            SketchDimPin.SetPinned(sketch, false);
            ApplyMode(sketch, mode, respectPinned: false, dims);
        }

        /// <summary>
        /// Applies a dim mode to a single sketch. If <paramref name="respectPinned"/>
        /// is true and the sketch is pinned, it is treated as AllVisible.
        /// </summary>
        public static void ApplyMode(object sketch, DimPolicyMode mode, bool respectPinned, SketchDimensionPolicy dims)
        {
            if (sketch == null) throw new ArgumentNullException(nameof(sketch));
            if (dims == null) throw new ArgumentNullException(nameof(dims));

            if (respectPinned && SketchDimPin.IsPinned(sketch))
            {
                dims.ApplyShowAll(sketch);
                return;
            }

            switch (mode)
            {
                case DimPolicyMode.AllVisible:
                    dims.ApplyShowAll(sketch);
                    break;

                case DimPolicyMode.AllHidden:
                    dims.ApplyHideAll(sketch);
                    break;

                case DimPolicyMode.NamedOnly:
                default:
                    dims.ApplyNamedOnly(sketch);
                    break;
            }
        }
    }
}
