using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Settings;
using RMAC_Efficiency_Addin.UI.Wizard.Steps;

namespace RMAC_Efficiency_Addin.UI.Wizard
{
    internal sealed class SetupWizardForm : KryptonForm
    {
        private readonly KryptonManager _kryptonManager = new();
        private readonly List<IWizardStepPanel> _steps = new();
        private readonly KryptonLabel[] _sidebarLabels;
        private readonly Panel _contentHost = new() { Dock = DockStyle.Fill };
        private readonly Label _lblStepTitle = new();
        private readonly Label _lblStepDesc = new();
        private readonly KryptonButton _btnBack = new() { Text = "< Back", AutoSize = false };
        private readonly KryptonButton _btnNext = new() { Text = "Next >", AutoSize = false };
        private readonly KryptonButton _btnCancel = new() { Text = "Cancel", AutoSize = false };
        private int _currentStepIndex;

        private static readonly Color ActiveStepColor = Color.FromArgb(220, 220, 220);
        private static readonly Color InactiveStepColor = Color.FromArgb(130, 130, 130);
        internal static readonly Color DarkBg = Color.FromArgb(43, 43, 43);

        public SetupWizardForm()
        {
            _kryptonManager.GlobalPaletteMode = PaletteMode.Microsoft365Black;

            Text = "RMAC Setup Wizard";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(860, 560);
            MinimumSize = new Size(700, 480);

            // Register steps
            _steps.Add(new WelcomeStep());
            _steps.Add(new ExportCategoriesStep());
            _steps.Add(new CommentListsStep());
            _steps.Add(new PartNumberingStep());

            _steps.Add(new SettingsLocationStep());
            _steps.Add(new FinishStep());

            _sidebarLabels = new KryptonLabel[_steps.Count];

            BuildUi();
            WireEvents();
            ShowStep(0);
        }

        private void BuildUi()
        {
            // Root: sidebar | main content, with button bar at bottom
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0),
                Margin = new Padding(0),
                BackColor = DarkBg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            // Sidebar
            var sidebar = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 20, 8, 10)
            };

            var sidebarLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false
            };

            for (int i = 0; i < _steps.Count; i++)
            {
                var lbl = new KryptonLabel
                {
                    Text = $"  {i + 1}. {_steps[i].StepTitle}",
                    AutoSize = true,
                    LabelStyle = LabelStyle.NormalPanel,
                    Margin = new Padding(0, 4, 0, 4)
                };
                _sidebarLabels[i] = lbl;
                sidebarLayout.Controls.Add(lbl);
            }

            sidebar.Controls.Add(sidebarLayout);
            root.Controls.Add(sidebar, 0, 0);

            // Main content area
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 16, 16, 8)
            };

            _lblStepTitle.AutoSize = true;
            _lblStepTitle.Dock = DockStyle.Top;
            _lblStepTitle.ForeColor = Color.White;
            _lblStepTitle.Margin = new Padding(0, 0, 0, 4);
            _lblStepTitle.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);

            _lblStepDesc.AutoSize = true;
            _lblStepDesc.Dock = DockStyle.Top;
            _lblStepDesc.ForeColor = Color.FromArgb(200, 200, 200);
            _lblStepDesc.Margin = new Padding(0, 0, 0, 12);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            headerPanel.Controls.Add(_lblStepDesc);
            headerPanel.Controls.Add(_lblStepTitle);

            mainPanel.Controls.Add(_contentHost);
            mainPanel.Controls.Add(headerPanel);

            root.Controls.Add(mainPanel, 1, 0);

            // Button bar
            var buttonBar = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 16, 6)
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            foreach (var btn in new[] { _btnBack, _btnNext, _btnCancel })
            {
                btn.Width = 90;
                btn.Height = 30;
                btn.Margin = new Padding(6, 0, 0, 0);
            }

            buttons.Controls.Add(_btnBack);
            buttons.Controls.Add(_btnNext);
            buttons.Controls.Add(_btnCancel);

            buttonBar.Controls.Add(buttons);
            root.Controls.Add(buttonBar, 0, 1);
            root.SetColumnSpan(buttonBar, 2);

            Controls.Add(root);
        }

        private void WireEvents()
        {
            _btnBack.Click += (_, _) => GoBack();
            _btnNext.Click += (_, _) => GoNext();
            _btnCancel.Click += (_, _) => CancelWizard();
        }

        private void ShowStep(int index)
        {
            _currentStepIndex = index;

            // Update sidebar
            for (int i = 0; i < _sidebarLabels.Length; i++)
            {
                bool active = i == index;
                var lbl = _sidebarLabels[i];
                lbl.Text = (active ? "> " : "  ") + $"{i + 1}. {_steps[i].StepTitle}";
                lbl.StateCommon.ShortText.Color1 = active ? ActiveStepColor : InactiveStepColor;
                lbl.StateCommon.ShortText.Font = active
                    ? new Font(Font, FontStyle.Bold)
                    : new Font(Font, FontStyle.Regular);
            }

            // Update header
            _lblStepTitle.Text = _steps[index].StepTitle;
            _lblStepDesc.Text = _steps[index].StepDescription;

            // Swap content
            _contentHost.Controls.Clear();
            var panel = _steps[index].BuildPanel();
            panel.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(panel);
            _steps[index].LoadFromSettings();

            // Update buttons
            _btnBack.Enabled = index > 0;
            _btnNext.Text = index == _steps.Count - 1 ? "Finish" : "Next >";
        }

        private void GoNext()
        {
            if (!_steps[_currentStepIndex].Validate(out var error))
            {
                MessageBox.Show(this, error, "Setup Wizard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _steps[_currentStepIndex].ApplyToSettings();

            if (_currentStepIndex == _steps.Count - 1)
            {
                CompleteWizard();
                return;
            }

            ShowStep(_currentStepIndex + 1);
        }

        private void GoBack()
        {
            if (_currentStepIndex == 0) return;
            _steps[_currentStepIndex].ApplyToSettings();
            ShowStep(_currentStepIndex - 1);
        }

        private void CompleteWizard()
        {
            AddinSettings.Current.WizardCompleted = true;
            var result = AddinSettings.SaveWithResult();

            if (result.Error != null)
            {
                MessageBox.Show(this,
                    $"Settings saved with warning:\n{result.Error}\n\nSaved to: {result.Path}",
                    "Setup Wizard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelWizard()
        {
            // Discard in-memory changes by reloading from disk
            AddinSettings.Load();
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && DialogResult == DialogResult.None)
            {
                CancelWizard();
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }
    }
}
