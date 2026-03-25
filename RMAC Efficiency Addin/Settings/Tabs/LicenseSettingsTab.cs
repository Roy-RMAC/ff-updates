using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Navigator;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Licensing;
using RMAC_Efficiency_Addin.UI.Dialogs;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    internal sealed class LicenseSettingsTab : SettingsTabBase
    {
        public LicenseSettingsTab(ISettingsTabHost host) : base(host) { }

        public override string TabTitle => "License";

        private KryptonPage? _tab;

        private const int RowHeight = 34;

        private readonly KryptonLabel _lblStatusValue = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            LabelStyle = LabelStyle.NormalPanel
        };

        private readonly KryptonLabel _lblKeyValue = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            LabelStyle = LabelStyle.NormalPanel
        };

        private readonly KryptonLabel _lblMachineValue = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            LabelStyle = LabelStyle.NormalPanel
        };

        private readonly KryptonLabel _lblValidatedValue = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            LabelStyle = LabelStyle.NormalPanel
        };

        private readonly KryptonLabel _lblSubscriptionValue = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            LabelStyle = LabelStyle.NormalPanel
        };

        private readonly KryptonButton _btnDeactivate = new()
        {
            Text = "Deactivate License",
            AutoSize = false
        };

        private readonly KryptonButton _btnChangeKey = new()
        {
            Text = "Change License Key",
            AutoSize = false
        };

        public override KryptonPage BuildTab()
        {
            if (_tab != null) return _tab;

            _tab = new KryptonPage { Text = TabTitle, Padding = new Padding(10) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int r = 0;
            void AddRow(string labelText, Control value)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));
                var lbl = new KryptonLabel
                {
                    Text = labelText,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 8, 0)
                };
                lbl.StateNormal.ShortText.Color1 = LabelTextColor;
                layout.Controls.Add(lbl, 0, r);
                layout.Controls.Add(value, 1, r);
                r++;
            }

            AddRow("Status:", _lblStatusValue);
            AddRow("License key:", _lblKeyValue);
            AddRow("Machine ID:", _lblMachineValue);
            AddRow("Last validated:", _lblValidatedValue);
            AddRow("Subscription:", _lblSubscriptionValue);

            // Buttons row
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            btnPanel.Controls.Add(_btnDeactivate);
            btnPanel.Controls.Add(_btnChangeKey);
            layout.Controls.Add(btnPanel, 0, r);
            layout.SetColumnSpan(btnPanel, 2);
            r++;

            // Info text
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var lblInfo = new KryptonLabel
            {
                Text = "Deactivating frees your license seat so it can be used on another machine.",
                AutoSize = true,
                Dock = DockStyle.Fill,
                LabelStyle = LabelStyle.NormalPanel,
                Margin = new Padding(0, 8, 0, 0)
            };
            lblInfo.StateNormal.ShortText.Color1 = Color.FromArgb(160, 160, 160);
            layout.Controls.Add(lblInfo, 0, r);
            layout.SetColumnSpan(lblInfo, 2);

            _tab.Controls.Add(layout);
            return _tab;
        }

        public override void WireEvents()
        {
            _btnDeactivate.Click += OnDeactivateClick;
            _btnChangeKey.Click += OnChangeKeyClick;
        }

        public override void LoadFromSettings()
        {
            RefreshDisplay();
        }

        public override void ApplyToSettings()
        {
            // No settings to persist — this tab is display-only
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            // Nothing to validate
        }

        private void RefreshDisplay()
        {
            var info = LicenseManager.GetCurrentInfo();

            // Status with color
            switch (info.State)
            {
                case LicenseState.Activated:
                    _lblStatusValue.Text = "Activated";
                    _lblStatusValue.StateNormal.ShortText.Color1 = Color.MediumSeaGreen;
                    break;
                default:
                    _lblStatusValue.Text = info.State.ToString();
                    _lblStatusValue.StateNormal.ShortText.Color1 = Color.IndianRed;
                    break;
            }

            _lblKeyValue.Text = info.MaskedKey ?? "(none)";
            _lblMachineValue.Text = info.MachineId ?? "Unknown";
            _lblValidatedValue.Text = info.LastValidated?.ToLocalTime().ToString("g") ?? "(never)";
            _lblSubscriptionValue.Text = info.SubscriptionStatus ?? "(n/a)";

            _btnDeactivate.Enabled = info.State == LicenseState.Activated;
        }

        private void OnDeactivateClick(object? sender, EventArgs e)
        {
            var confirm = MessageBox.Show(
                Host.Owner,
                "Are you sure you want to deactivate your license?\n\n" +
                "This will free the seat for use on another machine.\n" +
                "You will need to restart Inventor to re-activate.",
                "Deactivate License",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            var ok = LicenseManager.Deactivate();
            if (ok)
            {
                MessageBox.Show(Host.Owner,
                    "License deactivated. Please restart Inventor to re-activate on this or another machine.",
                    "License Deactivated",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(Host.Owner,
                    "Could not contact the license server to deactivate.\n" +
                    "The local license data has been removed. Please ensure you have an internet connection.",
                    "Deactivation Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            RefreshDisplay();
        }

        private void OnChangeKeyClick(object? sender, EventArgs e)
        {
            using var dlg = new LicenseActivationDialog();
            dlg.ShowDialog(Host.Owner);
            RefreshDisplay();
        }
    }
}
