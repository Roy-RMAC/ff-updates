using System;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Stores per-sketch pin state inside the model using AttributeSets.
    /// </summary>
    internal static class SketchDimPin
    {
        private const string AttrSetName = "RMAC_DimPolicy";
        private const string AttrName = "PinnedFullDims";

        public static bool IsPinned(object sketchObj)
        {
            if (sketchObj == null) return false;

            try
            {
                dynamic sk = sketchObj;

                dynamic sets = sk.AttributeSets;
                if (sets == null) return false;

                if (!(bool)sets.NameIsUsed[AttrSetName]) return false;

                dynamic set = sets[AttrSetName];
                if (set == null) return false;

                // AttributeSet.NameIsUsed is 1-based indexer in some COM collections; use Item access pattern safely
                try
                {
                    bool used = false;
                    try { used = (bool)set.NameIsUsed[AttrName]; } catch { used = false; }
                    if (!used) return false;
                }
                catch
                {
                    // if NameIsUsed not available, fall back to try get
                }

                try
                {
                    dynamic attr = set[AttrName];
                    if (attr == null) return false;

                    object? val = null;
                    try { val = attr.Value; } catch { }

                    if (val is bool b) return b;

                    // Some COM interop returns short/int for bool
                    if (val is short s) return s != 0;
                    if (val is int i) return i != 0;

                    return false;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetPinned(object sketchObj, bool pinned)
        {
            if (sketchObj == null) return;

            try
            {
                dynamic sk = sketchObj;
                dynamic sets = sk.AttributeSets;
                if (sets == null) return;

                dynamic? set = null;
                try
                {
                    bool exists = false;
                    try { exists = (bool)sets.NameIsUsed[AttrSetName]; } catch { exists = false; }

                    if (exists)
                        set = sets[AttrSetName];
                    else
                        set = sets.Add(AttrSetName, true); // copy with owner
                }
                catch
                {
                    // If we can't create/read attribute sets, bail silently.
                    return;
                }

                if (pinned)
                {
                    try
                    {
                        bool attrExists = false;
                        try { attrExists = (bool)set.NameIsUsed[AttrName]; } catch { attrExists = false; }

                        if (attrExists)
                        {
                            dynamic a = set[AttrName];
                            try { a.Value = true; } catch { }
                        }
                        else
                        {
                            // kBooleanType = 11 in ValueTypeEnum, but use dynamic enum access where possible.
                            try { set.Add(AttrName, Inventor.ValueTypeEnum.kBooleanType, true); }
                            catch
                            {
                                // fallback
                                try { set.Add(AttrName, (Inventor.ValueTypeEnum)11, true); } catch { }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    // Remove attribute if present; if set becomes empty, delete the set.
                    try
                    {
                        bool attrExists = false;
                        try { attrExists = (bool)set.NameIsUsed[AttrName]; } catch { attrExists = false; }

                        if (attrExists)
                        {
                            try
                            {
                                dynamic a = set[AttrName];
                                try { a.Delete(); } catch { }
                            }
                            catch { }
                        }

                        try
                        {
                            int count = 0;
                            try { count = (int)set.Count; } catch { }

                            if (count <= 0)
                            {
                                try { set.Delete(); } catch { }
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
            }
            catch
            {
                // silent
            }
        }
    }
}
