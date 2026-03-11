using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    internal abstract class SettingsTabBase : ISettingsTabController
    {
        protected readonly ISettingsTabHost Host;

        // Light text color for labels so they read well on the dark palette background
        protected static readonly Color LabelTextColor = Color.FromArgb(220, 220, 220);

        protected SettingsTabBase(ISettingsTabHost host)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public abstract string TabTitle { get; }

        public abstract KryptonPage BuildTab();
        public abstract void WireEvents();
        public abstract void LoadFromSettings();
        public abstract void ApplyToSettings();

        public virtual void Validate(List<string> warnings, List<string> errors) { }

        protected static KryptonLabel MakeLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            LabelStyle = LabelStyle.NormalPanel,
            Margin = new Padding(0, 0, 8, 0)
        };

        protected void OnChanged()
        {
            if (Host.IsLoading) return;
            Host.MarkChanged();
        }
    }
}
