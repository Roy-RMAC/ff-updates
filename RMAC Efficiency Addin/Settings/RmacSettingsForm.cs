// Settings/RmacSettingsForm.cs
using Inventor;
// IMPORTANT: this resolves AddinSettings in the root namespace
using RMAC_Efficiency_Addin;
using RMAC_Efficiency_Addin.UI.Settings;
using System;
using System.Drawing;
using System.Windows.Forms;
using RMAC_Efficiency_Addin.Settings;
using Krypton.Navigator;
using Krypton.Toolkit;


namespace RMAC_Efficiency_Addin.UI
{
    internal sealed class RmacSettingsForm : KryptonForm, ISettingsTabHost
    {
        private readonly Inventor.Application? _invApp;

        // Krypton global palette — applies Microsoft 365 Blue theme to all Krypton controls
        private readonly KryptonManager _kryptonManager = new()
        {
            GlobalPaletteMode = PaletteMode.Microsoft365Black
        };

        private readonly KryptonNavigator _navigator = new()
        {
            Dock = DockStyle.Fill,
            NavigatorMode = NavigatorMode.BarTabGroup,
            Bar = { TabBorderStyle = TabBorderStyle.RoundedOutsizeMedium, TabStyle = TabStyle.StandardProfile, ItemSizing = BarItemSizing.Individual },
            PageBackStyle = PaletteBackStyle.PanelClient
        };

        private readonly KryptonButton _btnApply = new() { Text = "Apply" };
        private readonly KryptonButton _btnOk = new() { Text = "OK" };
        private readonly KryptonButton _btnCancel = new() { Text = "Cancel" };

        private bool _isLoading;
        private bool _settingsChanged;

        private readonly SettingsTabsRegistry _registry = new();

        // ISettingsTabHost
        public bool IsLoading => _isLoading;
        public Inventor.Application? InventorApp => _invApp;


        // Explicit interface impl avoids CS0108 'Owner' hiding Form.Owner
        IWin32Window ISettingsTabHost.Owner => this;

        public RmacSettingsForm(Inventor.Application? app = null)
        {
            _invApp = app;

            Text = "RMAC Settings";
            StartPosition = FormStartPosition.CenterParent;

            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            MinimumSize = new Size(720, 520);
            Width = 1640;
            Height = 1240;

            BuildUi();
            BuildTabs();
            LoadAllFromSettings();
            UpdateApplyState();
        }

        private void BuildUi()
        {
            var bottom = new KryptonPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                Padding = new Padding(10, 7, 10, 7)
            };

            const int btnW = 90;
            const int btnH = 30;
            _btnApply.Size = new Size(btnW, btnH);
            _btnApply.Margin = new Padding(0, 0, 6, 0);
            _btnOk.Size = new Size(btnW, btnH);
            _btnOk.Margin = new Padding(0, 0, 6, 0);
            _btnCancel.Size = new Size(btnW, btnH);
            _btnCancel.Margin = new Padding(0);

            _btnCancel.DialogResult = DialogResult.Cancel;

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = System.Drawing.Color.Transparent
            };

            flow.Controls.Add(_btnApply);
            flow.Controls.Add(_btnOk);
            flow.Controls.Add(_btnCancel);

            bottom.Controls.Add(flow);

