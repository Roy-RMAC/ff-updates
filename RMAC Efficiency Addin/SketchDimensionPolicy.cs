using System.Text.RegularExpressions;

namespace RMAC_Efficiency_Addin
{
    internal sealed class SketchDimensionPolicy
    {
        private static bool IsDefaultDimParameterName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            return Regex.IsMatch(name.Trim(), @"^d\d+$", RegexOptions.IgnoreCase);
        }

        /// <summary>For editing: make all sketch dimensions visible.</summary>
        public void OnEnter(object? sketchObj)
        {
            if (sketchObj == null) return;
            ApplyShowAll(sketchObj);
        }

        /// <summary>
        /// Default exit policy: show only named dimensions (parameters not matching d0/d1/...).
        /// </summary>
        public void OnExit(object? sketchObj)
        {
            if (sketchObj == null) return;
            ApplyNamedOnly(sketchObj);
        }

        public void ApplyShowAll(object? sketchObj)
        {
            if (sketchObj == null) return;
            try
            {
                dynamic sk = sketchObj;
                try { sk.DimensionsVisible = true; } catch { }

                try
                {
                    foreach (object dcObj in sk.DimensionConstraints)
                    {
                        try
                        {
                            dynamic dc = dcObj;
                            try { dc.Visible = true; } catch { }
                            try { dc.ModelDimension.Visible = true; } catch { }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            catch { }
        }

        public void ApplyHideAll(object? sketchObj)
        {
            if (sketchObj == null) return;
            try
            {
                dynamic sk = sketchObj;
                try { sk.DimensionsVisible = false; } catch { }

                try
                {
                    foreach (object dcObj in sk.DimensionConstraints)
                    {
                        try
                        {
                            dynamic dc = dcObj;
                            try { dc.Visible = false; } catch { }
                            try { dc.ModelDimension.Visible = false; } catch { }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            catch { }
        }

        public void ApplyNamedOnly(object? sketchObj)
        {
            if (sketchObj == null) return;
            try
            {
                dynamic sk = sketchObj;

                bool hasNamed = false;

                foreach (object dcObj in sk.DimensionConstraints)
                {
                    try
                    {
                        dynamic dc = dcObj;
                        string? pName = null;
                        try { pName = (string?)dc.Parameter.Name; } catch { }

                        if (!IsDefaultDimParameterName(pName))
                            hasNamed = true;
                    }
                    catch { }
                }

                try { sk.DimensionsVisible = hasNamed; } catch { }

                foreach (object dcObj in sk.DimensionConstraints)
                {
                    try
                    {
                        dynamic dc = dcObj;
                        string? pName = null;
                        try { pName = (string?)dc.Parameter.Name; } catch { }

                        bool keep = hasNamed && !IsDefaultDimParameterName(pName);

                        try { dc.Visible = keep; } catch { }
                        try { dc.ModelDimension.Visible = keep; } catch { }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
