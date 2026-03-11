// Exporting/Model/ExportItem.cs
using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;
using System;

namespace RMAC_Efficiency_Addin.Exporting.Model
{
    internal sealed class ExportItem
    {
        public ExportItem(_Document doc, string category)
        {
            Doc = doc ?? throw new ArgumentNullException(nameof(doc));
            Category = (category ?? "").Trim();
        }

        public _Document Doc { get; }

        public string Category { get; }

        public double? Quantity { get; set; }
        public string? ItemNumber { get; set; }

        public string IdentityKey
        {
            get
            {
                var p = DocumentHelpers.SafeFullFileName(Doc);
                if (!string.IsNullOrWhiteSpace(p)) return p;

                var i = DocumentHelpers.SafeInternalName(Doc);
                if (!string.IsNullOrWhiteSpace(i)) return i;

                return DocumentHelpers.SafeDisplayName(Doc);
            }
        }
    }
}
