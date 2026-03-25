using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Licensing;

namespace RMAC_Efficiency_Addin.UI.Dialogs
{
    internal sealed class LicenseActivationDialog : KryptonForm
    {
        // TODO: Replace with your actual Lemon Squeezy store URL
        private const string PurchaseUrl = "https://rmacdesign.lemonsqueezy.com/checkout/buy/71ec5a56-2336-4233-83b7-063a0eae5f2e";

        private static readonly Color LightText = Color.FromArgb(220, 220, 220);

        private readonly string? _expiredMessage;
        private bool _allowClose;

        private readonly KryptonManager _kryptonManager = new()
        {
            GlobalPaletteMode = PaletteMode.Microsoft365Black
        };

        private readonly KryptonLabel _lblHeading = new()
        {
            Text = "RMAC Efficiency",
            AutoSize = true,
            LabelStyle = LabelStyle.TitlePanel
        };

        private readonly KryptonLabel _lblStatus = new()
        {
            AutoSize = true,
            LabelStyle = LabelStyle.NormalPanel
        };

        private readonly KryptonTextBox _txtKey = new()
        {
            Dock = DockStyle.Fill
        };

        private readonly KryptonLabel _lblFeedback = new()
        {
            Text = "",
            AutoSize = true,
            Dock = DockStyle.Fill,
            LabelStyle = LabelStyle.NormalPanel
        };

        private readonly KryptonButton _btnActivate = new() { Text = "Activate", AutoSize = true };
        private readonly KryptonButton _btnBuy = new() { Text = "Start Free Trial / Buy License", AutoSize = true };
        private readonly KryptonButton _btnSkip = new() { Text = "Continue Without Addin", AutoSize = true };

        public LicenseActivationDialog(string? expiredMessage = null)
        {
            _expiredMessage = expiredMessage;

            Text = "RMAC Efficiency - License";
            ClientSize = new Size(460, 280);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;

            BuildLayout();
            ApplyTextColors();
            WireEvents();
            UpdateStatus();
        }

        private void BuildLayout()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 6,
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // heading
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 8)); // spacer
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // key label + input
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // feedback
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

            table.Controls.Add(_lblHeading, 0, 0);
            table.Controls.Add(_lblStatus, 0, 1);

            // Key input row
            var keyPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var lblKey = new KryptonLabel
            {
                Text = "License Key:",
                AutoSize = true,
                LabelStyle = LabelStyle.NormalPanel,
                Margin = new Padding(0, 0, 8, 0)
            };
            keyPanel.Controls.Add(lblKey, 0, 0);
            keyPanel.Controls.Add(_txtKey, 1, 0);
            table.Controls.Add(keyPanel, 0, 3);

            // Feedback
            _lblFeedback.Margin = new Padding(0, 4, 0, 0);
            table.Controls.Add(_lblFeedback, 0, 4);

            // Buttons
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };
            btnPanel.Controls.Add(_btnActivate);
            btnPanel.Controls.Add(_btnBuy);
            btnPanel.Controls.Add(_btnSkip);
            table.Controls.Add(btnPanel, 0, 5);

            Controls.Add(table);
        }

        private void ApplyTextColors()
        {
            _lblHeading.StateNormal.ShortText.Color1 = LightText;
            _lblStatus.StateNormal.ShortText.Color1 = LightText;

            // Walk all KryptonLabels in the form and apply light text
            void Apply(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is KryptonLabel lbl)
                        lbl.StateNormal.ShortText.Color1 = LightText;
                    if (c is TableLayoutPanel or FlowLayoutPanel)
                        c.BackColor = Color.Transparent;
                    Apply(c);
                }
            }
            Apply(this);
        }

        private void WireEvents()
        {
            _btnActivate.Click += OnActivateClick;
            _btnBuy.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(PurchaseUrl) { UseShellExecute = true }); }
                catch { }
            };
            _btnSkip.Click += OnSkipClick;
            FormClosing += OnFormClosing;
        }

        private void UpdateStatus()
        {
            if (_expiredMessage != null)
            {
                _lblStatus.StateNormal.ShortText.Color1 = Color.IndianRed;
                _lblStatus.Text = _expiredMessage;
            }
            else
            {
                _lblStatus.StateNormal.ShortText.Color1 = Color.FromArgb(220, 220, 220);
                _lblStatus.Text = "Enter a license key to activate, or start a free trial via our store.";
            }
        }

        private void OnActivateClick(object? sender, EventArgs e)
        {
            var key = _txtKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                ShowFeedback("Please enter a license key.", Color.IndianRed);
                return;
            }

            SetBusy(true, "Activating...");

            var result = LicenseManager.Activate(key);

            SetBusy(false);

            if (result.Success)
            {
                _allowClose = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                ShowFeedback(result.Error ?? "Activation failed.", Color.IndianRed);
            }
        }

        private void OnSkipClick(object? sender, EventArgs e)
        {
            _allowClose = true;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            // X button or any close = same as Skip (addin won't load, Inventor continues)
            if (!_allowClose)
                DialogResult = DialogResult.Cancel;
        }

        private void ShowFeedback(string text, Color color)
        {
            _lblFeedback.StateNormal.ShortText.Color1 = color;
            _lblFeedback.Text = text;
        }

        private void SetBusy(bool busy, string? message = null)
        {
            _btnActivate.Enabled = !busy;
            _txtKey.Enabled = !busy;
            if (message != null)
                ShowFeedback(message, Color.FromArgb(220, 220, 220));
            else
                _lblFeedback.Text = "";
        }
    }
}
