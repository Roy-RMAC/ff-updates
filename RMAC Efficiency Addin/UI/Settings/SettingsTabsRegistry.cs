using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    internal sealed class SettingsTabsRegistry
    {
        private readonly List<ISettingsTabController> _tabs = new();

        public IReadOnlyList<ISettingsTabController> Tabs => _tabs;

        public void Add(ISettingsTabController tab)
        {
            _tabs.Add(tab ?? throw new ArgumentNullException(nameof(tab)));
        }

        public void BuildInto(KryptonNavigator navigator)
        {
            navigator.SuspendLayout();
            try
            {
                foreach (var t in _tabs)
                {
                    var page = t.BuildTab();
                    PropagateTheme(page);
                    navigator.Pages.Add(page);
                }

                foreach (var t in _tabs)
                    t.WireEvents();
            }
            finally
            {
                navigator.ResumeLayout(true);
            }
        }

        public void LoadAll()
        {
            foreach (var t in _tabs)
                t.LoadFromSettings();
        }

        public void ApplyAll()
        {
            foreach (var t in _tabs)
                t.ApplyToSettings();
        }

        public (List<string> warnings, List<string> errors) ValidateAll()
        {
            var warnings = new List<string>();
            var errors = new List<string>();

            foreach (var t in _tabs)
                t.Validate(warnings, errors);

            return (warnings, errors);
        }

        private static void PropagateTheme(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TableLayoutPanel or FlowLayoutPanel)
                    c.BackColor = Color.Transparent;

                // Use NormalPanel label style for better contrast on dark palettes
                if (c is KryptonLabel lbl)
                    lbl.LabelStyle = LabelStyle.NormalPanel;

                PropagateTheme(c);
            }
        }
    }
}
