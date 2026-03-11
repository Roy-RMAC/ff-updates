// AddinSettings.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RMAC_Efficiency_Addin.Settings
{
    internal enum SettingsScope
    {
        Company, // ProgramData
        User,    // AppData
        Custom   // User-selected json path
    }

    internal readonly struct SettingsSaveResult
    {
        public SettingsSaveResult(SettingsScope scope, string path, bool usedFallback, string? error)
        {
            Scope = scope;
            Path = path;
            UsedFallback = usedFallback;
            Error = error;
        }

        public SettingsScope Scope { get; }
        public string Path { get; }
        public bool UsedFallback { get; }
        public string? Error { get; }
    }

    // ---------------------------
    // Export settings (v9)
    // ---------------------------

    internal sealed class ExportNamingSettings
    {
        public string DrawingTemplate { get; set; } = "<Part.Part Number>";
        public string DxfTemplate { get; set; } = "<Part.Part Number>";
        public string StepTemplate { get; set; } = "<Part.Part Number>";
        public string BomTemplate { get; set; } = "<Part.Part Number>";

        internal void SanitizeInPlace()
        {
            DrawingTemplate ??= "<Part.Part Number>";
            DxfTemplate ??= "<Part.Part Number>";
            StepTemplate ??= "<Part.Part Number>";
            BomTemplate ??= "<Part.Part Number>";
        }

        internal static ExportNamingSettings CreateDefaults() => new();
    }


    internal sealed class ExportProfileSettings
    {
        // Folder label for this category (e.g. "PROFILE & PRESS", "MACHINE", etc.)
        public string FolderName { get; set; } = "";

        // Per-process naming templates (stored per profile/category)
        public ExportNamingSettings Naming { get; set; } = ExportNamingSettings.CreateDefaults();

        // Per-category actions (matrix in Settings UI)
        public bool PackageDrawings { get; set; } = false;    // Drawings (PDF package)
        public bool ExportDXF { get; set; } = false;
        public bool DxfPostProcessing { get; set; } = false;  // run DXF post processor for this category
        public bool ExportSTEP { get; set; } = false;
        public bool ExportBOM { get; set; } = false;          // reserved (future per-category BOM output)

        // Legacy (removed from UI) – kept to avoid breaking existing settings.json.
        public bool Enabled { get; set; } = true;

        internal void SanitizeInPlace()
        {
            FolderName ??= "";

            Naming ??= ExportNamingSettings.CreateDefaults();
            Naming.SanitizeInPlace();

            // Locked behaviour: profile is considered "enabled" if it exists.
            Enabled = true;
        }

        internal bool HasAnyActionSelected()
            => PackageDrawings || ExportDXF || ExportSTEP || ExportBOM || DxfPostProcessing;
    }
    internal sealed class BomColumnDefinition
    {
        /// <summary>
        /// Token name matching IPropertyTokenRegistry.FindByName(),
        /// "Qty" for BOM quantity, or a custom user-defined property name.
        /// </summary>
        public string TokenName { get; set; } = "Part Number";

        /// <summary>Excel column header text.</summary>
        public string Header { get; set; } = "PART NUMBER";

        /// <summary>Whether numeric formatting is applied.</summary>
        public bool FormattingEnabled { get; set; } = false;

        /// <summary>Number format string: "0", "0.0", "0.00", "0.000".</summary>
        public string NumberFormat { get; set; } = "0.0";

        /// <summary>Whether to strip the unit suffix from the value before writing.</summary>
        public bool StripUnit { get; set; } = false;

        internal void SanitizeInPlace()
        {
            if (string.IsNullOrWhiteSpace(TokenName)) TokenName = "Part Number";
            if (string.IsNullOrWhiteSpace(Header)) Header = TokenName;
            if (string.IsNullOrWhiteSpace(NumberFormat)) NumberFormat = "0.0";

            var allowed = new[] { "0", "0.0", "0.00", "0.000" };
            if (!allowed.Contains(NumberFormat))
                NumberFormat = "0.0";
        }
    }

    internal sealed class ExportingSettings
    {
        // Global run controls
        public bool AlwaysOverwrite { get; set; } = true;

        // Legacy — kept so existing settings.json files deserialize without error. No longer shown in UI.
        public string PostProcessorExe { get; set; } = @"D:\RMAC Design Data\Inventor\RMAC VS Projects\DXF PostProcessor\bin\Debug\Inventor DXF Post Processor REV1.exe";

        // Legacy — kept so existing settings.json files deserialize without error. No longer shown in UI.
        public string DxfOutputVersion { get; set; } = "R12";

        // Optional global actions
        public bool ExportMasterPDF { get; set; } = true;
        public bool ExportBOMExcel { get; set; } = true;
        public bool ExportTopLevelAssemblySTEP { get; set; } = true;

        // Top-level naming templates (resolved via <Top.PropertyName> tokens against the assembly)
        public string TopLevelStepTemplate { get; set; } = "<Top.Part Number>";
        public string TopLevelBomTemplate { get; set; } = "<Top.Part Number>-Rev<Top.Revision Number> BOM";
        public string TopLevelDrawingTemplate { get; set; } = "<Top.Part Number>-Rev<Top.Revision Number> Construction Drawings";

        // Configurable BOM columns (Comments is always auto-appended as last column for filtering)
        public List<BomColumnDefinition> BomColumns { get; set; } = CreateDefaultBomColumns();

        // Comment category -> profile (keys are comment values, e.g. PROFILE/PRESS/MACHINE/etc.)
        public Dictionary<string, ExportProfileSettings> ProfilesByComment { get; set; } =
            new Dictionary<string, ExportProfileSettings>(StringComparer.OrdinalIgnoreCase);

        internal static List<BomColumnDefinition> CreateDefaultBomColumns() => new()
        {
            new() { TokenName = "Part Number",  Header = "PART NUMBER",  FormattingEnabled = false },
            new() { TokenName = "Qty",          Header = "ITEM QTY",     FormattingEnabled = false },
            new() { TokenName = "Stock Number", Header = "STOCK NUMBER", FormattingEnabled = false },
            new() { TokenName = "Description",  Header = "DESCRIPTION",  FormattingEnabled = false },
            new() { TokenName = "Material",     Header = "MATERIAL",     FormattingEnabled = false },
            new() { TokenName = "THICKNESS",    Header = "THICKNESS",    FormattingEnabled = true, NumberFormat = "0.0", StripUnit = true },
            new() { TokenName = "G_L",          Header = "LENGTH",       FormattingEnabled = true, NumberFormat = "0", StripUnit = true },
        };

        internal void SanitizeInPlace()
        {
            if (PostProcessorExe == null) PostProcessorExe = "";
            DxfOutputVersion = "R12"; // hardcoded — IxMilia.Dxf only produces reliable output in R12

            if (string.IsNullOrWhiteSpace(TopLevelStepTemplate)) TopLevelStepTemplate = "<Top.Part Number>";
            if (string.IsNullOrWhiteSpace(TopLevelBomTemplate)) TopLevelBomTemplate = "<Top.Part Number>-Rev<Top.Revision Number> BOM";
            if (string.IsNullOrWhiteSpace(TopLevelDrawingTemplate)) TopLevelDrawingTemplate = "<Top.Part Number>-Rev<Top.Revision Number> Construction Drawings";

            if (BomColumns == null || BomColumns.Count == 0)
                BomColumns = CreateDefaultBomColumns();
            foreach (var col in BomColumns)
                col.SanitizeInPlace();

            if (ProfilesByComment == null)
                ProfilesByComment = new Dictionary<string, ExportProfileSettings>(StringComparer.OrdinalIgnoreCase);

            // Ensure case-insensitive at runtime even if deserializer created a default dictionary
            if (ProfilesByComment.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                var rebuilt = new Dictionary<string, ExportProfileSettings>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in ProfilesByComment)
                    rebuilt[kv.Key ?? ""] = kv.Value ?? new ExportProfileSettings();
                ProfilesByComment = rebuilt;
            }

            foreach (var kv in ProfilesByComment)
            {
                if (kv.Value == null) ProfilesByComment[kv.Key] = new ExportProfileSettings();
                ProfilesByComment[kv.Key].SanitizeInPlace();
            }
        }

        internal static ExportingSettings CreateDefaults()
        {
            var e = new ExportingSettings();

            e.ProfilesByComment["MACHINE"] = new ExportProfileSettings
            {
                FolderName = "MACHINE",
                Naming = new ExportNamingSettings
                {
                    DrawingTemplate = "<Top.Part Number>-Rev<Top.Revision Number> MACHINING COMPONENT DRAWINGS",
                    DxfTemplate = "<Part.Part Number>",
                    StepTemplate = "<Part.Material>-<Part.Stock Number>-<Part.Part Number>-<Qty> REQ'D",
                    BomTemplate = "<Top.Part Number>-Rev<Top.Revision Number> MACHINE COMPONENT LIST"
                },
                PackageDrawings = true,
                ExportDXF = false,
                DxfPostProcessing = false,
                ExportSTEP = true,
                ExportBOM = true,
                Enabled = true
            };

            e.ProfilesByComment["PRESS"] = new ExportProfileSettings
            {
                FolderName = "PRESS",
                Naming = new ExportNamingSettings
                {
                    DrawingTemplate = "<Top.Part Number>-Rev<Top.Revision Number> PRESS COMPONENT DRAWINGS",
                    DxfTemplate = "<Part.Material>-<Part.THICKNESS|0.0|1|N|> mm-<Part.Part Number>-<Qty> REQ'D",
                    StepTemplate = "<Part.Material>-<Part.THICKNESS|0.0|1|N> mm-<Part.Part Number>-<Qty>",
                    BomTemplate = "<Top.Part Number>-Rev<Top.Revision Number> PRESSING COMPONENT LIST"
                },
                PackageDrawings = true,
                ExportDXF = true,
                DxfPostProcessing = true,
                ExportSTEP = true,
                ExportBOM = true,
                Enabled = true
            };

            e.ProfilesByComment["PROFILE"] = new ExportProfileSettings
            {
                FolderName = "PROFILE",
                Naming = new ExportNamingSettings
                {
                    DrawingTemplate = "<Top.Part Number>-Rev<Top.Revision Number> PROFILE COMPONENT DRAWINGS",
                    DxfTemplate = "<Part.Material>-<Part.THICKNESS|0.0|1|N> mm-<Part.Part Number>- <Qty> REQ'D",
                    StepTemplate = "<Part.Part Number>",
                    BomTemplate = "<Top.Part Number>-Rev<Top.Revision Number> PROFILE COMPONENT LIST"
                },
                PackageDrawings = true,
                ExportDXF = true,
                DxfPostProcessing = true,
                ExportSTEP = false,
                ExportBOM = true,
                Enabled = true
            };

            e.ProfilesByComment["TUBE LASER"] = new ExportProfileSettings
            {
                FolderName = "TUBE LASER",
                Naming = new ExportNamingSettings
                {
                    DrawingTemplate = "<Top.Part Number>-Rev<Top.Revision Number> TUBE LASER COMPONENT DRAWINGS",
                    DxfTemplate = "<Part.Part Number>",
                    StepTemplate = "<Part.Material>-<Part.Stock Number>-<Part.Part Number>-<Qty> REQ'D",
                    BomTemplate = "<Top.Part Number>-Rev<Top.Revision Number> TUBE LASER COMPONENT DRAWINGS"
                },
                PackageDrawings = true,
                ExportDXF = false,
                DxfPostProcessing = false,
                ExportSTEP = true,
                ExportBOM = true,
                Enabled = true
            };

            e.SanitizeInPlace();
            return e;
        }
    }

    // ---------------------------
    // Comments options (v10 - unified into settings.json)
    // ---------------------------

    internal sealed class CommentsOptionsSettings
    {
        public List<string> Part { get; set; } = new();
        public List<string> Assembly { get; set; } = new();
        public List<string> TreatAsInseparableWhenAssemblyIs { get; set; } = new();

        // Used by "Check Views Coverage" to ignore BOM items whose Comments match any of these
        public List<string> SkipCommentsForViewCoverage { get; set; } = new();

        internal static CommentsOptionsSettings CreateDefaults() => new()
        {
            Part = new List<string> { "FASTENER", "FITTING", "HAND CUT", "HYD FITTING", "HYD HOSE", "MACHINE", "PRESS", "PROFILE", "PURCHASE", "REFERENCE", "ROLL", "TUBE LASER" },
            Assembly = new List<string> { "GENERAL ASSEMBLY", "SUB-ASSEMBLY", "SUB-WELDMENT", "WELDMENT" },
            TreatAsInseparableWhenAssemblyIs = new List<string> { "HYD HOSE", "INSEPERABLE", "PURCHASE" },
            SkipCommentsForViewCoverage = new List<string> { "FASTENER", "FITTING", "HYD FITTING", "HYD HOSE", "PURCHASE", "REFERENCE" }
        };

        internal void SanitizeInPlace()
        {
            Part = CleanList(Part);
            Assembly = CleanList(Assembly);
            TreatAsInseparableWhenAssemblyIs = CleanList(TreatAsInseparableWhenAssemblyIs);
            SkipCommentsForViewCoverage = CleanList(SkipCommentsForViewCoverage);

            // Never allow an empty skip list to be written accidentally; keep a sane default.
            if (SkipCommentsForViewCoverage.Count == 0)
                SkipCommentsForViewCoverage = new List<string> { "FASTENER", "FITTING", "HYD FITTING", "HYD HOSE", "PURCHASE", "REFERENCE" };

            // Keep the two primary lists non-empty (avoid downstream throw behavior changes)
            if (Part.Count == 0)
                Part = CreateDefaults().Part;

            if (Assembly.Count == 0)
                Assembly = CreateDefaults().Assembly;
        }

        private static List<string> CleanList(IEnumerable<string>? items)
        {
            return (items ?? Array.Empty<string>())
                .Select(s => (s ?? "").Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    internal sealed class AddinSettings
    {
        private static readonly object _lock = new();
        private static bool _loaded;

        // Versioning
        // v1: base
        // v2: InactiveSketchPalette
        // v3: SheetMetalThicknessParamName
        // v4: PartNumberingMode
        // v5: PartNumberTemplate + related flags
        // v6: PartNumberPrefixTemplate / PartNumberSuffixTemplate
        // v7: SkipRenumber (iProperty gate)
        // v8: Drawing title block names (Assembly + Parts)
        // v9: ExportingSettings (category-controlled global exporter)
        // v10: CommentsOptionsSettings (unifies comment-options.json into settings.json)
        // v11: BomColumns (configurable BOM columns)
        public int SettingsVersion { get; set; } = 11;

        public bool Debug { get; set; } = false;
        public DimPolicyMode DimMode { get; set; } = DimPolicyMode.NamedOnly;

        public PartNumberingMode PartNumberingMode { get; set; } = PartNumberingMode.Structured;

        // ---- Part number formatting ----
        public string PartNumberTemplate { get; set; } = "<PartNumber>";
        public string PartNumberPrefixTemplate { get; set; } = "";
        public string PartNumberSuffixTemplate { get; set; } = "";
        public bool PartNumberTemplateUseTopLevelAssemblyProperties { get; set; } = false;
        public bool KeepTopLevelAssemblyPartNumberFixed { get; set; } = true;

        // ---- Skip renumbering gate (iProperty) ----
        public bool SkipRenumberEnabled { get; set; } = true;
        public string SkipRenumberPropertySelection { get; set; } = "Project";
        public string SkipRenumberCustomPropertyName { get; set; } = "";
        public string SkipRenumberMatchValue { get; set; } = "TEST";

        public InactiveSketchPaletteSettings InactiveSketchPalette { get; set; } =
            InactiveSketchPaletteSettings.CreateDefaults();

        public string SheetMetalThicknessParamName { get; set; } = "THICKNESS";

        // ---- Drawing title blocks (definition names) ----
        public string DrawingAssemblyTitleBlockName { get; set; } = "RMAC_Title_Block";
        public string DrawingPartsTitleBlockName { get; set; } = "RMAC Parts Title";

        // ---- Sheet ordering ----
        public SheetOrderingMode SheetOrderingMode { get; set; } = SheetOrderingMode.GroupByAssembly;

        // ---- Exporting (v9) ----
        public ExportingSettings Exporting { get; set; } = ExportingSettings.CreateDefaults();

        // ---- Comments options (v10) ----
        public CommentsOptionsSettings CommentsOptions { get; set; } = CommentsOptionsSettings.CreateDefaults();

        // ---- Setup wizard ----
        public bool WizardCompleted { get; set; } = false;

        // ---- Auto-update ----
        public string UpdateUrl { get; set; } = "";
        public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
        public string? SkippedVersion { get; set; }

        public static AddinSettings Current { get; private set; } = new AddinSettings();

        public static SettingsScope LoadedScope { get; private set; } = SettingsScope.User;
        public static string LoadedPath { get; private set; } = string.Empty;

        private static readonly JsonSerializerOptions JsonReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Replace
        };

        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true
        };

        internal static string CompanySettingsDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RMAC Efficiency Addin");

        internal static string CompanySettingsPath => Path.Combine(CompanySettingsDir, "settings.json");

        internal static string UserSettingsDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RMAC Efficiency Addin");

        internal static string UserSettingsPath => Path.Combine(UserSettingsDir, "settings.json");

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            Load();
        }

        public static void Load()
        {
            lock (_lock)
            {
                try
                {
                    // Priority: Custom -> Company -> User -> defaults
                    var customPath = ReadActiveCustomPath();
                    if (!string.IsNullOrWhiteSpace(customPath) &&
                        TryLoad(customPath, SettingsScope.Custom, out var sc))
                    {
                        Current = sc!;
                        LoadedScope = SettingsScope.Custom;
                        LoadedPath = customPath;
                        return;
                    }

                    if (TryLoad(CompanySettingsPath, SettingsScope.Company, out var s1))
                    {
                        Current = s1!;
                        LoadedScope = SettingsScope.Company;
                        LoadedPath = CompanySettingsPath;
                        return;
                    }

                    if (TryLoad(UserSettingsPath, SettingsScope.User, out var s2))
                    {
                        Current = s2!;
                        LoadedScope = SettingsScope.User;
                        LoadedPath = UserSettingsPath;
                        return;
                    }

                    Current = new AddinSettings();
                    LoadedScope = SettingsScope.User;
                    LoadedPath = UserSettingsPath;

                    // Ensure post-load sanitize
                    MigrateInPlace(Current, LoadedPath);
                }
                finally
                {
                    _loaded = true;
                }
            }
        }

        private static bool TryLoad(string path, SettingsScope scope, out AddinSettings? settings)
        {
            settings = null;

            try
            {
                if (!File.Exists(path)) return false;

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AddinSettings>(json, JsonReadOptions);

                settings = loaded ?? new AddinSettings();
                settings.WizardCompleted = true; // existing file = already configured
                MigrateInPlace(settings, path);
                return true;
            }
            catch
            {
                settings = null;
                return false;
            }
        }

        private static void MigrateInPlace(AddinSettings s, string? loadedSettingsPath)
        {
            if (s.SettingsVersion <= 0)
                s.SettingsVersion = 1;

            // step migrations
            while (s.SettingsVersion < 11)
            {
                switch (s.SettingsVersion)
                {
                    case 1:
                        if (s.InactiveSketchPalette == null)
                            s.InactiveSketchPalette = InactiveSketchPaletteSettings.CreateDefaults();
                        s.InactiveSketchPalette.SanitizeInPlace();
                        s.SettingsVersion = 2;
                        break;

                    case 2:
                        if (string.IsNullOrWhiteSpace(s.SheetMetalThicknessParamName))
                            s.SheetMetalThicknessParamName = "THICKNESS";
                        s.SettingsVersion = 3;
                        break;

                    case 3:
                        s.PartNumberingMode = PartNumberingMode.Structured;
                        s.SettingsVersion = 4;
                        break;

                    case 4:
                        if (string.IsNullOrWhiteSpace(s.PartNumberTemplate))
                            s.PartNumberTemplate = "<PartNumber>";
                        s.PartNumberTemplateUseTopLevelAssemblyProperties = false;
                        s.KeepTopLevelAssemblyPartNumberFixed = true;
                        s.SettingsVersion = 5;
                        break;

                    case 5:
                        DerivePrefixSuffixFromLegacyTemplate(s);
                        s.SettingsVersion = 6;
                        break;

                    case 6:
                        s.SkipRenumberEnabled = false;

                        if (s.SkipRenumberPropertySelection == null)
                            s.SkipRenumberPropertySelection = "Project";
                        if (s.SkipRenumberCustomPropertyName == null)
                            s.SkipRenumberCustomPropertyName = "";
                        if (s.SkipRenumberMatchValue == null)
                            s.SkipRenumberMatchValue = "";

                        s.SettingsVersion = 7;
                        break;

                    case 7:
                        // Drawing title block names
                        if (s.DrawingAssemblyTitleBlockName == null) s.DrawingAssemblyTitleBlockName = "RMAC_Title_Block";
                        if (s.DrawingPartsTitleBlockName == null) s.DrawingPartsTitleBlockName = "RMAC Parts Title";
                        s.SettingsVersion = 8;
                        break;

                    case 8:
                        // v9: Exporting
                        if (s.Exporting == null)
                            s.Exporting = ExportingSettings.CreateDefaults();
                        s.Exporting.SanitizeInPlace();
                        s.SettingsVersion = 9;
                        break;

                    case 9:
                        // v10: CommentsOptions (unify legacy comment-options.json into settings.json)
                        if (s.CommentsOptions == null)
                            s.CommentsOptions = CommentsOptionsSettings.CreateDefaults();

                        TryMigrateLegacyCommentOptionsIntoSettings(s, loadedSettingsPath);

                        s.CommentsOptions.SanitizeInPlace();
                        s.SettingsVersion = 10;
                        break;

                    case 10:
                        // v11: BomColumns (configurable BOM columns)
                        if (s.Exporting.BomColumns == null || s.Exporting.BomColumns.Count == 0)
                            s.Exporting.BomColumns = ExportingSettings.CreateDefaultBomColumns();
                        foreach (var col in s.Exporting.BomColumns)
                            col.SanitizeInPlace();
                        s.SettingsVersion = 11;
                        break;

                    default:
                        s.SettingsVersion = 11;
                        break;
                }
            }

            // sanitize
            if (s.InactiveSketchPalette == null)
                s.InactiveSketchPalette = InactiveSketchPaletteSettings.CreateDefaults();
            s.InactiveSketchPalette.SanitizeInPlace();

            if (string.IsNullOrWhiteSpace(s.SheetMetalThicknessParamName))
                s.SheetMetalThicknessParamName = "THICKNESS";

            if (string.IsNullOrWhiteSpace(s.PartNumberTemplate))
                s.PartNumberTemplate = "<PartNumber>";

            if (s.PartNumberPrefixTemplate == null) s.PartNumberPrefixTemplate = "";
            if (s.PartNumberSuffixTemplate == null) s.PartNumberSuffixTemplate = "";

            if (s.SkipRenumberPropertySelection == null) s.SkipRenumberPropertySelection = "Project";
            if (s.SkipRenumberCustomPropertyName == null) s.SkipRenumberCustomPropertyName = "";
            if (s.SkipRenumberMatchValue == null) s.SkipRenumberMatchValue = "";

            if (s.DrawingAssemblyTitleBlockName == null) s.DrawingAssemblyTitleBlockName = "RMAC_Title_Block";
            if (s.DrawingPartsTitleBlockName == null) s.DrawingPartsTitleBlockName = "RMAC Parts Title";

            if (s.Exporting == null)
                s.Exporting = ExportingSettings.CreateDefaults();
            s.Exporting.SanitizeInPlace();

            if (s.CommentsOptions == null)
                s.CommentsOptions = CommentsOptionsSettings.CreateDefaults();
            s.CommentsOptions.SanitizeInPlace();

            if (s.SettingsVersion < 11)
                s.SettingsVersion = 11;
        }

        private static void TryMigrateLegacyCommentOptionsIntoSettings(AddinSettings s, string? loadedSettingsPath)
        {
            try
            {
                // Legacy file is stored alongside the active settings.json (Custom/Company/User).
                // If a user had a comment-options.json, that was previously the source of truth.
                var dir = Path.GetDirectoryName(loadedSettingsPath ?? "");
                if (string.IsNullOrWhiteSpace(dir)) return;

                var legacyPath = Path.Combine(dir, "comment-options.json");
                if (!File.Exists(legacyPath)) return;

                var json = File.ReadAllText(legacyPath);

                // Local DTO to avoid taking a dependency on RMAC_Efficiency_Addin.CommentOptions here.
                var legacy = JsonSerializer.Deserialize<LegacyCommentOptions>(json, JsonReadOptions);
                if (legacy == null) return;

                s.CommentsOptions.Part = legacy.Part ?? new List<string>();
                s.CommentsOptions.Assembly = legacy.Assembly ?? new List<string>();
                s.CommentsOptions.TreatAsInseparableWhenAssemblyIs = legacy.TreatAsInseparableWhenAssemblyIs ?? new List<string>();
                s.CommentsOptions.SkipCommentsForViewCoverage = legacy.SkipCommentsForViewCoverage ?? new List<string>();

                s.CommentsOptions.SanitizeInPlace();
            }
            catch
            {
                // Silent: never break settings load/migrate because a legacy file is malformed.
            }
        }

        private sealed class LegacyCommentOptions
        {
            public List<string>? Part { get; set; }
            public List<string>? Assembly { get; set; }
            public List<string>? TreatAsInseparableWhenAssemblyIs { get; set; }
            public List<string>? SkipCommentsForViewCoverage { get; set; }
        }

        private static void DerivePrefixSuffixFromLegacyTemplate(AddinSettings s)
        {
            if (!string.IsNullOrWhiteSpace(s.PartNumberPrefixTemplate) ||
                !string.IsNullOrWhiteSpace(s.PartNumberSuffixTemplate))
                return;

            var legacy = s.PartNumberTemplate ?? "<PartNumber>";
            if (string.IsNullOrWhiteSpace(legacy))
            {
                s.PartNumberPrefixTemplate = "";
                s.PartNumberSuffixTemplate = "";
                return;
            }

            const string marker = "<PartNumber>";
            var idx = legacy.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (idx >= 0)
            {
                s.PartNumberPrefixTemplate = legacy.Substring(0, idx);
                s.PartNumberSuffixTemplate = legacy.Substring(idx + marker.Length);
            }
            else
            {
                s.PartNumberPrefixTemplate = "";
                s.PartNumberSuffixTemplate = legacy;
            }
        }

        public static void Save() => _ = SaveWithResult();

        public static SettingsSaveResult SaveWithResult()
        {
            lock (_lock)
            {
                if (LoadedScope == SettingsScope.Custom && !string.IsNullOrWhiteSpace(LoadedPath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(LoadedPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);

                        var json = JsonSerializer.Serialize(Current, JsonWriteOptions);
                        File.WriteAllText(LoadedPath, json);

                        WriteActiveCustomPath(LoadedPath);

                        return new SettingsSaveResult(SettingsScope.Custom, LoadedPath, usedFallback: false, error: null);
                    }
                    catch (Exception exCustom)
                    {
                        try
                        {
                            Directory.CreateDirectory(UserSettingsDir);
                            var json = JsonSerializer.Serialize(Current, JsonWriteOptions);
                            File.WriteAllText(UserSettingsPath, json);

                            LoadedScope = SettingsScope.User;
                            LoadedPath = UserSettingsPath;

                            WriteActiveCustomPath(null);

                            return new SettingsSaveResult(SettingsScope.User, UserSettingsPath, usedFallback: true,
                                error: $"Custom save failed: {exCustom.Message}");
                        }
                        catch (Exception exUser)
                        {
                            return new SettingsSaveResult(SettingsScope.User, UserSettingsPath, usedFallback: true,
                                error: $"Custom save failed: {exCustom.Message} | User save failed: {exUser.Message}");
                        }
                    }
                }

                try
                {
                    Directory.CreateDirectory(CompanySettingsDir);
                    var json = JsonSerializer.Serialize(Current, JsonWriteOptions);
                    File.WriteAllText(CompanySettingsPath, json);

                    LoadedScope = SettingsScope.Company;
                    LoadedPath = CompanySettingsPath;

                    return new SettingsSaveResult(SettingsScope.Company, CompanySettingsPath, usedFallback: false, error: null);
                }
                catch (Exception exCompany)
                {
                    try
                    {
                        Directory.CreateDirectory(UserSettingsDir);
                        var json = JsonSerializer.Serialize(Current, JsonWriteOptions);
                        File.WriteAllText(UserSettingsPath, json);

                        LoadedScope = SettingsScope.User;
                        LoadedPath = UserSettingsPath;

                        return new SettingsSaveResult(SettingsScope.User, UserSettingsPath, usedFallback: true, error: exCompany.Message);
                    }
                    catch (Exception exUser)
                    {
                        return new SettingsSaveResult(SettingsScope.User, UserSettingsPath, usedFallback: true,
                            error: $"Company save failed: {exCompany.Message} | User save failed: {exUser.Message}");
                    }
                }
            }
        }

        public static SettingsSaveResult ResetToDefaults()
        {
            lock (_lock)
            {
                Current = new AddinSettings();
                return SaveWithResult();
            }
        }

        public static string GetSettingsDirectoryForLoadedScope()
        {
            return LoadedScope switch
            {
                SettingsScope.Company => CompanySettingsDir,
                SettingsScope.Custom => Path.GetDirectoryName(LoadedPath) ?? UserSettingsDir,
                _ => UserSettingsDir
            };
        }

        public static void Log(string message)
        {
            try
            {
                if (!Current.Debug) return;
                System.Diagnostics.Debug.WriteLine($"[RMAC] {message}");
            }
            catch { }
        }

        private static string ActivePathPointerFile =>
            Path.Combine(UserSettingsDir, "active_settings_path.txt");

        private static string? ReadActiveCustomPath()
        {
            try
            {
                if (!File.Exists(ActivePathPointerFile)) return null;

                var p = File.ReadAllText(ActivePathPointerFile)?.Trim();
                if (string.IsNullOrWhiteSpace(p)) return null;

                return File.Exists(p) ? p : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteActiveCustomPath(string? path)
        {
            try
            {
                Directory.CreateDirectory(UserSettingsDir);

                if (string.IsNullOrWhiteSpace(path))
                {
                    if (File.Exists(ActivePathPointerFile))
                        File.Delete(ActivePathPointerFile);
                    return;
                }

                File.WriteAllText(ActivePathPointerFile, path);
            }
            catch
            {
                // ignore
            }
        }

        public static bool OpenCustom(string filePath, out string? error)
        {
            error = null;

            lock (_lock)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        error = "File does not exist.";
                        return false;
                    }

                    var json = File.ReadAllText(filePath);
                    var loaded = JsonSerializer.Deserialize<AddinSettings>(json, JsonReadOptions);

                    Current = loaded ?? new AddinSettings();
                    MigrateInPlace(Current, filePath);

                    LoadedScope = SettingsScope.Custom;
                    LoadedPath = filePath;

                    WriteActiveCustomPath(filePath);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        public static bool CreateNewCustom(string filePath, out string? error)
        {
            error = null;

            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    Current = new AddinSettings();
                    MigrateInPlace(Current, filePath);

                    LoadedScope = SettingsScope.Custom;
                    LoadedPath = filePath;

                    WriteActiveCustomPath(filePath);

                    _ = SaveWithResult();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Points the settings system at a custom file path for saving.
        /// If the file already exists it is opened; otherwise it will be created on next save.
        /// </summary>
        public static void SetCustomPath(string filePath)
        {
            lock (_lock)
            {
                LoadedScope = SettingsScope.Custom;
                LoadedPath = filePath;
                WriteActiveCustomPath(filePath);
            }
        }

        /// <summary>
        /// Clears any custom path pointer so settings resolve via the default User/Company chain.
        /// </summary>
        public static void ClearCustomPath()
        {
            lock (_lock)
            {
                WriteActiveCustomPath(null);
                LoadedScope = SettingsScope.User;
                LoadedPath = UserSettingsPath;
            }
        }
    }
}
