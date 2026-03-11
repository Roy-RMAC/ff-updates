using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.UI.Wizard.Steps
{
    internal sealed class SettingsLocationStep : WizardStepBase
    {
        public override string StepTitle => "Settings Location";
        public override string StepDescription => "Choose where to store your RMAC settings";

        private Panel? _panel;
        private RadioButton? _rbDefault;
        private RadioButton? _rbCustom;
        private KryptonTextBox? _txtCustomPath;
        private KryptonButton? _btnBrowse;

        public override Control BuildPanel()
        {
            if (_panel != null) return _panel;

            _panel = new Panel { Dock = DockStyle.Fill };

            // 2-column layout: most rows span both; custom path row uses col 0 + col 1
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(0, 10, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // info
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // radio default
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // default path info
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // radio custom
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // custom path row
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // spacer

            var info = MakeLabel(
                "Settings can be stored in the default location (per-user), or at a " +
                "custom location for team sharing via a network drive.");
            info.Margin = new Padding(0, 0, 0, 16);
            layout.Controls.Add(info, 0, 0);
            layout.SetColumnSpan(info, 2);

            _rbDefault = new RadioButton
            {
                Text = "Default (per-user)",
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 220),
                Margin = new Padding(0, 0, 0, 4)
            };
            layout.Controls.Add(_rbDefault, 0, 1);
            layout.SetColumnSpan(_rbDefault, 2);

            var defaultPath = new Label
            {
                AutoSize = true,
                Text = $"  Saves to: {AddinSettings.UserSettingsPath}",
                ForeColor = Color.FromArgb(160, 160, 160),
                Margin = new Padding(20, 0, 0, 16)
            };
            layout.Controls.Add(defaultPath, 0, 2);
            layout.SetColumnSpan(defaultPath, 2);

            _rbCustom = new RadioButton
            {
                Text = "Custom location",
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 220),
                Margin = new Padding(0, 0, 0, 4)
            };
            layout.Controls.Add(_rbCustom, 0, 3);
            layout.SetColumnSpan(_rbCustom, 2);

            _txtCustomPath = new KryptonTextBox { Dock = DockStyle.Fill, Margin = new Padding(20, 0, 0, 0) };
            layout.Controls.Add(_txtCustomPath, 0, 4);
            _btnBrowse = new KryptonButton { Text = "Browse...", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
            layout.Controls.Add(_btnBrowse, 1, 4);

            // Wire events
            _rbDefault.CheckedChanged += (_, _) => UpdateCustomEnabled();
            _rbCustom.CheckedChanged += (_, _) => UpdateCustomEnabled();
            _btnBrowse.Click += (_, _) => BrowseForPath();

            _panel.Controls.Add(layout);
            return _panel;
        }

        public override void LoadFromSettings()
        {
            var scope = AddinSettings.LoadedScope;
            if (scope == SettingsScope.Custom)
            {
                _rbCustom!.Checked = true;
                _txtCustomPath!.Text = AddinSettings.LoadedPath;
            }
            else
            {
                _rbDefault!.Checked = true;
                _txtCustomPath!.Text = "";
            }
            UpdateCustomEnabled();
        }

        public override void ApplyToSettings()
        {
            if (_rbCustom!.Checked && !string.IsNullOrWhiteSpace(_txtCustomPath!.Text))
            {
                var path = _txtCustomPath.Text.Trim();
                // Ensure the directory exists
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                }
                catch { }

                // Point the settings system at this custom file
                AddinSettings.SetCustomPath(path);
            }
            else
            {
                // Clear any custom path so default resolution applies
                AddinSettings.ClearCustomPath();
            }
        }

        public override bool Validate(out string? error)
        {
            if (_rbCustom!.Checked)
            {
                var path = _txtCustomPath!.Text.Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Please specify a custom settings file path, or choose Default.";
                    return false;
                }

                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(dir))
                    {
                        error = "Invalid file path.";
                        return false;
                    }
                }
                catch
                {
                    error = "Invalid file path.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private void UpdateCustomEnabled()
        {
            bool isCustom = _rbCustom?.Checked == true;
            if (_txtCustomPath != null) _txtCustomPath.Enabled = isCustom;
            if (_btnBrowse != null) _btnBrowse.Enabled = isCustom;
        }

        private void BrowseForPath()
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Choose settings file location",
                Filter = "JSON files (*.json)|*.json",
                FileName = "settings.json",
                OverwritePrompt = false
            };

            if (!string.IsNullOrWhiteSpace(_txtCustomPath!.Text))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_txtCustomPath.Text);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                }
                catch { }
            }

            if (dlg.ShowDialog() == DialogResult.OK)
                _txtCustomPath.Text = dlg.FileName;
        }
    }
}
