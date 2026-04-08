using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Inventor;
using IOPath = System.IO.Path;

namespace RMAC_Efficiency_Addin.Layouts
{
    /// <summary>
    /// Pure derive logic extracted from <see cref="LayoutsController"/>. No UI, no
    /// dependency on the controller instance, no <c>_app</c> reference. Caller is
    /// responsible for transaction management, view refresh, and UI presentation.
    ///
    /// The "custom derive" feature filters which sketches/work-features from a source
    /// part get included in the derived part component, identified by stable string IDs
    /// computed from each entity (preferring Inventor reference keys, falling back to
    /// internal names or display names).
    /// </summary>
    internal static class CustomDeriveCore
    {
        // ============================================================
        // Top-level operations
        // ============================================================

        /// <summary>
        /// Adds a custom-filtered DerivedPartComponent to <paramref name="targetPart"/>,
        /// referencing <paramref name="sourceFullPath"/>, with only the entities whose IDs
        /// appear in <paramref name="selectedEntityIds"/> included.
        /// </summary>
        public static void AddCustomDerive(
            PartDocument targetPart,
            string sourceFullPath,
            _Document sourcePart,
            HashSet<string> selectedEntityIds)
        {
            if (targetPart == null) throw new ArgumentNullException(nameof(targetPart));
            if (string.IsNullOrWhiteSpace(sourceFullPath)) throw new ArgumentException("Source path is required.", nameof(sourceFullPath));
            if (sourcePart == null) throw new ArgumentNullException(nameof(sourcePart));
            if (selectedEntityIds == null) throw new ArgumentNullException(nameof(selectedEntityIds));

            dynamic dpcs = targetPart.ComponentDefinition.ReferenceComponents.DerivedPartComponents;
            dynamic def = dpcs.CreateDefinition(sourceFullPath);

            ConfigureDerivedDefinition(def, sourcePart, selectedEntityIds);

            dpcs.Add(def);
        }

        /// <summary>
        /// Adds a "full" DerivedPartComponent (no filtering — all defaults).
        /// Mirrors the legacy "Full Derive" button in LayoutsController.
        /// </summary>
        public static void AddFullDerive(PartDocument targetPart, string sourceFullPath)
        {
            if (targetPart == null) throw new ArgumentNullException(nameof(targetPart));
            if (string.IsNullOrWhiteSpace(sourceFullPath)) throw new ArgumentException("Source path is required.", nameof(sourceFullPath));

            dynamic dpcs = targetPart.ComponentDefinition.ReferenceComponents.DerivedPartComponents;
            dynamic def = dpcs.CreateDefinition(sourceFullPath);
            dpcs.Add(def);
        }

        /// <summary>
        /// Replace an existing custom derive on <paramref name="targetPart"/> for the given
        /// <paramref name="sourceFullPath"/> with a new one driven by <paramref name="selectedEntityIds"/>.
        /// Deletes the existing DerivedPartComponent (if any) and adds a fresh one.
        /// This is what the "Edit Derive" button does at its core.
        /// </summary>
        public static void ReplaceCustomDerive(
            PartDocument targetPart,
            string sourceFullPath,
            _Document sourcePart,
            HashSet<string> selectedEntityIds)
        {
            if (targetPart == null) throw new ArgumentNullException(nameof(targetPart));
            if (string.IsNullOrWhiteSpace(sourceFullPath)) throw new ArgumentException("Source path is required.", nameof(sourceFullPath));
            if (sourcePart == null) throw new ArgumentNullException(nameof(sourcePart));
            if (selectedEntityIds == null) throw new ArgumentNullException(nameof(selectedEntityIds));

            // Delete the existing derived component for this source (if present)
            var existing = FindDerivedPartComponent(targetPart, sourceFullPath);
            if (existing != null)
            {
                try { existing.Delete(); } catch { }
            }

            AddCustomDerive(targetPart, sourceFullPath, sourcePart, selectedEntityIds);
        }

        // ============================================================
        // Inspection helpers
        // ============================================================