            Controls.Add(_navigator);
            Controls.Add(bottom);
            bottom.BringToFront();

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            _btnApply.Click += (_, __) => Apply();
            _btnOk.Click += (_, __) =>
            {
                if (Apply())
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            _btnCancel.Click += (_, __) => Close();

            FormClosing += (_, e) =>
            {
                if (!_settingsChanged) return;
                if (DialogResult == DialogResult.OK) return;

                var r = MessageBox.Show(
                    this,
                    "You have unsaved changes. Save before closing?",
                    "RMAC Settings",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                switch (r)
                {
                    case DialogResult.Yes:
                        if (!Apply())
                            e.Cancel = true;    // save failed, stay open
                        break;
                    case DialogResult.Cancel:
                        e.Cancel = true;        // don't close
                        break;
                    // DialogResult.No → discard and close
                }
            };
        }

        private void BuildTabs()
        {
            AddinSettings.EnsureLoaded();

            // Register tabs (order = UI order)
            _registry.Add(new GeneralSettingsTab(
                this,
                openSettingsFile: OpenSettingsFile,
                newSettingsFile: NewSettingsFile,
                openSettingsFolder: OpenSettingsFolder,
                resetDefaults: ResetSettings,
                runWizard: RunSetupWizard));

            _registry.Add(new DrawingSettingsTab(this));
            _registry.Add(new PartNumberingSettingsTab(this));
            _registry.Add(new ExporterSettingsTab(this));
            _registry.Add(new CommentSettingsTab(this));
            _registry.Add(new SheetMetalSettingsTab(this));
            _registry.Add(new SketchColoursSettingsTab(this));
            _registry.Add(new LicenseSettingsTab(this));

            _registry.BuildInto(_navigator);
        }

        public void MarkChanged()
        {
            _settingsChanged = true;
            UpdateApplyState();
        }

        private void LoadAllFromSettings()
        {
            _isLoading = true;
            try
            {
                AddinSettings.EnsureLoaded();
                _registry.LoadAll();
                _settingsChanged = false;
            }
            finally
            {
                _isLoading = false;
                UpdateApplyState();
                UpdateTitle();
            }
        }

        private bool Apply()
        {
            try
            {
                _registry.ApplyAll();

                var (warnings, errors) = _registry.ValidateAll();

                if (errors.Count > 0)
                {
                    MessageBox.Show(
                        this,
                        "Fix these issues before saving:\r\n\r\n" + string.Join("\r\n", errors),
                        "Settings",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return false;
                }

                if (warnings.Count > 0)
                {
                    var r = MessageBox.Show(
                        this,
                        "Warnings:\r\n\r\n" + string.Join("\r\n", warnings) + "\r\n\r\nSave anyway?",
                        "Settings",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (r != DialogResult.Yes)
                        return false;
                }

                var result = AddinSettings.SaveWithResult();

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    MessageBox.Show(this, result.Error, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                _settingsChanged = false;
                UpdateApplyState();
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Apply - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void UpdateApplyState()
        {
            _btnApply.Enabled = _settingsChanged;
        }

        private void UpdateTitle()
        {
            var path = AddinSettings.LoadedPath;
            Text = string.IsNullOrWhiteSpace(path)
                ? "RMAC Settings"
                : $"RMAC Settings  —  {path}";
        }

        // ---------------------------
        // General tab actions
        // ---------------------------

        private void OpenSettingsFile()
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Link to RMAC Settings",
                Filter = "RMAC Settings (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            if (!AddinSettings.OpenCustom(ofd.FileName, out var error))
            {
                MessageBox.Show(this, error ?? "Link failed.", "Link - Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            LoadAllFromSettings();
        }

        private void NewSettingsFile()
        {
            using var sfd = new SaveFileDialog
            {
                Title = "Make New RMAC Settings (defaults)",
                Filter = "RMAC Settings (*.json)|*.json|All files (*.*)|*.*",
                FileName = "RMAC_settings.json",
                OverwritePrompt = true
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
                return;

            if (!AddinSettings.CreateNewCustom(sfd.FileName, out var error))
            {
                MessageBox.Show(this, error ?? "Create failed.", "Make New File - Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            LoadAllFromSettings();
        }

        private void OpenSettingsFolder()
        {
            try
            {
                var dir = AddinSettings.GetSettingsDirectoryForLoadedScope();
                if (string.IsNullOrWhiteSpace(dir)) return;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void ResetSettings()
        {
            if (MessageBox.Show(this, "Reset all settings to defaults?", "Settings",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            AddinSettings.ResetToDefaults();
            LoadAllFromSettings();
        }

        private void RunSetupWizard()
        {
            using var wizard = new Wizard.SetupWizardForm();
            if (wizard.ShowDialog(this) == DialogResult.OK)
                LoadAllFromSettings();
        }
    }
}
