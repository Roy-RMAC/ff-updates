using System;

namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal sealed class FrameMemberMatcher
    {
        internal sealed class FrameData
        {
            public string Material = "";
            public string Stock = "";
            public string Length = "";

            public double Mass;
            public double Volume;
            public double Area;

            public double Ixx, Iyy, Izz;
            public double Ixy, Iyz, Ixz;

            public double COG_X, COG_Y, COG_Z;
            public double BBZ;
        }

        public FrameData Extract(RenumberingContext ctx, dynamic compDef, dynamic doc)
        {
            var f = new FrameData();

            f.Material = ctx.GetIPropString(doc, "Design Tracking Properties", "Material") ?? "";
            f.Stock = ctx.GetIPropString(doc, "Design Tracking Properties", "Stock Number") ?? "";
            f.Length = (ctx.GetUserPropString(doc, "G_L") ?? "").Trim();

            // Mass properties (preferred)
            object? mpObj = ctx.TryGet(compDef, "MassProperties");

            if (mpObj != null)
            {
                dynamic mp = mpObj;

                f.Mass = ctx.Round(ctx.GetDouble(mp, "Mass", 0), 4);
                f.Volume = ctx.Round(ctx.GetDouble(mp, "Volume", 0), 4);

                double sa = ctx.GetDouble(mp, "SurfaceArea", double.NaN);
                if (double.IsNaN(sa))
                    sa = ctx.ParseDouble(ctx.GetIPropString(doc, "Design Tracking Properties", "SurfaceArea"));
                f.Area = ctx.Round(sa, 4);

                object? comObj = ctx.TryGet(mp, "CenterOfMass");
                if (comObj != null)
                {
                    dynamic com = comObj;
                    f.COG_X = ctx.Round(ctx.GetDouble(com, "X", 0), 4);
                    f.COG_Y = ctx.Round(ctx.GetDouble(com, "Y", 0), 4);
                    f.COG_Z = ctx.Round(ctx.GetDouble(com, "Z", 0), 4);
                }

                ctx.TryGetXYZInertia(
                    mp,
                    out double ixx, out double iyy, out double izz,
                    out double ixy, out double iyz, out double ixz);

                f.Ixx = ctx.Round(ixx, 4);
                f.Iyy = ctx.Round(iyy, 4);
                f.Izz = ctx.Round(izz, 4);
                f.Ixy = ctx.Round(ixy, 4);
                f.Iyz = ctx.Round(iyz, 4);
                f.Ixz = ctx.Round(ixz, 4);
            }
            else
            {
                // Fallback: iProps (best effort)
                f.Mass = ctx.Round(ctx.ParseDouble(ctx.GetIPropString(doc, "Design Tracking Properties", "Mass")), 4);
                f.Volume = ctx.Round(ctx.ParseDouble(ctx.GetIPropString(doc, "Design Tracking Properties", "Volume")), 4);
                f.Area = ctx.Round(ctx.ParseDouble(ctx.GetIPropString(doc, "Design Tracking Properties", "SurfaceArea")), 4);
            }

            // Range box for BBZ (bounding box Z height)
            object? boxObj = ctx.TryGet(compDef, "RangeBox");
            if (boxObj != null)
            {
                dynamic box = boxObj;

                object? maxObj = ctx.TryGet(box, "MaxPoint");
                object? minObj = ctx.TryGet(box, "MinPoint");

                if (maxObj != null && minObj != null)
                {
                    dynamic maxP = maxObj;
                    dynamic minP = minObj;
                    f.BBZ = ctx.Round(ctx.GetDouble(maxP, "Z", 0) - ctx.GetDouble(minP, "Z", 0), 4);
                }
            }

            return f;
        }

        public bool IsMatch(FrameData f1, FrameData f2)
        {
            return Step1(f1, f2) && Step2(f1, f2) && Step3(f1, f2);
        }

        private static bool Step1(FrameData f1, FrameData f2)
        {
            return
                f1.Material == f2.Material &&
                f1.Stock == f2.Stock &&
                f1.Length == f2.Length &&
                Math.Abs(f1.Mass - f2.Mass) < 0.01 &&
                Math.Abs(f1.Volume - f2.Volume) < 0.5 &&
                Math.Abs(f1.Area - f2.Area) < 1.0;
        }

        private static bool Step2(FrameData f1, FrameData f2)
        {
            return Math.Abs(f1.Ixx - f2.Ixx) < 0.0001
                && Math.Abs(f1.Iyy - f2.Iyy) < 0.0001
                && Math.Abs(f1.Izz - f2.Izz) < 0.0001;
        }

        private static bool Step3(FrameData f1, FrameData f2)
        {
            if (f1.COG_X == f2.COG_X && f1.COG_Y == f2.COG_Y && f1.COG_Z == f2.COG_Z) return true;
            if (f1.COG_X == -f2.COG_X && f1.COG_Y == -f2.COG_Y && f1.COG_Z == f2.COG_Z) return true;

            if (f1.COG_Z == f2.BBZ - f2.COG_Z && f1.COG_X == f2.COG_X && f1.COG_Y == -f2.COG_Y) return true;
            if (f1.COG_Z == f2.BBZ - f2.COG_Z && f1.COG_X == -f2.COG_X && f1.COG_Y == f2.COG_Y) return true;
            if (f1.COG_Z == f2.BBZ - f2.COG_Z && f1.COG_X == -f2.COG_X && f1.COG_Y == -f2.COG_Y) return true;

            if (f1.COG_Z == f2.BBZ - f2.COG_Z && f1.COG_X == 0 && f2.COG_X == 0 && f1.COG_Y == -f2.COG_Y) return true;
            if (f1.COG_Z == f2.BBZ - f2.COG_Z && f1.COG_X == f2.COG_X && f1.COG_Y == 0 && f2.COG_Y == 0) return true;

            if (f1.COG_X == f2.COG_X && f1.COG_Y == f2.COG_Y) return true;

            return false;
        }
    }
}