        /// <summary>
        /// True if <paramref name="targetPart"/> already has a DerivedPartComponent that
        /// references <paramref name="sourceFullPath"/>.
        /// </summary>
        public static bool IsAlreadyDerived(PartDocument targetPart, string sourceFullPath)
        {
            try
            {
                dynamic dpcs = targetPart.ComponentDefinition.ReferenceComponents.DerivedPartComponents;

                foreach (object obj in dpcs)
                {
                    try
                    {
                        dynamic dpc = obj;
                        string? existing = null;

                        try { existing = (string?)dpc.ReferencedDocumentDescriptor?.FullDocumentName; } catch { }

                        if (string.IsNullOrWhiteSpace(existing)) continue;

                        if (string.Equals(
                                IOPath.GetFullPath(existing),
                                IOPath.GetFullPath(sourceFullPath),
                                StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Find the DerivedPartComponent in the target part that references the given source path.
        /// Returns null if not found.
        /// </summary>
        public static dynamic? FindDerivedPartComponent(PartDocument targetPart, string sourceFullPath)
        {
            try
            {
                dynamic dpcs = targetPart.ComponentDefinition.ReferenceComponents.DerivedPartComponents;

                foreach (object obj in dpcs)
                {
                    try
                    {
                        dynamic dpc = obj;
                        string? existing = null;
                        try { existing = (string?)dpc.ReferencedDocumentDescriptor?.FullDocumentName; } catch { }

                        if (!string.IsNullOrWhiteSpace(existing) &&
                            string.Equals(IOPath.GetFullPath(existing), IOPath.GetFullPath(sourceFullPath),
                                StringComparison.OrdinalIgnoreCase))
                            return dpc;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Read the entity IDs of currently-included sketches/work features from an existing
        /// DerivedPartComponent's definition. Used to verify what's actually in a derive
        /// after the operation.
        /// </summary>
        public static HashSet<string> ReadIncludedEntityIds(dynamic dpc, _Document sourceDoc)
        {
            var included = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                dynamic def = dpc.Definition;
                ReadIncludedFromCollection(def, "Sketches", sourceDoc, included);
                ReadIncludedFromCollection(def, "Sketches3D", sourceDoc, included);
                ReadIncludedFromCollection(def, "WorkFeatures", sourceDoc, included);
            }
            catch { }

            return included;
        }

        // ============================================================
        // Entity ID computation
        // ============================================================

        /// <summary>
        /// Compute a stable string ID for an Inventor entity, used to identify entities
        /// across the source/derived boundary. Prefers reference keys, falls back to
        /// internal name, falls back to display name + type.
        /// </summary>
        public static string GetEntityId(_Document doc, object obj)
        {
            string rk = GetReferenceKeyBase64(doc, obj);
            if (!string.IsNullOrWhiteSpace(rk))
                return "RK:" + rk;

            try
            {
                dynamic d = obj;
                string internalName = "";
                try { internalName = (string)d.InternalName; } catch { internalName = ""; }

                if (!string.IsNullOrWhiteSpace(internalName))
                    return "IN:" + internalName;
            }
            catch { }

            try
            {
                dynamic d = obj;
                string name = "";
                try { name = (string)d.Name; } catch { name = ""; }

                string typeName = obj.GetType().FullName ?? obj.GetType().Name;
                return "NM:" + typeName + ":" + name;
            }
            catch
            {
                return "OBJ:" + (obj.GetType().FullName ?? obj.GetType().Name);
            }
        }

        /// <summary>
        /// Get the base64-encoded reference key for an Inventor entity, or empty string
        /// if the entity doesn't support reference keys.
        /// </summary>
        public static string GetReferenceKeyBase64(_Document doc, object inventorObject)
        {
            if (doc == null || inventorObject == null) return "";

            try
            {
                var t = inventorObject.GetType();
                var methods = t.GetMethods().Where(m => m.Name == "GetReferenceKey").ToArray();
                if (methods.Length == 0) return "";

                foreach (var m in methods)
                {
                    var ps = m.GetParameters();
                    if (ps.Length < 1) continue;

                    bool firstIsByRefByteArray = ps[0].ParameterType == typeof(byte[]).MakeByRefType();
                    if (!firstIsByRefByteArray) continue;

                    object[] args = (ps.Length == 1)
                        ? new object[] { Array.Empty<byte>() }
                        : new object[] { Array.Empty<byte>(), 0L };

                    m.Invoke(inventorObject, args);

                    var key = args[0] as byte[];
                    if (key != null && key.Length > 0)
                        return Convert.ToBase64String(key);
                }
            }
            catch { }

            return "";
        }

        // ============================================================
        // Internal pipeline (private — only AddCustomDerive should call ConfigureDerivedDefinition)
        // ============================================================

        private static void ConfigureDerivedDefinition(
            dynamic def,
            _Document sourceDoc,
            HashSet<string> selectedEntityIds)
        {
            if (def == null) return;

            TrySetDerivedOption(def, "IncludeAllSolids", DerivedComponentOptionEnum.kDerivedExcludeAll);
            TrySetDerivedOption(def, "IncludeAllSurfaces", DerivedComponentOptionEnum.kDerivedExcludeAll);

            TrySetBool(def, "IncludeBody", false);
            TrySetBool(def, "BodyAsSolidBody", false);

            TrySetBool(def, "IncludeAllParameters", false);

            TrySetDerivedOption(def, "IncludeAllSketches", DerivedComponentOptionEnum.kDerivedIndividualDefined);
            TrySetDerivedOption(def, "IncludeAllSketches3D", DerivedComponentOptionEnum.kDerivedIndividualDefined);
            TrySetDerivedOption(def, "IncludeAll3DSketches", DerivedComponentOptionEnum.kDerivedIndividualDefined);
            TrySetDerivedOption(def, "IncludeAllWorkFeatures", DerivedComponentOptionEnum.kDerivedIndividualDefined);

            TrySetDerivedOption(def, "IncludeAllSketchBlockDefinitions", DerivedComponentOptionEnum.kDerivedExcludeAll);

            ApplySelectionToDerivedCollection(def, "Sketches", sourceDoc, selectedEntityIds);
            ApplySelectionToDerivedCollection(def, "Sketches3D", sourceDoc, selectedEntityIds);
            ApplySelectionToDerivedCollection(def, "WorkFeatures", sourceDoc, selectedEntityIds);

            ApplySelectionToDerivedCollection(def, "Parameters", sourceDoc, selectedEntityIds, forceExcludeAll: true);
            ApplySelectionToDerivedCollection(def, "SketchBlockDefinitions", sourceDoc, selectedEntityIds, forceExcludeAll: true);
            ApplySelectionToDerivedCollection(def, "SketchBlocks", sourceDoc, selectedEntityIds, forceExcludeAll: true);
        }

        private static void ApplySelectionToDerivedCollection(
            dynamic def,
            string collectionName,
            _Document sourceDoc,
            HashSet<string> selectedEntityIds,
            bool forceExcludeAll = false)
        {
            object? colObj = null;
            try { colObj = GetComPropertyValue(def, collectionName); } catch { colObj = null; }
            if (colObj == null) return;

            if (colObj is not System.Collections.IEnumerable en) return;

            foreach (var item in en)
            {
                if (item == null) continue;

                try
                {
                    dynamic di = item;

                    object? referenced = null;
                    try { referenced = di.ReferencedEntity; } catch { referenced = null; }
                    if (referenced == null)
                    {
                        try { referenced = di.ReferencedObject; } catch { referenced = null; }
                    }

                    bool include = false;

                    if (!forceExcludeAll && referenced != null)
                    {
                        string id = GetEntityId(sourceDoc, referenced);
                        include = selectedEntityIds.Contains(id);
                    }

                    try { di.IncludeEntity = include; }
                    catch
                    {
                        try { di.Include = include; } catch { }
                    }
                }
                catch { }
            }
        }

        private static void ReadIncludedFromCollection(
            dynamic def,
            string collectionName,
            _Document sourceDoc,
            HashSet<string> result)
        {
            object? colObj = null;
            try { colObj = GetComPropertyValue(def, collectionName); } catch { return; }
            if (colObj == null) return;

            if (colObj is not System.Collections.IEnumerable en) return;

            foreach (var item in en)
            {
                if (item == null) continue;

                try
                {
                    dynamic di = item;

                    bool isIncluded = false;
                    try { isIncluded = (bool)di.IncludeEntity; }
                    catch { try { isIncluded = (bool)di.Include; } catch { } }

                    if (!isIncluded) continue;

                    object? referenced = null;
                    try { referenced = di.ReferencedEntity; } catch { }
                    if (referenced == null)
                        try { referenced = di.ReferencedObject; } catch { }

                    if (referenced != null)
                    {
                        string id = GetEntityId(sourceDoc, referenced);
                        if (!string.IsNullOrWhiteSpace(id))
                            result.Add(id);
                    }
                }
                catch { }
            }
        }

        private static void TrySetDerivedOption(dynamic def, string propName, DerivedComponentOptionEnum value)
        {
            try
            {
                object? current = GetComPropertyValue(def, propName);
                if (current == null)
                {
                    SetComPropertyValue(def, propName, value);
                    return;
                }

                if (current is int)
                    SetComPropertyValue(def, propName, (int)value);
                else
                    SetComPropertyValue(def, propName, value);
            }
            catch { }
        }

        private static void TrySetBool(dynamic def, string propName, bool value)
        {
            try { SetComPropertyValue(def, propName, value); } catch { }
        }

        private static object? GetComPropertyValue(object comObj, string propName)
        {
            var props = TypeDescriptor.GetProperties(comObj);
            var pd = props.Find(propName, ignoreCase: true);
            if (pd == null) return null;
            return pd.GetValue(comObj);
        }

        private static void SetComPropertyValue(object comObj, string propName, object value)
        {
            var props = TypeDescriptor.GetProperties(comObj);
            var pd = props.Find(propName, ignoreCase: true);
            if (pd == null) return;
            pd.SetValue(comObj, value);
        }
    }
}
