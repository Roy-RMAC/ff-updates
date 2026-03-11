using Inventor;
using System;

namespace RMAC_Efficiency_Addin.Infrastructure
{
    internal static class SketchHelpers
    {
        internal static string? TryGetName(object? obj)
        {
            if (obj == null) return null;
            try { dynamic d = obj; return (string?)d.Name; }
            catch { return null; }
        }

        internal static Sketch? Find2DSketchByName(PartDocument partDoc, string name)
        {
            try
            {
                foreach (object o in partDoc.ComponentDefinition.Sketches)
                    if (o is Sketch sk && string.Equals(sk.Name, name, StringComparison.OrdinalIgnoreCase))
                        return sk;
            }
            catch { }
            return null;
        }

        internal static Sketch3D? Find3DSketchByName(PartDocument partDoc, string name)
        {
            try
            {
                foreach (object o in partDoc.ComponentDefinition.Sketches3D)
                    if (o is Sketch3D sk && string.Equals(sk.Name, name, StringComparison.OrdinalIgnoreCase))
                        return sk;
            }
            catch { }
            return null;
        }
    }
}
