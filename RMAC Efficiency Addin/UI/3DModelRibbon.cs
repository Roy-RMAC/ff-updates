using Inventor;
using System;

namespace RMAC_Efficiency_Addin.UI
{
    /// <summary>
    /// Builds RMAC controls for the Part environment "3D Model" tab.
    /// </summary>
    internal sealed class _3DModelRibbon
    {
        private readonly string _clientId;

        public _3DModelRibbon(string clientId)
        {
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        }

        public void Build(UserInterfaceManager uiMan, RmacCommands cmd)
        {
            if (uiMan == null) return;
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));

            // ============================================================
            // PART ribbon set (Part environment)
            // ============================================================
            try
            {
                var partRibbon = uiMan.Ribbons["Part"];
                var modelTab = FindTab(partRibbon, "3D Model", "id_TabModel");

                if (modelTab != null)
                {
                    // Panel A: Dims
                    var pDims = EnsurePanel(modelTab, "Dims", "RMAC_3DModel_Dims_Panel");
                    AddComboSafe(pDims, cmd.CmbDimMode, true);
                    RibbonHelpers.AddButtonSafe(pDims, cmd.BtnShowSketchDims, false);
                    RibbonHelpers.AddButtonSafe(pDims, cmd.BtnHideSketchDims, false);

                    // Panel B: Visibility (Sketches)
                    var pVis = EnsurePanel(modelTab, "Visibility", "RMAC_3DModel_Visibility_Sketch_Panel");
                    RibbonHelpers.AddButtonSafe(pVis, cmd.BtnHideAllSketches, true);
                    RibbonHelpers.AddButtonSafe(pVis, cmd.BtnShowAllSketches, false);
                    RibbonHelpers.AddButtonSafe(pVis, cmd.BtnLegacySweep, false);

                    // Panel C: Highlight
                    var pHi = EnsurePanel(modelTab, "Highlight", "RMAC_3DModel_Visibility_Highlight_Panel");
                    RibbonHelpers.AddButtonSafe(pHi, cmd.BtnHighlight, true);
                    RibbonHelpers.AddButtonSafe(pHi, cmd.BtnClearHighlight, false);


                }

                // Keep your parking-lot tabs unchanged (create if missing)
                EnsureTab(partRibbon, "RMAC Tools", "RMAC_Tools_Tab");
                EnsureTab(partRibbon, "RMAC Efficiency", "RMAC_Efficiency_Tab");
            }
            catch
            {
                // UI build is best-effort
            }
        }

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

        private static void AddComboSafe(RibbonPanel panel, ComboBoxDefinition? combo, bool insertBefore)
        {
            if (panel == null || combo == null) return;
            try { panel.CommandControls.AddComboBox(combo, string.Empty, insertBefore); } catch { }
        }
    }
}
