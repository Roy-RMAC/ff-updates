using System;
using Inventor;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Pure logic for the legacy sketch colour sweep. Applies the inactive palette
    /// to every sketch in a part document via the provided <see cref="SketchStyler"/>.
    /// Caller is responsible for document-type validation and running inside a transaction.
    /// </summary>
    internal static class LegacySweepCore
    {
        public static void Run(PartDocument partDoc, SketchStyler styler)
        {
            if (partDoc == null) throw new ArgumentNullException(nameof(partDoc));
            if (styler == null) throw new ArgumentNullException(nameof(styler));

            styler.ApplyInactiveToPart(partDoc);
        }
    }
}
