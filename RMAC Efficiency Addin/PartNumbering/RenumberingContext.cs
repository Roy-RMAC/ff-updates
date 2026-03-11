// RenumberingContext.cs
using Inventor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IOPath = System.IO.Path;
using System.Reflection;
using System.Text;

namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal sealed class RenumberingContext
    {
        public Inventor.Application App { get; }
        public AssemblyDocument TopAssembly { get; }
        public dynamic BomView { get; }
        public bool Debug { get; }

        // Part number template settings (post-processor)
        public string PartNumberTemplate { get; set; } = "<PartNumber>";
        public bool PartNumberTemplateUseTopLevelAssemblyProperties { get; set; } = false;
        public bool KeepTopLevelAssemblyPartNumberFixed { get; set; } = true;

        // Skip renumbering gate (exact, case-sensitive match).
        // If enabled and iProperty equals SkipRenumberMatchValue, the doc is skipped.
        // If the doc is an assembly, the entire subtree is skipped by the schemes (they avoid recursion).
        public bool SkipRenumberEnabled { get; set; } = false;
        public string SkipRenumberPropertySet { get; set; } = "";
        public string SkipRenumberPropertyName { get; set; } = "";
        public string SkipRenumberMatchValue { get; set; } = "";


        // Collected during a run (reported at end)
        private readonly HashSet<string> _missingPropertyKeys = new(StringComparer.OrdinalIgnoreCase);
        public List<string> MissingProperties { get; } = new();

        public int AssemblyCounter { get; set; } = 2;  // Structured/sequential can use
        public int GenPartCounter { get; set; } = 1;   // Structured uses

        // Progress tracking (wired by orchestrator)
        public Action<int, string>? OnProgress { get; set; }
        public Action<string>? OnStatusText { get; set; }
        private int _progressStep;

        /// <summary>
        /// Advances the progress bar by one step and updates the operation text.
        /// Called by numbering schemes for each BOM row processed.
        /// </summary>
        public void StepProgress(string text)
        {
            _progressStep++;
            OnProgress?.Invoke(_progressStep, text);
        }

        /// <summary>
        /// Updates the status text label without advancing the progress bar.
        /// Used for phase messages (e.g. "Preloading documents...", "Resetting flags...").
        /// </summary>
        public void UpdateStatusText(string text)
        {
            OnStatusText?.Invoke(text);
        }

        public RenumberingContext(Inventor.Application app, AssemblyDocument topAsm, dynamic bomView, bool debug)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            TopAssembly = topAsm ?? throw new ArgumentNullException(nameof(topAsm));
            BomView = bomView;
            Debug = debug;
        }

        // -----------------------------
        // Logging
        // -----------------------------
        public string? LogPath { get; private set; }
        private StreamWriter? _writer;

        public void LogInit()
        {
            if (!Debug) return;

            try
            {
                string dir = IOPath.Combine(IOPath.GetTempPath(), "RMAC_Efficiency_Addin");
                Directory.CreateDirectory(dir);

                LogPath = IOPath.Combine(dir, $"RMAC_Renumber_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                _writer = new StreamWriter(LogPath, append: false, encoding: Encoding.UTF8) { AutoFlush = true };

                Info($"Log started: {LogPath}");
            }
            catch
            {
                LogPath = null;
                _writer = null;
            }
        }

        public void LogClose()
        {
            try { _writer?.Flush(); } catch { }
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }

        public void Info(string msg) => Write("INFO", msg, always: true);
        public void Warn(string msg) => Write("WARN", msg, always: true);
        public void Error(string msg) => Write("ERR ", msg, always: true);
        public void Dbg(string msg) => Write("DBG ", msg, always: Debug);

        private void Write(string level, string msg, bool always)
        {
            if (!always) return;

            string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
            try { System.Diagnostics.Debug.WriteLine("[RMAC] " + line); } catch { }
            try { _writer?.WriteLine(line); } catch { }
        }

        // -----------------------------
        // COM / dynamic helpers
        // -----------------------------
        public dynamic ComItem(dynamic collection, object index)
        {
            try { return collection[index]; } catch { }
            try { return collection.Item(index); } catch { }
            return null!;
        }

        public int GetCount(dynamic col)
        {
            try { return (int)col.Count; } catch { return 0; }
        }

        public object? TryGet(dynamic obj, string prop)
        {
            try
            {
                return ((object)obj).GetType().InvokeMember(
                    prop,
                    BindingFlags.GetProperty,
                    null,
                    (object)obj,
                    null);
            }
            catch
            {
                try { return obj[prop]; } catch { return null; }
            }
        }

        public bool IsModifiable(dynamic doc)
        {
            try { return (bool)doc.IsModifiable; } catch { return false; }
        }

        /// <summary>
        /// Returns true if this doc should be skipped:
        /// - Not modifiable, OR
        /// - SkipRenumberEnabled and the selected iProperty equals SkipRenumberMatchValue (exact match)
        /// Missing iProperty => not skipped.
        /// </summary>
        public bool ShouldSkip(dynamic doc, out string reason)
        {
            reason = string.Empty;

            if (doc == null)
            {
                reason = "Null document";
                return true;
            }

            // Gate 1: modifiable
            if (!IsModifiable(doc))
            {
                reason = "Document not modifiable";
                return true;
            }

            // Gate 2: feature off
            if (!SkipRenumberEnabled)
                return false;

            string matchVal = SkipRenumberMatchValue ?? string.Empty;

            // Decide whether we are in "alias/selection" mode or "explicit set/name" mode.
            // Alias mode triggers when:
            // - PropertyName looks like a selection token (contains '.'), OR
            // - PropertyName is "Custom…", OR
            // - PropertySet is blank (meaning caller wants global search), OR
            // - PropertyName matches an alias key (e.g. "Status")
            string selection = (SkipRenumberPropertyName ?? string.Empty).Trim();
            string customName = (SkipRenumberPropertySet ?? string.Empty).Trim(); // used only when selection == "Custom…"

            bool looksLikeSelection =
                string.IsNullOrWhiteSpace(selection) == false &&
                (selection.Contains(".") ||
                 string.Equals(selection, "Custom…", StringComparison.Ordinal) ||
                 string.IsNullOrWhiteSpace(SkipRenumberPropertySet) ||
                 IsAliasKey(selection));

            // ----------------------------
            // MODE A: Alias / Selection mode
            // ----------------------------
            if (looksLikeSelection)
            {
                foreach (var (setName, propName) in SkipPropertyResolver.Resolve(selection, customName))
                {
                    if (string.IsNullOrWhiteSpace(propName))
                        continue;

                    string actual;
                    bool found;

                    if (!string.IsNullOrWhiteSpace(setName))
                        found = TryGetIPropertyStringExact(doc, setName, propName, out actual);
                    else
                        found = TryGetIPropertyStringAnySet(doc, propName, out actual); // last-resort search all sets

                    if (Debug)
                    {
                        if (!found)
                            Dbg($"SkipCheck: {SafeName(doc)} | {setName}\\{propName} NOT FOUND | match='{matchVal}'");
                        else
                            Dbg($"SkipCheck: {SafeName(doc)} | {setName}\\{propName} actual='{actual}' | match='{matchVal}'");
                    }

                    if (!found)
                        continue;

                    if (actual == matchVal) // exact, case-sensitive
                    {
                        reason = $"Skip match: {setName}\\{propName} == \"{matchVal}\"";
                        return true;
                    }
                }

                return false;
            }

            // ----------------------------
            // MODE B: Explicit Set/Name mode (legacy)
            // ----------------------------
            if (string.IsNullOrWhiteSpace(SkipRenumberPropertyName))
                return false;

            {
                string actual;
                bool found = TryGetIPropertyStringExact(doc, SkipRenumberPropertySet, SkipRenumberPropertyName, out actual);

                if (Debug)
                {
                    if (!found)
                        Dbg($"SkipCheck: {SafeName(doc)} | {SkipRenumberPropertySet}\\{SkipRenumberPropertyName} NOT FOUND | match='{matchVal}'");
                    else
                        Dbg($"SkipCheck: {SafeName(doc)} | {SkipRenumberPropertySet}\\{SkipRenumberPropertyName} actual='{actual}' | match='{matchVal}'");
                }

                if (!found)
                    return false;

                if (actual == matchVal)
                {
                    reason = $"Skip match: {SkipRenumberPropertySet}\\{SkipRenumberPropertyName} == \"{matchVal}\"";
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns true if the selection string is one of the alias keys.
        /// This avoids forcing callers to include a '.' for alias mode.
        /// </summary>
        private static bool IsAliasKey(string selection)
        {
            // Keep in sync with SkipPropertyResolver alias keys.
            // Minimal check: the common one you care about.
            return string.Equals(selection, "Status", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "UserStatus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "DesignStatus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "PartNumber", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "StockNumber", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "Material", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "Project", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "Description", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "Author", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "RevisionNumber", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "Comments", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection, "Thickness", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetIPropertyStringExact(dynamic doc, string setName, string propName, out string value)
        {
            value = "";

            if (doc == null || string.IsNullOrWhiteSpace(propName))
                return false;

            // Try direct set first (fast path)
            if (!string.IsNullOrWhiteSpace(setName))
            {
                if (TryReadFromSet(doc!, setName, propName, out value))
                    return true;

                // Custom props: Inventor sometimes exposes this set name instead
                if (string.Equals(setName, "User Defined Properties", StringComparison.Ordinal) &&
                    TryReadFromSet(doc!, "Inventor User Defined Properties", propName, out value))
                    return true;

                return false;
            }

            // If no set name supplied, search all sets
            try
            {
                foreach (dynamic ps in doc!.PropertySets)
                {
                    if (TryReadFromSetObject(ps, propName, out value))
                        return true;
                }
            }
            catch { }

            return false;

            static bool TryReadFromSet(dynamic d, string sn, string pn, out string v)
            {
                v = "";
                try
                {
                    dynamic ps = d.PropertySets[sn];
                    dynamic p = ps[pn];
                    v = Convert.ToString(p.Value) ?? "";
                    return true;
                }
                catch { return false; }
            }

            static bool TryReadFromSetObject(dynamic ps, string pn, out string v)
            {
                v = "";
                try
                {
                    dynamic p = ps[pn];
                    v = Convert.ToString(p.Value) ?? "";
                    return true;
                }
                catch { return false; }
            }
        }


        public int GetInt(dynamic obj, string prop, int fallback)
        {
            try
            {
                return Convert.ToInt32(((object)obj).GetType().InvokeMember(
                    prop,
                    BindingFlags.GetProperty,
                    null,
                    (object)obj,
                    null), CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public double GetDouble(dynamic obj, string prop, double fallback)
        {
            try
            {
                object? o = ((object)obj).GetType().InvokeMember(
                    prop,
                    BindingFlags.GetProperty,
                    null,
                    (object)obj,
                    null);
                if (o == null) return fallback;
                return Convert.ToDouble(o, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public double GetVolume(dynamic compDef)
        {
            try { return (double)compDef.MassProperties.Volume; } catch { return 0.0; }
        }

        public bool HasInterest(dynamic doc, string interest)
        {
            try { return (bool)doc.DocumentInterests.HasInterest(interest); } catch { return false; }
        }

        public bool PropertySetExists(dynamic doc, string setName)
        {
            try
            {
                object ps;
                return (bool)doc.PropertySets.PropertySetExists(setName, out ps);
            }
            catch
            {
                try { _ = ComItem(doc.PropertySets, setName); return true; } catch { }
                return false;
            }
        }

        public IEnumerable<dynamic> GetRowComponentDefinitions(dynamic bomRow)
        {
            object? defsObj = TryGet(bomRow, "ComponentDefinitions");
            if (defsObj == null) yield break;

            dynamic defs = defsObj;
            int n = GetCount(defs);
            for (int i = 1; i <= n; i++)
            {
                dynamic cd = ComItem(defs, i);
                if (cd != null) yield return cd;
            }
        }

        // -----------------------------
        // iProperties + User Props
        // -----------------------------
        public dynamic GetUserProps(dynamic doc)
        {
            try { return ComItem(doc.PropertySets, "User Defined Properties"); } catch { }
            try { return ComItem(doc.PropertySets, 4); } catch { }
            return null!;
        }

        public int GetUserPropInt(dynamic doc, string propName, int defaultValue)
        {
            try
            {
                dynamic ups = GetUserProps(doc);
                if (ups == null) return defaultValue;

                dynamic prop = ComItem(ups, propName);
                object v = prop.Value;
                if (v == null) return defaultValue;
                return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        public string? GetUserPropString(dynamic doc, string propName)
        {
            try
            {
                dynamic ups = GetUserProps(doc);
                if (ups == null) return null;

                dynamic prop = ComItem(ups, propName);
                object v = prop.Value;
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public void SetUserPropInt(dynamic doc, string propName, int value)
        {
            try
            {
                dynamic ups = GetUserProps(doc);
                if (ups == null) return;

                dynamic prop = ComItem(ups, propName);
                prop.Value = value;
            }
            catch { }
        }

        /// <summary>
        /// iProperty read:
        /// - if property exists but empty => returns "" (not null)
        /// - if property missing or inaccessible => returns "ERROR"
        /// </summary>
        public string GetIPropStringOrError(dynamic doc, string setName, string propName)
        {
            try
            {
                if (doc == null) return "ERROR";

                dynamic ps;
                try { ps = ComItem(doc.PropertySets, setName); }
                catch { return "ERROR"; }

                if (ps == null) return "ERROR";

                dynamic p;
                try { p = ComItem(ps, propName); }
                catch { return "ERROR"; }

                if (p == null) return "ERROR";

                object v;
                try { v = p.Value; }
                catch { return "ERROR"; }

                string s = v?.ToString() ?? string.Empty;

                // exists but empty => insert nothing
                return string.IsNullOrWhiteSpace(s) ? string.Empty : s;
            }
            catch
            {
                return "ERROR";
            }
        }

        // Keep the original signature for existing call sites (best effort backward compat)
        // NOTE: This now returns null only if doc is null; otherwise it returns either value, "" or "ERROR".
        public string? GetIPropString(dynamic doc, string setName, string propName)
        {
            if (doc == null) return null;
            return GetIPropStringOrError(doc, setName, propName);
        }

        public void SetIProp(dynamic doc, string setName, string propName, object value)
        {
            try
            {
                dynamic ps = ComItem(doc.PropertySets, setName);
                dynamic p = ComItem(ps, propName);
                p.Value = value;
            }
            catch { }
        }

        // -----------------------------
        // Save / math helpers
        // -----------------------------
        public void SaveIfDirty(dynamic doc)
        {
            try
            {
                if (!IsModifiable(doc)) return;

                bool dirty;
                try { dirty = (bool)doc.Dirty; } catch { dirty = true; }
                if (!dirty) return;

                try { doc.Save2(true); return; } catch { }
                try { doc.Save(); } catch { }
            }
            catch { }
        }

        public double ParseDouble(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0.0;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
            return 0.0;
        }

        public double Round(double v, int dp) => Math.Round(v, dp);

        public void TryGetXYZInertia(dynamic massProps,
                                     out double ixx, out double iyy, out double izz,
                                     out double ixy, out double iyz, out double ixz)
        {
            ixx = iyy = izz = ixy = iyz = ixz = 0.0;

            try
            {
                object[] args = new object[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };

                ((object)massProps).GetType().InvokeMember(
                    "XYZMomentsOfInertia",
                    BindingFlags.InvokeMethod,
                    null,
                    (object)massProps,
                    args
                );

                ixx = Convert.ToDouble(args[0], CultureInfo.InvariantCulture);
                iyy = Convert.ToDouble(args[1], CultureInfo.InvariantCulture);
                izz = Convert.ToDouble(args[2], CultureInfo.InvariantCulture);
                ixy = Convert.ToDouble(args[3], CultureInfo.InvariantCulture);
                iyz = Convert.ToDouble(args[4], CultureInfo.InvariantCulture);
                ixz = Convert.ToDouble(args[5], CultureInfo.InvariantCulture);
            }
            catch
            {
                // leave zeros
            }
        }

        public string SafeName(dynamic doc)
        {
            try
            {
                string f = doc.FullFileName;
                if (!string.IsNullOrWhiteSpace(f)) return f;
            }
            catch { }

            try
            {
                string n = doc.DisplayName;
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            catch { }

            return "<unknown doc>";
        }

        // -----------------------------
        // Part Number Template + Missing Property reporting
        // -----------------------------

        /// <summary>
        /// Sets Design Tracking Properties -> Part Number using the active numbering scheme output,
        /// then applies the optional template post-processor.
        /// </summary>
        public void SetPartNumber(dynamic doc, string basePartNumber)
        {
            if (doc == null) return;

            // If requested, keep top-level part number fixed.
            if (KeepTopLevelAssemblyPartNumberFixed && IsTopAssembly(doc))
                return;

            string finalPn;
            try
            {
                finalPn = PartNumberTemplateFormatter.Apply(this, doc, basePartNumber ?? string.Empty);
            }
            catch
            {
                // Fall back to base if formatter fails.
                finalPn = basePartNumber ?? string.Empty;
            }

            SetIProp(doc, "Design Tracking Properties", "Part Number", finalPn);
        }

        /// <summary>
        /// Tracks missing properties/tokens during template expansion so we can show one report at the end.
        /// Returns the replacement string per your policy:
        /// - missing/inaccessible => "ERROR"
        /// - exists but empty => ""
        /// </summary>
        public string ResolveIPropToken(dynamic docForLookup, string setName, string propName, string tokenForReporting)
        {
            string val = GetIPropStringOrError(docForLookup, setName, propName);

            // If ERROR, log it once per doc/token so it shows up in the report.
            // (Empty string is not an error per your new rule.)
            if (string.Equals(val, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                RegisterMissingProperty(docForLookup, tokenForReporting, $"{setName}.{propName}");
            }

            return val;
        }

        /// <summary>
        /// Tracks missing properties/tokens during template expansion so we can show one report at the end.
        /// </summary>
        public void RegisterMissingProperty(dynamic doc, string token, string detail)
        {
            try
            {
                string docName = SafeName(doc);
                string t = token ?? "<token>";
                string d = detail ?? "";
                string line = $"{docName}  |  {t}  |  {d}";

                if (_missingPropertyKeys.Add(line))
                    MissingProperties.Add(line);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Builds a human-readable report of missing template tokens/properties.
        /// Returns empty string if nothing missing.
        /// </summary>
        public string BuildMissingPropertiesReport()
        {
            if (MissingProperties.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("Some template tokens could not be resolved. These were replaced with 'ERROR':");
            sb.AppendLine();
            sb.AppendLine("Document  |  Token  |  Lookup");
            sb.AppendLine(new string('-', 90));

            foreach (var line in MissingProperties)
                sb.AppendLine(line);

            sb.AppendLine();
            sb.AppendLine("Fix: set the required iProperties (or change the template / enable top-level property source).");
            return sb.ToString();
        }

        private bool IsTopAssembly(dynamic doc)
        {
            try
            {
                if (ReferenceEquals(doc, TopAssembly)) return true;

                string a = "";
                string b = "";
                try { a = (string)doc.FullFileName; } catch { a = ""; }
                try { b = (string)TopAssembly.FullFileName; } catch { b = ""; }
                if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                    return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return false;
        }

        internal static class SkipPropertyResolver
        {
            // Canonical set names from your iLogic reporter
            private const string SET_DTP = "Design Tracking Properties";
            private const string SET_SUM = "Inventor Summary Information";
            private const string SET_DSUM = "Inventor Document Summary Information";
            private const string SET_UDP = "Inventor User Defined Properties";

            // Alias -> candidate (Set, Name) list in priority order
            private static readonly Dictionary<string, (string Set, string Name)[]> _aliases =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    // Your "Status" convenience alias (maps to real Inventor props)
                    ["Status"] = new[]
                    {
                (SET_DTP, "User Status"),
                (SET_DTP, "Design Status"),
                    },

                    ["UserStatus"] = new[] { (SET_DTP, "User Status") },
                    ["DesignStatus"] = new[] { (SET_DTP, "Design Status") },

                    // Common renumber-related fields
                    ["PartNumber"] = new[] { (SET_DTP, "Part Number") },
                    ["StockNumber"] = new[] { (SET_DTP, "Stock Number") },
                    ["Material"] = new[] { (SET_DTP, "Material") },
                    ["Project"] = new[] { (SET_DTP, "Project") },
                    ["Description"] = new[] { (SET_DTP, "Description") },

                    // Summary information examples
                    ["Author"] = new[] { (SET_SUM, "Author") },
                    ["RevisionNumber"] = new[] { (SET_SUM, "Revision Number") },
                    ["Comments"] = new[] { (SET_SUM, "Comments") },

                    // Custom examples (you can add more as needed)
                    ["Thickness"] = new[] { (SET_UDP, "THICKNESS") },
                };

            public static IEnumerable<(string Set, string Name)> Resolve(string selection, string customName)
            {
                if (string.IsNullOrWhiteSpace(selection))
                    yield break;

                // Custom… => custom property name (in Inventor User Defined Properties)
                if (string.Equals(selection, "Custom…", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(customName))
                        yield return (SET_UDP, customName.Trim());
                    yield break;
                }

                // If the dropdown gives "Set.Property" (your current pattern), map friendly sets -> canonical sets
                int dot = selection.IndexOf('.');
                if (dot > 0 && dot < selection.Length - 1)
                {
                    string rawSet = selection.Substring(0, dot).Trim();
                    string prop = selection.Substring(dot + 1).Trim();

                    string canonSet = rawSet;

                    // friendly -> canonical mapping
                    if (rawSet.Equals("Project", StringComparison.OrdinalIgnoreCase))
                        canonSet = SET_DTP;
                    else if (rawSet.Equals("Summary Information", StringComparison.OrdinalIgnoreCase))
                        canonSet = SET_SUM;
                    else if (rawSet.Equals("Document Summary Information", StringComparison.OrdinalIgnoreCase))
                        canonSet = SET_DSUM;
                    else if (rawSet.Equals("User Defined Properties", StringComparison.OrdinalIgnoreCase))
                        canonSet = SET_UDP;
                    else if (rawSet.Equals("Design Tracking Properties", StringComparison.OrdinalIgnoreCase))
                        canonSet = SET_DTP;

                    yield return (canonSet, prop);
                    yield break;
                }

                // Otherwise treat it as an alias key first
                if (_aliases.TryGetValue(selection.Trim(), out var candidates))
                {
                    foreach (var c in candidates) yield return c;
                    yield break;
                }

                // Last resort: treat as a literal property name; search all sets later
                yield return (string.Empty, selection.Trim());
            }
        }

        internal static bool TryGetIPropertyStringAnySet(dynamic doc, string propertyName, out string value)
        {
            value = "";
            if (doc == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            try
            {
                foreach (dynamic ps in doc!.PropertySets)
                {
                    try
                    {
                        dynamic p = ps[propertyName];
                        value = Convert.ToString(p.Value) ?? "";
                        return true;
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }


    }
}
