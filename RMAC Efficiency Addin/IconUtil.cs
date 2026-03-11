using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using PictureDisp = stdole.IPictureDisp;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Small helper for converting embedded PNG resources to stdole.IPictureDisp
    /// (what Inventor expects for ButtonDefinition images).
    /// </summary>
    internal static class IconUtil
    {
        private sealed class AxHostConverter : System.Windows.Forms.AxHost
        {
            private AxHostConverter() : base("") { }

            public static PictureDisp ToPictureDisp(Image image) =>
                (PictureDisp)GetIPictureDispFromPicture(image);
        }

        public static PictureDisp LoadPngAsPictureDisp(Assembly asm, string resourceSuffix)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));
            if (string.IsNullOrWhiteSpace(resourceSuffix)) throw new ArgumentNullException(nameof(resourceSuffix));

            string? fullName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (fullName == null)
                throw new FileNotFoundException($"Embedded resource not found: {resourceSuffix}");

            using var s = asm.GetManifestResourceStream(fullName)
                ?? throw new FileNotFoundException($"Resource stream missing: {fullName}");

            using var img = Image.FromStream(s);
            using var bmp = new Bitmap(img); // detach from stream
            return AxHostConverter.ToPictureDisp((Image)bmp.Clone());
        }
    }
}
