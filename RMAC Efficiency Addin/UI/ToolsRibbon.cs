using Inventor;
using System;

namespace RMAC_Efficiency_Addin.UI
{
    /// <summary>
    /// Adds RMAC UI to multiple environments so Settings is always reachable:
    /// - ZeroDoc (no documents open)
    /// - Part
    /// - Assembly
    /// - Drawing
    ///
    /// We create/ensure an "RMAC" tab in each ribbon environment and add Settings there.
    /// This avoids relying on any specific built-in "Tools" tab name/ID.
    /// </summary>
    internal sealed class _ToolsRibbon
    {
        private readonly string _clientId;

        public _ToolsRibbon(string clientId)
        {
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        }

        public void Build(UserInterfaceManager uiMan, RmacCommands cmd)
        {
            if (uiMan == null) return;
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));

            // Ensure Settings is available in all common contexts:
            // ZeroDoc = no document open
            // Part/Assembly/Drawing = common working environments
            TryAddSettingsToRibbon(uiMan, "ZeroDoc", cmd);
            TryAddSettingsToRibbon(uiMan, "Part", cmd);
            TryAddSettingsToRibbon(uiMan, "Assembly", cmd);
            TryAddSettingsToRibbon(uiMan, "Drawing", cmd);

            // Some installs may have additional names/locales; best effort only.
            TryAddSettingsToRibbon(uiMan, "Presentation", cmd);
        }

        private void TryAddSettingsToRibbon(UserInterfaceManager uiMan, string ribbonName, RmacCommands cmd)
        {
            Ribbon? ribbon = null;
            try { ribbon = uiMan.Ribbons[ribbonName]; } catch { }

            if (ribbon == null) return;

            try
            {
                // Create an RMAC tab per environment to avoid dependency on built-in tabs.
                var tab = EnsureTab(ribbon, "RMAC", $"RMAC_Global_{ribbonName}_Tab");
                var panel = EnsurePanel(tab, "Settings", $"RMAC_Global_{ribbonName}_Settings_Panel");
                RibbonHelpers.AddButtonSafe(panel, cmd.BtnSettings, true);
            }
            catch
            {
                // UI build is best-effort
            }
        }

        private RibbonTab EnsureTab(Ribbon ribbon, string displayName, string internalName)
        {
            foreach (RibbonTab t in ribbon.RibbonTabs)
            {
                try
                {
                    if (string.Equals(t.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                        return t;
                }
                catch { }
            }

            return ribbon.RibbonTabs.Add(displayName, internalName, _clientId);
        }

        private RibbonPanel EnsurePanel(RibbonTab tab, string displayName, string internalName)
        {
            foreach (RibbonPanel p in tab.RibbonPanels)
            {
                try
                {
                    if (string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                catch { }
            }

            return tab.RibbonPanels.Add(displayName, internalName, _clientId);
        }

    }
}
