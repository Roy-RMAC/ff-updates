using Inventor;
using System;

namespace RMAC_Efficiency_Addin.UI
{
    /// <summary>
    /// Builds RMAC controls for Sheet Metal environment.
    /// Handles both UI styles:
    ///  1) A dedicated "Sheet Metal" ribbon (rare / install-dependent)
    ///  2) A "Sheet Metal" tab inside the Part ribbon (common)
    /// </summary>
    internal sealed class _SheetMetalRibbon
    {
        private readonly string _clientId;

        public _SheetMetalRibbon(string clientId)
        {
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        }

        public void Build(UserInterfaceManager uiMan, RmacCommands cmd)
        {
            if (uiMan == null) return;
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));

            // 1) Try a dedicated Sheet Metal ribbon first (if present)
            Ribbon? smRibbon = null;
            try { smRibbon = uiMan.Ribbons["SheetMetal"]; } catch { }
            if (smRibbon == null)
            {
                try { smRibbon = uiMan.Ribbons["Sheet Metal"]; } catch { }
            }

            if (smRibbon != null)
            {
                BuildOnRibbon(smRibbon, cmd);
                return;
            }

            // 2) Fallback: Sheet Metal is usually a TAB inside the Part ribbon
            Ribbon? partRibbon = null;
            try { partRibbon = uiMan.Ribbons["Part"]; } catch { }
            if (partRibbon == null) return;

            var sheetMetalTab = FindTab(partRibbon, "Sheet Metal", "id_TabSheetMetal");
            if (sheetMetalTab == null)
            {
                // Some installs/locales may use different display names; try internal-only scan
                sheetMetalTab = FindTabByContains(partRibbon, "sheet");
            }

            if (sheetMetalTab == null) return;

            BuildOnTab(sheetMetalTab, cmd);
        }

        // -------------------- Build implementations --------------------

        private void BuildOnRibbon(Ribbon ribbon, RmacCommands cmd)
        {
            // Matches your earlier "RMAC Efficiency" tab approach
            var tab = EnsureTab(ribbon, "RMAC Efficiency", "RMAC_Efficiency_SM_Tab");
            var panel = EnsurePanel(tab, "RMAC Tools", "RMAC_Efficiency_SM_Panel");

            RibbonHelpers.AddButtonSafe(panel, cmd.BtnSmThicknessFromLine, true);
            AddComboSafe(panel, cmd.CmbDimMode, false);
            RibbonHelpers.AddButtonSafe(panel, cmd.BtnShowSketchDims, false);
            RibbonHelpers.AddButtonSafe(panel, cmd.BtnHideSketchDims, false);
            RibbonHelpers.AddButtonSafe(panel, cmd.BtnHighlight, false);
            RibbonHelpers.AddButtonSafe(panel, cmd.BtnClearHighlight, false);
        }

        private void BuildOnTab(RibbonTab sheetMetalTab, RmacCommands cmd)
        {
            // This matches your Part "3D Model" pattern: panels on a built-in tab.

            // Panel A: Dims (same as 3D Model)
            var pDims = EnsurePanel(sheetMetalTab, "Dims", "RMAC_SheetMetal_Dims_Panel");
            AddComboSafe(pDims, cmd.CmbDimMode, true);
            RibbonHelpers.AddButtonSafe(pDims, cmd.BtnShowSketchDims, false);
            RibbonHelpers.AddButtonSafe(pDims, cmd.BtnHideSketchDims, false);

            // Panel B: Visibility (Sketches) - optional but consistent with your 3D Model ribbon
            var pVis = EnsurePanel(sheetMetalTab, "Visibility", "RMAC_SheetMetal_Visibility_Sketch_Panel");
            RibbonHelpers.AddButtonSafe(pVis, cmd.BtnHideAllSketches, true);
            RibbonHelpers.AddButtonSafe(pVis, cmd.BtnShowAllSketches, false);

            // Panel C: Tools - includes Thickness from Line
            var pTools = EnsurePanel(sheetMetalTab, "Tools", "RMAC_SheetMetal_Tools_Panel");
            RibbonHelpers.AddButtonSafe(pTools, cmd.BtnSmThicknessFromLine, true);

            // Panel D: Highlight (same as 3D Model)
            var pHi = EnsurePanel(sheetMetalTab, "Highlight", "RMAC_SheetMetal_Highlight_Panel");
            RibbonHelpers.AddButtonSafe(pHi, cmd.BtnHighlight, true);
            RibbonHelpers.AddButtonSafe(pHi, cmd.BtnClearHighlight, false);
        }

        // -------------------- Find / Ensure helpers --------------------

        private static RibbonTab? FindTab(Ribbon ribbon, string displayName, string internalName)
        {
            foreach (RibbonTab t in ribbon.RibbonTabs)
            {
                try
                {
                    if (string.Equals(t.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                        return t;
                }
                catch { }
            }

            foreach (RibbonTab t in ribbon.RibbonTabs)
            {
                if (string.Equals(t.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }

        private static RibbonTab? FindTabByContains(Ribbon ribbon, string containsLower)
        {
            foreach (RibbonTab t in ribbon.RibbonTabs)
            {
                try
                {
                    var dn = (t.DisplayName ?? string.Empty).ToLowerInvariant();
                    var inn = (t.InternalName ?? string.Empty).ToLowerInvariant();
                    if (dn.Contains(containsLower) || inn.Contains(containsLower))
                        return t;
                }
                catch { }
            }
            return null;
        }

        private RibbonTab EnsureTab(Ribbon ribbon, string displayName, string internalName)
        {
            foreach (RibbonTab t in ribbon.RibbonTabs)
            {
                if (string.Equals(t.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return ribbon.RibbonTabs.Add(displayName, internalName, _clientId);
        }

        private RibbonPanel EnsurePanel(RibbonTab tab, string displayName, string internalName)
        {
            foreach (RibbonPanel p in tab.RibbonPanels)
            {
                if (string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return tab.RibbonPanels.Add(displayName, internalName, _clientId);
        }

        // -------------------- Control add helpers --------------------

        private static void AddComboSafe(RibbonPanel panel, ComboBoxDefinition? combo, bool insertBefore)
        {
            if (panel == null || combo == null) return;
            try { panel.CommandControls.AddComboBox(combo, string.Empty, insertBefore); } catch { }
        }
    }
}
