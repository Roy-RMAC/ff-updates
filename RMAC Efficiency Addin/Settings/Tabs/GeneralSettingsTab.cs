// Settings/Tabs/GeneralSettingsTab.cs
using RMAC_Efficiency_Addin.Settings;
using RMAC_Efficiency_Addin.Updates;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    internal sealed class GeneralSettingsTab : SettingsTabBase
    {
        public GeneralSettingsTab(
            ISettingsTabHost host,
            Action? openSettingsFile = null,
            Action? newSettingsFile = null,
            Action? openSettingsFolder = null,
            Action? resetDefaults = null,
            Action? runWizard = null) : base(host)
        {
            _openSettingsFile = openSettingsFile;
            _newSettingsFile = newSettingsFile;
            _openSettingsFolder = openSettingsFolder; // supported (not shown)
            _resetDefaults = resetDefaults;
            _runWizard = runWizard;
        }

        public override string TabTitle => "General";

        private readonly Action? _openSettingsFile;
        private readonly Action? _newSettingsFile;
        private readonly Action? _openSettingsFolder;
        private readonly Action? _resetDefaults;
        private readonly Action? _runWizard;

        private KryptonPage? _tab;

        private const int LabelColWidth = 180;

        private readonly KryptonTextBox _txtSettingsPath = new()
        {
            ReadOnly = true,
            Dock = DockStyle.Fill
        };

        private readonly KryptonButton _btnOpenFile = new() { Text = "Open", AutoSize = false };
        private readonly KryptonButton _btnNewFile = new() { Text = "New", AutoSize = false };
        private readonly KryptonButton _btnReset = new() { Text = "Reset Defaults", AutoSize = false };
        private readonly KryptonButton _btnWizard = new() { Text = "Setup Wizard", AutoSize = false };
        private readonly KryptonButton _btnCheckUpdates = new() { Text = "Check for Updates", AutoSize = false };

        private readonly KryptonCheckBox _chkDebug = new()
        {
            Text = "Enable debug logging",
            AutoSize = true
        };

        private readonly KryptonComboBox _cmbDimMode = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        private readonly KryptonTextBox _txtInfo = new()
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill
        };

        public override KryptonPage BuildTab()
        {
            if (_tab != null) return _tab;

            _tab = new KryptonPage { Text = TabTitle, Padding = new Padding(10) };

            if (_cmbDimMode.Items.Count == 0)
            {
                foreach (var v in Enum.GetValues(typeof(DimPolicyMode)))
                    _cmbDimMode.Items.Add(v);
            }

            // DPI-safe sizing
            int fieldH = _txtSettingsPath.Height;
            int rowH = fieldH + 10;

            // Button sizing (uniform width + same height as textboxes)
            int wNew = TextRenderer.MeasureText(_btnNewFile.Text, _btnNewFile.Font).Width;
            int wOpen = TextRenderer.MeasureText(_btnOpenFile.Text, _btnOpenFile.Font).Width;
            int wReset = TextRenderer.MeasureText(_btnReset.Text, _btnReset.Font).Width;
            int wWizard = TextRenderer.MeasureText(_btnWizard.Text, _btnWizard.Font).Width;
            int wUpdate = TextRenderer.MeasureText(_btnCheckUpdates.Text, _btnCheckUpdates.Font).Width;
            int btnW = Math.Max(wUpdate, Math.Max(wWizard, Math.Max(wReset, Math.Max(wNew, wOpen)))) + 32;

            int buttonColW = btnW + 24;

            _btnNewFile.Height = fieldH;
            _btnOpenFile.Height = fieldH;
            _btnReset.Height = fieldH;
            _btnWizard.Height = fieldH;
            _btnCheckUpdates.Height = fieldH;

            _btnNewFile.Width = btnW;
            _btnOpenFile.Width = btnW;
            _btnReset.Width = btnW;
            _btnWizard.Width = btnW;
            _btnCheckUpdates.Width = btnW;

            // Layout: explicit row indices
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 5,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Key change: remove the unused right column width so controls can reach the same right edge as the info box.
            // We keep the 3rd column but make it a 0px spacer to avoid any TableLayout surprises.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));

            // --- Row 0: Settings file ---
            layout.Controls.Add(MakeLabel("Settings file"), 0, 0);

            _txtSettingsPath.Margin = new Padding(0, 3, 0, 3);
            layout.Controls.Add(_txtSettingsPath, 1, 0);

            // --- Row 1: Actions ---
            layout.Controls.Add(MakeLabel("Actions"), 0, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            _btnNewFile.Margin = new Padding(0, 0, 10, 0);
            _btnOpenFile.Margin = new Padding(0, 0, 10, 0);
            _btnReset.Margin = new Padding(0, 0, 10, 0);
            _btnWizard.Margin = new Padding(0, 0, 10, 0);
            _btnCheckUpdates.Margin = new Padding(0, 0, 0, 0);

            actions.Controls.Add(_btnNewFile);
            actions.Controls.Add(_btnOpenFile);
            actions.Controls.Add(_btnReset);
            actions.Controls.Add(_btnWizard);
            actions.Controls.Add(_btnCheckUpdates);

            actions.Margin = new Padding(0, 3, 0, 3);
            layout.Controls.Add(actions, 1, 1);

            // --- Row 2: Debug ---
            layout.Controls.Add(MakeLabel("Debug"), 0, 2);

            _chkDebug.Dock = DockStyle.Left;
            _chkDebug.Margin = new Padding(0, 0, 0, 0);

            var debugPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            _chkDebug.Location = new Point(0, (rowH - _chkDebug.Height) / 2);
            debugPanel.Controls.Add(_chkDebug);

            layout.Controls.Add(debugPanel, 1, 2);

            // --- Row 3: Dimension policy ---
            layout.Controls.Add(MakeLabel("Dimension policy"), 0, 3);

            _cmbDimMode.Dock = DockStyle.Fill;
            _cmbDimMode.Margin = new Padding(0);
            _cmbDimMode.DropDownWidth = 520;

            var dimPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 0)
            };
            dimPanel.Controls.Add(_cmbDimMode);

            layout.Controls.Add(dimPanel, 1, 3);

            // --- Row 4: Info fill ---
            layout.Controls.Add(_txtInfo, 0, 4);
            layout.SetColumnSpan(_txtInfo, 3);

            _txtInfo.Text =
                "General settings affecting logging and basic tool behaviour.\r\n\r\n" +
                "Settings scope:\r\n" +
                "• User: stored in AppData\r\n" +
                "• Company: stored in ProgramData\r\n" +
                "• Custom: user-selected settings file\r\n";

            _tab.Controls.Add(layout);
            return _tab;
        }

        public override void WireEvents()
        {
            _chkDebug.CheckedChanged += (_, __) => OnChanged();
            _cmbDimMode.SelectedIndexChanged += (_, __) => OnChanged();

            _btnOpenFile.Click += (_, __) => (_openSettingsFile ?? (() =>
                MessageBox.Show(Host.Owner, "Open action not wired.", "General",
                    MessageBoxButtons.OK, MessageBoxIcon.Information)))();

            _btnNewFile.Click += (_, __) => (_newSettingsFile ?? (() =>
                MessageBox.Show(Host.Owner, "New action not wired.", "General",
                    MessageBoxButtons.OK, MessageBoxIcon.Information)))();

            _btnWizard.Click += (_, __) => _runWizard?.Invoke();

            _btnCheckUpdates.Click += async (_, __) =>
            {
                var url = AddinSettings.Current.UpdateUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    MessageBox.Show(Host.Owner,
                        "No update URL configured.\n\nSet the UpdateUrl property in your settings.json file.",
                        "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _btnCheckUpdates.Enabled = false;
                _btnCheckUpdates.Text = "Checking...";
                try
                {
                    var update = await UpdateChecker.CheckForUpdateAsync(url);
                    AddinSettings.Current.LastUpdateCheck = DateTime.UtcNow;
                    AddinSettings.SaveWithResult();

                    if (update == null)
                    {
                        MessageBox.Show(Host.Owner,
                            "You're on the latest version.",
                            "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    using var dlg = new UpdateNotificationForm(update);
                    dlg.ShowDialog(Host.Owner);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Host.Owner,
                        $"Update check failed:\n{ex.Message}",
                        "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    _btnCheckUpdates.Text = "Check for Updates";
                    _btnCheckUpdates.Enabled = true;
                }
            };

            _btnReset.Click += (_, __) =>
            {
                // If a delegate is provided (from RmacSettingsForm), it handles its own confirmation dialog.
                if (_resetDefaults != null) { _resetDefaults(); return; }

                if (MessageBox.Show(
                        Host.Owner,
                        "Reset all settings to defaults?",
                        "General",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                AddinSettings.ResetToDefaults();
                Host.MarkChanged();
            };
        }

        public override void LoadFromSettings()
        {
            _txtSettingsPath.Text = AddinSettings.LoadedPath ?? "";

            var s = AddinSettings.Current;
            _chkDebug.Checked = s.Debug;

            var idx = _cmbDimMode.Items.IndexOf(s.DimMode);
            _cmbDimMode.SelectedIndex = idx >= 0 ? idx : (_cmbDimMode.Items.Count > 0 ? 0 : -1);
        }

        public override void ApplyToSettings()
        {
            var s = AddinSettings.Current;
            s.Debug = _chkDebug.Checked;

            if (_cmbDimMode.SelectedItem is DimPolicyMode m)
                s.DimMode = m;
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            // no-op
        }
    }
}
